using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("图片聚类", 240)]
    public partial class ImageClusteringView : UserControl
    {
        public ImageClusteringView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.ImageClusteringViewModel();
            }
        }
    }
}
