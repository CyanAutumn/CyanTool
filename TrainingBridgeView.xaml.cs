using CyanTools.Navigation;
using CyanTools.ViewModel;
using System.Windows.Controls;

namespace CyanTools
{
    [ToolPage("训练", 157)]
    public partial class TrainingBridgeView : UserControl
    {
        public TrainingBridgeView()
        {
            InitializeComponent();
            DataContext = new TrainingBridgeViewModel();
            Loaded += TrainingBridgeView_Loaded;
            Unloaded += TrainingBridgeView_Unloaded;
        }

        private void TrainingBridgeView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is TrainingBridgeViewModel viewModel)
            {
                viewModel.StartMonitoring();
                viewModel.StartObservingSystemMonitorWidget();
            }
        }

        private void TrainingBridgeView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is TrainingBridgeViewModel viewModel)
            {
                viewModel.StopMonitoring();
                viewModel.StopObservingSystemMonitorWidget();
            }
        }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.ScrollToEnd();
            }
        }
    }
}
