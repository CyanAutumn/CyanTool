using System.Configuration;
using System.Data;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Threading.Tasks;
using AutoUpdaterDotNET;

namespace CyanTools
{



    public partial class App : System.Windows.Application
    {
        private const string UpdateRepo = "CyanAutumn/CyanToolsUpdate";
        private static readonly HttpClient UpdateHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            AutoUpdater.Start("https://raw.githubusercontent.com/CyanAutumn/CyanToolsUpdate/main/version.xml");
        }

        private async void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            if (args.Error != null)
            {
                MessageBox.Show(args.Error.Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!args.IsUpdateAvailable)
            {
                return;
            }

            var releaseNotes = await TryFetchReleaseNotesAsync(
                args.ChangelogURL,
                args.CurrentVersion?.ToString(),
                args.DownloadURL);
            var isMandatory = args.Mandatory != null && args.Mandatory.Value;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = new UpdateWindow(
                    args.CurrentVersion?.ToString() ?? string.Empty,
                    args.InstalledVersion?.ToString() ?? string.Empty,
                    releaseNotes,
                    isMandatory);

                var result = window.ShowDialog();
                if (result == true)
                {
                    try
                    {
                        if (AutoUpdater.DownloadUpdate(args))
                        {
                            Application.Current.Shutdown();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, ex.GetType().ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            });
        }

        private static async Task<string> TryFetchReleaseNotesAsync(string? changelogUrl, string? versionTag, string? downloadUrl)
        {
            var directNotes = await TryFetchTextAsync(changelogUrl);
            if (!string.IsNullOrWhiteSpace(directNotes))
            {
                return directNotes;
            }

            var tag = TryDeriveTagFromDownloadUrl(downloadUrl);
            if (string.IsNullOrWhiteSpace(tag))
            {
                tag = versionTag;
            }

            if (string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            return await TryFetchReleaseNotesFromGitHubAsync(UpdateRepo, tag);
        }

        private static async Task<string> TryFetchTextAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            try
            {
                using var response = await UpdateHttpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return string.Empty;
                }

                return await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static async Task<string> TryFetchReleaseNotesFromGitHubAsync(string repo, string tag)
        {
            try
            {
                var apiUrl = $"https://api.github.com/repos/{repo}/releases/tags/{Uri.EscapeDataString(tag)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.UserAgent.ParseAdd("CyanToolsUpdater/1.0");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var response = await UpdateHttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return string.Empty;
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                if (doc.RootElement.TryGetProperty("body", out var bodyElement) &&
                    bodyElement.ValueKind == JsonValueKind.String)
                {
                    return bodyElement.GetString() ?? string.Empty;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string? TryDeriveTagFromDownloadUrl(string? downloadUrl)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return null;
            }

            try
            {
                var uri = new Uri(downloadUrl);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < segments.Length - 1; i++)
                {
                    if (string.Equals(segments[i], "download", StringComparison.OrdinalIgnoreCase) &&
                        i + 1 < segments.Length)
                    {
                        return segments[i + 1];
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }
    }
}
