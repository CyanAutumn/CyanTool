using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("同源图片归类", 245)]
    public partial class SameSourceImageGroupingView : UserControl
    {
        public SameSourceImageGroupingView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.SameSourceImageGroupingViewModel();
            }
        }
    }
}
