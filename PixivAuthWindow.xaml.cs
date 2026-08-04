using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace CyanTools
{
    public partial class PixivAuthWindow : FluentWindow
    {
        private const string RedirectUri = "https://app-api.pixiv.net/web/v1/users/auth/pixiv/callback";
        private readonly TaskCompletionSource<string?> _codeCompletion = new();
        private readonly string _loginUrl;

        public PixivAuthWindow(string loginUrl)
        {
            InitializeComponent();
            _loginUrl = loginUrl ?? throw new ArgumentNullException(nameof(loginUrl));
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        public Task<string?> WaitForCodeAsync() => _codeCompletion.Task;

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                WebView.NavigationStarting += OnNavigationStarting;
                await WebView.EnsureCoreWebView2Async();
                WebView.Source = new Uri(_loginUrl);
            }
            catch (Exception ex)
            {
                _codeCompletion.TrySetException(ex);
                Close();
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (!_codeCompletion.Task.IsCompleted)
            {
                _codeCompletion.TrySetResult(null);
            }
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (TryExtractCode(e.Uri, out var code))
            {
                _codeCompletion.TrySetResult(code);
                e.Cancel = true;
                Close();
            }
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
    }
}
