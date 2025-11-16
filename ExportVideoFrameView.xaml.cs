using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CyanTools.Navigation;
using CyanTools.ViewModel;

namespace CyanTools
{
    [ToolPage("导出视频帧", 210)]
    public partial class ExportVideoFrameView : UserControl
    {
        public ExportVideoFrameView()
        {
            InitializeComponent();
        }

        private void FrameIntervalTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {

            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ExportVideoFrameViewModel viewModel)
            {
                viewModel.Dispose();
            }
        }
    }
}
