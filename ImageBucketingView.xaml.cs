using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("图片分辨率归类", 170)]
    public partial class ImageBucketingView : UserControl
    {
        public ImageBucketingView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.ImageBucketingViewModel();
            }
        }
    }
}
