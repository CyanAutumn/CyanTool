
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class ConvertImageFormatViewModel : INotifyPropertyChanged, IPipelineDirectoryNode, IPipelineRunnableViewModel
    {
        private const string ToolDisplayName = "格式转换";

        private string _inputPath;
        private string _outputPath;
        private string _selectedFormat;
        private double _progressValue;
        private bool _isConverting;
        private bool _isWorkflowMode;

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
                OnPropertyChanged(nameof(InputPath));
                OutputPath = ToolOutputDirectoryDefaults.RefreshForInputChange(OutputPath, previousInput, value, ToolDisplayName);
            }
        }

        public string OutputPath
        {
            get => _outputPath;
            set { _outputPath = value; OnPropertyChanged(nameof(OutputPath)); }
        }

        public ObservableCollection<string> ImageFormats { get; } = new ObservableCollection<string>
        {
            "PNG", "JPEG", "BMP", "WEBP"
        };

        public string SelectedFormat
        {
            get => _selectedFormat;
            set { _selectedFormat = value; OnPropertyChanged(nameof(SelectedFormat)); }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(nameof(ProgressValue)); }
        }

        public bool IsConverting
        {
            get => _isConverting;
            set { _isConverting = value; OnPropertyChanged(nameof(IsConverting)); CommandManager.InvalidateRequerySuggested(); }
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set { _isWorkflowMode = value; OnPropertyChanged(nameof(IsWorkflowMode)); }
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;
        public string PipelineInputPath { get => InputPath ?? string.Empty; set => InputPath = value ?? string.Empty; }
        public string PipelineOutputPath { get => OutputPath ?? string.Empty; set => OutputPath = value ?? string.Empty; }

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand StartConversionCommand { get; }

        public ConvertImageFormatViewModel()
        {
            SelectInputCommand = new RelayCommand(SelectInputPath);
            SelectOutputCommand = new RelayCommand(SelectOutputPath);
            StartConversionCommand = new RelayCommand(async () => await StartConversion(), () => !IsConverting);
            SelectedFormat = ImageFormats.First();
        }

        private void SelectPath(Action<string> setPath)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
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

        private Task StartConversion()
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
            if (string.IsNullOrEmpty(InputPath) || string.IsNullOrEmpty(OutputPath))
            {
                const string validationMessage = "请选择输入和输出路径。";
                pipelineLog?.Invoke(validationMessage);
                if (showDialogs)
                {
                    System.Windows.MessageBox.Show(validationMessage, "提示");
                }
                else if (rethrowErrors)
                {
                    throw new InvalidOperationException(validationMessage);
                }

                return;
            }

            IsConverting = true;
            ProgressValue = 0;
            pipelineLog?.Invoke("开始格式转换。");

            var progress = new Progress<double>(value => ProgressValue = value);

            try
            {
                token.ThrowIfCancellationRequested();
                await ImageFormatConverter.ConvertAsync(InputPath, OutputPath, SelectedFormat, progress);
                pipelineLog?.Invoke("格式转换完成。");
            }
            catch (Exception ex)
            {
                string failureMessage = $"转换过程中发生错误: {ex.Message}";
                pipelineLog?.Invoke(failureMessage);
                if (showDialogs)
                {
                    System.Windows.MessageBox.Show(failureMessage, "错误");
                }

                if (rethrowErrors)
                {
                    throw;
                }
            }
            finally
            {
                IsConverting = false;
                ProgressValue = 100;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
