
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
using RestSharp;

namespace CyanTools.ViewModel
{
    public class DanbooruCrawlerViewModel : INotifyPropertyChanged
    {
        private string _tags;
        private string _cookies;
        private string _postId;
        private string _logText;
        private bool _isCrawling;
        private CancellationTokenSource _cancellationTokenSource;
        private static readonly Random _random = new Random();
        

        private readonly RestClient _danbooruClient;
        private static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "output");
        private const string CookiesFileName = "cookies.txt";

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
            var options = new RestClientOptions("https://danbooru.donmai.us") { MaxTimeout = -1 };
            _danbooruClient = new RestClient(options);

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

            while (!token.IsCancellationRequested)
            {
                var request = new RestRequest($"/posts.json?format=json&tags={Uri.EscapeDataString(Tags)}&limit=200&page={page}", Method.Get);
                var response = await _danbooruClient.ExecuteAsync(request, token);

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
                    var posts = JsonConvert.DeserializeObject<List<Danbooru.Post>>(response.Content);
                    if (posts == null || posts.Count == 0)
                    {
                        Print("抓取完毕。");
                        break;
                    }

                    foreach (var post in posts)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!string.IsNullOrWhiteSpace(GetBestFileUrl(post)))
                        {
                            await DownloadFileAsync(post, token);
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
                var request = new RestRequest($"/posts/{PostId}.json", Method.Get);
                var response = await _danbooruClient.ExecuteAsync(request);

                if (response.IsSuccessful && !string.IsNullOrWhiteSpace(response.Content))
                {
                    var post = JsonConvert.DeserializeObject<Danbooru.Post>(response.Content);
                    if (post != null && !string.IsNullOrWhiteSpace(GetBestFileUrl(post)))
                    {
                        await DownloadFileAsync(post, CancellationToken.None);
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

        private async Task DownloadFileAsync(Danbooru.Post post, CancellationToken token)
        {
            try
            {
                var url = GetBestFileUrl(post);
                if (string.IsNullOrWhiteSpace(url)) return;

                var fileName = Path.GetFileName(new Uri(url).LocalPath);
                Directory.CreateDirectory(OutputDirectory);

                var localPath = Path.Combine(OutputDirectory, fileName);
                var txtPath = Path.Combine(OutputDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}.txt");


                var request = new RestRequest(url, Method.Get);
                if (Cookies != null) 
                    request.AddHeader("Cookie", Cookies);

                var response = await _danbooruClient.ExecuteAsync(request, token);
                if (response.IsSuccessful)
                {
                    await File.WriteAllBytesAsync(localPath, response.RawBytes, token);
                    Print($"图片下载成功: {localPath}");

                    if (post.tag_string != null)
                    {
                        await File.WriteAllTextAsync(txtPath, post.tag_string.Replace(" ", ","), token);
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
