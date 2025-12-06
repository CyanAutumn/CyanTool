using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("高斯模糊", 230)]
    public partial class GaussianBlurView : UserControl
    {
        public GaussianBlurView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.GaussianBlurViewModel();
            }
        }
    }
}
