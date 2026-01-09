
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace CyanTools.ViewModel
{
    public class DanbooruCrawlerViewModel : INotifyPropertyChanged
    {
        private string _tags;
        private string _cookies;
        private string _postId;
        private string _logText;
        private string _selectedSource;
        private bool _isCrawling;
        private CancellationTokenSource _cancellationTokenSource;
        private static readonly Random _random = new Random();


        private readonly RestClient _danbooruClient;
        private readonly RestClient _gelbooruClient;
        private const string DanbooruBaseUrl = "https://danbooru.donmai.us";
        private const string GelbooruBaseUrl = "https://gelbooru.com";
        private const int DanbooruPageSize = 200;
        private const int GelbooruPageSize = 100;
        private static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "output");
        private const string CookiesFileName = "cookies.txt";
        private static readonly IReadOnlyList<string> SourceOptionsList = new[] { "Danbooru", "Gelbooru" };

        public string Tags
        {
            get => _tags;
            set { _tags = value; OnPropertyChanged(); }
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

        public string StartButtonText => IsCrawling ? "停止" : "开始";

        public ICommand StartStopCommand { get; }
        public ICommand DownloadByIdCommand { get; }

        public DanbooruCrawlerViewModel()
        {
            var danbooruOptions = new RestClientOptions(DanbooruBaseUrl) { MaxTimeout = -1 };
            _danbooruClient = new RestClient(danbooruOptions);

            var gelbooruOptions = new RestClientOptions(GelbooruBaseUrl) { MaxTimeout = -1 };
            _gelbooruClient = new RestClient(gelbooruOptions);
            _selectedSource = SourceOptionsList[0];

            StartStopCommand = new RelayCommand(async _ => await StartStopCrawling());
            DownloadByIdCommand = new RelayCommand(async _ => await DownloadSinglePost(), _ => !IsCrawling && !string.IsNullOrWhiteSpace(PostId));
            
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
                IsCrawling = true;
                _cancellationTokenSource = new CancellationTokenSource();
                try
                {
                    await CrawlAsync(_cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    Print("抓取操作已取消。");
                }
                catch (Exception ex)
                {
                    Print($"发生意外错误: {ex.Message}");
                }
                finally
                {
                    IsCrawling = false;
                }
            }
        }

        private async Task CrawlAsync(CancellationToken token)
        {
            Print("开始抓取...");
            int page = 0;
            bool isGelbooru = IsGelbooruSource();
            var client = GetClient(isGelbooru);

            while (!token.IsCancellationRequested)
            {
                var request = isGelbooru
                    ? BuildGelbooruListRequest(Tags, page)
                    : BuildDanbooruListRequest(Tags, page);
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
                            await DownloadFileAsync(post, token, isGelbooru);
                            await Task.Delay(5000, token);
                        }
                    }
                }
                catch (JsonException jsonEx)
                {
                    Print($"JSON解析错误: {jsonEx.Message}");
                    break;
                }
                
                page++;
            }
            Print("抓取任务结束。");
        }

        private async Task DownloadSinglePost()
        {
            if (string.IsNullOrWhiteSpace(PostId)) return;

            Print($"开始下载 Post ID: {PostId}...");
            IsCrawling = true;

            try
            {
                bool isGelbooru = IsGelbooruSource();
                var client = GetClient(isGelbooru);
                var request = isGelbooru
                    ? BuildGelbooruIdRequest(PostId)
                    : BuildDanbooruIdRequest(PostId);
                var response = await client.ExecuteAsync(request);

                if (response.IsSuccessful && !string.IsNullOrWhiteSpace(response.Content))
                {
                    var post = isGelbooru
                        ? ParseGelbooruPosts(response.Content).FirstOrDefault()
                        : ParseDanbooruSingle(response.Content);
                    if (post != null && !string.IsNullOrWhiteSpace(post.FileUrl))
                    {
                        await DownloadFileAsync(post, CancellationToken.None, isGelbooru);
                    }
                    else
                    {
                        Print($"无法找到 Post ID: {PostId} 或该 Post 没有文件。");
                    }
                }
                else
                {
                    Print($"下载 Post ID: {PostId} 失败: {response.StatusCode} - {response.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Print($"下载 Post ID: {PostId} 时发生错误: {ex.Message}");
            }
            finally
            {
                Print("单个下载任务结束。");
                IsCrawling = false;
            }
        }

        private async Task DownloadFileAsync(BooruPostInfo post, CancellationToken token, bool isGelbooru)
        {
            try
            {
                var url = NormalizeFileUrl(post.FileUrl, isGelbooru);
                if (string.IsNullOrWhiteSpace(url)) return;

                var fileName = Path.GetFileName(new Uri(url).LocalPath);
                Directory.CreateDirectory(OutputDirectory);

                var localPath = Path.Combine(OutputDirectory, fileName);
                var txtPath = Path.Combine(OutputDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}.txt");


                var request = new RestRequest(url, Method.Get);
                if (Cookies != null) 
                    request.AddHeader("Cookie", Cookies);

                var response = await GetClient(isGelbooru).ExecuteAsync(request, token);
                if (response.IsSuccessful)
                {
                    await File.WriteAllBytesAsync(localPath, response.RawBytes, token);
                    Print($"图片下载成功: {localPath}");

                    if (!string.IsNullOrWhiteSpace(post.Tags))
                    {
                        await File.WriteAllTextAsync(txtPath, NormalizeTagsForTxt(post.Tags), token);
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
                    Tags = post.tag_string
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
                Tags = post.tag_string
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
                    Tags = post.tags
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
            public string? Tags { get; init; }
        }

        public void LoadCookies()
        {
            try
            {
                if (File.Exists(CookiesFileName))
                {
                    Cookies = File.ReadAllText(CookiesFileName);
                    Print("Cookies 已加载。");
                }
            }
            catch (Exception ex)
            {
                Print($"加载 Cookies 失败: {ex.Message}");
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
                System.Windows.MessageBox.Show($"保存 Cookies 失败: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
