using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public class FolderProcessingViewModel : INotifyPropertyChanged, IPipelineDirectoryNode, IPipelineRunnableViewModel
    {
        private const string ToolDisplayName = "\u6587\u4EF6\u5939\u5904\u7406";

        private readonly StringBuilder _logBuilder = new StringBuilder();
        private string _inputPath = string.Empty;
        private string _outputPath = string.Empty;
        private FolderProcessingOperation _selectedOperation = FolderProcessingOperation.MoveFilesToTopLevel;
        private string _parameterText = string.Empty;
        private string _logText = string.Empty;
        private bool _overwriteExisting;
        private bool _isProcessing;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;

        public FolderProcessingViewModel()
        {
            AvailableOperations = new ObservableCollection<FolderProcessingOperationOption>
            {
                new FolderProcessingOperationOption(FolderProcessingOperation.MoveFilesToTopLevel, "\u79FB\u52A8\u5B50\u76EE\u5F55\u4E2D\u7684\u6587\u4EF6\u5230\u9876\u5C42"),
                new FolderProcessingOperationOption(FolderProcessingOperation.DeleteEmptyDirectories, "\u5220\u9664\u7A7A\u6587\u4EF6\u5939"),
                new FolderProcessingOperationOption(FolderProcessingOperation.RetainDirectoriesByKeyword, "\u4FDD\u7559\u542B\u6709\u6307\u5B9A\u5173\u952E\u5B57\u7684\u6587\u4EF6\u5939"),
                new FolderProcessingOperationOption(FolderProcessingOperation.ExtractArchives, "\u89E3\u538B\u6240\u6709\u538B\u7F29\u5305"),
                new FolderProcessingOperationOption(FolderProcessingOperation.ConvertDiffusionPipeOutputToSdScript, "\u8F6C\u6362diffusion-pipe\u8F93\u51FA\u81F3sd-script")
            };

            SelectInputPathCommand = new RelayCommand(SelectInputPath);
            SelectOutputPathCommand = new RelayCommand(SelectOutputPath);
            ExecuteCommand = new RelayCommand(async () => await ExecuteFromPageAsync(), () => !IsProcessing);
        }

        public ObservableCollection<FolderProcessingOperationOption> AvailableOperations { get; }

        public string InputPath
        {
            get => _inputPath;
            set
            {
                if (_inputPath == value)
                {
                    return;
                }

                _inputPath = value ?? string.Empty;
                OnPropertyChanged(nameof(InputPath));
                OnPropertyChanged(nameof(EffectiveOutputPath));
            }
        }

        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (_outputPath == value)
                {
                    return;
                }

                _outputPath = value ?? string.Empty;
                OnPropertyChanged(nameof(OutputPath));
                OnPropertyChanged(nameof(EffectiveOutputPath));
            }
        }

        public string EffectiveOutputPath =>
            string.IsNullOrWhiteSpace(OutputPath) ? InputPath : OutputPath;

        public FolderProcessingOperation SelectedOperation
        {
            get => _selectedOperation;
            set
            {
                if (_selectedOperation == value)
                {
                    return;
                }

                _selectedOperation = value;
                OnPropertyChanged(nameof(SelectedOperation));
                OnPropertyChanged(nameof(ParameterLabel));
                OnPropertyChanged(nameof(IsParameterVisible));
                OnPropertyChanged(nameof(IsOutputPathVisible));
                OnPropertyChanged(nameof(IsOverwriteOptionVisible));
            }
        }

        public string ParameterText
        {
            get => _parameterText;
            set
            {
                if (_parameterText == value)
                {
                    return;
                }

                _parameterText = value ?? string.Empty;
                OnPropertyChanged(nameof(ParameterText));
            }
        }

        public bool OverwriteExisting
        {
            get => _overwriteExisting;
            set
            {
                if (_overwriteExisting == value)
                {
                    return;
                }

                _overwriteExisting = value;
                OnPropertyChanged(nameof(OverwriteExisting));
            }
        }

        public string ParameterLabel
        {
            get
            {
                return SelectedOperation switch
                {
                    FolderProcessingOperation.RetainDirectoriesByKeyword => "\u5173\u952E\u5B57:",
                    FolderProcessingOperation.ExtractArchives => "\u89E3\u538B\u5BC6\u7801:",
                    _ => string.Empty
                };
            }
        }

        public bool IsParameterVisible =>
            SelectedOperation == FolderProcessingOperation.RetainDirectoriesByKeyword ||
            SelectedOperation == FolderProcessingOperation.ExtractArchives;

        public bool IsOutputPathVisible =>
            SelectedOperation == FolderProcessingOperation.ConvertDiffusionPipeOutputToSdScript;

        public bool IsOverwriteOptionVisible =>
            SelectedOperation == FolderProcessingOperation.ConvertDiffusionPipeOutputToSdScript;

        public string LogText
        {
            get => _logText;
            private set
            {
                if (_logText == value)
                {
                    return;
                }

                _logText = value;
                OnPropertyChanged(nameof(LogText));
            }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                if (_isProcessing == value)
                {
                    return;
                }

                _isProcessing = value;
                OnPropertyChanged(nameof(IsProcessing));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set
            {
                if (_isWorkflowMode == value)
                {
                    return;
                }

                _isWorkflowMode = value;
                OnPropertyChanged(nameof(IsWorkflowMode));
            }
        }

        public bool HasInheritedInputPath
        {
            get => _hasInheritedInputPath;
            set
            {
                if (_hasInheritedInputPath == value)
                {
                    return;
                }

                _hasInheritedInputPath = value;
                OnPropertyChanged(nameof(HasInheritedInputPath));
            }
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.InPlace;

        public string PipelineInputPath
        {
            get => InputPath;
            set => InputPath = value ?? string.Empty;
        }

        public string PipelineOutputPath
        {
            get => InputPath;
            set => InputPath = value ?? string.Empty;
        }

        public ICommand SelectInputPathCommand { get; }

        public ICommand SelectOutputPathCommand { get; }

        public ICommand ExecuteCommand { get; }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            return ExecuteAsync(
                showDialogs: false,
                rethrowErrors: true,
                pipelineLog: message => context.AppendLog($"[{ToolDisplayName}] {message}"),
                token);
        }

        private Task ExecuteFromPageAsync()
        {
            return ExecuteAsync(
                showDialogs: true,
                rethrowErrors: false,
                pipelineLog: null,
                CancellationToken.None);
        }

        private async Task ExecuteAsync(bool showDialogs, bool rethrowErrors, Action<string>? pipelineLog, CancellationToken token)
        {
            if (IsProcessing)
            {
                return;
            }

            IsProcessing = true;
            _logBuilder.Clear();
            LogText = string.Empty;

            try
            {
                string outputPath = IsWorkflowMode ? string.Empty : OutputPath;
                await FolderProcessingPipelineProcessor.ExecuteAsync(
                    InputPath,
                    new FolderProcessingOptions
                    {
                        Operation = SelectedOperation,
                        ParameterText = ParameterText,
                        OutputPath = outputPath,
                        OverwriteExisting = OverwriteExisting
                    },
                    message =>
                    {
                        AppendLog(message);
                        pipelineLog?.Invoke(message);
                    },
                    token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string message = $"\u5904\u7406\u5931\u8D25: {ex.Message}";
                AppendLog(message);
                pipelineLog?.Invoke(message);

                if (showDialogs)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                        MessageBox.Show(ex.Message, "\u9519\u8BEF", MessageBoxButton.OK, MessageBoxImage.Error));
                }

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

        private void SelectInputPath()
        {
            SelectFolder(path => InputPath = path, "\u9009\u62E9\u8F93\u5165\u6587\u4EF6\u5939");
        }

        private void SelectOutputPath()
        {
            SelectFolder(path => OutputPath = path, "\u9009\u62E9\u8F93\u51FA\u6587\u4EF6\u5939");
        }

        private static void SelectFolder(Action<string> assignPath, string title)
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = title
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                assignPath(dialog.FileName);
            }
        }

        private void AppendLog(string message)
        {
            void UpdateLog()
            {
                _logBuilder.AppendLine(message);
                LogText = _logBuilder.ToString();
            }

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                UpdateLog();
            }
            else
            {
                Application.Current?.Dispatcher?.Invoke(UpdateLog);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class FolderProcessingOperationOption
    {
        public FolderProcessingOperationOption(FolderProcessingOperation operation, string displayName)
        {
            Operation = operation;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        }

        public FolderProcessingOperation Operation { get; }

        public string DisplayName { get; }
    }
}
