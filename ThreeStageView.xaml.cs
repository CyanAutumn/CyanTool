using System.Windows.Controls;

namespace CyanTools
{



    public partial class ThreeStageView : System.Windows.Controls.UserControl
    {
        public ThreeStageView()
        {
            InitializeComponent();
            DataContext = new ViewModel.ThreeStageViewModel();
        }
    }
}