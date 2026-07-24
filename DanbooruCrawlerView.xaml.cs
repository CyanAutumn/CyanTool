using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("数据集下载器", 290)]
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
        }
    }
}
