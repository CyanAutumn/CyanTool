using CyanTools.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CyanTools.ViewModel
{
    public class KemonoDownloaderViewModel : INotifyPropertyChanged
    {
        private const string ApiBaseUrl = "https://kemono.cr";
        private const int PageSize = 50;
        private const int DownloadRetryCount = 3;
        private static readonly TimeSpan DownloadDelay = TimeSpan.FromSeconds(20);
        private static readonly HttpClient HttpClient;

        static KemonoDownloaderViewModel()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            HttpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(ApiBaseUrl),
                Timeout = TimeSpan.FromSeconds(60)
            };

            HttpClient.DefaultRequestHeaders.Accept.Clear();
            HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/css"));

            HttpClient.DefaultRequestHeaders.AcceptEncoding.Clear();
            HttpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            HttpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            HttpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CyanToolsKemonoDownloader/1.0");
        }

        private readonly RelayCommand _startStopCommand;
        private readonly RelayCommand _openOutputFolderCommand;
        private CancellationTokenSource _cancellationTokenSource;
        private string _artistId = string.Empty;
        private string _outputDirectory = Path.Combine(AppContext.BaseDirectory, "output", "kemono");
        private string _logText = string.Empty;
        private string _selectedService = string.Empty;
        private bool _isDownloading;
        private bool _hasShownCancelMessage;

        public IReadOnlyList<string> ServiceOptions { get; }

        public string ArtistId
        {
            get => _artistId;
            set
            {
                if (_artistId != value)
                {
                    _artistId = value;
                    OnPropertyChanged();
                    _startStopCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string OutputDirectory
        {
            get => _outputDirectory;
            set
            {
                var sanitized = value?.Trim() ?? string.Empty;
                if (_outputDirectory != sanitized)
                {
                    _outputDirectory = sanitized;
                    OnPropertyChanged();
                    _startStopCommand?.RaiseCanExecuteChanged();
                    _openOutputFolderCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string SelectedService
        {
            get => _selectedService;
            set
            {
                if (_selectedService != value)
                {
                    _selectedService = value ?? ServiceOptions.First();
                    OnPropertyChanged();
                }
            }
        }

        public string LogText
        {
            get => _logText;
            set
            {
                _logText = value;
                OnPropertyChanged();
            }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            private set
            {
                if (_isDownloading != value)
                {
                    _isDownloading = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StartButtonText));
                    _startStopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StartButtonText => IsDownloading ? "停止" : "开始下载";

        public ICommand StartStopCommand => _startStopCommand;
        public ICommand OpenOutputFolderCommand => _openOutputFolderCommand;

        public KemonoDownloaderViewModel()
        {
            ServiceOptions = new List<string>
            {
                "fanbox",
                "patreon",
                "fantia",
                "subscribestar",
                "boosty",
                "gumroad"
            };

            Directory.CreateDirectory(_outputDirectory);
            _selectedService = ServiceOptions.First();

            _startStopCommand = new RelayCommand(async _ => await StartOrStopAsync(), _ => CanStartOrStop());
            _openOutputFolderCommand = new RelayCommand(_ => OpenOutputFolder(), _ => !string.IsNullOrWhiteSpace(OutputDirectory));
        }

        private bool CanStartOrStop()
        {
            if (IsDownloading)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(ArtistId) && !string.IsNullOrWhiteSpace(OutputDirectory);
        }

        private async Task StartOrStopAsync()
        {
            if (IsDownloading)
            {
                if (!_hasShownCancelMessage)
                {
                    AppendLog("正在尝试停止当前任务...");
                    _hasShownCancelMessage = true;
                }
                _cancellationTokenSource?.Cancel();
                return;
            }

            _hasShownCancelMessage = false;
            Directory.CreateDirectory(OutputDirectory);
            _cancellationTokenSource = new CancellationTokenSource();
            IsDownloading = true;
            var artist = (ArtistId ?? string.Empty).Trim();
            AppendLog($"开始抓取 {SelectedService} - {artist} 的作品...");

            try
            {
                await DownloadAsync(_cancellationTokenSource.Token);
                AppendLog("下载任务完成。");
            }
            catch (OperationCanceledException)
            {
                AppendLog("任务已取消。");
            }
            catch (Exception ex)
            {
                AppendLog($"发生错误: {ex.Message}");
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                IsDownloading = false;
            }
        }

        private async Task DownloadAsync(CancellationToken token)
        {
            var artistId = (ArtistId ?? string.Empty).Trim();
            var service = string.IsNullOrWhiteSpace(SelectedService) ? ServiceOptions.First() : SelectedService;
            int page = 0;
            int savedCount = 0;
            var postContentCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            while (!token.IsCancellationRequested)
            {
                var posts = await FetchPostsAsync(artistId, service, page, token);
                if (posts.Count == 0)
                {
                    break;
                }

                var downloads = BuildDownloads(posts, page, artistId).ToList();
                if (downloads.Count == 0)
                {
                    page++;
                    continue;
                }

                foreach (var asset in downloads)
                {
                    token.ThrowIfCancellationRequested();
                    AppendLog($"下载 {asset.FileName}...");
                    if (await DownloadAssetAsync(asset, OutputDirectory, token))
                    {
                        savedCount++;
                        AppendLog($"已保存 {asset.FileName}");
                        var content = await ResolvePostContentAsync(asset, artistId, service, postContentCache, token);
                        await SaveContentAsync(content, asset.FileName, OutputDirectory, token);
                    }
                    await Task.Delay(DownloadDelay, token);
                }

                page++;
            }

            if (savedCount == 0)
            {
                AppendLog("没有可下载的文件。");
            }
            else
            {
                AppendLog($"共保存 {savedCount} 个文件。");
            }
        }

        private async Task<List<KemonoPost>> FetchPostsAsync(string artistId, string service, int page, CancellationToken token)
        {
            int offset = page * PageSize;
            var requestUri = $"/api/v1/{service}/user/{artistId}/posts?o={offset}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

            var payload = await response.Content.ReadAsStringAsync(token);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new List<KemonoPost>();
            }

            if (response.StatusCode == HttpStatusCode.BadRequest &&
                payload.IndexOf("offset", StringComparison.OrdinalIgnoreCase) >= 0 &&
                payload.IndexOf("total", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new List<KemonoPost>();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Kemono API 返回 {response.StatusCode}: {payload}");
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return new List<KemonoPost>();
            }

            try
            {
                var tokenJson = JToken.Parse(payload);
                if (tokenJson.Type == JTokenType.Object)
                {
                    var error = tokenJson.Value<string>("error");
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        if (string.Equals(error, "Not Found", StringComparison.OrdinalIgnoreCase))
                        {
                            return new List<KemonoPost>();
                        }
                        throw new InvalidOperationException($"Kemono API 错误: {error}");
                    }

                    throw new InvalidOperationException($"Kemono API 返回未知数据: {tokenJson}");
                }

                var posts = tokenJson.ToObject<List<KemonoPost>>();
                return posts ?? new List<KemonoPost>();
            }
            catch (JsonException jsonEx)
            {
                var preview = payload.Length > 200 ? payload[..200] + "..." : payload;
                throw new InvalidOperationException($"无法解析 Kemono API 响应，返回内容: {preview}", jsonEx);
            }
        }

        private IEnumerable<KemonoAssetDownload> BuildDownloads(IEnumerable<KemonoPost> posts, int page, string artistId)
        {
            foreach (var post in posts)
            {
                var assets = new List<(string Type, KemonoAsset Asset)>();
                if (post.File != null)
                {
                    assets.Add(("file", post.File));
                }

                if (post.Attachments != null)
                {
                    foreach (var attachment in post.Attachments)
                    {
                        if (attachment != null)
                        {
                            assets.Add(("attachment", attachment));
                        }
                    }
                }

                if (assets.Count == 0)
                {
                    continue;
                }

                int assetIndex = 0;
                string dateSegment = FormatDate(post.Published);
                string? postContent = GetPostContent(post);
                string? postId = post.Id;

                foreach (var (type, asset) in assets)
                {
                    if (string.IsNullOrWhiteSpace(asset?.Path))
                    {
                        continue;
                    }

                    var originalName = !string.IsNullOrWhiteSpace(asset.Name)
                        ? asset.Name
                        : Path.GetFileName(asset.Path) ?? string.Empty;

                    var extension = Path.GetExtension(originalName);
                    if (string.IsNullOrWhiteSpace(extension))
                    {
                        extension = Path.GetExtension(asset.Path);
                    }

                    if (string.IsNullOrWhiteSpace(extension))
                    {
                        extension = ".bin";
                    }

                    string fileName = $"{artistId}_{dateSegment}_{page}_{type}_{assetIndex}{extension}";
                    string downloadUrl = asset.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? asset.Path
                        : $"{ApiBaseUrl}{asset.Path}";

                    yield return new KemonoAssetDownload(downloadUrl, fileName, postContent, postId);
                    assetIndex++;
                }
            }
        }

        private static string FormatDate(string? published)
        {
            if (string.IsNullOrWhiteSpace(published))
            {
                return "unknown";
            }

            if (DateTime.TryParse(published, out DateTime parsed))
            {
                return parsed.ToString("yyyy_MM_dd");
            }

            var datePart = published.Split('T').FirstOrDefault();
            return string.IsNullOrWhiteSpace(datePart) ? "unknown" : datePart.Replace('-', '_');
        }

        private static string? GetPostContent(KemonoPost post)
        {
            if (!string.IsNullOrWhiteSpace(post.ContentRaw))
            {
                return post.ContentRaw;
            }

            if (!string.IsNullOrWhiteSpace(post.Content))
            {
                return post.Content;
            }

            return null;
        }

        private async Task<string?> ResolvePostContentAsync(KemonoAssetDownload asset, string artistId, string service, IDictionary<string, string?> cache, CancellationToken token)
        {
            if (!string.IsNullOrWhiteSpace(asset.Content))
            {
                return asset.Content;
            }

            if (string.IsNullOrWhiteSpace(asset.PostId))
            {
                return null;
            }

            if (cache.TryGetValue(asset.PostId, out var cached))
            {
                return cached;
            }

            var content = await FetchPostContentAsync(artistId, service, asset.PostId, token);
            cache[asset.PostId] = content;
            return content;
        }

        private async Task<string?> FetchPostContentAsync(string artistId, string service, string postId, CancellationToken token)
        {
            var requestUri = $"/api/v1/{service}/user/{artistId}/post/{postId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

            var payload = await response.Content.ReadAsStringAsync(token);

            if (!response.IsSuccessStatusCode)
            {
                AppendLog($"获取内容 {postId} 失败: {response.StatusCode}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                var tokenJson = JToken.Parse(payload);
                if (tokenJson.Type != JTokenType.Object)
                {
                    return null;
                }

                var error = tokenJson.Value<string>("error");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    AppendLog($"获取内容 {postId} 失败: {error}");
                    return null;
                }

                var contentRaw = tokenJson.Value<string>("content_raw");
                var content = tokenJson.Value<string>("content");
                if (string.IsNullOrWhiteSpace(contentRaw) && string.IsNullOrWhiteSpace(content))
                {
                    var postToken = tokenJson["post"];
                    contentRaw = postToken?.Value<string>("content_raw");
                    content = postToken?.Value<string>("content");
                }

                if (!string.IsNullOrWhiteSpace(contentRaw))
                {
                    return contentRaw;
                }

                return string.IsNullOrWhiteSpace(content) ? null : content;
            }
            catch (JsonException jsonEx)
            {
                AppendLog($"解析内容 {postId} 失败: {jsonEx.Message}");
                return null;
            }
        }

        private async Task<bool> DownloadAssetAsync(KemonoAssetDownload asset, string outputDirectory, CancellationToken token)
        {
            Directory.CreateDirectory(outputDirectory);
            var targetPath = Path.Combine(outputDirectory, asset.FileName);

            for (int attempt = 0; attempt <= DownloadRetryCount; attempt++)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    using var response = await HttpClient.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();

                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await input.CopyToAsync(output, token);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (attempt < DownloadRetryCount)
                    {
                        AppendLog($"下载 {asset.FileName} 失败，重试 {attempt + 1}/{DownloadRetryCount}: {ex.Message}");
                        continue;
                    }

                    AppendLog($"下载 {asset.FileName} 失败: {ex.Message}");
                    return false;
                }
            }

            return false;
        }

        private async Task SaveContentAsync(string? content, string fileName, string outputDirectory, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            try
            {
                var txtPath = Path.Combine(outputDirectory, Path.ChangeExtension(fileName, ".txt"));
                await File.WriteAllTextAsync(txtPath, content, Encoding.UTF8, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppendLog($"保存内容 {fileName} 失败: {ex.Message}");
            }
        }

        private void OpenOutputFolder()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(OutputDirectory))
                {
                    return;
                }

                Directory.CreateDirectory(OutputDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = OutputDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开目录: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AppendLog(string message)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LogText += $"{DateTime.Now:HH:mm:ss} - {message}\n";
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed record KemonoAssetDownload(string Url, string FileName, string? Content, string? PostId);

        private sealed class KemonoPost
        {
            [JsonProperty("id")]
            public string? Id { get; set; }

            [JsonProperty("published")]
            public string? Published { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }

            [JsonProperty("content_raw")]
            public string? ContentRaw { get; set; }

            [JsonProperty("file")]
            public KemonoAsset? File { get; set; }

            [JsonProperty("attachments")]
            public List<KemonoAsset>? Attachments { get; set; }
        }

        private sealed class KemonoAsset
        {
            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("path")]
            public string? Path { get; set; }
        }
    }
}
