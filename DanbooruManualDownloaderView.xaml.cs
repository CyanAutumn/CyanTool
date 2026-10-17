using System.Windows.Controls;
using CyanTools.Navigation;
using CyanTools.ViewModel;

namespace CyanTools;

[ToolPage("Danbooru 手动下载器", 291)]
public partial class DanbooruManualDownloaderView : UserControl
{
    public DanbooruManualDownloaderView()
    {
        InitializeComponent();
        DataContext = new DanbooruManualDownloaderViewModel();
    }
}

public sealed class DanbooruManualDownloaderViewModel : DatasetDownloaderViewModel
{
    public DanbooruManualDownloaderViewModel()
    {
        IsManualDownloader = true;
        SelectedMode = DatasetDownloadMode.PostId;
        EnableDanbooru = true;
        EnablePixiv = false;
        EnableKemono = false;
    }
}
