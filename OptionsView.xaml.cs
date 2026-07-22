using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CyanTools.Navigation;
using CyanTools.ViewModel;

namespace CyanTools
{
    [ToolPage("选项", 900)]
    public partial class OptionsView : UserControl
    {
        public OptionsView()
        {
            InitializeComponent();
            DataContext = new OptionsViewModel();
        }

        private void CudnnPackage_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = TryGetDroppedZipPath(e, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private async void CudnnPackage_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is not OptionsViewModel viewModel ||
                !TryGetDroppedZipPath(e, out string packagePath))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            await viewModel.InstallCudnnPackageAsync(packagePath);
        }

        private static bool TryGetDroppedZipPath(DragEventArgs e, out string packagePath)
        {
            packagePath = string.Empty;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
                e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            {
                return false;
            }

            packagePath = files.FirstOrDefault(file =>
                file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(packagePath);
        }

        private void DeploymentLogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.ScrollToEnd();
            }
        }
    }
}
