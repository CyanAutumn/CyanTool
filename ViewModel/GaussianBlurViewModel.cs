using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class GaussianBlurViewModel : INotifyPropertyChanged, IPipelineDirectoryNode, IPipelineRunnableViewModel
    {
        private const string ToolDisplayName = "高斯模糊";

        private string _inputPath;
        private string _outputPath;
        private int _blurRadius = 5;
        private string _statusText;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;
        private bool _shouldClearInputDirectoryAfterCompletion;

        public string InputPath
        {
            get => _inputPath;
            set
            {
                if (_inputPath == value)
                {
                    return;
                }

                var previousInput = _inputPath;
                _inputPath = value;
                OnPropertyChanged();
                OutputPath = ToolOutputDirectoryDefaults.RefreshForInputChange(OutputPath, previousInput, value, ToolDisplayName);
            }
        }

        public string OutputPath
        {
            get => _outputPath;
            set { _outputPath = value; OnPropertyChanged(); }
        }

        public int BlurRadius
        {
            get => _blurRadius;
            set { _blurRadius = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set { _isWorkflowMode = value; OnPropertyChanged(); }
        }

        public bool HasInheritedInputPath
        {
            get => _hasInheritedInputPath;
            set { _hasInheritedInputPath = value; OnPropertyChanged(); }
        }

        public bool ShouldClearInputDirectoryAfterCompletion
        {
            get => _shouldClearInputDirectoryAfterCompletion;
            set { _shouldClearInputDirectoryAfterCompletion = value; OnPropertyChanged(); }
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;
        public string PipelineInputPath { get => InputPath ?? string.Empty; set => InputPath = value ?? string.Empty; }
        public string PipelineOutputPath { get => OutputPath ?? string.Empty; set => OutputPath = value ?? string.Empty; }

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand ProcessCommand { get; }

        public GaussianBlurViewModel()
        {
            SelectInputCommand = new RelayCommand(SelectInputFolder);
            SelectOutputCommand = new RelayCommand(SelectOutputFolder);
            ProcessCommand = new RelayCommand(async () => await ProcessImages());
        }

        private void SelectInputFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = LocalizationManager.Translate("选择输入路径")
            };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                InputPath = dialog.FileName;
            }
        }

        private void SelectOutputFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = LocalizationManager.Translate("选择输出路径")
            };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputPath = dialog.FileName;
            }
        }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            return ExecuteProcessingAsync(
                rethrowErrors: true,
                pipelineLog: message => context.AppendLog($"[{ToolDisplayName}] {message}"),
                token);
        }

        private Task ProcessImages()
        {
            return ExecuteProcessingAsync(rethrowErrors: false, pipelineLog: null, CancellationToken.None);
        }

        private async Task ExecuteProcessingAsync(bool rethrowErrors, Action<string>? pipelineLog, CancellationToken token)
        {
            OutputPath = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputPath, InputPath, ToolDisplayName);
            if (string.IsNullOrEmpty(InputPath) || string.IsNullOrEmpty(OutputPath))
            {
                StatusText = "请选择输入路径和输出路径。";
                pipelineLog?.Invoke(StatusText);
                if (rethrowErrors)
                {
                    throw new InvalidOperationException(StatusText);
                }

                return;
            }

            if (!Directory.Exists(InputPath))
            {
                StatusText = "输入路径不存在。";
                pipelineLog?.Invoke(StatusText);
                if (rethrowErrors)
                {
                    throw new DirectoryNotFoundException(StatusText);
                }

                return;
            }

            if (!Directory.Exists(OutputPath))
            {
                Directory.CreateDirectory(OutputPath);
            }

            StatusText = "正在处理...";
            bool success = false;
            try
            {
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var files = Directory.GetFiles(InputPath, "*.*", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        var extension = Path.GetExtension(file).ToLower();
                        if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp")
                        {
                            var outputFileName = Path.Combine(OutputPath, Path.GetFileName(file));
                            ImageProcessor.ApplyGaussianBlur(file, outputFileName, BlurRadius);
                        }
                    }
                }, token);
                success = true;
                StatusText = "处理完成。";
                pipelineLog?.Invoke(StatusText);
            }
            catch (Exception ex)
            {
                StatusText = $"处理失败: {ex.Message}";
                pipelineLog?.Invoke(StatusText);
                if (rethrowErrors)
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
            }
        }

        private void TryClearInputDirectoryAfterCompletion(Action<string>? pipelineLog)
        {
            if (!ShouldClearInputDirectoryAfterCompletion)
            {
                return;
            }

            InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.TryClearContents(InputPath, new[] { OutputPath });
            StatusText = $"{StatusText} {result.Message}".Trim();
            pipelineLog?.Invoke(result.Message);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
