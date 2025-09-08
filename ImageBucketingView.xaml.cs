using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using DrawingImage = System.Drawing.Image;

namespace CyanTools
{
    public partial class ImageBucketingView : System.Windows.Controls.UserControl
    {
        private BackgroundWorker backgroundWorker;

        public ImageBucketingView()
        {
            InitializeComponent();
            InitializeBackgroundWorker();
        }

        private void InitializeBackgroundWorker()
        {
            backgroundWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            backgroundWorker.DoWork += BackgroundWorker_DoWork;
            backgroundWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
            backgroundWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;
        }

        private void BrowseInputFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择包含图片的输入文件夹"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtInputFolder.Text = dialog.SelectedPath;
            }
        }

        private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择分类后图片的输出文件夹"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtOutputFolder.Text = dialog.SelectedPath;
            }
        }

        private void StartClassification_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput())
            {
                StartClassification();
            }
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(txtInputFolder.Text))
            {
                WpfMessageBox.Show("请选择输入文件夹", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(txtInputFolder.Text))
            {
                WpfMessageBox.Show("输入文件夹不存在", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtOutputFolder.Text))
            {
                WpfMessageBox.Show("请选择输出文件夹", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var supportedExtensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif" };
            var imageFiles = supportedExtensions.SelectMany(ext =>
                Directory.GetFiles(txtInputFolder.Text, ext, SearchOption.TopDirectoryOnly)).ToArray();

            if (imageFiles.Length == 0)
            {
                WpfMessageBox.Show("在输入文件夹中未找到支持的图片文件", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }


            if (!int.TryParse(txtDivisible.Text, out int divisible) || divisible < 8 || divisible > 128)
            {
                WpfMessageBox.Show("分辨率步长必须是8-128之间的整数", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!double.TryParse(txtMaxArError.Text, out double maxArError) || maxArError < 0.1 || maxArError > 10)
            {
                WpfMessageBox.Show("最大宽高比误差必须是0.1-10之间的数值", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!int.TryParse(txtDimLimit.Text, out int dimLimit) || dimLimit < 256 || dimLimit > 4096)
            {
                WpfMessageBox.Show("最大尺寸限制必须是256-4096之间的整数", "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void StartClassification()
        {
            btnStart.IsEnabled = false;
            progressBar.IsIndeterminate = true;
            txtLog.Clear();
            LogMessage("开始图片分类...");

            var parameters = new ClassificationParameters
            {
                InputFolder = txtInputFolder.Text,
                OutputFolder = txtOutputFolder.Text,
                Divisible = int.Parse(txtDivisible.Text),
                MaxArError = double.Parse(txtMaxArError.Text),
                DimLimit = int.Parse(txtDimLimit.Text),
                Debug = chkDebug.IsChecked ?? false
            };

            backgroundWorker.RunWorkerAsync(parameters);
        }

        private void LogMessage(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
                txtLog.ScrollToEnd();
            }
            else
            {
                Dispatcher.Invoke(() => LogMessage(message));
            }
        }

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var parameters = (ClassificationParameters)e.Argument;
            var worker = sender as BackgroundWorker;

            try
            {
                worker.ReportProgress(10, "正在加载图片...");

                var imageData = new Dictionary<string, System.Drawing.Size>();
                var dicFileName = new Dictionary<string, string>();
                int fileIndex = 10000;

                var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                var imageFiles = supportedExtensions.SelectMany(ext =>
                    Directory.GetFiles(parameters.InputFolder, $"*{ext}", SearchOption.TopDirectoryOnly)).ToArray();

                int processedFiles = 0;
                foreach (var filePath in imageFiles)
                {
                    if (worker.CancellationPending)
                    {
                        e.Cancel = true;
                        return;
                    }

                    try
                    {
                        using (var image = DrawingImage.FromFile(filePath))
                        {
                            var resolution = image.Size;
                            var fileName = Path.GetFileName(filePath);
                            imageData[fileIndex.ToString()] = resolution;
                            dicFileName[fileIndex.ToString()] = fileName;
                            fileIndex++;
                        }
                    }
                    catch (Exception ex)
                    {
                        worker.ReportProgress(0, $"处理 {Path.GetFileName(filePath)} 时出错: {ex.Message}");
                    }

                    processedFiles++;
                    int progress = (int)((processedFiles / (double)imageFiles.Length) * 30) + 10;
                    worker.ReportProgress(progress, $"已处理 {processedFiles}/{imageFiles.Length} 张图片");
                }

                worker.ReportProgress(40, "正在创建分类桶...");

                var bm = new BucketManager(imageData,
                    divisible: parameters.Divisible,
                    maxArError: parameters.MaxArError,
                    dimLimit: parameters.DimLimit,
                    debug: false);

                worker.ReportProgress(60, "正在将图片分类到各个桶中...");

                int totalImages = bm.Buckets.Values.Sum(bucket => bucket.Count);
                int copiedImages = 0;

                foreach (var bucketId in bm.Buckets.Keys.OrderByDescending(b => bm.Buckets[b].Count))
                {
                    if (worker.CancellationPending)
                    {
                        e.Cancel = true;
                        return;
                    }

                    var resolution = bm.Resolutions[bucketId];
                    var outPath = Path.Combine(parameters.OutputFolder, $"{resolution.Width}x{resolution.Height}");

                    if (!Directory.Exists(outPath))
                        Directory.CreateDirectory(outPath);

                    worker.ReportProgress(0, $"正在处理分辨率 {resolution.Width}x{resolution.Height} ({bm.Buckets[bucketId].Count} 张图片)");

                    foreach (var fileIndexStr in bm.Buckets[bucketId])
                    {
                        if (worker.CancellationPending)
                        {
                            e.Cancel = true;
                            return;
                        }

                        var fileName = dicFileName[fileIndexStr];
                        var fileSrcPath = Path.Combine(parameters.InputFolder, fileName);
                        var destPath = Path.Combine(outPath, fileName);

                        try
                        {
                            File.Copy(fileSrcPath, destPath, true);
                            copiedImages++;

                            if (parameters.Debug)
                            {
                                worker.ReportProgress(0, $"复制: {fileName} -> {resolution.Width}x{resolution.Height}");
                            }
                        }
                        catch (Exception ex)
                        {
                            worker.ReportProgress(0, $"复制 {fileName} 时出错: {ex.Message}");
                        }

                        int progress = (int)((copiedImages / (double)totalImages) * 30) + 60;
                        worker.ReportProgress(progress);
                    }
                }

                e.Result = new ClassificationResult
                {
                    TotalImages = totalImages,
                    BucketCount = bm.Buckets.Count,
                    BucketManager = bm
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"分类失败: {ex.Message}", ex);
            }
        }

        private void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage > 0)
            {
                progressBar.IsIndeterminate = false;
                progressBar.Value = Math.Min(e.ProgressPercentage, 100);
            }

            if (e.UserState is string message)
            {
                LogMessage(message);
            }
        }

        private void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            progressBar.IsIndeterminate = false;
            btnStart.IsEnabled = true;

            if (e.Cancelled)
            {
                progressBar.Value = 0;
                LogMessage("分类操作被取消");
            }
            else if (e.Error != null)
            {
                progressBar.Value = 0;
                LogMessage($"错误: {e.Error.Message}");
                WpfMessageBox.Show($"分类失败: {e.Error.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                progressBar.Value = 100;
                var result = (ClassificationResult)e.Result;
                LogMessage($"分类完成!");
                LogMessage($"总计处理图片: {result.TotalImages} 张");
                LogMessage($"创建分类桶数量: {result.BucketCount} 个");
                LogMessage("");
                LogMessage("各分辨率分布:");


                foreach (var bucketId in result.BucketManager.Buckets.Keys.OrderByDescending(b => result.BucketManager.Buckets[b].Count))
                {
                    var resolution = result.BucketManager.Resolutions[bucketId];
                    var count = result.BucketManager.Buckets[bucketId].Count;
                    LogMessage($"  {resolution.Width}x{resolution.Height}: {count} 张图片");
                }

                WpfMessageBox.Show($"分类完成!\n\n总计图片: {result.TotalImages} 张\n创建分类: {result.BucketCount} 个",
                    "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private class ClassificationParameters
        {
            public string InputFolder { get; set; }
            public string OutputFolder { get; set; }
            public int Divisible { get; set; }
            public double MaxArError { get; set; }
            public int DimLimit { get; set; }
            public bool Debug { get; set; }
        }

        private class ClassificationResult
        {
            public int TotalImages { get; set; }
            public int BucketCount { get; set; }
            public BucketManager BucketManager { get; set; }
        }
    }
}