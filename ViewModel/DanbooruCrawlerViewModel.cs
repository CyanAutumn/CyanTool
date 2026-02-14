
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Model;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace CyanTools.ViewModel
{
    public class DanbooruCrawlerViewModel : INotifyPropertyChanged
    {
        private string _tags;
        private string _artistId;
        private string _cookies;
        private string _postId;
        private string _outputDirectory = DefaultOutputDirectory;
        private string _logText;
        private string _selectedSource;
        private bool _isCrawling;
        private bool _includeArtist = false;
        private bool _includeCopyright = false;
        private bool _includeCharacter = true;
        private bool _includeGeneral = true;
        private bool _includeMeta = true;
        private volatile bool _isDownloadInProgress;
        private CancellationTokenSource? _cancellationTokenSource;
        private static readonly Random _random = new Random();
        private static readonly string SettingsDirectory = Path.Combine(AppContext.BaseDirectory, "Config");
        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "danbooru_downloader.json");
        private static readonly System.Text.Json.JsonSerializerOptions SettingsSerializerOptions = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            WriteIndented = true
        };
        private readonly ConcurrentQueue<DownloadQueueItem> _downloadQueue = new ConcurrentQueue<DownloadQueueItem>();
        private readonly SemaphoreSlim _downloadQueueSignal = new SemaphoreSlim(0);
        private readonly SemaphoreSlim _sessionLock = new SemaphoreSlim(1, 1);
        private Task _downloadQueueWorkerTask = Task.CompletedTask;
        private DateTime _lastQueueItemCompletedAtUtc = DateTime.MinValue;
        private bool _isApplyingSettings;
        private string _lastSavedOutputDirectory = DefaultOutputDirectory;


        private readonly RestClient _danbooruClient;
        private readonly RestClient _gelbooruClient;
        private const string DanbooruBaseUrl = "https://danbooru.donmai.us";
        private const string GelbooruBaseUrl = "https://gelbooru.com";
        private const int DanbooruPageSize = 200;
        private const int GelbooruPageSize = 100;
        private const int DownloadIntervalMilliseconds = 5000;
        private const string DefaultOutputDirectory = "./output";
        private const string CookiesFileName = "cookies.txt";
        private static readonly IReadOnlyList<string> SourceOptionsList = new[] { "Danbooru", "Gelbooru" };

        public string Tags
        {
            get => _tags;
            set { _tags = value; OnPropertyChanged(); }
        }

        public string ArtistId
        {
            get => _artistId;
            set { _artistId = value; OnPropertyChanged(); }
        }

        public string Cookies
        {
            get => _cookies;
            set { _cookies = value; OnPropertyChanged(); }
        }

        public string PostId
        {
            get => _postId;
            set { _postId = value; OnPropertyChanged(); }
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
                }
            }
        }

        public string LogText
        {
            get => _logText;
            set { _logText = value; OnPropertyChanged(); }
        }

        public IReadOnlyList<string> SourceOptions => SourceOptionsList;

        public string SelectedSource
        {
            get => _selectedSource;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? SourceOptionsList[0] : value;
                if (_selectedSource != next)
                {
                    _selectedSource = next;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsCrawling
        {
            get => _isCrawling;
            set
            {
                if (_isCrawling != value)
                {
                    _isCrawling = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StartButtonText));

                    ((RelayCommand)DownloadByIdCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IncludeArtist
        {
            get => _includeArtist;
            set { _includeArtist = value; OnPropertyChanged(); }
        }

        public bool IncludeCopyright
        {
            get => _includeCopyright;
            set { _includeCopyright = value; OnPropertyChanged(); }
        }

        public bool IncludeCharacter
        {
            get => _includeCharacter;
            set { _includeCharacter = value; OnPropertyChanged(); }
        }

        public bool IncludeGeneral
        {
            get => _includeGeneral;
            set { _includeGeneral = value; OnPropertyChanged(); }
        }

        public bool IncludeMeta
        {
            get => _includeMeta;
            set { _includeMeta = value; OnPropertyChanged(); }
        }

        public string StartButtonText => IsCrawling ? "停止" : "开始";

        public ICommand StartStopCommand { get; }
        public ICommand DownloadByIdCommand { get; }
        public ICommand BrowseOutputFolderCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }

        public DanbooruCrawlerViewModel()
        {
            var danbooruOptions = new RestClientOptions(DanbooruBaseUrl) { MaxTimeout = -1 };
            _danbooruClient = new RestClient(danbooruOptions);

            var gelbooruOptions = new RestClientOptions(GelbooruBaseUrl) { MaxTimeout = -1 };
            _gelbooruClient = new RestClient(gelbooruOptions);
            _selectedSource = SourceOptionsList[0];

            StartStopCommand = new RelayCommand(async _ => await StartStopCrawling());
            DownloadByIdCommand = new RelayCommand(async _ => await DownloadSinglePost(), _ => !string.IsNullOrWhiteSpace(PostId));
            BrowseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder());
            OpenOutputFolderCommand = new RelayCommand(_ => OpenOutputFolder());

            LoadSettings();
            LoadCookies();
        }

        private async Task StartStopCrawling()
        {
            if (IsCrawling)
            {
                _cancellationTokenSource?.Cancel();
            }
            else
            {
                await RunDownloadSessionAsync(runCrawler: true);
            }
        }

        private async Task RunDownloadSessionAsync(bool runCrawler)
        {
            await _sessionLock.WaitAsync();
            try
            {
                if (IsCrawling)
                {
                    return;
                }

                IsCrawling = true;
                _cancellationTokenSource = new CancellationTokenSource();
                try
                {
                    _downloadQueueWorkerTask = ProcessDownloadQueueAsync(_cancellationTokenSource.Token);
                    if (runCrawler)
                    {
                        await CrawlAsync(_cancellationTokenSource.Token);
                    }

                    await WaitForQueueIdleAsync(_cancellationTokenSource.Token);
                    _cancellationTokenSource.Cancel();
                }
                catch (OperationCanceledException)
                {
                    Print(runCrawler ? "抓取操作已取消。" : "下载队列已停止。");
                }
                catch (Exception ex)
                {
                    Print($"发生意外错误: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        await _downloadQueueWorkerTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Print($"下载队列处理异常: {ex.Message}");
                    }

                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                    _downloadQueueWorkerTask = Task.CompletedTask;
                    IsCrawling = false;
                }
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private async Task CrawlAsync(CancellationToken token)
        {
            Print("开始抓取...");
            int page = 0;
            bool isGelbooru = IsGelbooruSource();
            var client = GetClient(isGelbooru);
            var effectiveTags = await ResolveEffectiveTagsAsync(token);
            if (effectiveTags == null)
            {
                Print("没有可用的查询标签，抓取已取消。");
                return;
            }

            while (!token.IsCancellationRequested)
            {
                var request = isGelbooru
                    ? BuildGelbooruListRequest(effectiveTags, page)
                    : BuildDanbooruListRequest(effectiveTags, page);
                var response = await client.ExecuteAsync(request, token);

                token.ThrowIfCancellationRequested();

                if (!response.IsSuccessful)
                {
                    Print($"错误: {response.StatusCode} - {response.ErrorMessage}");
                    break;
                }


                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    Print("收到了空的响应，抓取结束。");
                    break;
                }

                try
                {
                    var posts = isGelbooru
                        ? ParseGelbooruPosts(response.Content)
                        : ParseDanbooruPosts(response.Content);
                    if (posts.Count == 0)
                    {
                        Print("抓取完毕。");
                        break;
                    }

                    foreach (var post in posts)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!string.IsNullOrWhiteSpace(post.FileUrl))
                        {
                            EnqueueDownload(new DownloadQueueItem
                            {
                                Post = post,
                                IsGelbooru = isGelbooru
                            });
                        }
                    }
                }
                catch (Newtonsoft.Json.JsonException jsonEx)
                {
                    Print($"JSON解析错误: {jsonEx.Message}");
                    break;
                }
                
                page++;
            }
            Print("抓取任务结束。");
        }

        private async Task<string?> ResolveEffectiveTagsAsync(CancellationToken token)
        {
            var manualTags = (Tags ?? string.Empty).Trim();
            var artistId = (ArtistId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(artistId))
            {
                return manualTags;
            }

            if (IsGelbooruSource())
            {
                Print("当前来源为 Gelbooru，暂不支持通过 Pixiv 画师 ID 自动解析 artist 标签。将仅使用手动标签抓取。");
                return manualTags;
            }

            var artistTag = await ResolveDanbooruArtistTagByPixivUserIdAsync(artistId, token);
            if (string.IsNullOrWhiteSpace(artistTag))
            {
                Print($"未找到 Pixiv 画师 ID {artistId} 对应的 Danbooru artist 标签。将仅使用手动标签抓取。");
                if (string.IsNullOrWhiteSpace(manualTags))
                {
                    return null;
                }
                return manualTags;
            }

            Print($"画师 ID {artistId} -> artist tag: {artistTag}");

            if (string.IsNullOrWhiteSpace(manualTags))
            {
                return artistTag;
            }

            if (manualTags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(tag => string.Equals(tag, artistTag, StringComparison.OrdinalIgnoreCase)))
            {
                return manualTags;
            }

            return $"{artistTag} {manualTags}";
        }

        private async Task<string?> ResolveDanbooruArtistTagByPixivUserIdAsync(string pixivUserId, CancellationToken token)
        {
            try
            {
                var pattern = $"*pixiv.net/users/{pixivUserId}*";
                var request = new RestRequest($"/artists.json?search[url_matches]={Uri.EscapeDataString(pattern)}", Method.Get);
                var response = await _danbooruClient.ExecuteAsync(request, token);

                if (!response.IsSuccessful)
                {
                    Print($"查询画师标签失败: {response.StatusCode} - {response.ErrorMessage}");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    return null;
                }

                var artists = JArray.Parse(response.Content);
                if (artists.Count == 0)
                {
                    return null;
                }

                var firstArtist = artists[0] as JObject;
                return firstArtist?["name"]?.ToString();
            }
            catch (Exception ex)
            {
                Print($"解析画师标签时发生错误: {ex.Message}");
                return null;
            }
        }

        private async Task DownloadSinglePost()
        {
            if (string.IsNullOrWhiteSpace(PostId)) return;

            var postId = PostId.Trim();
            bool isGelbooru = IsGelbooruSource();

            EnqueueDownload(new DownloadQueueItem
            {
                PostId = postId,
                IsGelbooru = isGelbooru
            });

            Print($"帖子 ID: {postId} 已加入下载队列。当前队列长度: {_downloadQueue.Count}");
            if (!IsCrawling)
            {
                Print("检测到队列任务，自动开始下载。");
                _ = RunDownloadSessionAsync(runCrawler: false);
            }

            await Task.CompletedTask;
        }

        private void EnqueueDownload(DownloadQueueItem item)
        {
            _downloadQueue.Enqueue(item);
            _downloadQueueSignal.Release();
        }

        private async Task ProcessDownloadQueueAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await _downloadQueueSignal.WaitAsync(token);

                if (!_downloadQueue.TryDequeue(out var item) || item == null)
                {
                    continue;
                }

                await WaitForDownloadIntervalAsync(token);

                _isDownloadInProgress = true;
                try
                {
                    await ProcessQueueItemAsync(item, token);
                }
                finally
                {
                    _isDownloadInProgress = false;
                    _lastQueueItemCompletedAtUtc = DateTime.UtcNow;
                }
            }
        }

        private async Task WaitForDownloadIntervalAsync(CancellationToken token)
        {
            var elapsed = DateTime.UtcNow - _lastQueueItemCompletedAtUtc;
            var remaining = TimeSpan.FromMilliseconds(DownloadIntervalMilliseconds) - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, token);
            }
        }

        private async Task ProcessQueueItemAsync(DownloadQueueItem item, CancellationToken token)
        {
            BooruPostInfo? post;
            if (item.Post != null)
            {
                post = item.Post;
            }
            else if (!string.IsNullOrWhiteSpace(item.PostId))
            {
                post = await FetchPostByIdAsync(item.PostId, item.IsGelbooru, token);
                if (post == null)
                {
                    return;
                }
            }
            else
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(post.FileUrl))
            {
                if (!string.IsNullOrWhiteSpace(item.PostId))
                {
                    Print($"无法找到帖子 ID: {item.PostId} 或该帖子没有文件。");
                }
                return;
            }

            await DownloadFileAsync(post, token, item.IsGelbooru);
        }

        private async Task<BooruPostInfo?> FetchPostByIdAsync(string postId, bool isGelbooru, CancellationToken token)
        {
            try
            {
                var client = GetClient(isGelbooru);
                var request = isGelbooru
                    ? BuildGelbooruIdRequest(postId)
                    : BuildDanbooruIdRequest(postId);
                var response = await client.ExecuteAsync(request, token);

                if (!response.IsSuccessful)
                {
                    Print($"下载帖子 ID: {postId} 失败: {response.StatusCode} - {response.ErrorMessage}");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    Print($"下载帖子 ID: {postId} 失败: 响应为空。");
                    return null;
                }

                return isGelbooru
                    ? ParseGelbooruPosts(response.Content).FirstOrDefault()
                    : ParseDanbooruSingle(response.Content);
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                Print($"下载帖子 ID: {postId} 失败: JSON解析错误: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Print($"下载帖子 ID: {postId} 时发生错误: {ex.Message}");
                return null;
            }
        }

        private async Task WaitForQueueIdleAsync(CancellationToken token)
        {
            while ((!_downloadQueue.IsEmpty || _isDownloadInProgress) && !token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
            }
        }

        private async Task DownloadFileAsync(BooruPostInfo post, CancellationToken token, bool isGelbooru)
        {
            try
            {
                var url = NormalizeFileUrl(post.FileUrl, isGelbooru);
                if (string.IsNullOrWhiteSpace(url)) return;

                var fileName = Path.GetFileName(new Uri(url).LocalPath);
                var outputDirectory = ResolveOutputDirectory();
                Directory.CreateDirectory(outputDirectory);

                var localPath = Path.Combine(outputDirectory, fileName);
                var txtPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}.txt");

                if (File.Exists(localPath))
                {
                    Print($"文件已存在，跳过下载: {localPath}");

                    if (!File.Exists(txtPath))
                    {
                        var selectedTags = BuildTagsForTxt(post, isGelbooru);
                        if (!string.IsNullOrWhiteSpace(selectedTags))
                        {
                            await File.WriteAllTextAsync(txtPath, selectedTags, token);
                            Print($"标签文件已保存: {txtPath}");
                        }
                    }

                    return;
                }


                var request = new RestRequest(url, Method.Get);
                if (Cookies != null) 
                    request.AddHeader("Cookie", Cookies);

                var response = await GetClient(isGelbooru).ExecuteAsync(request, token);
                if (response.IsSuccessful)
                {
                    await File.WriteAllBytesAsync(localPath, response.RawBytes, token);
                    Print($"图片下载成功: {localPath}");

                    var selectedTags = BuildTagsForTxt(post, isGelbooru);
                    if (!string.IsNullOrWhiteSpace(selectedTags))
                    {
                        await File.WriteAllTextAsync(txtPath, selectedTags, token);
                        Print($"标签文件已保存: {txtPath}");
                    }
                }
                else
                {
                    Print($"下载失败: {response.StatusCode} - {response.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Print($"下载过程中发生错误: {ex.Message}");
            }
        }

        private void Print(string text)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LogText += $"{DateTime.Now:HH:mm:ss} - {text}\n";
            });
        }

        private void BrowseOutputFolder()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputDirectory = dialog.FileName;
            }
        }

        private void OpenOutputFolder()
        {
            try
            {
                var outputDirectory = ResolveOutputDirectory();
                Directory.CreateDirectory(outputDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = outputDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Print($"打开输出目录失败: {ex.Message}");
            }
        }

        private string ResolveOutputDirectory()
        {
            var configured = string.IsNullOrWhiteSpace(OutputDirectory) ? DefaultOutputDirectory : OutputDirectory.Trim();
            var rootDirectory = Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
            return Path.Combine(rootDirectory, ResolveOutputSubdirectoryName());
        }

        private string ResolveOutputSubdirectoryName()
        {
            var artistId = (ArtistId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(artistId))
            {
                return SanitizePathSegment(artistId, "unknown_artist");
            }

            var tags = (Tags ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(tags))
            {
                return SanitizePathSegment(tags, "untagged");
            }

            return "untagged";
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

        private static string? GetBestFileUrl(Danbooru.Post post)
        {
            if (!string.IsNullOrWhiteSpace(post.file_url))
            {
                return post.file_url;
            }

            var originalVariant = post.media_asset?.variants?
                .FirstOrDefault(variant => string.Equals(variant.type, "original", StringComparison.OrdinalIgnoreCase));
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

        private static string? GetBestFileUrl(Gelbooru.Post post)
        {
            if (!string.IsNullOrWhiteSpace(post.file_url))
            {
                return post.file_url;
            }

            if (!string.IsNullOrWhiteSpace(post.sample_url))
            {
                return post.sample_url;
            }

            return null;
        }

        private bool IsGelbooruSource()
        {
            return string.Equals(SelectedSource, "Gelbooru", StringComparison.OrdinalIgnoreCase);
        }

        private RestClient GetClient(bool isGelbooru)
        {
            return isGelbooru ? _gelbooruClient : _danbooruClient;
        }

        private static RestRequest BuildDanbooruListRequest(string tags, int page)
        {
            var safeTags = tags ?? string.Empty;
            return new RestRequest($"/posts.json?format=json&tags={Uri.EscapeDataString(safeTags)}&limit={DanbooruPageSize}&page={page}", Method.Get);
        }

        private static RestRequest BuildGelbooruListRequest(string tags, int page)
        {
            var safeTags = tags ?? string.Empty;
            return new RestRequest($"/index.php?page=dapi&s=post&q=index&json=1&limit={GelbooruPageSize}&pid={page}&tags={Uri.EscapeDataString(safeTags)}", Method.Get);
        }

        private static RestRequest BuildDanbooruIdRequest(string postId)
        {
            return new RestRequest($"/posts/{postId}.json", Method.Get);
        }

        private static RestRequest BuildGelbooruIdRequest(string postId)
        {
            return new RestRequest($"/index.php?page=dapi&s=post&q=index&json=1&id={Uri.EscapeDataString(postId)}", Method.Get);
        }

        private static List<BooruPostInfo> ParseDanbooruPosts(string content)
        {
            var posts = JsonConvert.DeserializeObject<List<Danbooru.Post>>(content) ?? new List<Danbooru.Post>();
            return posts
                .Select(post => new BooruPostInfo
                {
                    FileUrl = GetBestFileUrl(post),
                    ArtistTags = post.tag_string_artist,
                    CopyrightTags = post.tag_string_copyright,
                    CharacterTags = post.tag_string_character,
                    GeneralTags = post.tag_string_general,
                    MetaTags = post.tag_string_meta
                })
                .ToList();
        }

        private static BooruPostInfo? ParseDanbooruSingle(string content)
        {
            var post = JsonConvert.DeserializeObject<Danbooru.Post>(content);
            if (post == null)
            {
                return null;
            }

            return new BooruPostInfo
            {
                FileUrl = GetBestFileUrl(post),
                ArtistTags = post.tag_string_artist,
                CopyrightTags = post.tag_string_copyright,
                CharacterTags = post.tag_string_character,
                GeneralTags = post.tag_string_general,
                MetaTags = post.tag_string_meta
            };
        }

        private static List<BooruPostInfo> ParseGelbooruPosts(string content)
        {
            var result = new List<BooruPostInfo>();
            var rootToken = JToken.Parse(content);
            if (rootToken.Type == JTokenType.Array)
            {
                return result;
            }

            var root = rootToken as JObject;
            if (root == null)
            {
                return result;
            }

            var postToken = root["post"];
            if (postToken == null || postToken.Type == JTokenType.Null)
            {
                return result;
            }

            IEnumerable<Gelbooru.Post> posts;
            if (postToken.Type == JTokenType.Array)
            {
                posts = postToken.ToObject<List<Gelbooru.Post>>() ?? new List<Gelbooru.Post>();
            }
            else
            {
                var single = postToken.ToObject<Gelbooru.Post>();
                posts = single != null ? new[] { single } : Array.Empty<Gelbooru.Post>();
            }

            foreach (var post in posts)
            {
                var info = new BooruPostInfo
                {
                    FileUrl = GetBestFileUrl(post),
                    GeneralTags = post.tags
                };
                result.Add(info);
            }

            return result;
        }

        private static string NormalizeTagsForTxt(string tags)
        {
            var parts = tags.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(",", parts);
        }

        private string BuildTagsForTxt(BooruPostInfo post, bool isGelbooru)
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

            if (!isGelbooru && IncludeMeta && !string.IsNullOrWhiteSpace(post.MetaTags))
            {
                selected.Add(post.MetaTags);
            }

            if (selected.Count == 0)
            {
                return string.Empty;
            }

            return NormalizeTagsForTxt(string.Join(" ", selected));
        }

        private static string? NormalizeFileUrl(string? url, bool isGelbooru)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            {
                return absolute.ToString();
            }

            var baseUrl = isGelbooru ? GelbooruBaseUrl : DanbooruBaseUrl;
            if (Uri.TryCreate(new Uri(baseUrl), url, out var resolved))
            {
                return resolved.ToString();
            }

            return url;
        }

        private sealed class BooruPostInfo
        {
            public string? FileUrl { get; init; }
            public string? ArtistTags { get; init; }
            public string? CopyrightTags { get; init; }
            public string? CharacterTags { get; init; }
            public string? GeneralTags { get; init; }
            public string? MetaTags { get; init; }
        }

        private sealed class DownloadQueueItem
        {
            public BooruPostInfo? Post { get; init; }
            public string? PostId { get; init; }
            public bool IsGelbooru { get; init; }
        }

        public void LoadCookies()
        {
            try
            {
                if (File.Exists(CookiesFileName))
                {
                    Cookies = File.ReadAllText(CookiesFileName);
                    Print("登录信息已加载。");
                }
            }
            catch (Exception ex)
            {
                Print($"加载登录信息失败: {ex.Message}");
            }
        }

        public async Task SaveCookiesAsync()
        {
            try
            {
                await File.WriteAllTextAsync(CookiesFileName, Cookies);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存登录信息失败: {ex.Message}");
            }
        }

        public async Task SaveStateOnCloseAsync()
        {
            await SaveCookiesAsync();
            await SaveOutputDirectoryIfChangedAsync();
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    _lastSavedOutputDirectory = DefaultOutputDirectory;
                    return;
                }

                var json = File.ReadAllText(SettingsFilePath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<DanbooruCrawlerSettings>(json, SettingsSerializerOptions);
                var outputDirectory = string.IsNullOrWhiteSpace(settings?.OutputDirectory)
                    ? DefaultOutputDirectory
                    : settings.OutputDirectory.Trim();

                _isApplyingSettings = true;
                OutputDirectory = outputDirectory;
                _lastSavedOutputDirectory = outputDirectory;
            }
            catch (Exception ex)
            {
                Print($"加载 Booru 下载器配置失败: {ex.Message}");
            }
            finally
            {
                _isApplyingSettings = false;
            }
        }

        private async Task SaveOutputDirectoryIfChangedAsync()
        {
            if (_isApplyingSettings)
            {
                return;
            }

            var normalizedOutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
                ? DefaultOutputDirectory
                : OutputDirectory.Trim();
            if (string.Equals(normalizedOutputDirectory, _lastSavedOutputDirectory, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(SettingsDirectory);

                var settings = new DanbooruCrawlerSettings
                {
                    OutputDirectory = normalizedOutputDirectory
                };

                var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsSerializerOptions);
                var tempPath = SettingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, SettingsFilePath, overwrite: true);

                _lastSavedOutputDirectory = normalizedOutputDirectory;
            }
            catch (Exception ex)
            {
                Print($"保存 Booru 下载器配置失败: {ex.Message}");
            }
        }

        private sealed class DanbooruCrawlerSettings
        {
            public string? OutputDirectory { get; set; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
