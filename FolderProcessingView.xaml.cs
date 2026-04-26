using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("文件夹处理", 205)]
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
