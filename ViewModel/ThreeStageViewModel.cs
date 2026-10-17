using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CyanTools.ViewModel
{
    public class ThreeStageViewModel : INotifyPropertyChanged, IPipelineDirectoryNode, IPipelineRunnableViewModel
    {
        private const string ToolDisplayName = "图片三分法";

        private string _inputFolder = "";
        private string _outputFolder = "";
        private string _progressText = "就绪";
        private double _progressPercentage = 0;
        private bool _isProcessing = false;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;
        private bool _shouldClearInputDirectoryAfterCompletion;

        private CancellationTokenSource? _cancellationTokenSource;
        private ThreeStageProcessor _processor;
        
        private const int MaxLogLines = 1000;

        public ThreeStageViewModel()
        {
            _processor = new ThreeStageProcessor();
            LogLines = new ObservableCollection<string>();

            BrowseInputFolderCommand = new RelayCommand(BrowseInputFolder);
            BrowseOutputFolderCommand = new RelayCommand(BrowseOutputFolder);
            StartProcessingCommand = new RelayCommand(StartProcessing, () => !_isProcessing && !string.IsNullOrEmpty(InputFolder));
            StopProcessingCommand = new RelayCommand(StopProcessing, () => _isProcessing);
            ClearLogCommand = new RelayCommand(ClearLog);


            PersonConfThreshold = 0.3f;
            HalfbodyConfThreshold = 0.5f;
            HeadConfThreshold = 0.4f;
            EyeConfThreshold = 0.3f;
            HeadScale = 1.5f;
            EyeScale = 2.4f;
            SplitPerson = true;
            SplitHalfbody = true;
            SplitHead = true;
            SplitEyes = false;
            KeepOriginTags = false;
            HalfbodyAppendTag = "upper body";
            HeadAppendTag = "head view";
            EyeAppendTag = "eyes view";
        }

        #region Properties

        public ObservableCollection<string> LogLines { get; }

        public string InputFolder
        {
            get => _inputFolder;
            set
            {
                if (_inputFolder == value)
                {
                    return;
                }

                var previousInput = _inputFolder;
                _inputFolder = value;
                OnPropertyChanged();
                OutputFolder = ToolOutputDirectoryDefaults.RefreshForInputChange(OutputFolder, previousInput, value, ToolDisplayName);
                (StartProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                _outputFolder = value;
                OnPropertyChanged();
                (StartProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set
            {
                _isWorkflowMode = value;
                OnPropertyChanged();
            }
        }

        public bool HasInheritedInputPath
        {
            get => _hasInheritedInputPath;
            set
            {
                _hasInheritedInputPath = value;
                OnPropertyChanged();
            }
        }

        public bool ShouldClearInputDirectoryAfterCompletion
        {
            get => _shouldClearInputDirectoryAfterCompletion;
            set
            {
                _shouldClearInputDirectoryAfterCompletion = value;
                OnPropertyChanged();
            }
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;
        public string PipelineInputPath { get => InputFolder; set => InputFolder = value ?? string.Empty; }
        public string PipelineOutputPath { get => OutputFolder; set => OutputFolder = value ?? string.Empty; }
        
        public string ProgressText
        {
            get => _progressText;
            set
            {
                _progressText = value;
                OnPropertyChanged();
            }
        }

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set
            {
                _progressPercentage = value;
                OnPropertyChanged();
            }
        }


        public bool SplitPerson { get => _processor.SplitPerson; set { _processor.SplitPerson = value; OnPropertyChanged(); } }
        public bool SplitHalfbody { get => _processor.SplitHalfbody; set { _processor.SplitHalfbody = value; OnPropertyChanged(); } }
        public bool SplitHead { get => _processor.SplitHead; set { _processor.SplitHead = value; OnPropertyChanged(); } }
        public bool SplitEyes { get => _processor.SplitEyes; set { _processor.SplitEyes = value; OnPropertyChanged(); } }
        public bool KeepOriginTags { get => _processor.KeepOriginTags; set { _processor.KeepOriginTags = value; OnPropertyChanged(); } }
        public float PersonConfThreshold { get => _processor.PersonConfThreshold; set { _processor.PersonConfThreshold = value; OnPropertyChanged(); } }
        public float HalfbodyConfThreshold { get => _processor.HalfbodyConfThreshold; set { _processor.HalfbodyConfThreshold = value; OnPropertyChanged(); } }
        public float HeadConfThreshold { get => _processor.HeadConfThreshold; set { _processor.HeadConfThreshold = value; OnPropertyChanged(); } }
        public float EyeConfThreshold { get => _processor.EyeConfThreshold; set { _processor.EyeConfThreshold = value; OnPropertyChanged(); } }
        public float HeadScale { get => _processor.HeadScale; set { _processor.HeadScale = value; OnPropertyChanged(); } }
        public float EyeScale { get => _processor.EyeScale; set { _processor.EyeScale = value; OnPropertyChanged(); } }
        public string HalfbodyAppendTag { get => _processor.HalfbodyAppendTag; set { _processor.HalfbodyAppendTag = value ?? string.Empty; OnPropertyChanged(); } }
        public string HeadAppendTag { get => _processor.HeadAppendTag; set { _processor.HeadAppendTag = value ?? string.Empty; OnPropertyChanged(); } }
        public string EyeAppendTag { get => _processor.EyeAppendTag; set { _processor.EyeAppendTag = value ?? string.Empty; OnPropertyChanged(); } }

        #endregion

        #region Commands

        public ICommand BrowseInputFolderCommand { get; }
        public ICommand BrowseOutputFolderCommand { get; }
        public ICommand StartProcessingCommand { get; }
        public ICommand StopProcessingCommand { get; }
        public ICommand ClearLogCommand { get; }

        #endregion

        #region Command Methods

        private void BrowseInputFolder()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                InputFolder = dialog.FileName;
            }
        }

        private void BrowseOutputFolder()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputFolder = dialog.FileName;
            }
        }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            return ExecuteProcessingAsync(
                showDialogs: false,
                externalToken: token,
                pipelineLog: message => context.AppendLog($"[{ToolDisplayName}] {message}"));
        }

        private async void StartProcessing()
        {
            await ExecuteProcessingAsync(showDialogs: true, externalToken: CancellationToken.None, pipelineLog: null);
        }

        private async Task ExecuteProcessingAsync(bool showDialogs, CancellationToken externalToken, Action<string>? pipelineLog)
        {
            bool success = false;
            try
            {
                OutputFolder = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputFolder, InputFolder, ToolDisplayName);
                if (string.IsNullOrWhiteSpace(InputFolder) || !Directory.Exists(InputFolder))
                {
                    const string inputInvalidMessage = "输入路径无效。";
                    AddLog(inputInvalidMessage);
                    pipelineLog?.Invoke(inputInvalidMessage);
                    ProgressText = inputInvalidMessage;
                    if (!showDialogs)
                    {
                        throw new DirectoryNotFoundException(inputInvalidMessage);
                    }

                    return;
                }

                if (string.IsNullOrWhiteSpace(OutputFolder))
                {
                    AddLog("输出路径无效。");
                    pipelineLog?.Invoke("输出路径无效。");
                    ProgressText = "输出路径无效";
                    if (!showDialogs)
                    {
                        throw new InvalidOperationException("输出路径无效。");
                    }

                    return;
                }

                _isProcessing = true;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
                CancellationToken token = _cancellationTokenSource.Token;

                (StartProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StopProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();

                ClearLog();
                ProgressPercentage = 0;
                ProgressText = "准备处理...";

                AddLog("开始三分法处理...");
                AddLog($"输入路径: {InputFolder}");
                AddLog($"输出路径: {OutputFolder}");
                pipelineLog?.Invoke("开始三分法处理...");
                pipelineLog?.Invoke($"输入路径: {InputFolder}");
                pipelineLog?.Invoke($"输出路径: {OutputFolder}");

                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
                var imageFiles = Directory.GetFiles(InputFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLower()))
                    .ToList();

                if (imageFiles.Count == 0)
                {
                    AddLog("在输入路径中未找到图片文件！");
                    pipelineLog?.Invoke("在输入路径中未找到图片文件！");
                    if (!showDialogs)
                    {
                        throw new InvalidOperationException("在输入路径中未找到图片文件！");
                    }

                    return;
                }

                AddLog($"找到 {imageFiles.Count} 个图片文件");
                pipelineLog?.Invoke($"找到 {imageFiles.Count} 个图片文件");
                Directory.CreateDirectory(OutputFolder);

                var progress = new ExecutionProgress<string>(message =>
                {
                    AddLog(message);
                    pipelineLog?.Invoke(message);
                });
                int processedCount = 0;

                await Task.Run(async () =>
                {
                    foreach (var imageFile in imageFiles)
                    {
                        token.ThrowIfCancellationRequested();

                        try
                        {
                            AddLog($"处理文件: {Path.GetFileName(imageFile)}");
                            pipelineLog?.Invoke($"处理文件: {Path.GetFileName(imageFile)}");
                            using var image = Image.Load<Rgb24>(imageFile);
                            var meta = new Dictionary<string, object> { ["filename"] = Path.GetFileName(imageFile) };
                            var inputItem = new ImageItem(image, meta);
                            var results = await _processor.ProcessImageAsync(inputItem, progress, token);

                            foreach (var result in results)
                            {
                                token.ThrowIfCancellationRequested();
                                var outputFilename = result.Meta.TryGetValue("filename", out var fn) ? fn.ToString() : $"{Path.GetFileNameWithoutExtension(imageFile)}_processed{Path.GetExtension(imageFile)}";
                                var outputPath = Path.Combine(OutputFolder, outputFilename!);
                                result.Image.Save(outputPath);
                                if (result.Meta.TryGetValue("append_tag", out var appendTagValue))
                                {
                                    AppendTagToCompanionText(outputPath, appendTagValue?.ToString());
                                }
                                result.Image.Dispose();
                            }

                            processedCount++;
                            UiDispatch.Invoke(() =>
                            {
                                ProgressPercentage = (double)processedCount / imageFiles.Count * 100;
                                ProgressText = $"已处理 {processedCount}/{imageFiles.Count} 个文件";
                                AddLog($"完成处理: {Path.GetFileName(imageFile)}");
                            });
                            pipelineLog?.Invoke($"完成处理: {Path.GetFileName(imageFile)}");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            AddLog($"处理文件 {Path.GetFileName(imageFile)} 时出错: {ex.Message}");
                            pipelineLog?.Invoke($"处理文件 {Path.GetFileName(imageFile)} 时出错: {ex.Message}");
                        }
                    }
                }, token);

                if (token.IsCancellationRequested)
                {
                    AddLog("处理已取消");
                    pipelineLog?.Invoke("处理已取消");
                    ProgressText = "处理已取消";
                }
                else
                {
                    AddLog($"处理完成！共处理 {processedCount} 个文件");
                    pipelineLog?.Invoke($"处理完成！共处理 {processedCount} 个文件");
                    ProgressText = $"处理完成 ({processedCount}/{imageFiles.Count})";
                    ProgressPercentage = 100;
                    success = true;
                }
            }
            catch (OperationCanceledException)
            {
                AddLog("处理已取消");
                pipelineLog?.Invoke("处理已取消");
                ProgressText = "处理已取消";
                if (!showDialogs)
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                AddLog($"处理过程中发生错误: {ex.Message}");
                pipelineLog?.Invoke($"处理过程中发生错误: {ex.Message}");
                if (showDialogs)
                {
                    MessageBox.Show($"处理过程中发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                if (success)
                {
                    TryClearInputDirectoryAfterCompletion(pipelineLog);
                }

                _isProcessing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                (StartProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StopProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void TryClearInputDirectoryAfterCompletion(Action<string>? pipelineLog)
        {
            if (!ShouldClearInputDirectoryAfterCompletion)
            {
                return;
            }

            InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.TryClearContents(InputFolder, new[] { OutputFolder });
            AddLog(result.Message);
            pipelineLog?.Invoke(result.Message);
        }

        private void StopProcessing()
        {
            _cancellationTokenSource?.Cancel();
            AddLog("正在停止处理...");
        }

        private void ClearLog()
        {
            LogLines.Clear();
        }

        #endregion

        private static void AppendTagToCompanionText(string imagePath, string? appendTag)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(appendTag))
            {
                return;
            }

            string tag = appendTag.Trim();
            string txtPath = Path.ChangeExtension(imagePath, ".txt");
            if (string.IsNullOrWhiteSpace(txtPath))
            {
                return;
            }

            if (!File.Exists(txtPath))
            {
                File.WriteAllText(txtPath, tag);
                return;
            }

            string content = File.ReadAllText(txtPath).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                File.WriteAllText(txtPath, tag);
                return;
            }

            var tags = content
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            bool exists = tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                tags.Add(tag);
            }

            File.WriteAllText(txtPath, string.Join(", ", tags));
        }

        private void AddLog(string message)
        {
            UiDispatch.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                LogLines.Add($"[{timestamp}] {message}");
                if (LogLines.Count > MaxLogLines)
                {
                    LogLines.RemoveAt(0);
                }
            });
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
