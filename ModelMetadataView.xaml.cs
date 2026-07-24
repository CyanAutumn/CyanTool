using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CyanTools.Navigation;
using CyanTools.ViewModel;

namespace CyanTools
{
    [ToolPage("模型源数据", 157)]
    public partial class ModelMetadataView : UserControl
    {
        public ModelMetadataView()
        {
            InitializeComponent();
        }

        private void UserControl_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = TryGetSafetensorsPath(e, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private async void UserControl_Drop(object sender, DragEventArgs e)
        {
            if (!TryGetSafetensorsPath(e, out string? modelPath) || DataContext is not ModelMetadataViewModel viewModel)
            {
                return;
            }

            await viewModel.LoadModelAsync(modelPath);
            e.Handled = true;
        }

        private static bool TryGetSafetensorsPath(DragEventArgs e, out string? modelPath)
        {
            modelPath = null;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
                e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            {
                return false;
            }

            modelPath = files.FirstOrDefault(path =>
                File.Exists(path) &&
                string.Equals(Path.GetExtension(path), ".safetensors", System.StringComparison.OrdinalIgnoreCase));
            return !string.IsNullOrWhiteSpace(modelPath);
        }
    }
}
