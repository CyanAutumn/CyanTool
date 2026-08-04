using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CyanTools.Utils
{
    public sealed class ProxyNode : INotifyPropertyChanged
    {
        private int _usageCount;

        public ProxyNode(string id, string name, string type, string server, int port, bool isLocal = false)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim();
            Type = string.IsNullOrWhiteSpace(type) ? "http" : type.Trim().ToLowerInvariant();
            Server = server?.Trim() ?? string.Empty;
            Port = port;
            IsLocal = isLocal;
        }

        public string Id { get; }
        public string Name { get; }
        public string Type { get; }
        public string Server { get; }
        public int Port { get; }
        public bool IsLocal { get; }
        public int UsageCount
        {
            get => _usageCount;
            private set
            {
                if (_usageCount == value)
                {
                    return;
                }

                _usageCount = Math.Max(0, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public bool IsUsable => IsLocal || Type is "http" or "https" or "socks5";
        public string DisplayName => UsageCount > 0 ? $"{UsageCount}使用中-{Name}" : Name;
        public string EndpointText => IsLocal ? $"127.0.0.1:{Port}" : $"{Server}:{Port}";
        public string AvailabilityText => IsUsable ? "可直接连接" : "需经本地 Clash";

        public IWebProxy? CreateWebProxy()
        {
            if (!IsUsable || Port <= 0 || string.IsNullOrWhiteSpace(Server))
            {
                return IsLocal && Port > 0 ? new WebProxy($"http://127.0.0.1:{Port}") : null;
            }

            var scheme = Type switch
            {
                "https" => "https",
                "socks5" => "socks5",
                _ => "http"
            };
            return new WebProxy($"{scheme}://{Server}:{Port}");
        }

        public void Acquire() => UsageCount++;
        public void Release() => UsageCount = Math.Max(0, UsageCount - 1);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class ProxySettingsViewModel : INotifyPropertyChanged
    {
        private static readonly string SettingsDirectory = Path.Combine(AppContext.BaseDirectory, "Config");
        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "proxy_settings.json");
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _clearSubscriptionCommand;
        private ProxyNode _localNode;
        private string _subscriptionUrl = string.Empty;
        private string _localHttpPort = "7897";
        private string _statusText = "未配置订阅，仅使用本地 Clash 端口。";
        private ProxyNode? _selectedNode;
        private bool _isRefreshing;

        public ProxySettingsViewModel()
        {
            Nodes = new ObservableCollection<ProxyNode>();
            Nodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSubscriptionNodes));
            _localNode = new ProxyNode("local", "本地 Clash 端口", "http", "127.0.0.1", 7897, isLocal: true);
            Nodes.Add(_localNode);
            _refreshCommand = new RelayCommand(async _ => await RefreshSubscriptionAsync(), _ => !IsRefreshing);
            _clearSubscriptionCommand = new RelayCommand(_ => ClearSubscription(), _ => !IsRefreshing && !string.IsNullOrWhiteSpace(SubscriptionUrl));
            Load();
        }

        public ObservableCollection<ProxyNode> Nodes { get; }
        public ProxyNode LocalNode => _localNode;
        public bool HasSubscriptionNodes => Nodes.Count > 1;
        public ICommand RefreshCommand => _refreshCommand;
        public ICommand ClearSubscriptionCommand => _clearSubscriptionCommand;

        public string SubscriptionUrl
        {
            get => _subscriptionUrl;
            set
            {
                var normalized = value?.Trim() ?? string.Empty;
                if (_subscriptionUrl == normalized)
                {
                    return;
                }

                _subscriptionUrl = normalized;
                OnPropertyChanged();
                _clearSubscriptionCommand.RaiseCanExecuteChanged();
                Save();
            }
        }

        public string LocalHttpPort
        {
            get => _localHttpPort;
            set
            {
                var normalized = value?.Trim() ?? string.Empty;
                if (_localHttpPort == normalized)
                {
                    return;
                }

                _localHttpPort = normalized;
                if (int.TryParse(normalized, out var port) && port is > 0 and <= 65535)
                {
                    ReplaceLocalNode(port);
                }

                OnPropertyChanged();
                Save();
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value)
                {
                    return;
                }

                _statusText = value;
                OnPropertyChanged();
            }
        }

        public ProxyNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode == value)
                {
                    return;
                }

                _selectedNode = value ?? _localNode;
                OnPropertyChanged();
            }
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set
            {
                if (_isRefreshing == value)
                {
                    return;
                }

                _isRefreshing = value;
                OnPropertyChanged();
                _refreshCommand.RaiseCanExecuteChanged();
                _clearSubscriptionCommand.RaiseCanExecuteChanged();
            }
        }

        public async Task RefreshSubscriptionAsync()
        {
            var url = SubscriptionUrl;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                StatusText = "请输入有效的 HTTP/HTTPS 订阅链接。";
                return;
            }

            IsRefreshing = true;
            try
            {
                if (Nodes.Any(node => !node.IsLocal && node.UsageCount > 0))
                {
                    StatusText = "有任务正在使用订阅节点，请等待任务结束后再更新。";
                    return;
                }

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CyanTools/1.0");
                var payload = await client.GetStringAsync(uri);
                var parsed = ParseClashProxies(payload);
                ReplaceNodes(parsed);
                StatusText = parsed.Count == 0
                    ? "订阅已响应，但没有解析到可用节点。"
                    : $"已更新 {parsed.Count} 个订阅节点；不支持的协议仍可通过本地 Clash 端口使用。";
                Save();
            }
            catch (Exception ex)
            {
                StatusText = $"订阅更新失败：{ex.Message}";
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        public void ClearSubscription()
        {
            if (Nodes.Any(node => !node.IsLocal && node.UsageCount > 0))
            {
                StatusText = "有任务正在使用订阅节点，暂不能清空。";
                return;
            }

            SubscriptionUrl = string.Empty;
            ReplaceNodes(Array.Empty<ProxyNode>());
            StatusText = "已清空订阅，仅使用本地 Clash 端口。";
            Save();
        }

        public ProxyNode GetSelectedNodeOrLocal()
            => SelectedNode ?? _localNode;

        private void ReplaceLocalNode(int port)
        {
            var previous = _localNode;
            var index = Nodes.IndexOf(previous);
            if (index < 0 || previous.Port == port)
            {
                return;
            }

            var replacement = new ProxyNode("local", "本地 Clash 端口", "http", "127.0.0.1", port, isLocal: true);
            Nodes[index] = replacement;
            if (ReferenceEquals(SelectedNode, previous))
            {
                SelectedNode = replacement;
            }

            _localNode = replacement;
        }

        private void ReplaceNodes(IEnumerable<ProxyNode> nodes)
        {
            var selectedId = SelectedNode?.Id;
            var local = Nodes.FirstOrDefault(node => node.IsLocal) ?? _localNode;
            var existing = Nodes
                .Where(node => !node.IsLocal)
                .ToDictionary(BuildNodeSignature, node => node, StringComparer.OrdinalIgnoreCase);
            Nodes.Clear();
            Nodes.Add(local);
            foreach (var node in nodes.Where(node => !node.IsLocal).GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
            {
                Nodes.Add(existing.TryGetValue(BuildNodeSignature(node), out var retained) ? retained : node);
            }

            SelectedNode = Nodes.FirstOrDefault(node => string.Equals(node.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? local;
            OnPropertyChanged(nameof(HasSubscriptionNodes));
        }

        private static string BuildNodeSignature(ProxyNode node)
            => $"{node.Type}|{node.Server}|{node.Port}|{node.Name}";

        private void Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    SelectedNode = _localNode;
                    return;
                }

                var settings = JsonSerializer.Deserialize<ProxySettingsFile>(File.ReadAllText(SettingsFilePath), SerializerOptions);
                if (settings == null)
                {
                    SelectedNode = _localNode;
                    return;
                }

                _subscriptionUrl = settings.SubscriptionUrl?.Trim() ?? string.Empty;
                _localHttpPort = settings.LocalHttpPort is > 0 and <= 65535 ? settings.LocalHttpPort.ToString() : "7897";
                ReplaceLocalNode(settings.LocalHttpPort is > 0 and <= 65535 ? settings.LocalHttpPort : 7897);
                ReplaceNodes(settings.Nodes?.Select(node => new ProxyNode(node.Id ?? Guid.NewGuid().ToString("N"), node.Name ?? "未命名节点", node.Type ?? "http", node.Server ?? string.Empty, node.Port)) ?? Array.Empty<ProxyNode>());
                StatusText = string.IsNullOrWhiteSpace(_subscriptionUrl)
                    ? "未配置订阅，仅使用本地 Clash 端口。"
                    : $"已加载 {Math.Max(0, Nodes.Count - 1)} 个已保存的订阅节点。";
                OnPropertyChanged(nameof(SubscriptionUrl));
                OnPropertyChanged(nameof(LocalHttpPort));
            }
            catch
            {
                SelectedNode = _localNode;
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                var file = new ProxySettingsFile
                {
                    SubscriptionUrl = SubscriptionUrl,
                    LocalHttpPort = int.TryParse(LocalHttpPort, out var port) ? port : 7897,
                    Nodes = Nodes.Where(node => !node.IsLocal).Select(node => new ProxyNodeFile
                    {
                        Id = node.Id,
                        Name = node.Name,
                        Type = node.Type,
                        Server = node.Server,
                        Port = node.Port
                    }).ToList()
                };
                File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(file, SerializerOptions));
            }
            catch
            {

            }
        }

        private static List<ProxyNode> ParseClashProxies(string payload)
        {
            var result = new List<ProxyNode>();
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > 8_000_000)
            {
                return result;
            }

            var inProxies = false;
            Dictionary<string, string>? current = null;
            void Flush()
            {
                if (current == null || !current.TryGetValue("server", out var server) || !int.TryParse(current.GetValueOrDefault("port"), out var port) || port <= 0)
                {
                    return;
                }

                var name = current.GetValueOrDefault("name") ?? $"{server}:{port}";
                var type = current.GetValueOrDefault("type") ?? "http";
                var id = $"subscription-{result.Count}-{name}";
                result.Add(new ProxyNode(id, name, type, server, port));
            }

            foreach (var rawLine in payload.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal) || line.Length == 0)
                {
                    continue;
                }

                if (line.Equals("proxies:", StringComparison.OrdinalIgnoreCase))
                {
                    inProxies = true;
                    continue;
                }

                if (!inProxies)
                {
                    continue;
                }

                if (!rawLine.StartsWith(" ") && !rawLine.StartsWith("\t") && line.EndsWith(":", StringComparison.Ordinal))
                {
                    Flush();
                    break;
                }

                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    Flush();
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    line = line[2..].Trim();
                    if (line.StartsWith("{", StringComparison.Ordinal))
                    {
                        foreach (var pair in line.Trim('{', '}').Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            AddKeyValue(current, pair);
                        }
                        Flush();
                        current = null;
                    }
                    else
                    {
                        AddKeyValue(current, line);
                    }
                    continue;
                }

                if (current != null)
                {
                    AddKeyValue(current, line);
                }
            }

            Flush();
            return result;
        }

        private static void AddKeyValue(IDictionary<string, string> values, string line)
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                return;
            }

            var key = line[..separator].Trim().Trim('"', '\'');
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (key is "name" or "type" or "server" or "port")
            {
                values[key] = value;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private sealed class ProxySettingsFile
        {
            public string? SubscriptionUrl { get; set; }
            public int LocalHttpPort { get; set; }
            public List<ProxyNodeFile>? Nodes { get; set; }
        }

        private sealed class ProxyNodeFile
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Type { get; set; }
            public string? Server { get; set; }
            public int Port { get; set; }
        }
    }

    public static class ProxySettingsStore
    {
        private static readonly Lazy<ProxySettingsViewModel> Lazy = new(() => new ProxySettingsViewModel());
        public static ProxySettingsViewModel Shared => Lazy.Value;
    }
}
