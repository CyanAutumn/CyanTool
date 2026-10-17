using System;
using System.Collections.ObjectModel;
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
    public class ConvertTransparentChannelViewModel : INotifyPropertyChanged, IPipelineDirectoryNode, IPipelineRunnableViewModel
    {
        private const string ToolDisplayName = "转换透明通道";

        private string _inputPath;
        private string _outputPath;
        private double _progressValue;
        private bool _isProcessing;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;
        private bool _shouldClearInputDirectoryAfterCompletion;

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

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

        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                _isProcessing = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
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
        public string PipelineInputPath { get => InputPath ?? string.Empty; set => InputPath = value ?? string.Empty; }
        public string PipelineOutputPath { get => OutputPath ?? string.Empty; set => OutputPath = value ?? string.Empty; }

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand StartConversionCommand { get; }

        public ConvertTransparentChannelViewModel()
        {
            SelectInputCommand = new RelayCommand(SelectInputPath);
            SelectOutputCommand = new RelayCommand(SelectOutputPath);
            StartConversionCommand = new RelayCommand(async () => await StartConversionAsync(), () => !IsProcessing);
        }

        private void SelectPath(Action<string> setPath)
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                setPath(dialog.FileName);
            }
        }

        private void SelectInputPath()
        {
            SelectPath(path => InputPath = path);
        }

        private void SelectOutputPath()
        {
            SelectPath(path => OutputPath = path);
        }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            return ExecuteConversionAsync(
                showDialogs: false,
                rethrowErrors: true,
                pipelineLog: message => context.AppendLog($"[{ToolDisplayName}] {message}"),
                token);
        }

        private Task StartConversionAsync()
        {
            return ExecuteConversionAsync(
                showDialogs: true,
                rethrowErrors: false,
                pipelineLog: null,
                CancellationToken.None);
        }

        private async Task ExecuteConversionAsync(bool showDialogs, bool rethrowErrors, Action<string>? pipelineLog, CancellationToken token)
        {
            OutputPath = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputPath, InputPath, ToolDisplayName);
            if (string.IsNullOrWhiteSpace(InputPath) || string.IsNullOrWhiteSpace(OutputPath))
            {
                const string validationMessage = "请选择输入和输出路径。";
                pipelineLog?.Invoke(validationMessage);
                if (showDialogs)
                {
                    MessageBox.Show(validationMessage, "提示");
                }
                else if (rethrowErrors)
                {
                    throw new InvalidOperationException(validationMessage);
                }

                return;
            }

            if (!Directory.Exists(InputPath))
            {
                const string inputMissingMessage = "输入路径不存在。";
                pipelineLog?.Invoke(inputMissingMessage);
                if (showDialogs)
                {
                    MessageBox.Show(inputMissingMessage, "提示");
                }
                else if (rethrowErrors)
                {
                    throw new DirectoryNotFoundException(inputMissingMessage);
                }

                return;
            }

            IsProcessing = true;
            ProgressValue = 0;
            Logs.Clear();

            var progress = new ExecutionProgress<double>(value => ProgressValue = value);
            Action<string> logAction = message =>
            {
                App.Current.Dispatcher.Invoke(() => Logs.Add(message));
                pipelineLog?.Invoke(message);
            };
            bool success = false;

            try
            {
                token.ThrowIfCancellationRequested();
                await Task.Run(() => TransparentChannelConverter.Convert(InputPath, OutputPath, progress, logAction), token);
                success = true;
            }
            catch (Exception ex)
            {
                string failureMessage = $"转换失败: {ex.Message}";
                logAction(failureMessage);
                if (rethrowErrors)
                {
                    throw;
                }
            }
            finally
            {
                if (success)
                {
                    TryClearInputDirectoryAfterCompletion(logAction);
                }

                ProgressValue = 100;
                IsProcessing = false;
            }
        }

        private void TryClearInputDirectoryAfterCompletion(Action<string> logAction)
        {
            if (!ShouldClearInputDirectoryAfterCompletion)
            {
                return;
            }

            InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.TryClearContents(InputPath, new[] { OutputPath });
            logAction(result.Message);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
