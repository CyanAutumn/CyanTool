using Microsoft.WindowsAPICodePack.Dialogs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public class ImageBucketingViewModel : INotifyPropertyChanged, IPipelineDirectoryNode, IPipelineRunnableViewModel
    {
        private const string ToolDisplayName = "图片分辨率归类";

        private readonly BackgroundWorker _worker;
        private readonly StringBuilder _logBuilder = new StringBuilder();

        private string _inputFolder;
        private string _outputFolder;
        private string _divisibleText = "64";
        private string _maxArErrorText = "1.0";
        private string _dimLimitText = "2048";
        private bool _isDebugEnabled;
        private string _logText = string.Empty;
        private double _progressValue;
        private bool _isProgressIndeterminate;
        private bool _isProcessing;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;
        private bool _shouldClearInputDirectoryAfterCompletion;

        public ImageBucketingViewModel()
        {
            BrowseInputFolderCommand = new RelayCommand(BrowseInputFolder, () => !IsProcessing);
            BrowseOutputFolderCommand = new RelayCommand(BrowseOutputFolder, () => !IsProcessing);
            StartClassificationCommand = new RelayCommand(StartClassification, () => !IsProcessing);

            _worker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = false
            };
            _worker.DoWork += Worker_DoWork;
            _worker.ProgressChanged += Worker_ProgressChanged;
            _worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
        }

        public string InputFolder
        {
            get => _inputFolder;
            set
            {
                if (_inputFolder != value)
                {
                    var previousInput = _inputFolder;
                    _inputFolder = value;
                    OnPropertyChanged(nameof(InputFolder));
                    OutputFolder = ToolOutputDirectoryDefaults.RefreshForInputChange(OutputFolder, previousInput, value, ToolDisplayName);
                }
            }
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                if (_outputFolder != value)
                {
                    _outputFolder = value;
                    OnPropertyChanged(nameof(OutputFolder));
                }
            }
        }

        public string DivisibleText
        {
            get => _divisibleText;
            set
            {
                if (_divisibleText != value)
                {
                    _divisibleText = value;
                    OnPropertyChanged(nameof(DivisibleText));
                }
            }
        }

        public string MaxArErrorText
        {
            get => _maxArErrorText;
            set
            {
                if (_maxArErrorText != value)
                {
                    _maxArErrorText = value;
                    OnPropertyChanged(nameof(MaxArErrorText));
                }
            }
        }

        public string DimLimitText
        {
            get => _dimLimitText;
            set
            {
                if (_dimLimitText != value)
                {
                    _dimLimitText = value;
                    OnPropertyChanged(nameof(DimLimitText));
                }
            }
        }

        public bool IsDebugEnabled
        {
            get => _isDebugEnabled;
            set
            {
                if (_isDebugEnabled != value)
                {
                    _isDebugEnabled = value;
                    OnPropertyChanged(nameof(IsDebugEnabled));
                }
            }
        }

        public string LogText
        {
            get => _logText;
            private set
            {
                if (_logText != value)
                {
                    _logText = value;
                    OnPropertyChanged(nameof(LogText));
                }
            }
        }

        public double ProgressValue
        {
            get => _progressValue;
            private set
            {
                if (Math.Abs(_progressValue - value) > double.Epsilon)
                {
                    _progressValue = value;
                    OnPropertyChanged(nameof(ProgressValue));
                }
            }
        }

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            private set
            {
                if (_isProgressIndeterminate != value)
                {
                    _isProgressIndeterminate = value;
                    OnPropertyChanged(nameof(IsProgressIndeterminate));
                }
            }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                if (_isProcessing != value)
                {
                    _isProcessing = value;
                    OnPropertyChanged(nameof(IsProcessing));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set
            {
                if (_isWorkflowMode != value)
                {
                    _isWorkflowMode = value;
                    OnPropertyChanged(nameof(IsWorkflowMode));
                }
            }
        }

        public bool HasInheritedInputPath
        {
            get => _hasInheritedInputPath;
            set
            {
                if (_hasInheritedInputPath != value)
                {
                    _hasInheritedInputPath = value;
                    OnPropertyChanged(nameof(HasInheritedInputPath));
                }
            }
        }

        public bool ShouldClearInputDirectoryAfterCompletion
        {
            get => _shouldClearInputDirectoryAfterCompletion;
            set
            {
                if (_shouldClearInputDirectoryAfterCompletion != value)
                {
                    _shouldClearInputDirectoryAfterCompletion = value;
                    OnPropertyChanged(nameof(ShouldClearInputDirectoryAfterCompletion));
                }
            }
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;
        public string PipelineInputPath { get => InputFolder ?? string.Empty; set => InputFolder = value ?? string.Empty; }
        public string PipelineOutputPath { get => OutputFolder ?? string.Empty; set => OutputFolder = value ?? string.Empty; }

        public ICommand BrowseInputFolderCommand { get; }
        public ICommand BrowseOutputFolderCommand { get; }
        public ICommand StartClassificationCommand { get; }

        private void BrowseInputFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择输入路径"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                InputFolder = dialog.FileName;
            }
        }

        private void BrowseOutputFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择输出路径"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputFolder = dialog.FileName;
            }
        }

        private void StartClassification()
        {
            OutputFolder = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputFolder, InputFolder, ToolDisplayName);
            if (!ValidateInput(showDialogs: true, out var parameters, out _))
            {
                return;
            }

            Directory.CreateDirectory(parameters.OutputFolder);
            _logBuilder.Clear();
            LogText = string.Empty;
            ProgressValue = 0;
            IsProgressIndeterminate = true;
            IsProcessing = true;
            AppendLog("开始图片分类...");
            _worker.RunWorkerAsync(parameters);
        }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            return ExecutePipelineClassificationAsync(
                message => context.AppendLog($"[{ToolDisplayName}] {message}"),
                token);
        }

        private async Task ExecutePipelineClassificationAsync(Action<string>? pipelineLog, CancellationToken token)
        {
            OutputFolder = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputFolder, InputFolder, ToolDisplayName);
            if (!ValidateInput(showDialogs: false, out var parameters, out string errorMessage))
            {
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    AppendLog(errorMessage);
                    pipelineLog?.Invoke(errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                return;
            }

            Directory.CreateDirectory(parameters.OutputFolder);
            _logBuilder.Clear();
            LogText = string.Empty;
            ProgressValue = 0;
            IsProgressIndeterminate = true;
            IsProcessing = true;
            AppendLog("开始图片分类...");
            pipelineLog?.Invoke("开始图片分类...");
            bool success = false;

            try
            {
                token.ThrowIfCancellationRequested();
                ClassificationResult result = await Task.Run(() =>
                    RunClassificationWork(parameters, (progress, message) =>
                    {
                        if (progress > 0)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                IsProgressIndeterminate = false;
                                ProgressValue = Math.Min(progress, 100);
                            });
                        }

                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            AppendLog(message);
                            pipelineLog?.Invoke(message);
                        }
                    }), token);

                IsProgressIndeterminate = false;
                ProgressValue = 100;
                LogClassificationSummary(result);
                pipelineLog?.Invoke("分类完成。");
                success = true;
            }
            finally
            {
                if (success)
                {
                    TryClearInputDirectoryAfterCompletion(pipelineLog);
                }

                IsProcessing = false;
                IsProgressIndeterminate = false;
            }
        }

        private bool ValidateInput(bool showDialogs, out ClassificationParameters parameters, out string errorMessage)
        {
            parameters = null;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(InputFolder))
            {
                errorMessage = "请选择输入路径。";
            }
            else if (!Directory.Exists(InputFolder))
            {
                errorMessage = "输入路径不存在。";
            }
            else if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                errorMessage = "请选择输出路径。";
            }
            else
            {
                var supportedExtensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif" };
                var imageFiles = supportedExtensions.SelectMany(ext =>
                    Directory.GetFiles(InputFolder, ext, SearchOption.TopDirectoryOnly)).ToArray();

                if (imageFiles.Length == 0)
                {
                    errorMessage = "在输入路径中未找到支持的图片文件。";
                }
                else if (!int.TryParse(DivisibleText, out int divisible) || divisible < 8 || divisible > 128)
                {
                    errorMessage = "分辨率步长必须是 8-128 之间的整数。";
                }
                else if (!double.TryParse(MaxArErrorText, out double maxArError) || maxArError < 0.1 || maxArError > 10)
                {
                    errorMessage = "最大宽高比误差必须是 0.1-10 之间的数值。";
                }
                else if (!int.TryParse(DimLimitText, out int dimLimit) || dimLimit < 256 || dimLimit > 4096)
                {
                    errorMessage = "最大尺寸限制必须是 256-4096 之间的整数。";
                }
                else
                {
                    parameters = new ClassificationParameters
                    {
                        InputFolder = InputFolder,
                        OutputFolder = OutputFolder,
                        Divisible = divisible,
                        MaxArError = maxArError,
                        DimLimit = dimLimit,
                        Debug = IsDebugEnabled
                    };
                }
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                if (showDialogs)
                {
                    MessageBox.Show(errorMessage, "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return false;
            }

            return true;
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            var parameters = (ClassificationParameters)e.Argument;
            var worker = (BackgroundWorker)sender;

            try
            {
                e.Result = RunClassificationWork(parameters, (progress, message) => worker.ReportProgress(progress, message));
            }
            catch (Exception ex)
            {
                throw new Exception($"分类失败: {ex.Message}", ex);
            }
        }

        private ClassificationResult RunClassificationWork(ClassificationParameters parameters, Action<int, string?> reportProgress)
        {
            reportProgress(10, "正在加载图片...");
            var imageData = new Dictionary<string, SixLabors.ImageSharp.Size>();
            var dicFileName = new Dictionary<string, string>();
            int fileIndex = 10000;

            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            var imageFiles = supportedExtensions.SelectMany(ext =>
                Directory.GetFiles(parameters.InputFolder, $"*{ext}", SearchOption.TopDirectoryOnly)).ToArray();

            int processedFiles = 0;
            foreach (var filePath in imageFiles)
            {
                try
                {
                    using var image = Image.Load<Rgba32>(filePath);
                    imageData[fileIndex.ToString()] = image.Size;
                    dicFileName[fileIndex.ToString()] = Path.GetFileName(filePath);
                    fileIndex++;
                }
                catch (Exception ex)
                {
                    reportProgress(0, $"处理 {Path.GetFileName(filePath)} 时出错: {ex.Message}");
                }

                processedFiles++;
                int progress = (int)((processedFiles / (double)imageFiles.Length) * 30) + 10;
                reportProgress(progress, $"已处理 {processedFiles}/{imageFiles.Length} 张图片");
            }

            reportProgress(40, "正在创建分类桶...");

            var bucketManager = new BucketManager(imageData,
                divisible: parameters.Divisible,
                maxArError: parameters.MaxArError,
                dimLimit: parameters.DimLimit,
                debug: parameters.Debug);

            reportProgress(60, "正在将图片分类到各个桶中...");

            int totalImages = bucketManager.Buckets.Values.Sum(bucket => bucket.Count);
            int copiedImages = 0;

            foreach (var bucketId in bucketManager.Buckets.Keys.OrderByDescending(b => bucketManager.Buckets[b].Count))
            {
                var resolution = bucketManager.Resolutions[bucketId];
                var outPath = Path.Combine(parameters.OutputFolder, $"{resolution.Width}x{resolution.Height}");
                Directory.CreateDirectory(outPath);

                reportProgress(0, $"正在处理分辨率 {resolution.Width}x{resolution.Height} ({bucketManager.Buckets[bucketId].Count} 张图片)");

                foreach (var fileIndexStr in bucketManager.Buckets[bucketId])
                {
                    var fileName = dicFileName[fileIndexStr];
                    var fileSrcPath = Path.Combine(parameters.InputFolder, fileName);
                    var destPath = Path.Combine(outPath, fileName);

                    try
                    {
                        File.Copy(fileSrcPath, destPath, true);
                        copiedImages++;

                        if (parameters.Debug)
                        {
                            reportProgress(0, $"复制: {fileName} -> {resolution.Width}x{resolution.Height}");
                        }
                    }
                    catch (Exception ex)
                    {
                        reportProgress(0, $"复制 {fileName} 时出错: {ex.Message}");
                    }

                    int progress = totalImages == 0 ? 100 : (int)((copiedImages / (double)totalImages) * 30) + 60;
                    reportProgress(progress, null);
                }
            }

            return new ClassificationResult
            {
                TotalImages = totalImages,
                BucketCount = bucketManager.Buckets.Count,
                BucketManager = bucketManager
            };
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage > 0)
            {
                IsProgressIndeterminate = false;
                ProgressValue = Math.Min(e.ProgressPercentage, 100);
            }

            if (e.UserState is string message && !string.IsNullOrWhiteSpace(message))
            {
                AppendLog(message);
            }
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            IsProcessing = false;
            IsProgressIndeterminate = false;

            if (e.Cancelled)
            {
                ProgressValue = 0;
                AppendLog("分类操作被取消。");
                return;
            }

            if (e.Error != null)
            {
                ProgressValue = 0;
                AppendLog($"错误: {e.Error.Message}");
                MessageBox.Show($"分类失败: {e.Error.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ProgressValue = 100;
            if (e.Result is ClassificationResult result)
            {
                LogClassificationSummary(result);
                TryClearInputDirectoryAfterCompletion(null);

                MessageBox.Show($"分类完成!\n\n总计图片: {result.TotalImages} 张\n创建分类: {result.BucketCount} 个",
                    "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void TryClearInputDirectoryAfterCompletion(Action<string>? pipelineLog)
        {
            if (!ShouldClearInputDirectoryAfterCompletion)
            {
                return;
            }

            InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.TryClearContents(InputFolder, new[] { OutputFolder });
            AppendLog(result.Message);
            pipelineLog?.Invoke(result.Message);
        }

        private void LogClassificationSummary(ClassificationResult result)
        {
            AppendLog("分类完成!");
            AppendLog($"总计处理图片: {result.TotalImages} 张");
            AppendLog($"创建分类桶数量: {result.BucketCount} 个");
            AppendLog("");
            AppendLog("各分辨率分布:");

            foreach (var bucketId in result.BucketManager.Buckets.Keys.OrderByDescending(b => result.BucketManager.Buckets[b].Count))
            {
                var resolution = result.BucketManager.Resolutions[bucketId];
                var count = result.BucketManager.Buckets[bucketId].Count;
                AppendLog($"  {resolution.Width}x{resolution.Height}: {count} 张图片");
            }
        }

        private void AppendLog(string message)
        {
            void Update()
            {
                _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                LogText = _logBuilder.ToString();
            }

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                Update();
            }
            else
            {
                Application.Current?.Dispatcher?.Invoke(Update);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
