using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CyanTools.Navigation;
using CyanTools.ViewModel;

namespace CyanTools
{
    [ToolPage("图片元数据", 220)]
    public partial class ImageMetadataView : UserControl
    {
        public ImageMetadataView()
        {
            InitializeComponent();
        }

        private void UserControl_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void UserControl_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
            {
                return;
            }

            string imagePath = files.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(imagePath) || !IsImageFile(imagePath))
            {
                MessageBox.Show("请拖入 PNG/JPG/JPEG/BMP/GIF 格式的图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (DataContext is ImageMetadataViewModel viewModel)
            {
                viewModel.LoadImage(imagePath);
            }

            e.Handled = true;
        }

        private bool IsImageFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".gif";
        }
    }
}
