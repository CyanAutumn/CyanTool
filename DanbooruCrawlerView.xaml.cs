using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("数据集管理器", 290)]
    public partial class DanbooruCrawlerView : UserControl
    {
        public DanbooruCrawlerView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.DatasetDownloaderViewModel();
            }
            if (DataContext is ViewModel.DatasetDownloaderViewModel library)
                foreach (var item in library.DownloadTasks)
                    try { item.RefreshFolders(); } catch (System.Exception ex) { item.StatusText = ex.Message; }
        }

        private void OpenMaterialDirectory_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: ViewModel.DatasetDownloadTask item }) return;
            try
            {
                if (!System.IO.Directory.Exists(item.MaterialDirectory))
                {
                    item.StatusText = "素材目录不存在";
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.IO.Path.GetFullPath(item.MaterialDirectory),
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex) { item.StatusText = ex.Message; }
        }

        private void ClassifyMaterial_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: ViewModel.DatasetDownloadTask item } menu) return;
            if (!System.Enum.TryParse<Utils.MaterialClassificationTool>(menu.Tag?.ToString(), out var tool)) return;
            try
            {
                if (item.IsRunning) { item.StatusText = "请等待下载完成。"; return; }
                if (System.Windows.Window.GetWindow(this) is MainWindow window)
                    window.NavigateToTool(Utils.MaterialClassificationNavigation.PageType(tool), page =>
                        Utils.MaterialClassificationNavigation.Open(page, item.MaterialDirectory, tool,
                            () => window.NavigateToTool(typeof(DanbooruCrawlerView))));
            }
            catch (System.Exception ex) { item.StatusText = ex.Message; }
        }

        private async void RestoreMaterial_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: ViewModel.DatasetDownloadTask item }) return;
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    using var lease = Utils.MaterialClassificationStore.Acquire(item.MaterialDirectory);
                    lease.Restore();
                });
                item.StatusText = "图片已还原到原图目录。";
                item.RefreshFolders();
            }
            catch (System.Exception ex) { item.StatusText = ex.Message; }
        }

        private void ConvertMaterial_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: ViewModel.DatasetDownloadTask item }) return;
            try
            {
                if (Utils.MaterialClassificationStore.HasActiveView(item.MaterialDirectory))
                {
                    item.StatusText = "请先使用右键菜单还原原图，再执行格式转换。";
                    return;
                }
                if (!System.IO.Directory.Exists(item.SelectedFolderPath))
                {
                    item.StatusText = "所选素材文件夹不存在";
                    return;
                }
                if (System.Windows.Window.GetWindow(this) is MainWindow window)
                    window.OpenMaterialFormatConversion(item.SelectedFolderPath, item.MaterialDirectory);
            }
            catch (System.Exception ex) { item.StatusText = ex.Message; }
        }
    }
}
