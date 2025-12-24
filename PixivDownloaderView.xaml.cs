using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("Pixiv下载器", 295, true)]
    public partial class PixivDownloaderView : UserControl
    {
        public PixivDownloaderView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.PixivDownloaderViewModel();
            }
        }
    }
}
