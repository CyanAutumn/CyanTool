using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("分辨率筛选", 190)]
    public partial class ResolutionFilterView : UserControl
    {
        public ResolutionFilterView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.ResolutionFilterViewModel();
            }
        }
    }
}
