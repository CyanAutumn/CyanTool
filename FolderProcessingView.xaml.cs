using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("\u6587\u4EF6\u5939\u5904\u7406", 205)]
    public partial class FolderProcessingView : UserControl
    {
        public FolderProcessingView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.FolderProcessingViewModel();
            }
        }
    }
}
