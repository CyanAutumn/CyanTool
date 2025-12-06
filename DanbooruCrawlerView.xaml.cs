using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("Danbooru下载器", 290, true)]
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
                DataContext = new ViewModel.DanbooruCrawlerViewModel();
            }
        }
    }
}
