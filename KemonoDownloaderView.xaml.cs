using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("Kemono下载器", 300, true)]
    public partial class KemonoDownloaderView : UserControl
    {
        public KemonoDownloaderView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.KemonoDownloaderViewModel();
            }
        }
    }
}
