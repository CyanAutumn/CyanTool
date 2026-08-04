using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace CyanTools
{
    public partial class DanbooruManualCollectorWindow : FluentWindow
    {
        private const string DefaultStartUrl = "https://danbooru.donmai.us/posts?tags=";
        private const string DanbooruHost = "danbooru.donmai.us";
        private static readonly Regex PostPathRegex = new(@"/posts/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly List<BrowserTabContext> _tabs = new();
        private readonly string _startUrl;
        private BrowserTabContext? _listTab;
        private bool _suppressSelectionRedirect;

        public DanbooruManualCollectorWindow(string? startUrl)
        {
            InitializeComponent();
            _startUrl = NormalizeStartUrl(startUrl);
            CollectedPostIds = Array.Empty<string>();

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        public IReadOnlyList<string> CollectedPostIds { get; private set; }
        public string LastListUrl { get; private set; } = DefaultStartUrl;

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                UrlTextBox.Text = _startUrl;
                await OpenListTabAsync(_startUrl);
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"初始化浏览器失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            foreach (var tab in _tabs)
            {
                if (tab.View?.CoreWebView2 != null)
                {
                    tab.View.CoreWebView2.NavigationCompleted -= tab.NavigationCompletedHandler;
                    tab.View.CoreWebView2.NewWindowRequested -= tab.NewWindowRequestedHandler;
                    tab.View.CoreWebView2.WebMessageReceived -= tab.WebMessageReceivedHandler;
                }
            }
        }

        private async void OnOpenClicked(object sender, RoutedEventArgs e)
        {
            var targetTab = GetSelectedTab() ?? _listTab;
            if (targetTab == null)
            {
                await OpenListTabAsync(UrlTextBox.Text);
                return;
            }


            if (targetTab.View == null && _listTab != null)
            {
                targetTab = _listTab;
                BrowserTabControl.SelectedItem = _listTab.Tab;
            }

            if (!TryNormalizeAnyUrl(UrlTextBox.Text, out var url))
            {
                MessageBox.Show(this, "URL 格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            NavigateTab(targetTab, url);
        }

        private void OnBrowserTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionRedirect)
            {
                return;
            }

            var selected = GetSelectedTab();
            if (selected == null || selected.View != null)
            {
                return;
            }

            var fallback = _listTab?.Tab
                ?? _tabs.FirstOrDefault(t => t.View != null)?.Tab;
            if (fallback == null)
            {
                return;
            }

            _suppressSelectionRedirect = true;
            try
            {
                BrowserTabControl.SelectedItem = fallback;
            }
            finally
            {
                _suppressSelectionRedirect = false;
            }
        }

        private async void OnOpenListClicked(object sender, RoutedEventArgs e)
        {
            await OpenListTabAsync(UrlTextBox.Text);
        }

        private void OnCloseCurrentTabClicked(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedTab();
            if (selected == null)
            {
                return;
            }

            if (selected.IsListTab)
            {
                MessageBox.Show(this, "列表页标签不能关闭。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RemoveTab(selected);
        }

        private void OnExtractAndDownloadClicked(object sender, RoutedEventArgs e)
        {
            var postIds = CollectPostIdsFromPostTabs();
            if (postIds.Count == 0)
            {
                MessageBox.Show(this, "未检测到帖子标签页（/posts/{id}）。请先中键打开帖子页标签。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CollectedPostIds = postIds;
            DialogResult = true;
            Close();
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async Task OpenListTabAsync(string? rawUrl)
        {
            var url = TryNormalizeListUrl(rawUrl) ?? DefaultStartUrl;
            if (_listTab == null)
            {
                _listTab = await CreateTabAsync(url, isListTab: true, activate: true);
                LastListUrl = url;
                UpdateStatus();
                return;
            }

            BrowserTabControl.SelectedItem = _listTab.Tab;
            NavigateTab(_listTab, url);
        }

        private async Task<BrowserTabContext> CreateTabAsync(string url, bool isListTab, bool activate)
        {
            if (!isListTab)
            {
                return CreateDeferredPostTab(url, activate);
            }

            var webView = new WebView2();
            var tabItem = new TabItem { Content = webView };
            var context = new BrowserTabContext(tabItem, isListTab, view: webView, placeholder: null);
            context.Tab.Header = "列表页";

            context.NavigationCompletedHandler = (_, args) => OnNavigationCompleted(context, args);
            context.NewWindowRequestedHandler = (_, args) => OnNewWindowRequested(context, args);
            context.WebMessageReceivedHandler = (_, args) => OnWebMessageReceived(context, args);

            _tabs.Add(context);
            BrowserTabControl.Items.Add(tabItem);
            if (activate)
            {
                BrowserTabControl.SelectedItem = tabItem;
            }

            SetBusy(true);
            try
            {
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.NavigationCompleted += context.NavigationCompletedHandler;
                webView.CoreWebView2.NewWindowRequested += context.NewWindowRequestedHandler;
                webView.CoreWebView2.WebMessageReceived += context.WebMessageReceivedHandler;
                NavigateTab(context, url);
            }
            finally
            {
                SetBusy(false);
            }

            return context;
        }

        private BrowserTabContext CreateDeferredPostTab(string url, bool activate)
        {
            var selectedBefore = BrowserTabControl.SelectedItem as TabItem;
            var placeholder = new TextBlock
            {
                Margin = new Thickness(12),
                TextWrapping = TextWrapping.Wrap,
                Text = "后台标签页（未加载）"
            };
            var tabItem = new TabItem { Content = placeholder };
            var context = new BrowserTabContext(tabItem, isListTab: false, view: null, placeholder: placeholder)
            {
                CurrentUrl = url
            };
            if (TryExtractPostId(url, out var postId))
            {
                context.PostId = postId;
            }

            UpdateDeferredPlaceholder(context);
            UpdateTabHeader(context);

            _tabs.Add(context);
            BrowserTabControl.Items.Add(tabItem);
            if (activate)
            {
                BrowserTabControl.SelectedItem = tabItem;
            }
            else
            {
                var fallback = selectedBefore ?? _listTab?.Tab ?? _tabs.FirstOrDefault(t => t.View != null)?.Tab;
                if (fallback != null)
                {
                    _suppressSelectionRedirect = true;
                    try
                    {
                        BrowserTabControl.SelectedItem = fallback;
                    }
                    finally
                    {
                        _suppressSelectionRedirect = false;
                    }
                }
            }

            UpdateStatus();
            return context;
        }

        private async void OnNavigationCompleted(BrowserTabContext tab, CoreWebView2NavigationCompletedEventArgs e)
        {
            tab.CurrentUrl = tab.View.Source?.ToString();
            if (e.IsSuccess)
            {
                await TryInstallMiddleClickHookAsync(tab.View);
            }

            if (tab.IsListTab && TryNormalizeListUrl(tab.CurrentUrl) is { } listUrl)
            {
                LastListUrl = listUrl;
            }

            tab.PostId = TryExtractPostId(tab.CurrentUrl, out var postId) ? postId : null;
            UpdateTabHeader(tab);
            UpdateStatus();
        }

        private async void OnNewWindowRequested(BrowserTabContext sourceTab, CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (!TryNormalizeAnyUrl(e.Uri, out var url))
            {
                return;
            }

            e.Handled = true;
            await CreateTabAsync(url, isListTab: false, activate: false);
            BrowserTabControl.SelectedItem = sourceTab.Tab;
        }

        private async void OnWebMessageReceived(BrowserTabContext sourceTab, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var raw = e.TryGetWebMessageAsString();
            if (!TryParseOpenTabMessage(raw, out var url))
            {
                return;
            }

            if (!TryNormalizeAnyUrl(url, out var normalized))
            {
                return;
            }

            await CreateTabAsync(normalized, isListTab: false, activate: false);
            BrowserTabControl.SelectedItem = sourceTab.Tab;
        }

        private static bool TryParseOpenTabMessage(string? raw, out string? url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!doc.RootElement.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                if (!string.Equals(typeElement.GetString(), "open-tab", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!doc.RootElement.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                url = urlElement.GetString();
                return !string.IsNullOrWhiteSpace(url);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryNormalizeAnyUrl(string? raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var trimmed = raw.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                if (Uri.TryCreate(new Uri(DefaultStartUrl), trimmed, out var relativeResolved))
                {
                    uri = relativeResolved;
                }
                else
                {
                    return false;
                }
            }

            normalized = uri.ToString();
            return true;
        }

        private static string? TryNormalizeListUrl(string? raw)
        {
            if (!TryNormalizeAnyUrl(raw, out var normalized))
            {
                return null;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                return null;
            }

            if (!string.Equals(uri.Host, DanbooruHost, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!uri.AbsolutePath.StartsWith("/posts", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return uri.ToString();
        }

        private static bool TryExtractPostId(string? url, out string postId)
        {
            postId = string.Empty;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var match = PostPathRegex.Match(uri.AbsolutePath);
            if (!match.Success)
            {
                return false;
            }

            postId = match.Groups[1].Value;
            return !string.IsNullOrWhiteSpace(postId);
        }

        private List<string> CollectPostIdsFromPostTabs()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tab in _tabs.Where(t => !t.IsListTab))
            {
                if (TryExtractPostId(tab.CurrentUrl, out var postId))
                {
                    set.Add(postId);
                }
            }

            return set.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        private BrowserTabContext? GetSelectedTab()
        {
            if (BrowserTabControl.SelectedItem is not TabItem tabItem)
            {
                return null;
            }

            return _tabs.FirstOrDefault(t => ReferenceEquals(t.Tab, tabItem));
        }

        private void NavigateTab(BrowserTabContext tab, string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return;
            }

            tab.CurrentUrl = uri.ToString();
            tab.PostId = TryExtractPostId(tab.CurrentUrl, out var postId) ? postId : null;

            if (tab.View != null)
            {
                tab.View.Source = uri;
            }
            else
            {
                UpdateDeferredPlaceholder(tab);
            }

            UrlTextBox.Text = tab.CurrentUrl;
            UpdateTabHeader(tab);
            UpdateStatus();
        }

        private async Task TryInstallMiddleClickHookAsync(WebView2 view)
        {
            if (view.CoreWebView2 == null)
            {
                return;
            }

            const string script = """
(() => {
  if (window.__cyanOpenTabHookInstalled) {
    return;
  }

  window.__cyanOpenTabHookInstalled = true;

  const resolveAnchor = (target) => {
    let current = target;
    while (current) {
      if (current.tagName && current.tagName.toLowerCase() === 'a' && current.href) {
        return current;
      }
      current = current.parentElement;
    }
    return null;
  };

  const postOpenTab = (href) => {
    if (!href || !window.chrome || !chrome.webview) {
      return;
    }
    chrome.webview.postMessage(JSON.stringify({ type: 'open-tab', url: href }));
  };

  document.addEventListener('auxclick', (event) => {
    if (event.button !== 1) {
      return;
    }
    const anchor = resolveAnchor(event.target);
    if (!anchor) {
      return;
    }
    event.preventDefault();
    postOpenTab(anchor.href);
  }, true);

  document.addEventListener('click', (event) => {
    if (!event.ctrlKey) {
      return;
    }
    const anchor = resolveAnchor(event.target);
    if (!anchor) {
      return;
    }
    event.preventDefault();
    postOpenTab(anchor.href);
  }, true);
})()
""";

            try
            {
                await view.ExecuteScriptAsync(script);
            }
            catch
            {

            }
        }

        private void RemoveTab(BrowserTabContext tab)
        {
            if (tab.View?.CoreWebView2 != null)
            {
                tab.View.CoreWebView2.NavigationCompleted -= tab.NavigationCompletedHandler;
                tab.View.CoreWebView2.NewWindowRequested -= tab.NewWindowRequestedHandler;
                tab.View.CoreWebView2.WebMessageReceived -= tab.WebMessageReceivedHandler;
            }

            BrowserTabControl.Items.Remove(tab.Tab);
            _tabs.Remove(tab);
            UpdateStatus();
        }

        private void SetBusy(bool isBusy)
        {
            OpenButton.IsEnabled = !isBusy;
            OpenListButton.IsEnabled = !isBusy;

            CloseTabButton.IsEnabled = true;
            ExtractAndDownloadButton.IsEnabled = true;
        }

        private static string NormalizeStartUrl(string? url)
        {
            if (TryNormalizeListUrl(url) is { } listUrl)
            {
                return listUrl;
            }

            if (TryNormalizeAnyUrl(url, out var normalized))
            {
                return normalized;
            }

            return DefaultStartUrl;
        }

        private void UpdateTabHeader(BrowserTabContext tab)
        {
            if (tab.IsListTab)
            {
                tab.Tab.Header = "列表页";
                return;
            }

            if (!string.IsNullOrWhiteSpace(tab.PostId))
            {
                tab.Tab.Header = $"帖子 {tab.PostId}";
                return;
            }

            if (string.IsNullOrWhiteSpace(tab.CurrentUrl))
            {
                tab.Tab.Header = "新标签页";
                return;
            }

            if (Uri.TryCreate(tab.CurrentUrl, UriKind.Absolute, out var uri))
            {
                tab.Tab.Header = uri.Host;
                return;
            }

            tab.Tab.Header = "新标签页";
        }

        private static void UpdateDeferredPlaceholder(BrowserTabContext tab)
        {
            if (tab.Placeholder == null)
            {
                return;
            }

            var idText = string.IsNullOrWhiteSpace(tab.PostId) ? "未知" : tab.PostId;
            var urlText = string.IsNullOrWhiteSpace(tab.CurrentUrl) ? "未知" : tab.CurrentUrl;
            tab.Placeholder.Text = $"后台标签页（未加载）\n帖子 ID: {idText}\nURL: {urlText}";
        }

        private void UpdateStatus()
        {
            var postTabs = _tabs.Count(t => !t.IsListTab && !string.IsNullOrWhiteSpace(t.PostId));
            StatusTextBlock.Text = $"标签页总数：{_tabs.Count}，帖子页：{postTabs}。提取会按这些帖子页批量下载（间隔 5 秒）。";
        }

        private sealed class BrowserTabContext
        {
            public BrowserTabContext(TabItem tab, bool isListTab, WebView2? view, TextBlock? placeholder)
            {
                Tab = tab;
                View = view;
                Placeholder = placeholder;
                IsListTab = isListTab;
            }

            public TabItem Tab { get; }
            public WebView2? View { get; }
            public TextBlock? Placeholder { get; }
            public bool IsListTab { get; }
            public string? CurrentUrl { get; set; }
            public string? PostId { get; set; }
            public EventHandler<CoreWebView2NavigationCompletedEventArgs>? NavigationCompletedHandler { get; set; }
            public EventHandler<CoreWebView2NewWindowRequestedEventArgs>? NewWindowRequestedHandler { get; set; }
            public EventHandler<CoreWebView2WebMessageReceivedEventArgs>? WebMessageReceivedHandler { get; set; }
        }
    }
}
