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
        private const string ToolDisplayName = "文件夹处理";

        private readonly StringBuilder _logBuilder = new StringBuilder();
        private string _inputPath = string.Empty;
        private FolderProcessingOperation _selectedOperation = FolderProcessingOperation.MoveFilesToTopLevel;
        private string _parameterText = string.Empty;
        private string _logText = string.Empty;
        private bool _isProcessing;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;

        public FolderProcessingViewModel()
        {
            AvailableOperations = new ObservableCollection<FolderProcessingOperationOption>
            {
                new FolderProcessingOperationOption(FolderProcessingOperation.MoveFilesToTopLevel, "移动子目录中的文件到顶层"),
                new FolderProcessingOperationOption(FolderProcessingOperation.DeleteEmptyDirectories, "删除空文件夹"),
                new FolderProcessingOperationOption(FolderProcessingOperation.RetainDirectoriesByKeyword, "保留含有指定关键字的文件夹"),
                new FolderProcessingOperationOption(FolderProcessingOperation.ExtractArchives, "解压所有压缩包")
            };

            SelectInputPathCommand = new RelayCommand(SelectInputPath);
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
            }
        }

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

        public string ParameterLabel
        {
            get
            {
                return SelectedOperation switch
                {
                    FolderProcessingOperation.RetainDirectoriesByKeyword => "关键字:",
                    FolderProcessingOperation.ExtractArchives => "解压密码:",
                    _ => string.Empty
                };
            }
        }

        public bool IsParameterVisible =>
            SelectedOperation == FolderProcessingOperation.RetainDirectoriesByKeyword ||
            SelectedOperation == FolderProcessingOperation.ExtractArchives;

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
                await FolderProcessingPipelineProcessor.ExecuteAsync(
                    InputPath,
                    new FolderProcessingOptions
                    {
                        Operation = SelectedOperation,
                        ParameterText = ParameterText
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
                string message = $"处理失败: {ex.Message}";
                AppendLog(message);
                pipelineLog?.Invoke(message);

                if (showDialogs)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                        MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error));
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
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择输入文件夹"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                InputPath = dialog.FileName;
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
