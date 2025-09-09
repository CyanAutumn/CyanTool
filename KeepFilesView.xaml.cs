using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CyanTools
{
    public partial class KeepFilesView : System.Windows.Controls.UserControl
    {
        public KeepFilesView()
        {
            InitializeComponent();
        }

        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                FolderPathTextBox.Text = dialog.FileName;
            }
        }

        private void DeleteFilesButton_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = FolderPathTextBox.Text;
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                System.Windows.MessageBox.Show("请先选择一个有效的文件夹。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var extensionsToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (JpgCheckBox.IsChecked == true) extensionsToKeep.Add(".jpg");
            if (PngCheckBox.IsChecked == true) extensionsToKeep.Add(".png");
            if (WebpCheckBox.IsChecked == true) extensionsToKeep.Add(".webp");
            if (BmpCheckBox.IsChecked == true) extensionsToKeep.Add(".bmp");
            if (JpegCheckBox.IsChecked == true) extensionsToKeep.Add(".jpeg");
            if (NpzCheckBox.IsChecked == true) extensionsToKeep.Add(".npz");
            if (TxtCheckBox.IsChecked == true) extensionsToKeep.Add(".txt");

            try
            {
                var files = Directory.GetFiles(folderPath);
                int deletedCount = 0;
                LogTextBox.Clear();
                LogTextBox.Text += $"开始处理文件夹: {folderPath}\n";

                foreach (var file in files)
                {
                    var extension = Path.GetExtension(file);
                    if (!extensionsToKeep.Contains(extension))
                    {
                        File.Delete(file);
                        LogTextBox.Text += $"已删除: {file}\n";
                        deletedCount++;
                    }
                }

                LogTextBox.Text += $"操作完成。共删除了 {deletedCount} 个文件。\n";
                System.Windows.MessageBox.Show($"操作完成。共删除了 {deletedCount} 个文件。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"处理文件时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                LogTextBox.Text += $"错误: {ex.Message}\n";
            }
        }
    }
}
