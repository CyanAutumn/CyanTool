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
using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace CyanTools.ViewModel
{
    public enum DatasetDownloadMode
    {
        ArtistId,
        Tag,
        PostId
    }

    public sealed class DatasetDownloaderViewModel : INotifyPropertyChanged, IPipelineRunnableViewModel, IPipelineRuntimeOutputNode
    {
        private const string DanbooruBaseUrl = "https://danbooru.donmai.us";
        private const int DanbooruPageSize = 200;
        private const string KemonoApiBaseUrl = "https://kemono.cr";
        private const int KemonoPageSize = 50;
        private const int KemonoRetryCount = 3;
        private const string PixivApiBaseUrl = "https://app-api.pixiv.net";
        private const string PixivAuthTokenUrl = "https://oauth.secure.pixiv.net/auth/token";
        private const string PixivLoginUrl = "https://app-api.pixiv.net/web/v1/login";
        private const string PixivRedirectUri = "https://app-api.pixiv.net/web/v1/users/auth/pixiv/callback";
        private const string PixivClientId = "MOBrBDS8blbauoSck0ZfDbtuzpyT";
        private const string PixivClientSecret = "lsACyCD94FhDUtGTXi3QzcFE2uU1hqtDaKeqrdwj";
        private const string PixivHashSecret = "28c1fdd170a5204386cb1313c7077b34f83e4aaf4aa829ce78c231e05b0bae2c";
        private const string PixivUserAgent = "PixivIOSApp/7.13.3 (iOS 14.6; iPhone13,2)";
        private const string PixivAppOs = "ios";
        private const string PixivAppOsVersion = "14.6";
        private const string DefaultOutputDirectory = "./output_{time}_{id}_{name}";
        private const int DefaultPixivIntervalSeconds = 20;
        private const int ImageDownloadRetryCount = 3;
        private static readonly string SettingsDirectory = Path.Combine(AppContext.BaseDirectory, "Config");
        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "dataset_downloader.json");
        private static readonly string DanbooruTokenFilePath = Path.Combine(AppContext.BaseDirectory, "cookies.txt");
        private static readonly string PixivRefreshTokenDirectory = Path.Combine(AppContext.BaseDirectory, "Configs", "Crawler");
        private static readonly string PixivRefreshTokenFilePath = Path.Combine(PixivRefreshTokenDirectory, "pixiv_refresh_token.txt");
        private static readonly TimeSpan PixivAccessTokenRefreshLeadTime = TimeSpan.FromMinutes(5);
        private static readonly string[] PixivContentTypes = { "illust", "manga" };

        private static readonly JsonSerializerOptions SettingsSerializerOptions = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };

        private static readonly HttpClient PixivAuthClient;
        private static readonly HttpClient PixivApiClient;
        private static readonly HttpClient PixivImageClient;
        private static readonly HttpClient KemonoHttpClient;

        static DatasetDownloaderViewModel()
        {
            PixivAuthClient = CreateHttpClient();
            PixivApiClient = CreateHttpClient(new Uri(PixivApiBaseUrl));
            PixivImageClient = CreateHttpClient();
            ConfigurePixivApiClient(PixivApiClient);
            ConfigurePixivImageClient(PixivImageClient);

            var kemonoHandler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };
            KemonoHttpClient = new HttpClient(kemonoHandler)
            {
                BaseAddress = new Uri(KemonoApiBaseUrl),
                Timeout = TimeSpan.FromSeconds(60)
            };
            KemonoHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CyanToolsDatasetDownloader/1.0");
            KemonoHttpClient.DefaultRequestHeaders.Accept.Clear();
            KemonoHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/css"));
            KemonoHttpClient.DefaultRequestHeaders.AcceptEncoding.Clear();
            KemonoHttpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            KemonoHttpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            KemonoHttpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        }

        private readonly RelayCommand _startStopCommand;
        private readonly RelayCommand _clearLogCommand;
        private readonly RelayCommand _browseOutputFolderCommand;
        private readonly RelayCommand _getPixivRefreshTokenCommand;
        private readonly RelayCommand _openDanbooruManualCollectorCommand;
        private readonly RestClient _danbooruClient;

        private CancellationTokenSource? _cancellationTokenSource;
        private readonly RemoteRequestPacer _danbooruRequestPacer = new();
        private readonly RemoteRequestPacer _pixivRequestPacer = new();
        private readonly RemoteRequestPacer _kemonoRequestPacer = new();
        private PipelineRuntimeOutputDirectoryValidator.RunOutputRootSnapshot? _activeRunOutputRootSnapshot;
        private Action<string>? _pipelineLogSink;
        private string? _pixivAccessToken;
        private DateTimeOffset _pixivAccessTokenExpiresAtUtc = DateTimeOffset.MinValue;
        private string _runtimeOutputPath = string.Empty;
        private string _inputText = string.Empty;
        private string _danbooruToken = string.Empty;
        private string _pixivRefreshToken = string.Empty;
        private string _outputDirectory = DefaultOutputDirectory;
        private string _logText = string.Empty;
        private string _danbooruDownloadSummary = "Danbooru：等待下载";
        private string _pixivDownloadSummary = "Pixiv：等待下载";
        private string _kemonoDownloadSummary = "Kemono：等待下载";
        private string _outputDirectoryImageTotalSummary = "输出目录总计：等待下载";
        private int _downloadIntervalSeconds = DefaultPixivIntervalSeconds;
        private bool _enableDanbooru = true;
        private bool _enablePixiv = true;
        private bool _enableKemono = true;
        private bool _includeArtist;
        private bool _includeCopyright;
        private bool _includeCharacter = true;
        private bool _includeGeneral = true;
        private bool _includeMeta = true;
        private bool _isDownloading;
        private DatasetDownloadMode _selectedMode = DatasetDownloadMode.ArtistId;
        private string _sessionOutputIdSegment = "unknown";
        private string _sessionOutputArtistSegment = "unknown_name";
        private string _lastDanbooruListUrl = $"{DanbooruBaseUrl}/posts?tags=";

        public DatasetDownloaderViewModel()
        {
            _danbooruClient = new RestClient(new RestClientOptions(DanbooruBaseUrl) { MaxTimeout = -1 });
            _startStopCommand = new RelayCommand(async _ => await StartOrStopAsync(), _ => CanStartOrStop());
            _clearLogCommand = new RelayCommand(_ => ClearLog());
            _browseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder());
            _getPixivRefreshTokenCommand = new RelayCommand(async _ => await FetchPixivRefreshTokenAsync(), _ => !IsDownloading);
            _openDanbooruManualCollectorCommand = new RelayCommand(async _ => await OpenDanbooruManualCollectorAsync(), _ => !IsDownloading);

            LoadSettings();
            LoadDanbooruToken();
            LoadPixivRefreshToken();
        }

        public DatasetDownloadMode SelectedMode
        {
            get => _selectedMode;
            set
            {
                if (_selectedMode == value)
                {
                    return;
                }

                _selectedMode = value;
                if (IsTagOrPostMode)
                {
                    EnablePixiv = false;
                    EnableKemono = false;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsArtistMode));
                OnPropertyChanged(nameof(IsTagMode));
                OnPropertyChanged(nameof(IsPostIdMode));
                OnPropertyChanged(nameof(IsTagOrPostMode));
                OnPropertyChanged(nameof(CanSelectAllSources));
                _startStopCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsArtistMode
        {
            get => SelectedMode == DatasetDownloadMode.ArtistId;
            set
            {
                if (value)
                {
                    SelectedMode = DatasetDownloadMode.ArtistId;
                }
            }
        }

        public bool IsTagMode
        {
            get => SelectedMode == DatasetDownloadMode.Tag;
            set
            {
                if (value)
                {
                    SelectedMode = DatasetDownloadMode.Tag;
                }
            }
        }

        public bool IsPostIdMode
        {
            get => SelectedMode == DatasetDownloadMode.PostId;
            set
            {
                if (value)
                {
                    SelectedMode = DatasetDownloadMode.PostId;
                }
            }
        }

        public bool IsTagOrPostMode => SelectedMode != DatasetDownloadMode.ArtistId;

        public bool CanSelectAllSources => !IsTagOrPostMode;

        public bool EnableDanbooru
        {
            get => _enableDanbooru;
            set
            {
                if (_enableDanbooru != value)
                {
                    _enableDanbooru = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanEditDanbooruSettings));
                    _startStopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanEditDanbooruSettings => EnableDanbooru;

        public bool EnablePixiv
        {
            get => _enablePixiv;
            set
            {
                var normalized = IsTagOrPostMode ? false : value;
                if (_enablePixiv != normalized)
                {
                    _enablePixiv = normalized;
                    OnPropertyChanged();
                    _startStopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool EnableKemono
        {
            get => _enableKemono;
            set
            {
                var normalized = IsTagOrPostMode ? false : value;
                if (_enableKemono != normalized)
                {
                    _enableKemono = normalized;
                    OnPropertyChanged();
                    _startStopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string InputText
        {
            get => _inputText;
            set
            {
                var sanitized = value?.Trim() ?? string.Empty;
                if (_inputText != sanitized)
                {
                    _inputText = sanitized;
                    OnPropertyChanged();
                    _startStopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string DanbooruToken
        {
            get => _danbooruToken;
            set
            {
                if (_danbooruToken != value)
                {
                    _danbooruToken = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public bool IncludeArtist
        {
            get => _includeArtist;
            set
            {
                if (_includeArtist != value)
                {
                    _includeArtist = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IncludeCopyright
        {
            get => _includeCopyright;
            set
            {
                if (_includeCopyright != value)
                {
                    _includeCopyright = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IncludeCharacter
        {
            get => _includeCharacter;
            set
            {
                if (_includeCharacter != value)
                {
                    _includeCharacter = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IncludeGeneral
        {
            get => _includeGeneral;
            set
            {
                if (_includeGeneral != value)
                {
                    _includeGeneral = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IncludeMeta
        {
            get => _includeMeta;
            set
            {
                if (_includeMeta != value)
                {
                    _includeMeta = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PixivRefreshToken
        {
            get => _pixivRefreshToken;
            set
            {
                if (_pixivRefreshToken != value)
                {
                    _pixivRefreshToken = value ?? string.Empty;
                    OnPropertyChanged();
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
                    _startStopCommand.RaiseCanExecuteChanged();
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

        public string DanbooruDownloadSummary => _danbooruDownloadSummary;

        public string PixivDownloadSummary => _pixivDownloadSummary;

        public string KemonoDownloadSummary => _kemonoDownloadSummary;

        public string OutputDirectoryImageTotalSummary => _outputDirectoryImageTotalSummary;

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
                    _getPixivRefreshTokenCommand.RaiseCanExecuteChanged();
                    _openDanbooruManualCollectorCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StartButtonText => IsDownloading ? "停止" : "开始下载";

        public ICommand StartStopCommand => _startStopCommand;
        public ICommand ClearLogCommand => _clearLogCommand;
        public ICommand BrowseOutputFolderCommand => _browseOutputFolderCommand;
        public ICommand GetPixivRefreshTokenCommand => _getPixivRefreshTokenCommand;
        public ICommand OpenDanbooruManualCollectorCommand => _openDanbooruManualCollectorCommand;

        private bool CanStartOrStop()
        {
            if (IsDownloading)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(InputText)
                && !string.IsNullOrWhiteSpace(OutputDirectory)
                && (EnableDanbooru || EnablePixiv || EnableKemono);
        }

        private void ResetRemoteRequestPacing()
        {
            _danbooruRequestPacer.Reset();
            _pixivRequestPacer.Reset();
            _kemonoRequestPacer.Reset();
        }

        private Task WaitForNextRemoteRequestAsync(RemoteRequestPacer pacer, CancellationToken token)
        {
            return pacer.WaitAsync(DownloadIntervalSeconds, token);
        }

        private async Task StartOrStopAsync()
        {
            if (IsDownloading)
            {
                _cancellationTokenSource?.Cancel();
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            IsDownloading = true;
            try
            {
                await ExecuteDownloadAsync(
                    _cancellationTokenSource.Token,
                    pipelineLog: null,
                    rethrowErrors: false,
                    validateWorkflowOutput: false);
                AppendLog("所有任务结束。");
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
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
                IsDownloading = false;
            }
        }

        public void ResetRuntimeOutputPath()
        {
            _runtimeOutputPath = string.Empty;
        }

        public bool TryGetRuntimeOutputPath(out string path)
        {
            path = _runtimeOutputPath;
            return !string.IsNullOrWhiteSpace(path);
        }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            return ExecuteDownloadAsync(
                token,
                message => context.AppendLog($"[数据集下载器] {message}"),
                rethrowErrors: true,
                validateWorkflowOutput: true);
        }

        private async Task FetchPixivRefreshTokenAsync()
        {
            try
            {
                var codeVerifier = GeneratePixivCodeVerifier();
                var codeChallenge = ComputePixivCodeChallenge(codeVerifier);
                var loginUrl = BuildPixivLoginUrl(codeChallenge);

                AppendLog("[Pixiv] 打开登录窗口...");
                var authWindow = new PixivAuthWindow(loginUrl)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                authWindow.Show();

                var code = await authWindow.WaitForCodeAsync();
                if (string.IsNullOrWhiteSpace(code))
                {
                    AppendLog("[Pixiv] 未获取到授权 code。");
                    return;
                }

                var tokenResponse = await ExchangePixivAuthCodeAsync(code, codeVerifier, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
                {
                    throw new InvalidOperationException("Pixiv 返回空 refresh_token。");
                }

                PixivRefreshToken = tokenResponse.RefreshToken;
                _pixivAccessToken = tokenResponse.AccessToken;
                UpdatePixivAccessTokenExpiry(tokenResponse);
                await SavePixivRefreshTokenAsync();
                AppendLog("[Pixiv] 刷新令牌已更新。");
            }
            catch (Exception ex)
            {
                AppendLog($"[Pixiv] 获取刷新令牌失败: {ex.Message}");
            }
        }

        private async Task OpenDanbooruManualCollectorAsync()
        {
            try
            {
                var startUrl = BuildDanbooruManualCollectorStartUrl();
                var collectorWindow = new DanbooruManualCollectorWindow(startUrl)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var result = collectorWindow.ShowDialog();
                if (result != true)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(collectorWindow.LastListUrl))
                {
                    _lastDanbooruListUrl = collectorWindow.LastListUrl;
                }

                if (collectorWindow.CollectedPostIds.Count == 0)
                {
                    AppendLog("[Danbooru] 手动获取未返回任何帖子 ID。");
                    return;
                }

                SelectedMode = DatasetDownloadMode.PostId;
                EnableDanbooru = true;
                InputText = string.Join(" ", collectorWindow.CollectedPostIds);
                AppendLog($"[Danbooru] 手动获取完成，检测到 {collectorWindow.CollectedPostIds.Count} 个帖子 ID，开始按请求间隔下载。");

                await StartOrStopAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"[Danbooru] 打开手动获取窗口失败: {ex.Message}");
            }
        }

        private string BuildDanbooruManualCollectorStartUrl()
        {
            if (SelectedMode == DatasetDownloadMode.Tag && !string.IsNullOrWhiteSpace(InputText))
            {
                return $"{DanbooruBaseUrl}/posts?tags={Uri.EscapeDataString(InputText)}";
            }

            if (!string.IsNullOrWhiteSpace(_lastDanbooruListUrl))
            {
                return _lastDanbooruListUrl;
            }

            return $"{DanbooruBaseUrl}/posts?tags=";
        }

        private async Task<PixivTokenResponse> ExchangePixivAuthCodeAsync(string authCode, string codeVerifier, CancellationToken token)
        {
            var clientTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00");
            var clientHash = ComputePixivClientHash(clientTime);

            using var request = new HttpRequestMessage(HttpMethod.Post, PixivAuthTokenUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", PixivUserAgent);
            request.Headers.TryAddWithoutValidation("App-OS", PixivAppOs);
            request.Headers.TryAddWithoutValidation("App-OS-Version", PixivAppOsVersion);
            request.Headers.TryAddWithoutValidation("X-Client-Time", clientTime);
            request.Headers.TryAddWithoutValidation("X-Client-Hash", clientHash);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = PixivClientId,
                ["client_secret"] = PixivClientSecret,
                ["code"] = authCode,
                ["code_verifier"] = codeVerifier,
                ["grant_type"] = "authorization_code",
                ["include_policy"] = "true",
                ["redirect_uri"] = PixivRedirectUri
            });

            await WaitForNextRemoteRequestAsync(_pixivRequestPacer, token);
            using var response = await PixivAuthClient.SendAsync(request, token);
            var payload = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Pixiv 授权失败: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var tokenResponse = JsonConvert.DeserializeObject<PixivTokenResponse>(payload);
            if (tokenResponse == null)
            {
                throw new InvalidOperationException("无法解析 Pixiv 授权响应。");
            }

            return tokenResponse;
        }

        private async Task ExecuteDownloadAsync(
            CancellationToken token,
            Action<string>? pipelineLog,
            bool rethrowErrors,
            bool validateWorkflowOutput)
        {
            var input = (InputText ?? string.Empty).Trim();
            ResetRuntimeOutputPath();
            ResetRemoteRequestPacing();

            Action<string>? previousPipelineLogSink = _pipelineLogSink;
            PipelineRuntimeOutputDirectoryValidator.RunOutputRootSnapshot? previousRunOutputRootSnapshot = _activeRunOutputRootSnapshot;
            _pipelineLogSink = pipelineLog;
            _activeRunOutputRootSnapshot = new PipelineRuntimeOutputDirectoryValidator.RunOutputRootSnapshot();

            try
            {
                if (string.IsNullOrWhiteSpace(input))
                {
                    const string emptyInputMessage = "请输入 ID/标签。";
                    AppendLog(emptyInputMessage);
                    if (rethrowErrors)
                    {
                        throw new InvalidOperationException(emptyInputMessage);
                    }

                    return;
                }

                PrepareSessionOutputSegments(input);

                string resolvedOutputRoot = ResolveCurrentRunOutputRoot();
                pipelineLog?.Invoke($"解析后的输出路径: {resolvedOutputRoot}");

                if (!HasSelectedTasks())
                {
                    const string noTasksMessage = "没有可执行的来源任务。";
                    AppendLog(noTasksMessage);
                    if (rethrowErrors)
                    {
                        throw new InvalidOperationException(noTasksMessage);
                    }

                    return;
                }

                ResetDownloadSummaries();
                try
                {
                    await RunSelectedTasksAsync(input, token);
                }
                finally
                {
                    SetOutputDirectoryImageTotalSummary(BuildOutputDirectoryImageTotalSummary(resolvedOutputRoot));
                }

                if (!validateWorkflowOutput)
                {
                    return;
                }

                try
                {
                    _runtimeOutputPath = PipelineRuntimeOutputDirectoryValidator.ValidateAndGetPublishPath(
                        resolvedOutputRoot,
                        "数据集下载器");
                }
                catch (DirectoryNotFoundException ex)
                {
                    pipelineLog?.Invoke(ex.Message);
                    throw;
                }
                catch (InvalidOperationException ex)
                {
                    pipelineLog?.Invoke(ex.Message);
                    throw;
                }
            }
            finally
            {
                _activeRunOutputRootSnapshot = previousRunOutputRootSnapshot;
                _pipelineLogSink = previousPipelineLogSink;
            }
        }

        private bool HasSelectedTasks()
        {
            return EnableDanbooru || (EnablePixiv && !IsTagOrPostMode) || (EnableKemono && !IsTagOrPostMode);
        }

        private void PrepareSessionOutputSegments(string input)
        {
            _sessionOutputIdSegment = SanitizePathSegment(input, "unknown");
            _sessionOutputArtistSegment = SanitizePathSegment(input, "unknown_artist");
        }

        private async Task RunSelectedTasksAsync(string input, CancellationToken token)
        {
            var tasks = new List<Task>();
            if (EnableDanbooru)
            {
                tasks.Add(RunSourceIndependentlyAsync(
                    "Danbooru",
                    () => RunDanbooruAsync(input, token),
                    SetDanbooruDownloadSummary,
                    token));
            }

            if (EnablePixiv && !IsTagOrPostMode)
            {
                tasks.Add(RunSourceIndependentlyAsync(
                    "Pixiv",
                    () => RunPixivAsync(input, token),
                    SetPixivDownloadSummary,
                    token));
            }

            if (EnableKemono && !IsTagOrPostMode)
            {
                tasks.Add(RunSourceIndependentlyAsync(
                    "Kemono",
                    () => RunKemonoAsync(input, token),
                    SetKemonoDownloadSummary,
                    token));
            }

            await Task.WhenAll(tasks);
        }

        private async Task RunSourceIndependentlyAsync(
            string source,
            Func<Task> runSource,
            Action<string> setSummary,
            CancellationToken token)
        {
            try
            {
                await runSource();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppendLog($"[{source}] 任务失败: {ex.Message}");
                setSummary($"{source}：任务失败（{ex.Message}）");
            }
        }

        private async Task RunDanbooruAsync(string input, CancellationToken token)
        {
            AppendLog("[Danbooru] 任务开始。");
            SetDanbooruDownloadSummary("Danbooru：正在获取下载清单…");
            var counters = new SourceDownloadCounters(progress => SetDanbooruDownloadSummary(BuildDownloadSummary("Danbooru", progress)));
            string? outputDirectory = null;

            try
            {
                switch (SelectedMode)
                {
                    case DatasetDownloadMode.ArtistId:
                    {
                        var artistTag = await ResolveDanbooruArtistTagByPixivUserIdAsync(input, token);
                        if (string.IsNullOrWhiteSpace(artistTag))
                        {
                            AppendLog($"[Danbooru] 未找到 Pixiv 画师 ID {input} 对应 artist 标签。");
                            return;
                        }

                        AppendLog($"[Danbooru] 画师 ID {input} -> artist 标签 {artistTag}");
                        await SetDanbooruPlannedTotalAsync(artistTag, counters, token);
                        outputDirectory = ResolveSourceOutputDirectory("Danbooru", SanitizePathSegment(input, "artist"));
                        Directory.CreateDirectory(outputDirectory);
                        await DownloadDanbooruByTagsAsync(artistTag, outputDirectory, counters, token);
                        break;
                    }
                    case DatasetDownloadMode.Tag:
                    {
                        await SetDanbooruPlannedTotalAsync(input, counters, token);
                        outputDirectory = ResolveSourceOutputDirectory("Danbooru", SanitizePathSegment(input, "tag"));
                        Directory.CreateDirectory(outputDirectory);
                        await DownloadDanbooruByTagsAsync(input, outputDirectory, counters, token);
                        break;
                    }
                    case DatasetDownloadMode.PostId:
                    {
                        var ids = ParseIdentifiers(input);
                        if (ids.Count == 0)
                        {
                            AppendLog("[Danbooru] 帖子 ID 为空。");
                            return;
                        }

                        counters.SetTotal(ids.Count);
                        AppendLog($"[Danbooru] 已发现 {ids.Count} 张待处理图片。");
                        outputDirectory = ResolveSourceOutputDirectory("Danbooru", "posts");
                        Directory.CreateDirectory(outputDirectory);
                        foreach (var id in ids)
                        {
                            token.ThrowIfCancellationRequested();
                            counters.Record(await DownloadDanbooruByPostIdAsync(id, outputDirectory, token));
                        }
                        break;
                    }
                }
            }
            finally
            {
                SetDanbooruDownloadSummary(BuildDownloadSummary("Danbooru", counters));
                AppendLog("[Danbooru] 任务完成。");
            }
        }

        private async Task SetDanbooruPlannedTotalAsync(string tags, SourceDownloadCounters counters, CancellationToken token)
        {
            var total = await FetchDanbooruPostCountAsync(tags, token);
            if (total.HasValue)
            {
                counters.SetTotal(total.Value);
                AppendLog($"[Danbooru] 已发现 {total.Value} 个匹配帖子。");
            }
            else
            {
                AppendLog("[Danbooru] 无法获取匹配总数，将继续下载并显示已处理数量。");
            }
        }

        private async Task<int?> FetchDanbooruPostCountAsync(string tags, CancellationToken token)
        {
            var request = new RestRequest($"/counts/posts.json?tags={Uri.EscapeDataString(tags)}", Method.Get);
            await WaitForNextRemoteRequestAsync(_danbooruRequestPacer, token);
            var response = await _danbooruClient.ExecuteAsync(request, token);
            if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
            {
                return null;
            }

            try
            {
                var value = JToken.Parse(response.Content).SelectToken("counts.posts")?.Value<long?>();
                if (!value.HasValue || value.Value < 0 || value.Value > int.MaxValue)
                {
                    return null;
                }

                return (int)value.Value;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }

        private async Task DownloadDanbooruByTagsAsync(
            string tags,
            string outputDirectory,
            SourceDownloadCounters counters,
            CancellationToken token)
        {
            int page = 1;
            var seenPostIds = new HashSet<int>();
            while (!token.IsCancellationRequested)
            {
                var request = new RestRequest($"/posts.json?format=json&tags={Uri.EscapeDataString(tags)}&limit={DanbooruPageSize}&page={page}", Method.Get);
                await WaitForNextRemoteRequestAsync(_danbooruRequestPacer, token);
                var response = await _danbooruClient.ExecuteAsync(request, token);
                if (!response.IsSuccessful)
                {
                    AppendLog($"[Danbooru] 请求失败: {response.StatusCode} - {response.ErrorMessage}");
                    break;
                }

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    break;
                }

                var posts = ParseDanbooruPosts(response.Content);
                if (posts.Count == 0)
                {
                    break;
                }

                var newPosts = posts.Where(p => p.PostId > 0 && seenPostIds.Add(p.PostId)).ToList();
                if (newPosts.Count == 0)
                {
                    break;
                }

                foreach (var post in newPosts)
                {
                    token.ThrowIfCancellationRequested();
                    counters.Record(await DownloadDanbooruPostAsync(post, outputDirectory, token));
                }

                page++;
            }
        }

        private async Task<DownloadOutcome> DownloadDanbooruByPostIdAsync(string postId, string outputDirectory, CancellationToken token)
        {
            var request = new RestRequest($"/posts/{Uri.EscapeDataString(postId)}.json", Method.Get);
            await WaitForNextRemoteRequestAsync(_danbooruRequestPacer, token);
            var response = await _danbooruClient.ExecuteAsync(request, token);
            if (!response.IsSuccessful)
            {
                AppendLog($"[Danbooru] 获取帖子 {postId} 失败: {response.StatusCode} - {response.ErrorMessage}");
                return DownloadOutcome.None;
            }

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                AppendLog($"[Danbooru] 帖子 {postId} 响应为空。");
                return DownloadOutcome.None;
            }

            var post = ParseDanbooruSingle(response.Content);
            if (post == null)
            {
                AppendLog($"[Danbooru] 解析帖子 {postId} 失败。");
                return DownloadOutcome.None;
            }

            return await DownloadDanbooruPostAsync(post, outputDirectory, token);
        }

        private async Task<DownloadOutcome> DownloadDanbooruPostAsync(BooruPostInfo post, string outputDirectory, CancellationToken token)
        {
            var fileUrl = NormalizeDanbooruFileUrl(post.FileUrl);
            if (string.IsNullOrWhiteSpace(fileUrl))
            {
                return DownloadOutcome.None;
            }

            var fileName = Path.GetFileName(new Uri(fileUrl).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"danbooru_{DateTime.UtcNow.Ticks}.bin";
            }

            var imagePath = Path.Combine(outputDirectory, fileName);
            var txtPath = Path.Combine(outputDirectory, Path.ChangeExtension(fileName, ".txt"));

            if (File.Exists(imagePath) && ShouldSkipExistingDownloadedFile(imagePath, "Danbooru", fileName))
            {
                AppendLog($"[Danbooru] 已存在，跳过 {fileName}");
                if (!File.Exists(txtPath))
                {
                    await SaveDanbooruTagsTxtAsync(post, txtPath, token);
                }
                return DownloadOutcome.Skipped;
            }

            var request = new RestRequest(fileUrl, Method.Get);
            if (!string.IsNullOrWhiteSpace(DanbooruToken))
            {
                request.AddHeader("Cookie", DanbooruToken);
            }

            for (int attempt = 1; attempt <= ImageDownloadRetryCount; attempt++)
            {
                await WaitForNextRemoteRequestAsync(_danbooruRequestPacer, token);
                var response = await _danbooruClient.ExecuteAsync(request, token);
                if (!response.IsSuccessful || response.RawBytes == null || response.RawBytes.Length == 0)
                {
                    if (attempt < ImageDownloadRetryCount)
                    {
                        continue;
                    }

                    AppendLog($"[Danbooru] 下载失败: {response.StatusCode} - {response.ErrorMessage}");
                    return DownloadOutcome.None;
                }

                await File.WriteAllBytesAsync(imagePath, response.RawBytes, token);
                if (!ValidateDownloadedFileOrDelete(imagePath, "Danbooru", fileName, attempt, ImageDownloadRetryCount))
                {
                    if (attempt >= ImageDownloadRetryCount)
                    {
                        return DownloadOutcome.None;
                    }

                    continue;
                }

                await SaveDanbooruTagsTxtAsync(post, txtPath, token);
                AppendLog($"[Danbooru] 已保存 {fileName}");
                return DownloadOutcome.Saved;
            }

            return DownloadOutcome.None;
        }

        private async Task SaveDanbooruTagsTxtAsync(BooruPostInfo post, string txtPath, CancellationToken token)
        {
            var tags = BuildDanbooruTagsForTxt(post);
            if (string.IsNullOrWhiteSpace(tags))
            {
                return;
            }

            await File.WriteAllTextAsync(txtPath, tags, token);
        }

        private string BuildDanbooruTagsForTxt(BooruPostInfo post)
        {
            var selected = new List<string>();
            if (IncludeArtist && !string.IsNullOrWhiteSpace(post.ArtistTags))
            {
                selected.Add(post.ArtistTags);
            }

            if (IncludeCopyright && !string.IsNullOrWhiteSpace(post.CopyrightTags))
            {
                selected.Add(post.CopyrightTags);
            }

            if (IncludeCharacter && !string.IsNullOrWhiteSpace(post.CharacterTags))
            {
                selected.Add(post.CharacterTags);
            }

            if (IncludeGeneral && !string.IsNullOrWhiteSpace(post.GeneralTags))
            {
                selected.Add(post.GeneralTags);
            }

            if (IncludeMeta && !string.IsNullOrWhiteSpace(post.MetaTags))
            {
                selected.Add(post.MetaTags);
            }

            if (selected.Count == 0)
            {
                return string.Empty;
            }

            var parts = string.Join(" ", selected).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(",", parts);
        }

        private static List<BooruPostInfo> ParseDanbooruPosts(string content)
        {
            var posts = JsonConvert.DeserializeObject<List<Danbooru.Post>>(content) ?? new List<Danbooru.Post>();
            return posts.Select(MapDanbooruPost).ToList();
        }

        private static BooruPostInfo? ParseDanbooruSingle(string content)
        {
            var post = JsonConvert.DeserializeObject<Danbooru.Post>(content);
            return post == null ? null : MapDanbooruPost(post);
        }

        private static BooruPostInfo MapDanbooruPost(Danbooru.Post post)
        {
            return new BooruPostInfo
            {
                PostId = post.id,
                FileUrl = GetBestDanbooruFileUrl(post),
                ArtistTags = post.tag_string_artist,
                CopyrightTags = post.tag_string_copyright,
                CharacterTags = post.tag_string_character,
                GeneralTags = post.tag_string_general,
                MetaTags = post.tag_string_meta
            };
        }

        private static string? GetBestDanbooruFileUrl(Danbooru.Post post)
        {
            if (!string.IsNullOrWhiteSpace(post.file_url))
            {
                return post.file_url;
            }

            var originalVariant = post.media_asset?.variants?
                .FirstOrDefault(v => string.Equals(v.type, "original", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(originalVariant?.url))
            {
                return originalVariant.url;
            }

            if (!string.IsNullOrWhiteSpace(post.large_file_url))
            {
                return post.large_file_url;
            }

            return null;
        }

        private static string? NormalizeDanbooruFileUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            {
                return absolute.ToString();
            }

            if (Uri.TryCreate(new Uri(DanbooruBaseUrl), url, out var resolved))
            {
                return resolved.ToString();
            }

            return url;
        }

        private async Task<string?> ResolveDanbooruArtistTagByPixivUserIdAsync(string pixivUserId, CancellationToken token)
        {
            try
            {
                var pattern = $"*pixiv.net/users/{pixivUserId}*";
                var request = new RestRequest($"/artists.json?search[url_matches]={Uri.EscapeDataString(pattern)}", Method.Get);
                await WaitForNextRemoteRequestAsync(_danbooruRequestPacer, token);
                var response = await _danbooruClient.ExecuteAsync(request, token);
                if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                {
                    return null;
                }

                var artists = JArray.Parse(response.Content);
                if (artists.Count == 0)
                {
                    return null;
                }

                return (artists[0] as JObject)?["name"]?.ToString();
            }
            catch (Exception ex)
            {
                AppendLog($"[Danbooru] 解析 artist 标签失败: {ex.Message}");
                return null;
            }
        }

        private async Task RunPixivAsync(string artistId, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(PixivRefreshToken))
            {
                AppendLog("[Pixiv] 刷新令牌为空，已跳过。");
                SetPixivDownloadSummary("Pixiv：未配置刷新令牌");
                return;
            }

            var outputDirectory = ResolveSourceOutputDirectory("Pixiv", SanitizePathSegment(artistId, "unknown_artist"));
            Directory.CreateDirectory(outputDirectory);
            AppendLog($"[Pixiv] 输出目录: {outputDirectory}");
            SetPixivDownloadSummary("Pixiv：正在获取下载清单…");

            var counters = new SourceDownloadCounters(progress => SetPixivDownloadSummary(BuildDownloadSummary("Pixiv", progress)));
            var existingPixivFiles = LoadExistingPixivImageKeys(outputDirectory);

            try
            {
                await RefreshPixivAccessTokenAsync(token);
                var downloadPlan = await BuildPixivDownloadPlanAsync(artistId, token);
                counters.SetTotal(downloadPlan.Count);
                AppendLog($"[Pixiv] 已发现 {downloadPlan.Count} 张远端图片。");

                foreach (var item in downloadPlan)
                {
                    token.ThrowIfCancellationRequested();
                    var targetPath = Path.Combine(outputDirectory, item.FileName);
                    if (File.Exists(targetPath))
                    {
                        if (ShouldSkipExistingDownloadedFile(targetPath, "Pixiv", item.FileName))
                        {
                            counters.Record(DownloadOutcome.Skipped);
                            existingPixivFiles.Add(item.FileKey);
                            continue;
                        }

                        existingPixivFiles.Remove(item.FileKey);
                    }

                    if (existingPixivFiles.Contains(item.FileKey))
                    {
                        counters.Record(DownloadOutcome.Skipped);
                        continue;
                    }

                    if (await DownloadPixivImageAsync(item.Url, targetPath, token))
                    {
                        counters.Record(DownloadOutcome.Saved);
                        existingPixivFiles.Add(item.FileKey);
                        AppendLog($"[Pixiv] 已保存 {item.FileName}");
                    }
                    else
                    {
                        counters.Record(DownloadOutcome.None);
                    }

                }
            }
            finally
            {
                AppendPixivSummary(counters.Saved, counters.Skipped);
                SetPixivDownloadSummary(BuildDownloadSummary("Pixiv", counters));
            }
        }

        private async Task<List<PixivDownloadItem>> BuildPixivDownloadPlanAsync(string artistId, CancellationToken token)
        {
            var plan = new List<PixivDownloadItem>();
            var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var contentType in PixivContentTypes)
            {
                string? nextUrl = null;
                var seenNextUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (!token.IsCancellationRequested)
                {
                    var response = await FetchPixivUserIllustsAsync(artistId, contentType, nextUrl, token);
                    if (response == null || response.Illusts.Count == 0)
                    {
                        break;
                    }

                    foreach (var item in BuildPixivDownloadItems(response.Illusts))
                    {
                        if (seenFileNames.Add(item.FileName))
                        {
                            plan.Add(item);
                        }
                    }

                    nextUrl = response.NextUrl;
                    if (string.IsNullOrWhiteSpace(nextUrl) || !seenNextUrls.Add(nextUrl))
                    {
                        break;
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            return plan;
        }

        private void AppendPixivSummary(int savedCount, int skippedCount)
        {
            if (savedCount == 0 && skippedCount == 0)
            {
                AppendLog("[Pixiv] 没有可下载图片。");
            }
            else if (savedCount == 0)
            {
                AppendLog($"[Pixiv] 没有新图片，跳过 {skippedCount} 张已存在图片。");
            }
            else if (skippedCount == 0)
            {
                AppendLog($"[Pixiv] 共保存 {savedCount} 张图片。");
            }
            else
            {
                AppendLog($"[Pixiv] 共保存 {savedCount} 张图片，跳过 {skippedCount} 张已存在图片。");
            }
        }

        private async Task RefreshPixivAccessTokenAsync(CancellationToken token)
        {
            var normalizedRefreshToken = (PixivRefreshToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedRefreshToken))
            {
                throw new InvalidOperationException("Pixiv 刷新令牌为空。");
            }

            var clientTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00");
            var clientHash = ComputePixivClientHash(clientTime);

            using var request = new HttpRequestMessage(HttpMethod.Post, PixivAuthTokenUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", PixivUserAgent);
            request.Headers.TryAddWithoutValidation("App-OS", PixivAppOs);
            request.Headers.TryAddWithoutValidation("App-OS-Version", PixivAppOsVersion);
            request.Headers.TryAddWithoutValidation("X-Client-Time", clientTime);
            request.Headers.TryAddWithoutValidation("X-Client-Hash", clientHash);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["get_secure_url"] = "1",
                ["client_id"] = PixivClientId,
                ["client_secret"] = PixivClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = normalizedRefreshToken
            });

            await WaitForNextRemoteRequestAsync(_pixivRequestPacer, token);
            using var response = await PixivAuthClient.SendAsync(request, token);
            var payload = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Pixiv 刷新 Token 失败: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var tokenResponse = JsonConvert.DeserializeObject<PixivTokenResponse>(payload);
            if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
            {
                throw new InvalidOperationException("Pixiv 返回空 access_token。");
            }

            _pixivAccessToken = tokenResponse.AccessToken;
            UpdatePixivAccessTokenExpiry(tokenResponse);
            if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken) && tokenResponse.RefreshToken != PixivRefreshToken)
            {
                PixivRefreshToken = tokenResponse.RefreshToken;
                await SavePixivRefreshTokenAsync();
            }
        }

        private async Task<PixivUserIllustsResponse?> FetchPixivUserIllustsAsync(string artistId, string contentType, string? nextUrl, CancellationToken token)
        {
            if (ShouldRefreshPixivAccessTokenBeforeRequest())
            {
                await RefreshPixivAccessTokenAsync(token);
            }

            var requestUrl = string.IsNullOrWhiteSpace(nextUrl)
                ? $"/v1/user/illusts?user_id={Uri.EscapeDataString(artistId)}&filter=for_ios&type={contentType}"
                : nextUrl;

            var (statusCode, payload) = await SendPixivApiRequestAsync(requestUrl, token);
            if (ShouldRefreshPixivAccessToken(statusCode, payload))
            {
                await RefreshPixivAccessTokenAsync(token);
                (statusCode, payload) = await SendPixivApiRequestAsync(requestUrl, token);
            }

            if ((int)statusCode < 200 || (int)statusCode > 299)
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

        private bool ShouldRefreshPixivAccessTokenBeforeRequest()
        {
            return string.IsNullOrWhiteSpace(_pixivAccessToken)
                || _pixivAccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow.Add(PixivAccessTokenRefreshLeadTime);
        }

        private void UpdatePixivAccessTokenExpiry(PixivTokenResponse tokenResponse)
        {
            var expiresInSeconds = tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600;
            _pixivAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
        }

        private static bool ShouldRefreshPixivAccessToken(HttpStatusCode statusCode, string payload)
        {
            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
            {
                return true;
            }

            if (statusCode != HttpStatusCode.BadRequest || string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            return payload.IndexOf("invalid_grant", StringComparison.OrdinalIgnoreCase) >= 0
                || payload.IndexOf("invalid access token", StringComparison.OrdinalIgnoreCase) >= 0
                || (payload.IndexOf("oauth", StringComparison.OrdinalIgnoreCase) >= 0
                    && payload.IndexOf("access token", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private async Task<(HttpStatusCode StatusCode, string Payload)> SendPixivApiRequestAsync(string requestUrl, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _pixivAccessToken);
            request.Headers.TryAddWithoutValidation("User-Agent", PixivUserAgent);
            request.Headers.TryAddWithoutValidation("App-OS", PixivAppOs);
            request.Headers.TryAddWithoutValidation("App-OS-Version", PixivAppOsVersion);

            await WaitForNextRemoteRequestAsync(_pixivRequestPacer, token);
            using var response = await PixivApiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var payload = await response.Content.ReadAsStringAsync(token);
            return (response.StatusCode, payload);
        }

        private IEnumerable<PixivDownloadItem> BuildPixivDownloadItems(IEnumerable<PixivIllust> illusts)
        {
            foreach (var illust in illusts)
            {
                if (illust == null || string.Equals(illust.Type, "ugoira", StringComparison.OrdinalIgnoreCase))
                {
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

                    yield return new PixivDownloadItem(url, fileName, BuildPixivFileKey(illust.Id, index));
                    index++;
                }
            }
        }

        private static HashSet<string> LoadExistingPixivImageKeys(string outputDirectory)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(outputDirectory))
            {
                return keys;
            }

            foreach (var path in Directory.EnumerateFiles(outputDirectory))
            {
                var key = TryParsePixivFileKey(Path.GetFileNameWithoutExtension(path));
                if (!string.IsNullOrWhiteSpace(key))
                {
                    keys.Add(key);
                }
            }

            return keys;
        }

        private static string BuildPixivFileKey(long illustId, int pageIndex)
        {
            return $"{illustId}_p{pageIndex}";
        }

        private static string? TryParsePixivFileKey(string? fileNameWithoutExtension)
        {
            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            {
                return null;
            }

            var pageMarkerIndex = fileNameWithoutExtension.LastIndexOf("_p", StringComparison.OrdinalIgnoreCase);
            if (pageMarkerIndex <= 0 || pageMarkerIndex + 2 >= fileNameWithoutExtension.Length)
            {
                return null;
            }

            var idPart = fileNameWithoutExtension[..pageMarkerIndex];
            var pagePart = fileNameWithoutExtension[(pageMarkerIndex + 2)..];
            if (!idPart.All(char.IsDigit) || !pagePart.All(char.IsDigit))
            {
                return null;
            }

            return $"{idPart}_p{pagePart}";
        }

        private async Task<bool> DownloadPixivImageAsync(string url, string targetPath, CancellationToken token)
        {
            try
            {
                for (int attempt = 1; attempt <= ImageDownloadRetryCount; attempt++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Referrer = new Uri("https://app-api.pixiv.net/");
                    request.Headers.TryAddWithoutValidation("User-Agent", PixivUserAgent);

                    await WaitForNextRemoteRequestAsync(_pixivRequestPacer, token);
                    using var response = await PixivImageClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                    if (!response.IsSuccessStatusCode)
                    {
                        if (attempt < ImageDownloadRetryCount)
                        {
                            continue;
                        }

                        AppendLog($"[Pixiv] 下载失败: {response.StatusCode}");
                        return false;
                    }

                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    await using (var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await input.CopyToAsync(output, token);
                    }

                    if (!ValidateDownloadedFileOrDelete(targetPath, "Pixiv", Path.GetFileName(targetPath), attempt, ImageDownloadRetryCount))
                    {
                        if (attempt >= ImageDownloadRetryCount)
                        {
                            return false;
                        }

                        continue;
                    }

                    return true;
                }
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TryDeleteFile(targetPath);
                AppendLog($"[Pixiv] 下载失败: {ex.Message}");
                return false;
            }
        }

        private async Task RunKemonoAsync(string artistId, CancellationToken token)
        {
            const string selectedService = "fanbox";




            var outputDirectory = ResolveSourceOutputDirectory("Kemono", SanitizePathSegment(artistId, "unknown_artist"));
            Directory.CreateDirectory(outputDirectory);
            AppendLog($"[Kemono] 输出目录: {outputDirectory}");
            SetKemonoDownloadSummary("Kemono：正在获取下载清单…");

            var counters = new SourceDownloadCounters(progress => SetKemonoDownloadSummary(BuildDownloadSummary("Kemono", progress)));
            int savedCount = 0;
            int skippedCount = 0;
            var postContentCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var downloadPlan = await BuildKemonoDownloadPlanAsync(artistId, selectedService, token);
                int imageTotal = downloadPlan.Count(asset => DownloadedImageValidator.HasSupportedImageExtension(asset.FileName));
                counters.SetTotal(imageTotal);
                AppendLog($"[Kemono] 已发现 {imageTotal} 张远端图片（另有 {downloadPlan.Count - imageTotal} 个非图片附件）。");

                foreach (var asset in downloadPlan)
                {
                    token.ThrowIfCancellationRequested();
                    var targetPath = Path.Combine(outputDirectory, asset.FileName);
                    bool isImageAsset = DownloadedImageValidator.HasSupportedImageExtension(targetPath);
                    if (File.Exists(targetPath))
                    {
                        if (ShouldSkipExistingDownloadedFile(targetPath, "Kemono", asset.FileName))
                        {
                            skippedCount++;
                            if (isImageAsset)
                            {
                                counters.Record(DownloadOutcome.Skipped);
                            }
                            continue;
                        }
                    }

                    if (await DownloadKemonoAssetAsync(asset, outputDirectory, token))
                    {
                        savedCount++;
                        if (isImageAsset)
                        {
                            counters.Record(DownloadOutcome.Saved);
                        }
                        var content = await ResolveKemonoPostContentAsync(asset, artistId, selectedService, postContentCache, token);
                        await SaveKemonoContentAsync(content, asset.FileName, outputDirectory, token);
                        AppendLog($"[Kemono] 已保存 {asset.FileName}");
                    }
                    else if (isImageAsset)
                    {
                        counters.Record(DownloadOutcome.None);
                    }

                }
            }
            finally
            {
                AppendLog($"[Kemono] 保存 {savedCount} 个文件，跳过 {skippedCount} 个已存在文件。");
                SetKemonoDownloadSummary(BuildDownloadSummary("Kemono", counters));
            }
        }

        private async Task<List<KemonoAssetDownload>> BuildKemonoDownloadPlanAsync(string artistId, string service, CancellationToken token)
        {
            var plan = new List<KemonoAssetDownload>();
            int page = 0;

            while (!token.IsCancellationRequested)
            {
                var posts = await FetchKemonoPostsAsync(artistId, service, page, token);
                if (posts.Count == 0)
                {
                    break;
                }

                plan.AddRange(BuildKemonoDownloads(posts, page, artistId));
                page++;
            }

            token.ThrowIfCancellationRequested();
            return plan;
        }

        private async Task<List<KemonoPost>> FetchKemonoPostsAsync(string artistId, string service, int page, CancellationToken token)
        {
            int offset = page * KemonoPageSize;
            var requestUri = $"/api/v1/{service}/user/{artistId}/posts?o={offset}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            await WaitForNextRemoteRequestAsync(_kemonoRequestPacer, token);
            using var response = await KemonoHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var payload = await response.Content.ReadAsStringAsync(token);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new List<KemonoPost>();
            }

            if (response.StatusCode == HttpStatusCode.BadRequest
                && payload.IndexOf("offset", StringComparison.OrdinalIgnoreCase) >= 0
                && payload.IndexOf("total", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new List<KemonoPost>();
            }

            if (!response.IsSuccessStatusCode)
            {
                var preview = payload.Length > 200 ? payload[..200] + "..." : payload;
                throw new HttpRequestException($"Kemono API 返回 {response.StatusCode}: {preview}");
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new InvalidOperationException("Kemono API 返回为空，无法完成下载清单统计。");
            }

            var tokenJson = JToken.Parse(payload);
            if (tokenJson.Type == JTokenType.Object)
            {
                var error = tokenJson.Value<string>("error");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    throw new InvalidOperationException($"Kemono API 返回错误: {error}");
                }

                throw new InvalidOperationException("Kemono API 返回了无法识别的对象，无法完成下载清单统计。");
            }

            var posts = tokenJson.ToObject<List<KemonoPost>>();
            return posts ?? new List<KemonoPost>();
        }

        private static IEnumerable<KemonoAssetDownload> BuildKemonoDownloads(IEnumerable<KemonoPost> posts, int page, string artistId)
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
                    foreach (var attachment in post.Attachments.Where(a => a != null))
                    {
                        assets.Add(("attachment", attachment!));
                    }
                }

                if (assets.Count == 0)
                {
                    continue;
                }

                int assetIndex = 0;
                string dateSegment = FormatKemonoDate(post.Published);
                string? postContent = GetKemonoPostContent(post);
                string? postId = post.Id;

                foreach (var (type, asset) in assets)
                {
                    if (string.IsNullOrWhiteSpace(asset.Path))
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
                        : $"{KemonoApiBaseUrl}{asset.Path}";

                    yield return new KemonoAssetDownload(downloadUrl, fileName, postContent, postId);
                    assetIndex++;
                }
            }
        }

        private static string FormatKemonoDate(string? published)
        {
            if (string.IsNullOrWhiteSpace(published))
            {
                return "unknown";
            }

            if (DateTime.TryParse(published, out var parsed))
            {
                return parsed.ToString("yyyy_MM_dd");
            }

            var datePart = published.Split('T').FirstOrDefault();
            return string.IsNullOrWhiteSpace(datePart) ? "unknown" : datePart.Replace('-', '_');
        }

        private static string? GetKemonoPostContent(KemonoPost post)
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

        private async Task<string?> ResolveKemonoPostContentAsync(KemonoAssetDownload asset, string artistId, string service, IDictionary<string, string?> cache, CancellationToken token)
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

            var content = await FetchKemonoPostContentAsync(artistId, service, asset.PostId, token);
            cache[asset.PostId] = content;
            return content;
        }

        private async Task<string?> FetchKemonoPostContentAsync(string artistId, string service, string postId, CancellationToken token)
        {
            var requestUri = $"/api/v1/{service}/user/{artistId}/post/{postId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            await WaitForNextRemoteRequestAsync(_kemonoRequestPacer, token);
            using var response = await KemonoHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync(token);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var tokenJson = JToken.Parse(payload);
            if (tokenJson.Type != JTokenType.Object)
            {
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

            return !string.IsNullOrWhiteSpace(contentRaw) ? contentRaw : content;
        }

        private async Task<bool> DownloadKemonoAssetAsync(KemonoAssetDownload asset, string outputDirectory, CancellationToken token)
        {
            Directory.CreateDirectory(outputDirectory);
            var targetPath = Path.Combine(outputDirectory, asset.FileName);

            for (int attempt = 0; attempt <= KemonoRetryCount; attempt++)
            {
                try
                {
                    await WaitForNextRemoteRequestAsync(_kemonoRequestPacer, token);
                    using var response = await KemonoHttpClient.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    await using (var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await input.CopyToAsync(output, token);
                    }

                    if (ValidateDownloadedFileOrDelete(targetPath, "Kemono", asset.FileName, attempt + 1, KemonoRetryCount + 1))
                    {
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    TryDeleteFile(targetPath);
                    if (attempt >= KemonoRetryCount)
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private bool ShouldSkipExistingDownloadedFile(string filePath, string source, string displayName)
        {
            if (!DownloadedImageValidator.HasSupportedImageExtension(filePath)
                || DownloadedImageValidator.CanDecodeImage(filePath))
            {
                return true;
            }

            TryDeleteFile(filePath);
            AppendLog($"[{source}] Existing image failed validation and was deleted: {displayName}");
            return false;
        }

        private bool ValidateDownloadedFileOrDelete(string filePath, string source, string displayName, int attempt, int maxAttempts)
        {
            if (!DownloadedImageValidator.HasSupportedImageExtension(filePath)
                || DownloadedImageValidator.CanDecodeImage(filePath))
            {
                return true;
            }

            TryDeleteFile(filePath);
            AppendLog($"[{source}] Image validation failed, deleted bad file ({attempt}/{maxAttempts}): {displayName}");
            return false;
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }
        }

        private static async Task SaveKemonoContentAsync(string? content, string fileName, string outputDirectory, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            var txtPath = Path.Combine(outputDirectory, Path.ChangeExtension(fileName, ".txt"));
            await File.WriteAllTextAsync(txtPath, content, Encoding.UTF8, token);
        }

        private string ResolveOutputDirectoryRoot()
        {
            var configuredPattern = string.IsNullOrWhiteSpace(OutputDirectory) ? DefaultOutputDirectory : OutputDirectory.Trim();
            var configured = ApplyOutputDirectoryPlaceholders(configuredPattern);
            return configured;
        }

        private string ResolveSourceOutputDirectory(string source, string key)
        {
            return _activeRunOutputRootSnapshot?.ResolveOnce(ResolveOutputDirectoryRoot) ?? ResolveOutputDirectoryRoot();
        }

        private string ResolveCurrentRunOutputRoot()
        {
            return _activeRunOutputRootSnapshot?.ResolveOnce(ResolveOutputDirectoryRoot) ?? Path.GetFullPath(ResolveOutputDirectoryRoot());
        }

        private string ApplyOutputDirectoryPlaceholders(string pattern)
        {
            var result = pattern ?? string.Empty;
            result = result.Replace("{time}", DateTime.Now.ToString("yyyy年M月d日"), StringComparison.OrdinalIgnoreCase);
            result = result.Replace("{id}", _sessionOutputIdSegment, StringComparison.OrdinalIgnoreCase);
            result = result.Replace("{name}", _sessionOutputArtistSegment, StringComparison.OrdinalIgnoreCase);
            result = result.Replace("{artist}", _sessionOutputArtistSegment, StringComparison.OrdinalIgnoreCase);
            return result;
        }

        private static List<string> ParseIdentifiers(string input)
        {
            return input
                .Split(new[] { ',', ' ', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string SanitizePathSegment(string value, string fallback)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.Trim())
            {
                builder.Append(invalidChars.Contains(ch) || char.IsWhiteSpace(ch) ? '_' : ch);
            }

            var normalized = builder.ToString().Trim('_');
            if (normalized.Length > 80)
            {
                normalized = normalized[..80].TrimEnd('_');
            }

            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }

        private static string NormalizeDanbooruListUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return $"{DanbooruBaseUrl}/posts?tags=";
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return $"{DanbooruBaseUrl}/posts?tags=";
            }

            if (!string.Equals(uri.Host, "danbooru.donmai.us", StringComparison.OrdinalIgnoreCase))
            {
                return $"{DanbooruBaseUrl}/posts?tags=";
            }

            if (!uri.AbsolutePath.StartsWith("/posts", StringComparison.OrdinalIgnoreCase))
            {
                return $"{DanbooruBaseUrl}/posts?tags=";
            }

            return uri.ToString();
        }

        private void BrowseOutputFolder()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputDirectory = dialog.FileName;
            }
        }

        private void ResetDownloadSummaries()
        {
            SetDanbooruDownloadSummary(EnableDanbooru ? "Danbooru：等待下载" : "Danbooru：未选择");
            SetPixivDownloadSummary(EnablePixiv && !IsTagOrPostMode ? "Pixiv：等待下载" : "Pixiv：未选择");
            SetKemonoDownloadSummary(EnableKemono && !IsTagOrPostMode ? "Kemono：等待下载" : "Kemono：未选择");
            SetOutputDirectoryImageTotalSummary("输出目录总计：等待下载");
        }

        private void SetDanbooruDownloadSummary(string value)
        {
            if (_danbooruDownloadSummary == value)
            {
                return;
            }

            _danbooruDownloadSummary = value;
            OnPropertyChanged(nameof(DanbooruDownloadSummary));
        }

        private void SetPixivDownloadSummary(string value)
        {
            if (_pixivDownloadSummary == value)
            {
                return;
            }

            _pixivDownloadSummary = value;
            OnPropertyChanged(nameof(PixivDownloadSummary));
        }

        private void SetKemonoDownloadSummary(string value)
        {
            if (_kemonoDownloadSummary == value)
            {
                return;
            }

            _kemonoDownloadSummary = value;
            OnPropertyChanged(nameof(KemonoDownloadSummary));
        }

        private void SetOutputDirectoryImageTotalSummary(string value)
        {
            if (_outputDirectoryImageTotalSummary == value)
            {
                return;
            }

            _outputDirectoryImageTotalSummary = value;
            OnPropertyChanged(nameof(OutputDirectoryImageTotalSummary));
        }

        private static string BuildDownloadSummary(string source, SourceDownloadCounters counters)
        {
            var progress = counters.Total.HasValue
                ? $"已处理 {counters.Processed} / {counters.Total.Value} 张"
                : $"总数未知，已处理 {counters.Processed} 张";
            return $"{source}：{progress}，本次新增 {counters.Saved} 张，跳过 {counters.Skipped} 张";
        }

        private static string BuildOutputDirectoryImageTotalSummary(string? outputDirectory)
        {
            return $"输出目录总计：{CountDownloadedImages(outputDirectory)} 张";
        }

        private static int CountDownloadedImages(string? outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                return 0;
            }

            try
            {
                return Directory.EnumerateFiles(outputDirectory, "*", SearchOption.TopDirectoryOnly)
                    .Count(DownloadedImageValidator.HasSupportedImageExtension);
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }

        private void ClearLog()
        {
            LogText = string.Empty;
        }

        private void AppendLog(string message)
        {
            _pipelineLogSink?.Invoke(message);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LogText += $"{DateTime.Now:HH:mm:ss} - {message}\n";
            });
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return;
                }

                var json = File.ReadAllText(SettingsFilePath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<DatasetDownloaderSettings>(json, SettingsSerializerOptions);
                if (settings == null)
                {
                    return;
                }

                OutputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory) ? DefaultOutputDirectory : settings.OutputDirectory.Trim();
                var configuredInterval = settings.DownloadIntervalSeconds > 0
                    ? settings.DownloadIntervalSeconds
                    : settings.PixivDownloadIntervalSeconds;
                DownloadIntervalSeconds = configuredInterval <= 0 ? DefaultPixivIntervalSeconds : configuredInterval;
                IncludeArtist = settings.IncludeArtist;
                IncludeCopyright = settings.IncludeCopyright;
                IncludeCharacter = settings.IncludeCharacter;
                IncludeGeneral = settings.IncludeGeneral;
                IncludeMeta = settings.IncludeMeta;
                _lastDanbooruListUrl = NormalizeDanbooruListUrl(settings.DanbooruManualListUrl);
            }
            catch (Exception ex)
            {
                AppendLog($"加载数据集下载器配置失败: {ex.Message}");
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                var settings = new DatasetDownloaderSettings
                {
                    OutputDirectory = OutputDirectory,
                    DownloadIntervalSeconds = DownloadIntervalSeconds,
                    IncludeArtist = IncludeArtist,
                    IncludeCopyright = IncludeCopyright,
                    IncludeCharacter = IncludeCharacter,
                    IncludeGeneral = IncludeGeneral,
                    IncludeMeta = IncludeMeta,
                    DanbooruManualListUrl = _lastDanbooruListUrl
                };

                var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsSerializerOptions);
                var tempPath = SettingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, SettingsFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                AppendLog($"保存数据集下载器配置失败: {ex.Message}");
            }
        }

        private void LoadDanbooruToken()
        {
            try
            {
                if (File.Exists(DanbooruTokenFilePath))
                {
                    DanbooruToken = File.ReadAllText(DanbooruTokenFilePath);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"加载 Danbooru 令牌失败: {ex.Message}");
            }
        }

        private async Task SaveDanbooruTokenAsync()
        {
            try
            {
                await File.WriteAllTextAsync(DanbooruTokenFilePath, DanbooruToken ?? string.Empty);
            }
            catch (Exception ex)
            {
                AppendLog($"保存 Danbooru 令牌失败: {ex.Message}");
            }
        }

        private void LoadPixivRefreshToken()
        {
            try
            {
                if (File.Exists(PixivRefreshTokenFilePath))
                {
                    PixivRefreshToken = File.ReadAllText(PixivRefreshTokenFilePath).Trim();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"加载 Pixiv 刷新令牌失败: {ex.Message}");
            }
        }

        private async Task SavePixivRefreshTokenAsync()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(PixivRefreshToken))
                {
                    Directory.CreateDirectory(PixivRefreshTokenDirectory);
                    await File.WriteAllTextAsync(PixivRefreshTokenFilePath, PixivRefreshToken.Trim());
                }
            }
            catch (Exception ex)
            {
                AppendLog($"保存 Pixiv 刷新令牌失败: {ex.Message}");
            }
        }

        public async Task SaveStateOnCloseAsync()
        {
            await SaveSettingsAsync();
            await SaveDanbooruTokenAsync();
            await SavePixivRefreshTokenAsync();
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

        private static void ConfigurePixivApiClient(HttpClient client)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(PixivUserAgent);
            client.DefaultRequestHeaders.TryAddWithoutValidation("App-OS", PixivAppOs);
            client.DefaultRequestHeaders.TryAddWithoutValidation("App-OS-Version", PixivAppOsVersion);
            client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("zh-CN"));
        }

        private static void ConfigurePixivImageClient(HttpClient client)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(PixivUserAgent);
            client.DefaultRequestHeaders.Referrer = new Uri("https://app-api.pixiv.net/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        }

        private static string ComputePixivClientHash(string clientTime)
        {
            using var md5 = MD5.Create();
            var input = Encoding.UTF8.GetBytes(clientTime + PixivHashSecret);
            var hash = md5.ComputeHash(input);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }

        private static string GeneratePixivCodeVerifier()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string ComputePixivCodeChallenge(string codeVerifier)
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

        private static string BuildPixivLoginUrl(string codeChallenge)
        {
            return $"{PixivLoginUrl}?code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256&client=pixiv-android";
        }

        private static string? TryGetFileName(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return Path.GetFileName(uri.LocalPath);
            }

            return null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed record PixivDownloadItem(string Url, string FileName, string FileKey);
        private sealed record KemonoAssetDownload(string Url, string FileName, string? Content, string? PostId);

        private sealed class RemoteRequestPacer
        {
            private readonly SemaphoreSlim _lock = new(1, 1);
            private DateTimeOffset _nextRequestUtc = DateTimeOffset.MinValue;

            public void Reset()
            {
                _nextRequestUtc = DateTimeOffset.MinValue;
            }

            public async Task WaitAsync(int intervalSeconds, CancellationToken token)
            {
                await _lock.WaitAsync(token);
                try
                {
                    var remaining = _nextRequestUtc - DateTimeOffset.UtcNow;
                    if (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(remaining, token);
                    }

                    _nextRequestUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, intervalSeconds));
                }
                finally
                {
                    _lock.Release();
                }
            }
        }

        private enum DownloadOutcome
        {
            None,
            Saved,
            Skipped
        }

        private sealed class SourceDownloadCounters
        {
            private readonly Action<SourceDownloadCounters>? _changed;

            public SourceDownloadCounters(Action<SourceDownloadCounters>? changed = null)
            {
                _changed = changed;
            }

            public int? Total { get; private set; }
            public int Processed { get; private set; }
            public int Saved { get; private set; }
            public int Skipped { get; private set; }

            public void SetTotal(int total)
            {
                Total = Math.Max(0, total);
                _changed?.Invoke(this);
            }

            public void Record(DownloadOutcome outcome)
            {
                Processed++;
                switch (outcome)
                {
                    case DownloadOutcome.Saved:
                        Saved++;
                        break;
                    case DownloadOutcome.Skipped:
                        Skipped++;
                        break;
                }

                _changed?.Invoke(this);
            }
        }

        private sealed class BooruPostInfo
        {
            public int PostId { get; init; }
            public string? FileUrl { get; init; }
            public string? ArtistTags { get; init; }
            public string? CopyrightTags { get; init; }
            public string? CharacterTags { get; init; }
            public string? GeneralTags { get; init; }
            public string? MetaTags { get; init; }
        }

        private sealed class DatasetDownloaderSettings
        {
            public string? OutputDirectory { get; set; }
            public int DownloadIntervalSeconds { get; set; }
            public int PixivDownloadIntervalSeconds { get; set; }
            public bool IncludeArtist { get; set; }
            public bool IncludeCopyright { get; set; }
            public bool IncludeCharacter { get; set; } = true;
            public bool IncludeGeneral { get; set; } = true;
            public bool IncludeMeta { get; set; } = true;
            public string? DanbooruManualListUrl { get; set; }
        }

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
