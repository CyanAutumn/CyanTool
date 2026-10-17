using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class SameSourceImageGroupingViewModel : PipelineNodeViewModelBase, IPipelineDirectoryNode, IPipelineOutputPortProvider
    {
        public override string DisplayName => "同源图片归类";
        private const string DefaultOutputFolderName = "同源";
        private static readonly IReadOnlyList<PipelineOutputPort> OutputPorts =
        [
            new("root", "输出"),
            new("original", "原图", "原图"),
            new("thumbnail", "缩略图", "缩略图"),
            new("duplicate", "副本", "副本"),
            new("contained", "被包含", "被包含"),
            new("difference", "差分", "差分"),
            new("other", "其他", "其他")
        ];

        private string _inputPath = string.Empty;
        public string InputPath
        {
            get => _inputPath;
            set
            {
                string previousInput = _inputPath;
                if (SetField(ref _inputPath, value))
                {
                    OutputPath = ToolOutputDirectoryDefaults.RefreshForInputChange(OutputPath, previousInput, value, DefaultOutputFolderName);
                    RefreshCommandStates();
                }
            }
        }

        private string _outputPath = string.Empty;
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (SetField(ref _outputPath, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        private const string DefaultBackend = "cpu";
        private readonly string[] _backendOptions = { "cpu" };

        private string _computeBackend = DefaultBackend;
        public string ComputeBackend
        {
            get => _computeBackend;
            set => SetField(ref _computeBackend, DefaultBackend);
        }

        public IReadOnlyList<string> ComputeBackendOptions => _backendOptions;

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetField(ref _progress, value);
        }

        private string _log = string.Empty;
        public string Log
        {
            get => _log;
            set => SetField(ref _log, value);
        }

        private bool _isProcessing;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;
        private bool _shouldClearInputDirectoryAfterCompletion;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetField(ref _isProcessing, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set => SetField(ref _isWorkflowMode, value);
        }

        public bool HasInheritedInputPath
        {
            get => _hasInheritedInputPath;
            set => SetField(ref _hasInheritedInputPath, value);
        }

        public bool ShouldClearInputDirectoryAfterCompletion
        {
            get => _shouldClearInputDirectoryAfterCompletion;
            set => SetField(ref _shouldClearInputDirectoryAfterCompletion, value);
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;
        public string PipelineInputPath { get => InputPath; set => InputPath = value ?? string.Empty; }
        public string PipelineOutputPath { get => OutputPath; set => OutputPath = value ?? string.Empty; }

        public IReadOnlyList<PipelineOutputPort> GetPipelineOutputPorts() => OutputPorts;

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand StartCommand { get; }

        public SameSourceImageGroupingViewModel()
        {
            SelectInputCommand = new RelayCommand(SelectInputFolder, () => !IsProcessing);
            SelectOutputCommand = new RelayCommand(SelectOutputFolder, () => !IsProcessing);
            StartCommand = new RelayCommand(async () => await RunGroupingAsync(showDialogs: true, CancellationToken.None),
                () => !string.IsNullOrEmpty(InputPath) && !IsProcessing);
        }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            _ = context;
            return RunGroupingAsync(showDialogs: false, token);
        }

        public override PipelineNodeViewModelBase Clone()
        {
            return new SameSourceImageGroupingViewModel
            {
                InputPath = InputPath,
                OutputPath = OutputPath,
                ComputeBackend = ComputeBackend,
                ShouldClearInputDirectoryAfterCompletion = ShouldClearInputDirectoryAfterCompletion
            };
        }

        private void RefreshCommandStates()
        {
            (SelectInputCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SelectOutputCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void AppendLog(string message)
        {
            void Update() => Log += $"{DateTime.Now:HH:mm:ss} {message}\n";

            if (UiDispatch.CheckAccess())
            {
                Update();
            }
            else
            {
                UiDispatch.Invoke(Update);
            }

            LogToPipeline($"[同源图片归类] {message}");
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

        private async Task RunGroupingAsync(bool showDialogs, CancellationToken token)
        {
            if (IsProcessing) return;
            if (await MaterialClassificationSession.TryRunAsync(this, () => RunGroupingAsync(false, token))) return;
            if (MaterialClassificationSession.Current is { } materialSession) token = materialSession.Token;
            OutputPath = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputPath, InputPath, DefaultOutputFolderName);
            if (IsProcessing)
            {
                return;
            }

            IsProcessing = true;
            Progress = 0;
            Log = string.Empty;
            bool success = false;

            try
            {
                var progress = CreateProgressReporter();
                success = await Task.Run(() => RunGroupingWork(showDialogs, token, progress), token);
            }
            finally
            {
                if (success) MaterialClassificationSession.MarkSuccessful();
                if (success && MaterialClassificationSession.Current == null)
                {
                    TryClearInputDirectoryAfterCompletion();
                }

                IsProcessing = false;
            }
        }

        private bool RunGroupingWork(bool showDialogs, CancellationToken token, IProgress<double> progress)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(InputPath) || !Directory.Exists(InputPath))
                {
                    AppendLog("输入路径无效。");
                    if (showDialogs)
                    {
                        UiDispatch.Invoke(() =>
                            MessageBox.Show("输入路径无效。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    return false;
                }

                if (string.IsNullOrWhiteSpace(OutputPath))
                {
                    AppendLog("输出路径无效。");
                    if (showDialogs)
                    {
                        UiDispatch.Invoke(() =>
                            MessageBox.Show("输出路径无效。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    return false;
                }

                string inputFull = Path.GetFullPath(InputPath);
                string outputFull = Path.GetFullPath(OutputPath);
                string inputWithSlash = inputFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                bool outputInsideInput = outputFull.StartsWith(inputWithSlash, StringComparison.OrdinalIgnoreCase);
                if (string.Equals(inputFull, outputFull, StringComparison.OrdinalIgnoreCase) || outputInsideInput)
                {
                    AppendLog("输出路径不能与输入路径相同，且不能位于输入路径内。请选择独立输出路径，避免修改输入路径。");
                    if (showDialogs)
                    {
                        UiDispatch.Invoke(() =>
                            MessageBox.Show("输出路径不能与输入路径相同，且不能位于输入路径内。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    return false;
                }

                var options = new SameSourceGroupingOptions
                {
                    ComputeBackend = string.IsNullOrWhiteSpace(ComputeBackend) ? DefaultBackend : ComputeBackend
                };

                SameSourceImageGroupingProcessor.Process(InputPath, OutputPath, options, progress, AppendLog, token);

                UiDispatch.Invoke(() =>
                {
                    Progress = 100;
                    if (showDialogs)
                    {
                        MessageBox.Show("同源图片归类完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                });
                return true;
            }
            catch (OperationCanceledException)
            {
                AppendLog("任务已取消。");
                return false;
            }
            catch (Exception ex)
            {
                AppendLog($"发生错误: {ex.Message}");
                if (showDialogs)
                {
                    UiDispatch.Invoke(() =>
                        MessageBox.Show($"处理失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                return false;
            }
            finally
            {
                UiDispatch.Invoke(RefreshCommandStates);
            }
        }

        private void TryClearInputDirectoryAfterCompletion()
        {
            if (!ShouldClearInputDirectoryAfterCompletion)
            {
                return;
            }

            InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.TryClearContents(InputPath, new[] { OutputPath });
            AppendLog(result.Message);
        }

        private IProgress<double> CreateProgressReporter()
        {
            return new ExecutionProgress<double>(value =>
            {
                if (Application.Current == null)
                {
                    Progress = value;
                    return;
                }

                if (UiDispatch.CheckAccess())
                {
                    Progress = value;
                }
                else
                {
                    UiDispatch.Invoke(() => Progress = value);
                }
            });
        }
    }
}
