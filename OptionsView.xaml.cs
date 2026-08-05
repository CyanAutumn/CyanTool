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
            Loaded += OptionsView_Loaded;
        }

        private async void OptionsView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is OptionsViewModel viewModel)
            {
                await viewModel.ProxySettings.TestNodesAsync();
            }
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

        private void BackendStatusButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string backendId })
            {
                return;
            }

            FrameworkElement? target = backendId switch
            {
                "sd-scripts" => SdScriptsDeploymentSection,
                "anima-fast-lora" => AnimaFastLoraDeploymentSection,
                "wsl-sd-scripts" => WslSdScriptsDeploymentSection,
                "diffusion-pipe" => DiffusionPipeDeploymentSection,
                "comfyui-windows" => ComfyUiWindowsDeploymentSection,
                "comfyui-wsl" => ComfyUiWslDeploymentSection,
                "forge-neo-windows" => ForgeNeoWindowsDeploymentSection,
                "forge-neo-wsl" => ForgeNeoWslDeploymentSection,
                _ => null
            };
            if (target == null)
            {
                return;
            }

            target.BringIntoView();
            target.Focus();
        }
    }
}
