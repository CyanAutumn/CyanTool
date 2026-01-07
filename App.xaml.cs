using System.Configuration;
using System.Data;
using System.Net.Http;
using System.Windows;
using System.Threading.Tasks;
using AutoUpdaterDotNET;

namespace CyanTools
{



    public partial class App : System.Windows.Application
    {
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

            var releaseNotes = await TryFetchReleaseNotesAsync(args.ChangelogURL);
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

        private static async Task<string> TryFetchReleaseNotesAsync(string changelogUrl)
        {
            if (string.IsNullOrWhiteSpace(changelogUrl))
            {
                return string.Empty;
            }

            try
            {
                using var response = await UpdateHttpClient.GetAsync(changelogUrl);
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
    }
}
