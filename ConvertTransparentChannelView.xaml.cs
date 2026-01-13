using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("转换透明通道", 255)]
    public partial class ConvertTransparentChannelView : UserControl
    {
        public ConvertTransparentChannelView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.ConvertTransparentChannelViewModel();
            }
        }
    }
}
