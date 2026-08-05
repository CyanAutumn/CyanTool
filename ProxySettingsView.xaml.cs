using System.Windows.Controls;
using CyanTools.Navigation;
using CyanTools.Utils;

namespace CyanTools
{
    [ToolPage("代理设置", 895)]
    public partial class ProxySettingsView : UserControl
    {
        public ProxySettingsView()
        {
            InitializeComponent();
            DataContext = ProxySettingsStore.Shared;
        }
    }
}
