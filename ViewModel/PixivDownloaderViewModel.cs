using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Model;
using CyanTools.Pipeline;
using CyanTools.Utils;
using CyanTools;
using Newtonsoft.Json;

namespace CyanTools.ViewModel
{
    public class PixivDownloaderViewModel : INotifyPropertyChanged
    {
        private const string ApiBaseUrl = "https://app-api.pixiv.net";
        private const string AuthTokenUrl = "https://oauth.secure.pixiv.net/auth/token";
        private const string LoginUrl = "https://app-api.pixiv.net/web/v1/login";
        private const string RedirectUri = "https://app-api.pixiv.net/web/v1/users/auth/pixiv/callback";
        private const string ClientId = "MOBrBDS8blbauoSck0ZfDbtuzpyT";
        private const string ClientSecret = "lsACyCD94FhDUtGTXi3QzcFE2uU1hqtDaKeqrdwj";
        private const string HashSecret = "28c1fdd170a5204386cb1313c7077b34f83e4aaf4aa829ce78c231e05b0bae2c";
        private const string PixivUserAgent = "PixivIOSApp/7.13.3 (iOS 14.6; iPhone13,2)";
        private const string AppOs = "ios";
        private const string AppOsVersion = "14.6";
        private const string DefaultOutputDirectory = "./output";
        private const int DefaultDownloadIntervalSeconds = 20;
        private static readonly string SettingsDirectory = Path.Combine(AppContext.BaseDirectory, "Config");
        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "config.json");
        private static readonly string RefreshTokenDirectory = Path.Combine(AppContext.BaseDirectory, "Configs", "Crawler");
        private static readonly string RefreshTokenFilePath = Path.Combine(RefreshTokenDirectory, "pixiv_refresh_token.txt");
        private static readonly string[] ContentTypes = { "illust", "manga" };

        private static readonly HttpClient AuthClient;
        private static readonly HttpClient ApiClient;
        private static readonly HttpClient ImageClient;

        static PixivDownloaderViewModel()
        {
            AuthClient = CreateHttpClient();
            ApiClient = CreateHttpClient(new Uri(ApiBaseUrl));
            ImageClient = CreateHttpClient();

            ConfigureApiClient(ApiClient);
            ConfigureImageClient(ImageClient);
        }

        private readonly RelayCommand _startStopCommand;
        private readonly RelayCommand _openOutputFolderCommand;
        private readonly RelayCommand _getRefreshTokenCommand;
        private CancellationTokenSource? _cancellationTokenSource;
        private CancellationTokenSource? _saveSettingsCts;
        private string _artistId = string.Empty;
        private string _refreshToken = string.Empty;
        private string _outputDirectory = DefaultOutputDirectory;
        private int _downloadIntervalSeconds = DefaultDownloadIntervalSeconds;
        private string _logText = string.Empty;
        private bool _isDownloading;
        private bool _hasShownCancelMessage;
        private string? _accessToken;
        private Action<string>? _pipelineLogger;
        private bool _isApplyingSettings;
        private bool _settingsDirty;

