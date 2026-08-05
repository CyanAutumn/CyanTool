using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CyanTools.Localization;
using Microsoft.Web.WebView2.Core;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace CyanTools
{
    public partial class PixivAuthWindow : FluentWindow
    {
        private const string RedirectUri = "https://app-api.pixiv.net/web/v1/users/auth/pixiv/callback";
        private const string SessionVerificationUrl = "https://www.pixiv.net/settings/profile";
        private static readonly string SessionLoginUrl =
            $"https://accounts.pixiv.net/login?return_to={Uri.EscapeDataString(SessionVerificationUrl)}";
        private readonly TaskCompletionSource<string?> _codeCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _sessionVerificationCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _loginUrl;
        private readonly string? _userDataFolder;
        private readonly WindowMode _mode;
        private bool _verificationLoginOpened;

        public PixivAuthWindow(string loginUrl, string? userDataFolder = null)
            : this(loginUrl, userDataFolder, WindowMode.OAuthCode)
        {
        }

        private PixivAuthWindow(string loginUrl, string? userDataFolder, WindowMode mode)
        {
            InitializeComponent();
            _loginUrl = loginUrl ?? throw new ArgumentNullException(nameof(loginUrl));
            _userDataFolder = userDataFolder;
            _mode = mode;

            if (_mode == WindowMode.SessionVerification)
            {
                Title = LocalizationManager.Translate("Pixiv 登录验证");
                HeaderTitleText.Text = LocalizationManager.Translate("Pixiv 登录验证");
                HeaderSubtitleText.Text = LocalizationManager.Translate("正在检查已保存的登录凭证…");
            }

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        public static PixivAuthWindow CreateSessionVerification(string userDataFolder)
        {
            if (string.IsNullOrWhiteSpace(userDataFolder))
            {
                throw new ArgumentException("用户数据目录不能为空。", nameof(userDataFolder));
            }

            return new PixivAuthWindow(
                SessionVerificationUrl,
                userDataFolder,
                WindowMode.SessionVerification);
        }

        public Task<string?> WaitForCodeAsync() => _codeCompletion.Task;

        public Task<bool> WaitForSessionVerificationAsync()
        {
            if (_mode != WindowMode.SessionVerification)
            {
                throw new InvalidOperationException("当前窗口不是登录验证模式。");
            }

            return _sessionVerificationCompletion.Task;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                WebView.NavigationStarting += OnNavigationStarting;
                WebView.NavigationCompleted += OnNavigationCompleted;
                if (!string.IsNullOrWhiteSpace(_userDataFolder))
                {
                    Directory.CreateDirectory(_userDataFolder);
                    var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
                    await WebView.EnsureCoreWebView2Async(environment);
                }
                else
                {
                    await WebView.EnsureCoreWebView2Async();
                }
                WebView.Source = new Uri(_loginUrl);
            }
            catch (Exception ex)
            {
                if (_mode == WindowMode.SessionVerification)
                {
                    _sessionVerificationCompletion.TrySetException(ex);
                }
                else
                {
                    _codeCompletion.TrySetException(ex);
                }

                Close();
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            WebView.NavigationStarting -= OnNavigationStarting;
            WebView.NavigationCompleted -= OnNavigationCompleted;

            if (_mode == WindowMode.SessionVerification)
            {
                _sessionVerificationCompletion.TrySetResult(false);
            }
            else
            {
                _codeCompletion.TrySetResult(null);
            }
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_mode != WindowMode.OAuthCode)
            {
                return;
            }

            if (TryExtractCode(e.Uri, out var code))
            {
                _codeCompletion.TrySetResult(code);
                e.Cancel = true;
                Close();
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_mode != WindowMode.SessionVerification || _sessionVerificationCompletion.Task.IsCompleted)
            {
                return;
            }

            if (!e.IsSuccess)
            {
                HeaderSubtitleText.Text = LocalizationManager.Translate("无法连接 Pixiv，请检查网络后重试。");
                return;
            }

            if (IsVerifiedSessionUri(WebView.Source))
            {
                _sessionVerificationCompletion.TrySetResult(true);
                Close();
                return;
            }

            if (_verificationLoginOpened)
            {
                return;
            }

            _verificationLoginOpened = true;
            HeaderSubtitleText.Text = LocalizationManager.Translate("未检测到有效登录，请手动登录。成功后窗口会自动关闭。");
            WebView.Source = new Uri(SessionLoginUrl);
        }

        private static bool IsVerifiedSessionUri(Uri? uri)
        {
            if (uri == null
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "www.pixiv.net", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(
                uri.AbsolutePath.TrimEnd('/'),
                "/settings/profile",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryExtractCode(string? uriString, out string? code)
        {
            code = null;
            if (string.IsNullOrWhiteSpace(uriString))
            {
                return false;
            }

            if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!uri.AbsoluteUri.StartsWith(RedirectUri, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (TryGetParameterValue(uri.Query, "code", out code))
            {
                return true;
            }

            if (TryGetParameterValue(uri.Fragment, "code", out code))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetParameterValue(string? input, string key, out string? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var trimmed = input.TrimStart('?', '#');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return false;
            }

            foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var segments = part.Split('=', 2);
                if (segments.Length == 0)
                {
                    continue;
                }

                var name = Uri.UnescapeDataString(segments[0]);
                if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = segments.Length > 1 ? Uri.UnescapeDataString(segments[1]) : string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private enum WindowMode
        {
            OAuthCode,
            SessionVerification
        }
    }
}
