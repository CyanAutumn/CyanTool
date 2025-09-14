using System.Configuration;
using System.Data;
using System.Windows;
using AutoUpdaterDotNET;

namespace CyanTools
{



    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            AutoUpdater.Start("https://raw.githubusercontent.com/CyanAutumn/CyanToolsUpdate/main/version.xml");
        }

        private void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            if (args.IsUpdateAvailable)
            {
                var dialogResult = MessageBox.Show(
                    $"发现新版本 {args.CurrentVersion} (当前版本: {args.InstalledVersion})。是否立即下载并安装？",
                    "发现新版本",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (dialogResult == MessageBoxResult.Yes)
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
            }
        }
    }
}