        private static readonly JsonSerializerOptions SettingsSerializerOptions = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };

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
                    ScheduleSaveSettings();
                }
            }
        }

        public string RefreshToken
        {
            get => _refreshToken;
            set
            {
                if (_refreshToken != value)
                {
                    _refreshToken = value;
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
                    ScheduleSaveSettings();
                }
            }
        }

        public int DownloadIntervalSeconds
        {
            get => _downloadIntervalSeconds;
            set
            {
                var normalized = value < 0 ? 0 : value;
                if (_downloadIntervalSeconds != normalized)
                {
                    _downloadIntervalSeconds = normalized;
                    OnPropertyChanged();
                    ScheduleSaveSettings();
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
                    _getRefreshTokenCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StartButtonText => IsDownloading ? "停止" : "开始下载";

        public ICommand StartStopCommand => _startStopCommand;
        public ICommand OpenOutputFolderCommand => _openOutputFolderCommand;
        public ICommand GetRefreshTokenCommand => _getRefreshTokenCommand;

        public PixivDownloaderViewModel()
        {
            _startStopCommand = new RelayCommand(async _ => await StartOrStopAsync(), _ => CanStartOrStop());
            _openOutputFolderCommand = new RelayCommand(_ => OpenOutputFolder(), _ => !string.IsNullOrWhiteSpace(OutputDirectory));
            _getRefreshTokenCommand = new RelayCommand(async _ => await FetchRefreshTokenAsync(), _ => !IsDownloading);

            LoadSettings();
            Directory.CreateDirectory(string.IsNullOrWhiteSpace(OutputDirectory) ? DefaultOutputDirectory : OutputDirectory);
            LoadRefreshToken();
        }

        private bool CanStartOrStop()
        {
            if (IsDownloading)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(ArtistId)
                && !string.IsNullOrWhiteSpace(RefreshToken)
                && !string.IsNullOrWhiteSpace(OutputDirectory);
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
            _cancellationTokenSource = new CancellationTokenSource();
            IsDownloading = true;
            SaveSettings();
            var artistId = (ArtistId ?? string.Empty).Trim();
            AppendLog($"开始抓取 Pixiv 画师 {artistId} 的作品...");

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

        private async Task FetchRefreshTokenAsync()
        {
            try
            {
                var codeVerifier = GenerateCodeVerifier();
                var codeChallenge = ComputeCodeChallenge(codeVerifier);
                var loginUrl = BuildLoginUrl(codeChallenge);

                AppendLog("打开 Pixiv 登录窗口...");
                var authWindow = new PixivAuthWindow(loginUrl)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                authWindow.Show();

                var code = await authWindow.WaitForCodeAsync();
                if (string.IsNullOrWhiteSpace(code))
                {
                    AppendLog("未获取到授权 code。");
                    return;
                }

                AppendLog("已获取 code，正在请求刷新令牌...");
                var tokenResponse = await ExchangeAuthCodeAsync(code, codeVerifier, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
                {
                    throw new InvalidOperationException("Pixiv 返回了空的 refresh_token。");
                }

                RefreshToken = tokenResponse.RefreshToken;
                _accessToken = tokenResponse.AccessToken;
                await SaveRefreshTokenAsync();
                AppendLog("刷新令牌已更新。");
            }
            catch (Exception ex)
            {
                AppendLog($"获取刷新令牌失败: {ex.Message}");
            }
        }

        public async Task RunPipelineAsync(PipelineContext context, CancellationToken token)
        {
            if (IsDownloading)
            {
                context.AppendLog("Pixiv下载器正在运行，已跳过管道执行。");
                return;
            }

            _pipelineLogger = context.AppendLog;
            IsDownloading = true;
            try
            {
                await DownloadAsync(token);
            }
            finally
            {
                IsDownloading = false;
                _pipelineLogger = null;
            }
        }

        private async Task DownloadAsync(CancellationToken token)
        {
            var artistId = (ArtistId ?? string.Empty).Trim();
            var refreshToken = (RefreshToken ?? string.Empty).Trim();
            var baseDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
                ? DefaultOutputDirectory
                : OutputDirectory.Trim();

            var artistDirectory = Path.Combine(baseDirectory, artistId);
            Directory.CreateDirectory(artistDirectory);
            AppendLog($"输出目录: {artistDirectory}");

            await RefreshAccessTokenAsync(refreshToken, token);

            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int savedCount = 0;

            foreach (var contentType in ContentTypes)
            {
                AppendLog($"开始抓取类型: {contentType}");
                string? nextUrl = null;
                var seenNextUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (!token.IsCancellationRequested)
                {
                    var response = await FetchUserIllustsAsync(artistId, contentType, nextUrl, token);
                    if (response == null || response.Illusts.Count == 0)
                    {
                        break;
                    }

                    foreach (var item in BuildDownloadItems(response.Illusts))
                    {
                        token.ThrowIfCancellationRequested();

                        if (!seenFiles.Add(item.FileName))
                        {
                            continue;
                        }

                        var targetPath = Path.Combine(artistDirectory, item.FileName);
                        if (File.Exists(targetPath))
                        {
                            AppendLog($"已存在，跳过 {item.FileName}");
                            continue;
                        }

                        AppendLog($"下载 {item.FileName}...");
                        if (await DownloadImageAsync(item.Url, targetPath, token))
                        {
                            savedCount++;
                            AppendLog($"已保存 {item.FileName}");
                        }

                        await Task.Delay(GetDownloadDelay(), token);
                    }

                    nextUrl = response.NextUrl;
                    if (string.IsNullOrWhiteSpace(nextUrl))
                    {
                        break;
                    }

                    if (!seenNextUrls.Add(nextUrl))
                    {
                        AppendLog("检测到重复的分页链接，已停止翻页。");
                        break;
                    }
                }
            }

            AppendLog(savedCount == 0 ? "没有可下载的图片。" : $"共保存 {savedCount} 张图片。");
        }

        private async Task<PixivTokenResponse> ExchangeAuthCodeAsync(string authCode, string codeVerifier, CancellationToken token)
        {
            var clientTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00");
            var clientHash = ComputeClientHash(clientTime);

            using var request = new HttpRequestMessage(HttpMethod.Post, AuthTokenUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", PixivUserAgent);
            request.Headers.TryAddWithoutValidation("App-OS", AppOs);
            request.Headers.TryAddWithoutValidation("App-OS-Version", AppOsVersion);
            request.Headers.TryAddWithoutValidation("X-Client-Time", clientTime);
            request.Headers.TryAddWithoutValidation("X-Client-Hash", clientHash);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["code"] = authCode,
                ["code_verifier"] = codeVerifier,
                ["grant_type"] = "authorization_code",
                ["include_policy"] = "true",
                ["redirect_uri"] = RedirectUri
            });

            using var response = await AuthClient.SendAsync(request, token);
            var payload = await response.Content.ReadAsStringAsync(token);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"获取刷新令牌失败: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var tokenResponse = JsonConvert.DeserializeObject<PixivTokenResponse>(payload);
            if (tokenResponse == null)
            {
                throw new InvalidOperationException("无法解析 Pixiv 的授权响应。");
            }

            return tokenResponse;
        }

        private async Task RefreshAccessTokenAsync(string refreshToken, CancellationToken token)
        {
            var normalizedToken = refreshToken.Trim();
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                throw new InvalidOperationException("刷新令牌为空。");
            }

            var clientTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00");
            var clientHash = ComputeClientHash(clientTime);

            using var request = new HttpRequestMessage(HttpMethod.Post, AuthTokenUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", PixivUserAgent);
            request.Headers.TryAddWithoutValidation("App-OS", AppOs);
            request.Headers.TryAddWithoutValidation("App-OS-Version", AppOsVersion);
            request.Headers.TryAddWithoutValidation("X-Client-Time", clientTime);
            request.Headers.TryAddWithoutValidation("X-Client-Hash", clientHash);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["get_secure_url"] = "1",
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = normalizedToken
            });

            using var response = await AuthClient.SendAsync(request, token);
            var payload = await response.Content.ReadAsStringAsync(token);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"获取 Token 失败: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var tokenResponse = JsonConvert.DeserializeObject<PixivTokenResponse>(payload);
            if (tokenResponse?.AccessToken == null)
            {
                throw new InvalidOperationException("Pixiv 返回了空的 access_token。");
            }

            _accessToken = tokenResponse.AccessToken;
            if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken) && tokenResponse.RefreshToken != RefreshToken)
            {
                RefreshToken = tokenResponse.RefreshToken;
            }

            await SaveRefreshTokenAsync();
            AppendLog("访问令牌已刷新。");
        }

        private async Task<PixivUserIllustsResponse?> FetchUserIllustsAsync(string artistId, string contentType, string? nextUrl, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                await RefreshAccessTokenAsync(RefreshToken?.Trim() ?? string.Empty, token);
            }

            var requestUrl = string.IsNullOrWhiteSpace(nextUrl)
                ? $"/v1/user/illusts?user_id={Uri.EscapeDataString(artistId)}&filter=for_ios&type={contentType}"
                : nextUrl;

            var (statusCode, payload) = await SendApiRequestAsync(requestUrl, token);

            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
            {
                AppendLog("访问令牌失效，尝试刷新...");
                await RefreshAccessTokenAsync(RefreshToken?.Trim() ?? string.Empty, token);
                (statusCode, payload) = await SendApiRequestAsync(requestUrl, token);
            }

            if (!IsSuccess(statusCode))
            {
                var preview = payload.Length > 200 ? payload[..200] + "..." : payload;
                throw new InvalidOperationException($"Pixiv API 返回 {statusCode}: {preview}");
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<PixivUserIllustsResponse>(payload);
        }

        private async Task<(HttpStatusCode StatusCode, string Payload)> SendApiRequestAsync(string requestUrl, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            var accessToken = _accessToken ?? throw new InvalidOperationException("访问令牌未初始化。");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.TryAddWithoutValidation("User-Agent", PixivUserAgent);
            request.Headers.TryAddWithoutValidation("App-OS", AppOs);
            request.Headers.TryAddWithoutValidation("App-OS-Version", AppOsVersion);

            using var response = await ApiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var payload = await response.Content.ReadAsStringAsync(token);
            return (response.StatusCode, payload);
        }

        private static bool IsSuccess(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return code >= 200 && code <= 299;
        }

        private IEnumerable<PixivDownloadItem> BuildDownloadItems(IEnumerable<PixivIllust> illusts)
        {
            foreach (var illust in illusts)
            {
                if (illust == null)
                {
                    continue;
                }

                if (string.Equals(illust.Type, "ugoira", StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog($"跳过动图作品 {illust.Id}");
                    continue;
                }

                var urls = new List<string>();
                if (illust.MetaPages != null && illust.MetaPages.Count > 0)
                {
                    foreach (var page in illust.MetaPages)
                    {
                        var url = page?.ImageUrls?.Original ?? page?.ImageUrls?.Large;
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            urls.Add(url);
                        }
                    }
                }
                else
                {
                    var url = illust.MetaSinglePage?.OriginalImageUrl
                              ?? illust.ImageUrls?.Original
                              ?? illust.ImageUrls?.Large;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        urls.Add(url);
                    }
                }

                int index = 0;
                foreach (var url in urls)
                {
                    var fileName = TryGetFileName(url);
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        fileName = $"{illust.Id}_p{index}.jpg";
                    }

                    if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                    {
                        fileName += ".jpg";
                    }

                    yield return new PixivDownloadItem(url, fileName);
                    index++;
                }
            }
        }

        private async Task<bool> DownloadImageAsync(string url, string targetPath, CancellationToken token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri("https://app-api.pixiv.net/");
                request.Headers.TryAddWithoutValidation("User-Agent", PixivUserAgent);

                using var response = await ImageClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode)
                {
                    AppendLog($"下载失败: {response.StatusCode}");
                    return false;
                }

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
                AppendLog($"下载失败: {ex.Message}");
                return false;
            }
        }

        private void LoadRefreshToken()
        {
            try
            {
                if (File.Exists(RefreshTokenFilePath))
                {
                    RefreshToken = File.ReadAllText(RefreshTokenFilePath).Trim();
                    if (!string.IsNullOrWhiteSpace(RefreshToken))
                    {
                        AppendLog("刷新令牌已加载。");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"加载刷新令牌失败: {ex.Message}");
            }
        }

        private async Task SaveRefreshTokenAsync()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(RefreshToken))
                {
                    Directory.CreateDirectory(RefreshTokenDirectory);
                    await File.WriteAllTextAsync(RefreshTokenFilePath, RefreshToken.Trim());
                }
            }
            catch (Exception ex)
            {
                AppendLog($"保存刷新令牌失败: {ex.Message}");
            }
        }

        private void OpenOutputFolder()
        {
            try
            {
                var rootDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
                    ? DefaultOutputDirectory
                    : OutputDirectory.Trim();
                if (string.IsNullOrWhiteSpace(rootDirectory))
                {
                    return;
                }

                var artistId = (ArtistId ?? string.Empty).Trim();
                var targetDirectory = string.IsNullOrWhiteSpace(artistId)
                    ? rootDirectory
                    : Path.Combine(rootDirectory, artistId);

                Directory.CreateDirectory(targetDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetDirectory,
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
            _pipelineLogger?.Invoke($"[Pixiv] {message}");
        }

        private static string ComputeClientHash(string clientTime)
        {
            using var md5 = MD5.Create();
            var input = Encoding.UTF8.GetBytes(clientTime + HashSecret);
            var hash = md5.ComputeHash(input);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }

        private static string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string ComputeCodeChallenge(string codeVerifier)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string BuildLoginUrl(string codeChallenge)
        {
            return $"{LoginUrl}?code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256&client=pixiv-android";
        }

        private TimeSpan GetDownloadDelay()
        {
            return TimeSpan.FromSeconds(DownloadIntervalSeconds);
        }

        private static string? TryGetFileName(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return Path.GetFileName(uri.LocalPath);
            }

            return null;
        }

        private static HttpClient CreateHttpClient(Uri? baseAddress = null)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            if (baseAddress != null)
            {
                client.BaseAddress = baseAddress;
            }

            return client;
        }

        private static void ConfigureApiClient(HttpClient client)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(PixivUserAgent);
            client.DefaultRequestHeaders.TryAddWithoutValidation("App-OS", AppOs);
            client.DefaultRequestHeaders.TryAddWithoutValidation("App-OS-Version", AppOsVersion);
            client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("zh-CN"));
        }

        private static void ConfigureImageClient(HttpClient client)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(PixivUserAgent);
            client.DefaultRequestHeaders.Referrer = new Uri("https://app-api.pixiv.net/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    DownloadIntervalSeconds = DefaultDownloadIntervalSeconds;
                    return;
                }

                var json = File.ReadAllText(SettingsFilePath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<PixivDownloaderSettings>(json, SettingsSerializerOptions);
                if (settings == null)
                {
                    return;
                }

                _isApplyingSettings = true;
                ArtistId = settings.ArtistId ?? string.Empty;
                OutputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory) ? DefaultOutputDirectory : settings.OutputDirectory;
                DownloadIntervalSeconds = settings.DownloadIntervalSeconds;
            }
            catch (Exception ex)
            {
                AppendLog($"加载 Pixiv 下载器配置失败: {ex.Message}");
            }
            finally
            {
                _isApplyingSettings = false;
            }
        }

        private void ScheduleSaveSettings()
        {
            if (_isApplyingSettings)
            {
                return;
            }

            _settingsDirty = true;

            _saveSettingsCts?.Cancel();
            _saveSettingsCts = new CancellationTokenSource();

            var token = _saveSettingsCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(400, token);
                    SaveSettings();
                }
                catch (OperationCanceledException)
                {

                }
                catch (Exception ex)
                {
                    AppendLog($"保存 Pixiv 下载器配置失败: {ex.Message}");
                }
            });
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);

                var settings = new PixivDownloaderSettings
                {
                    ArtistId = ArtistId?.Trim() ?? string.Empty,
                    OutputDirectory = OutputDirectory?.Trim() ?? string.Empty,
                    DownloadIntervalSeconds = DownloadIntervalSeconds
                };

                var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsSerializerOptions);
                var tempPath = SettingsFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, SettingsFilePath, overwrite: true);
                _settingsDirty = false;
            }
            catch (Exception ex)
            {
                AppendLog($"保存 Pixiv 下载器配置失败: {ex.Message}");
            }
        }

        public void SaveSettingsOnClose()
        {
            _saveSettingsCts?.Cancel();
            _saveSettingsCts?.Dispose();
            _saveSettingsCts = null;

            if (_settingsDirty)
            {
                SaveSettings();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed record PixivDownloadItem(string Url, string FileName);

        private sealed class PixivDownloaderSettings
        {
            public string? ArtistId { get; set; }
            public string? OutputDirectory { get; set; }
            public int DownloadIntervalSeconds { get; set; } = DefaultDownloadIntervalSeconds;
        }
    }
}
