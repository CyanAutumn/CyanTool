using System.Windows.Controls;
using System.Windows;
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
            Loaded += ProxySettingsView_Loaded;
        }

        private async void ProxySettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            await ProxySettingsStore.Shared.TestNodesAsync();
        }
    }
}
