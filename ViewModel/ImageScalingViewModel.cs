using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class ImageScalingViewModel : INotifyPropertyChanged, IPipelineDirectoryNode, IPipelineRunnableViewModel
    {
        private const string ToolDisplayName = "图片上下采";

        private string _inputPath = string.Empty;
        private string _outputPath = string.Empty;
        private bool _isUpsampleEnabled = true;
        private int _upsampleTargetWidth = 1536;
        private int _upsampleTargetHeight = 1536;
        private bool _isDownsampleEnabled;
        private int _downsampleTargetWidth = 1536;
        private int _downsampleTargetHeight = 1536;
        private bool _isProcessing;
        private double _progressValue;
        private bool _isWorkflowMode;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand StartProcessingCommand { get; }
        public ICommand ClearLogCommand { get; }

        public string InputPath
        {
            get => _inputPath;
            set
            {
                var previousInput = _inputPath;
                if (SetField(ref _inputPath, value))
                {
                    OutputPath = ToolOutputDirectoryDefaults.RefreshForInputChange(OutputPath, previousInput, value, ToolDisplayName);
                }
            }
        }

        public string OutputPath
        {
            get => _outputPath;
            set => SetField(ref _outputPath, value);
        }

        public bool IsUpsampleEnabled
        {
            get => _isUpsampleEnabled;
            set
            {
                if (SetField(ref _isUpsampleEnabled, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public int UpsampleTargetWidth
        {
            get => _upsampleTargetWidth;
            set => SetField(ref _upsampleTargetWidth, NormalizeToNearest64(value));
        }

        public int UpsampleTargetHeight
        {
            get => _upsampleTargetHeight;
            set => SetField(ref _upsampleTargetHeight, NormalizeToNearest64(value));
        }

        public bool IsDownsampleEnabled
        {
            get => _isDownsampleEnabled;
            set
            {
                if (SetField(ref _isDownsampleEnabled, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public int DownsampleTargetWidth
        {
            get => _downsampleTargetWidth;
            set => SetField(ref _downsampleTargetWidth, NormalizeToNearest64(value));
        }

        public int DownsampleTargetHeight
        {
            get => _downsampleTargetHeight;
            set => SetField(ref _downsampleTargetHeight, NormalizeToNearest64(value));
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetField(ref _isProcessing, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetField(ref _progressValue, value);
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set => SetField(ref _isWorkflowMode, value);
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;
        public string PipelineInputPath { get => InputPath; set => InputPath = value ?? string.Empty; }
        public string PipelineOutputPath { get => OutputPath; set => OutputPath = value ?? string.Empty; }

        public ImageScalingViewModel()
        {
            SelectInputCommand = new RelayCommand(_ => SelectFolder(path => InputPath = path), _ => !IsProcessing);
            SelectOutputCommand = new RelayCommand(_ => SelectFolder(path => OutputPath = path), _ => !IsProcessing);
            StartProcessingCommand = new RelayCommand(async _ => await StartProcessingAsync(), _ => !IsProcessing);
            ClearLogCommand = new RelayCommand(_ => Logs.Clear(), _ => !IsProcessing);
        }

        private static int NormalizeToNearest64(int value)
        {
            if (value <= 0)
            {
                return 64;
            }

            var normalized = (int)Math.Round(value / 64.0, MidpointRounding.AwayFromZero) * 64;
            return Math.Max(64, normalized);
        }

        private void SelectFolder(Action<string> setPathAction)
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                setPathAction(dialog.FileName);
            }
        }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            return ExecuteProcessingAsync(
                showDialogs: false,
                rethrowErrors: true,
                pipelineLog: message => context.AppendLog($"[{ToolDisplayName}] {message}"),
                token);
        }

        private Task StartProcessingAsync()
        {
            return ExecuteProcessingAsync(
                showDialogs: true,
                rethrowErrors: false,
                pipelineLog: null,
                CancellationToken.None);
        }

        private async Task ExecuteProcessingAsync(bool showDialogs, bool rethrowErrors, Action<string>? pipelineLog, CancellationToken token)
        {
            OutputPath = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputPath, InputPath, ToolDisplayName);
            if (!ValidateInput(showDialogs, out string errorMessage))
            {
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    AppendLog(errorMessage);
                    pipelineLog?.Invoke(errorMessage);
                    if (rethrowErrors)
                    {
                        throw new InvalidOperationException(errorMessage);
                    }
                }

                return;
            }

            Logs.Clear();
            ProgressValue = 0;
            IsProcessing = true;
            AppendLog("开始批量图片上下采...");
            pipelineLog?.Invoke("开始批量图片上下采...");

            var options = new ImageScalingOptions
            {
                InputFolder = InputPath,
                OutputFolder = OutputPath,
                EnableUpsample = IsUpsampleEnabled,
                UpsampleTargetWidth = UpsampleTargetWidth,
                UpsampleTargetHeight = UpsampleTargetHeight,
                EnableDownsample = IsDownsampleEnabled,
                DownsampleTargetWidth = DownsampleTargetWidth,
                DownsampleTargetHeight = DownsampleTargetHeight
            };

            var progress = new Progress<double>(value => ProgressValue = value);

            try
            {
                token.ThrowIfCancellationRequested();
                await ImageScalingProcessor.ProcessFolderAsync(options, progress, AppendLog);
                ProgressValue = 100;
                AppendLog("处理完成。");
                pipelineLog?.Invoke("处理完成。");
            }
            catch (Exception ex)
            {
                string failureMessage = $"处理失败: {ex.Message}";
                AppendLog(failureMessage);
                pipelineLog?.Invoke(failureMessage);
                if (rethrowErrors)
                {
                    throw;
                }
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private bool ValidateInput(bool showDialogs, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(InputPath))
            {
                errorMessage = "请选择输入路径。";
            }
            else if (!Directory.Exists(InputPath))
            {
                errorMessage = "输入路径不存在。";
            }
            else if (string.IsNullOrWhiteSpace(OutputPath))
            {
                errorMessage = "请选择输出路径。";
            }
            else if (string.Equals(Path.GetFullPath(InputPath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "输入路径和输出路径不能相同。";
            }
            else if (!IsUpsampleEnabled && !IsDownsampleEnabled)
            {
                errorMessage = "请至少勾选一个处理选项。";
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                if (showDialogs)
                {
                    MessageBox.Show(errorMessage, "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return false;
            }

            UpsampleTargetWidth = UpsampleTargetWidth;
            UpsampleTargetHeight = UpsampleTargetHeight;
            DownsampleTargetWidth = DownsampleTargetWidth;
            DownsampleTargetHeight = DownsampleTargetHeight;

            return true;
        }

        private void AppendLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() => Logs.Add(message));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
