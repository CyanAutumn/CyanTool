using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("手指完整性筛选", 195)]
    public partial class HandIntegrityFilterView : UserControl
    {
        public HandIntegrityFilterView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.HandIntegrityFilterViewModel();
            }
        }
    }
}
