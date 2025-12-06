using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("图片超分", 110)]
    public partial class RealEsrganView : UserControl
    {
        public RealEsrganView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.RealEsrganViewModel();
            }
        }
    }
}
