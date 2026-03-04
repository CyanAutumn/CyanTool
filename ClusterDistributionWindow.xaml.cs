using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CyanTools
{
    public partial class ClusterDistributionWindow : Window
    {
        private readonly string _outputRoot;

        public ClusterDistributionWindow(string imagePath, string outputRoot, string summary)
        {
            InitializeComponent();
            _outputRoot = outputRoot;
            SummaryTextBlock.Text = summary;
            ImagePathTextBlock.Text = imagePath;
            LoadDistributionImage(imagePath);
        }

        private void LoadDistributionImage(string imagePath)
        {
            if (!File.Exists(imagePath))
            {
                SummaryTextBlock.Text += "\n分布图文件不存在。";
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                DistributionImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载分布图失败: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _outputRoot,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开结果目录失败: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            Close();
        }
    }
}
