using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CyanTools.Navigation;
using CyanTools.ViewModel;

namespace CyanTools
{
    [ToolPage("Gemini图像生成", 310)]
    public partial class GeminiImageGenerationView : UserControl
    {
        public GeminiImageGenerationView()
        {
            InitializeComponent();
        }

        private void OnInputImageDragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnInputImageDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
            {
                return;
            }

            if (DataContext is GeminiImageGenerationViewModel viewModel)
            {
                viewModel.AddInputImages(files);
            }

            e.Handled = true;
        }

        private void OnResultImageClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is GeminiImageGenerationViewModel viewModel)
            {
                viewModel.OpenLastOutputFolder();
            }
        }
    }
}
