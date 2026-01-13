using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("格式转换", 250)]
    public partial class ConvertImageFormatView : UserControl
    {
        public ConvertImageFormatView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.ConvertImageFormatViewModel();
            }
        }
    }
}
