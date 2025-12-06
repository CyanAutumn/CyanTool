using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("图片三分法", 180)]
    public partial class ThreeStageView : UserControl
    {
        public ThreeStageView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext == null)
            {
                DataContext = new ViewModel.ThreeStageViewModel();
            }
        }
    }
}
