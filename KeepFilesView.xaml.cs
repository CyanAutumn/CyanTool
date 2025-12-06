using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("保留指定文件", 200)]
    public partial class KeepFilesView : UserControl
    {
        public KeepFilesView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.KeepFilesViewModel();
            }
        }
    }
}
