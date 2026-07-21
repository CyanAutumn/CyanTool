using CyanTools.Navigation;
using CyanTools.ViewModel;
using System.Windows.Controls;

namespace CyanTools
{
    [ToolPage("训练桥", 157)]
    public partial class TrainingBridgeView : UserControl
    {
        public TrainingBridgeView()
        {
            InitializeComponent();
            DataContext = new TrainingBridgeViewModel();
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
