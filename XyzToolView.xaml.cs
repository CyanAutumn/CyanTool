using System.Windows.Controls;
using CyanTools.Navigation;

namespace CyanTools
{
    [ToolPage("XYZ\u5DE5\u5177", 275)]
    public partial class XyzToolView : UserControl
    {
        public XyzToolView()
        {
            InitializeComponent();
            DataContext = new ViewModel.XyzToolViewModel();
        }
    }
}
