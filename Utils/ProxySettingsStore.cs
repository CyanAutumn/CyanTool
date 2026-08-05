using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CyanTools.Utils
{
    public sealed class ProxyNode : INotifyPropertyChanged
    {
        private int _usageCount;
        private string _availabilityText = "未测试";

        public ProxyNode(
            string id,
            string name,
            string type,
            string server,
            int port,
            bool isLocal = false,
            string? mihomoProxyYaml = null)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim();
            Type = string.IsNullOrWhiteSpace(type) ? "http" : type.Trim().ToLowerInvariant();
            Server = server?.Trim() ?? string.Empty;
            Port = port;
            IsLocal = isLocal;
            MihomoProxyYaml = mihomoProxyYaml ?? string.Empty;
        }

        public string Id { get; }
        public string Name { get; }
        public string Type { get; }
        public string Server { get; }
        public int Port { get; }
        public bool IsLocal { get; }
        internal string MihomoProxyYaml { get; }
        public bool UsesManagedCore => !IsLocal && !string.IsNullOrWhiteSpace(MihomoProxyYaml);
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
        public string AvailabilityText
        {
            get => _availabilityText;
            private set
            {
                if (_availabilityText == value)
                {
                    return;
                }

                _availabilityText = value;
                OnPropertyChanged();
            }
        }

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

        internal void MarkTesting()
            => AvailabilityText = "测试中...";

        internal void SetProbeResult(int? latencyMilliseconds)
        {
            var routeHint = UsesManagedCore
                ? "（托管内核）"
                : IsUsable ? string.Empty : "（需经本地 Clash）";
            AvailabilityText = latencyMilliseconds.HasValue
                ? $"{latencyMilliseconds.Value} ms{routeHint}"
                : $"连接失败{routeHint}";
        }

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
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _clearSubscriptionCommand;
        private readonly RelayCommand _prepareManagedCoreCommand;
        private readonly RelayCommand _checkManagedCoreUpdateCommand;
        private readonly RelayCommand _updateManagedCoreCommand;
        private readonly string _settingsFilePath;
        private readonly Func<Uri, CancellationToken, Task<string>> _subscriptionLoader;
        private readonly Func<ProxyNode, CancellationToken, Task<int?>> _nodeProbe;
        private ProxyNode _localNode;
        private string _subscriptionUrl = string.Empty;
        private string _subscriptionPayload = string.Empty;
        private string _localHttpPort = "7897";
        private string _statusText = "未配置订阅，仅使用本地 Clash 端口。";
        private string _managedCoreStatusText = $"Mihomo {ManagedMihomoService.CoreVersion} 将在首次使用时自动准备。";
        private string _managedCoreInstalledVersionText = "未安装";
        private string _managedCoreLatestVersionText = "未检查";
        private ProxyNode? _selectedNode;
        private bool _isRefreshing;
        private bool _isTesting;
        private bool _isPreparingManagedCore;
        private bool _isLoading;

        public ProxySettingsViewModel()
            : this(SettingsFilePath, DownloadSubscriptionAsync, ProbeNodeAsync)
        {
        }

        internal ProxySettingsViewModel(
            string settingsFilePath,
            Func<Uri, CancellationToken, Task<string>>? subscriptionLoader = null,
            Func<ProxyNode, CancellationToken, Task<int?>>? nodeProbe = null)
        {
            _settingsFilePath = settingsFilePath;
            _subscriptionLoader = subscriptionLoader ?? DownloadSubscriptionAsync;
            _nodeProbe = nodeProbe ?? ProbeNodeAsync;
            Nodes = new ObservableCollection<ProxyNode>();
            Nodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSubscriptionNodes));
            _localNode = new ProxyNode("local", "本地 Clash 端口", "http", "127.0.0.1", 7897, isLocal: true);
            Nodes.Add(_localNode);
            _refreshCommand = new RelayCommand(async _ => await RefreshSubscriptionAsync(), _ => !IsRefreshing && !IsTesting);
            _clearSubscriptionCommand = new RelayCommand(_ => ClearSubscription(), _ => !IsRefreshing && !IsTesting && !string.IsNullOrWhiteSpace(SubscriptionUrl));
            _prepareManagedCoreCommand = new RelayCommand(
                async _ => await PrepareManagedCoreAsync(),
                _ => !IsPreparingManagedCore);
            _checkManagedCoreUpdateCommand = new RelayCommand(
                async _ => await CheckManagedCoreUpdateAsync(),
                _ => !IsPreparingManagedCore);
            _updateManagedCoreCommand = new RelayCommand(
                async _ => await UpdateManagedCoreAsync(),
                _ => !IsPreparingManagedCore);
            var installedVersion = ManagedMihomoService.Shared.InstalledVersion;
            if (!string.IsNullOrWhiteSpace(installedVersion))
            {
                _managedCoreInstalledVersionText = $"v{installedVersion}";
                _managedCoreStatusText = $"Mihomo {installedVersion} 已安装；使用前会重新校验。";
            }
            _isLoading = true;
            try
            {
                Load();
            }
            finally
            {
                _isLoading = false;
            }
        }

        public ObservableCollection<ProxyNode> Nodes { get; }
        public ProxyNode LocalNode => _localNode;
        public bool HasSubscriptionNodes => Nodes.Count > 1;
        public ICommand RefreshCommand => _refreshCommand;
        public ICommand ClearSubscriptionCommand => _clearSubscriptionCommand;
        public ICommand PrepareManagedCoreCommand => _prepareManagedCoreCommand;
        public ICommand CheckManagedCoreUpdateCommand => _checkManagedCoreUpdateCommand;
        public ICommand UpdateManagedCoreCommand => _updateManagedCoreCommand;

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
                if (!_isLoading)
                {
                    Save();
                }
            }
        }

        public string ManagedCoreStatusText
        {
            get => _managedCoreStatusText;
            private set
            {
                if (_managedCoreStatusText == value)
                {
                    return;
                }

                _managedCoreStatusText = value;
                OnPropertyChanged();
            }
        }

        public string ManagedCoreInstalledVersionText
        {
            get => _managedCoreInstalledVersionText;
            private set
            {
                if (_managedCoreInstalledVersionText == value)
                {
                    return;
                }

                _managedCoreInstalledVersionText = value;
                OnPropertyChanged();
            }
        }

        public string ManagedCoreLatestVersionText
        {
            get => _managedCoreLatestVersionText;
            private set
            {
                if (_managedCoreLatestVersionText == value)
                {
                    return;
                }

                _managedCoreLatestVersionText = value;
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

        public bool IsTesting
        {
            get => _isTesting;
            private set
            {
                if (_isTesting == value)
                {
                    return;
                }

                _isTesting = value;
                OnPropertyChanged();
                _refreshCommand.RaiseCanExecuteChanged();
                _clearSubscriptionCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsPreparingManagedCore
        {
            get => _isPreparingManagedCore;
            private set
            {
                if (_isPreparingManagedCore == value)
                {
                    return;
                }

                _isPreparingManagedCore = value;
                OnPropertyChanged();
                _prepareManagedCoreCommand.RaiseCanExecuteChanged();
                _checkManagedCoreUpdateCommand.RaiseCanExecuteChanged();
                _updateManagedCoreCommand.RaiseCanExecuteChanged();
            }
        }

        public async Task PrepareManagedCoreAsync()
        {
            if (IsPreparingManagedCore)
            {
                return;
            }

            IsPreparingManagedCore = true;
            try
            {
                var progress = new Progress<string>(message => ManagedCoreStatusText = message);
                await ManagedMihomoService.Shared.EnsureCoreInstalledAsync(
                    CancellationToken.None,
                    progress);
                ManagedCoreInstalledVersionText = $"v{ManagedMihomoService.Shared.InstalledVersion ?? ManagedMihomoService.CoreVersion}";
            }
            catch (Exception ex)
            {
                ManagedCoreStatusText = $"托管内核准备失败：{ex.Message}";
            }
            finally
            {
                IsPreparingManagedCore = false;
            }
        }

        public async Task CheckManagedCoreUpdateAsync()
        {
            if (IsPreparingManagedCore)
            {
                return;
            }

            IsPreparingManagedCore = true;
            ManagedCoreStatusText = "正在检查 Mihomo 更新...";
            try
            {
                var update = await ManagedMihomoService.Shared.CheckForUpdatesAsync();
                ManagedCoreInstalledVersionText = update.InstalledVersion == null
                    ? "未安装"
                    : $"v{update.InstalledVersion}";
                ManagedCoreLatestVersionText = $"v{update.LatestVersion}";
                ManagedCoreStatusText = update.InstalledVersion == null
                    ? $"可安装 Mihomo {update.LatestVersion}。"
                    : update.IsUpdateAvailable
                        ? $"发现 Mihomo {update.LatestVersion} 更新。"
                        : $"Mihomo {update.InstalledVersion} 已是最新稳定版。";
            }
            catch (Exception ex)
            {
                ManagedCoreStatusText = $"检查 Mihomo 更新失败：{ex.Message}";
            }
            finally
            {
                IsPreparingManagedCore = false;
            }
        }

        public async Task UpdateManagedCoreAsync()
        {
            if (IsPreparingManagedCore)
            {
                return;
            }

            IsPreparingManagedCore = true;
            try
            {
                var progress = new Progress<string>(message => ManagedCoreStatusText = message);
                var update = await ManagedMihomoService.Shared.InstallLatestAsync(
                    CancellationToken.None,
                    progress);
                ManagedCoreInstalledVersionText = $"v{update.InstalledVersion}";
                ManagedCoreLatestVersionText = $"v{update.LatestVersion}";
            }
            catch (Exception ex)
            {
                ManagedCoreStatusText = $"Mihomo 安装 / 更新失败：{ex.Message}";
            }
            finally
            {
                IsPreparingManagedCore = false;
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

                var payload = await _subscriptionLoader(uri, CancellationToken.None);
                var parsed = ClashSubscriptionParser.Parse(payload)
                    .Select(node => new ProxyNode(
                        node.Id,
                        node.Name,
                        node.Type,
                        node.Server,
                        node.Port,
                        mihomoProxyYaml: node.RawYaml))
                    .ToList();
                _subscriptionPayload = payload;
                ReplaceNodes(parsed);
                StatusText = parsed.Count == 0
                    ? "订阅已响应，但没有解析到可用节点。"
                    : $"已更新 {parsed.Count} 个订阅节点；下载任务可按需使用托管内核。";
                Save();
                await TestNodesAsync();
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

        public async Task TestNodesAsync()
        {
            if (IsTesting)
            {
                return;
            }

            var nodes = Nodes.ToArray();
            if (nodes.Length == 0)
            {
                return;
            }

            IsTesting = true;
            StatusText = $"正在测试 {nodes.Length} 个代理节点...";
            foreach (var node in nodes)
            {
                node.MarkTesting();
            }

            try
            {
                using var limiter = new SemaphoreSlim(24);
                var results = await Task.WhenAll(nodes.Select(async node =>
                {
                    await limiter.WaitAsync();
                    try
                    {
                        var latency = await _nodeProbe(node, CancellationToken.None);
                        node.SetProbeResult(latency);
                        return latency.HasValue;
                    }
                    catch
                    {
                        node.SetProbeResult(null);
                        return false;
                    }
                    finally
                    {
                        limiter.Release();
                    }
                }));

                StatusText = $"节点测试完成：{results.Count(result => result)}/{nodes.Length} 个端点可连接。";
            }
            finally
            {
                IsTesting = false;
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
            _subscriptionPayload = string.Empty;
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
                if (!File.Exists(_settingsFilePath))
                {
                    SelectedNode = _localNode;
                    return;
                }

                var settings = JsonSerializer.Deserialize<ProxySettingsFile>(File.ReadAllText(_settingsFilePath), SerializerOptions);
                if (settings == null)
                {
                    SelectedNode = _localNode;
                    return;
                }

                _subscriptionUrl = !string.IsNullOrWhiteSpace(settings.ProtectedSubscriptionUrl)
                    ? WindowsDataProtector.Unprotect(settings.ProtectedSubscriptionUrl).Trim()
                    : settings.SubscriptionUrl?.Trim() ?? string.Empty;
                _subscriptionPayload = !string.IsNullOrWhiteSpace(settings.ProtectedSubscriptionPayload)
                    ? WindowsDataProtector.Unprotect(settings.ProtectedSubscriptionPayload)
                    : string.Empty;
                _localHttpPort = settings.LocalHttpPort is > 0 and <= 65535 ? settings.LocalHttpPort.ToString() : "7897";
                ReplaceLocalNode(settings.LocalHttpPort is > 0 and <= 65535 ? settings.LocalHttpPort : 7897);
                var protectedNodes = ClashSubscriptionParser.Parse(_subscriptionPayload)
                    .Select(node => new ProxyNode(
                        node.Id,
                        node.Name,
                        node.Type,
                        node.Server,
                        node.Port,
                        mihomoProxyYaml: node.RawYaml))
                    .ToArray();
                ReplaceNodes(protectedNodes.Length > 0
                    ? protectedNodes
                    : settings.Nodes?.Select(node => new ProxyNode(
                        node.Id ?? Guid.NewGuid().ToString("N"),
                        node.Name ?? "未命名节点",
                        node.Type ?? "http",
                        node.Server ?? string.Empty,
                        node.Port)) ?? Array.Empty<ProxyNode>());
                SelectedNode = Nodes.FirstOrDefault(node => string.Equals(node.Id, settings.SelectedNodeId, StringComparison.OrdinalIgnoreCase)) ?? _localNode;
                StatusText = string.IsNullOrWhiteSpace(_subscriptionUrl)
                    ? "未配置订阅，仅使用本地 Clash 端口。"
                    : $"已加载 {Math.Max(0, Nodes.Count - 1)} 个已保存的订阅节点。";
                OnPropertyChanged(nameof(SubscriptionUrl));
                OnPropertyChanged(nameof(LocalHttpPort));
                if (string.IsNullOrWhiteSpace(settings.ProtectedSubscriptionUrl)
                    && !string.IsNullOrWhiteSpace(_subscriptionUrl))
                {
                    Save();
                }
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
                var settingsDirectory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrWhiteSpace(settingsDirectory))
                {
                    Directory.CreateDirectory(settingsDirectory);
                }

                var file = new ProxySettingsFile
                {
                    ProtectedSubscriptionUrl = WindowsDataProtector.Protect(SubscriptionUrl),
                    ProtectedSubscriptionPayload = WindowsDataProtector.Protect(_subscriptionPayload),
                    LocalHttpPort = int.TryParse(LocalHttpPort, out var port) ? port : 7897,
                    SelectedNodeId = SelectedNode?.Id,
                    Nodes = Nodes.Where(node => !node.IsLocal).Select(node => new ProxyNodeFile
                    {
                        Id = node.Id,
                        Name = node.Name,
                        Type = node.Type,
                        Server = node.Server,
                        Port = node.Port
                    }).ToList()
                };
                File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(file, SerializerOptions));
            }
            catch
            {

            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private static async Task<string> DownloadSubscriptionAsync(Uri uri, CancellationToken cancellationToken)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CyanTools/1.0");
            return await client.GetStringAsync(uri, cancellationToken);
        }

        private static async Task<int?> ProbeNodeAsync(ProxyNode node, CancellationToken cancellationToken)
        {
            if (node.Port is <= 0 or > 65535 || string.IsNullOrWhiteSpace(node.Server))
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = new TcpClient();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await client.ConnectAsync(node.Server, node.Port, timeout.Token);
                stopwatch.Stop();
                return Math.Max(1, (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds));
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (SocketException)
            {
                return null;
            }
        }

        private sealed class ProxySettingsFile
        {

            public string? SubscriptionUrl { get; set; }
            public string? ProtectedSubscriptionUrl { get; set; }
            public string? ProtectedSubscriptionPayload { get; set; }
            public int LocalHttpPort { get; set; }
            public string? SelectedNodeId { get; set; }
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
