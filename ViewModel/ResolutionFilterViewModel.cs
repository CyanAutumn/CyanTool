
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class ResolutionFilterViewModel : INotifyPropertyChanged, IPipelineDirectoryNode
    {
        private string _inputPath;
        private bool _isHeightFilterEnabled;
        private int _minHeight = 1024;
        private bool _isWidthFilterEnabled;
        private int _minWidth = 1024;
        private bool _isResolutionFilterEnabled;
        private int _minResolutionWidth = 1024;
        private int _minResolutionHeight = 1024;
        private double _progressValue;
        private bool _isStartButtonEnabled = true;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public string InputPath
        {
            get => _inputPath;
            set { _inputPath = value; OnPropertyChanged(nameof(InputPath)); }
        }

        public bool IsHeightFilterEnabled
        {
            get => _isHeightFilterEnabled;
            set { _isHeightFilterEnabled = value; OnPropertyChanged(nameof(IsHeightFilterEnabled)); }
        }

        public int MinHeight
        {
            get => _minHeight;
            set { _minHeight = value; OnPropertyChanged(nameof(MinHeight)); }
        }

        public bool IsWidthFilterEnabled
        {
            get => _isWidthFilterEnabled;
            set { _isWidthFilterEnabled = value; OnPropertyChanged(nameof(IsWidthFilterEnabled)); }
        }

        public int MinWidth
        {
            get => _minWidth;
            set { _minWidth = value; OnPropertyChanged(nameof(MinWidth)); }
        }

        public bool IsResolutionFilterEnabled
        {
            get => _isResolutionFilterEnabled;
            set { _isResolutionFilterEnabled = value; OnPropertyChanged(nameof(IsResolutionFilterEnabled)); }
        }

        public int MinResolutionWidth
        {
            get => _minResolutionWidth;
            set { _minResolutionWidth = value; OnPropertyChanged(nameof(MinResolutionWidth)); }
        }

        public int MinResolutionHeight
        {
            get => _minResolutionHeight;
            set { _minResolutionHeight = value; OnPropertyChanged(nameof(MinResolutionHeight)); }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(nameof(ProgressValue)); }
        }

        public bool IsStartButtonEnabled
        {
            get => _isStartButtonEnabled;
            set { _isStartButtonEnabled = value; OnPropertyChanged(nameof(IsStartButtonEnabled)); }
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set { _isWorkflowMode = value; OnPropertyChanged(nameof(IsWorkflowMode)); }
        }

        public bool HasInheritedInputPath
        {
            get => _hasInheritedInputPath;
            set { _hasInheritedInputPath = value; OnPropertyChanged(nameof(HasInheritedInputPath)); }
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.InPlace;
        public string PipelineInputPath { get => InputPath ?? string.Empty; set => InputPath = value ?? string.Empty; }
        public string PipelineOutputPath { get => InputPath ?? string.Empty; set => InputPath = value ?? string.Empty; }

        public ICommand BrowseInputCommand { get; }
        public ICommand StartCommand { get; }

        public ResolutionFilterViewModel()
        {
            BrowseInputCommand = new RelayCommand(BrowseInputPath);
            StartCommand = new RelayCommand(async () => await StartProcessing(), () => IsStartButtonEnabled);
        }

        private void BrowseInputPath()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择输入路径"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                InputPath = dialog.FileName;
            }
        }

        private async Task StartProcessing()
        {
            IsStartButtonEnabled = false;
            ProgressValue = 0;
            Logs.Clear();

            var options = new ResolutionFilter.FilterOptions
            {
                IsHeightFilterEnabled = this.IsHeightFilterEnabled,
                MinHeight = this.MinHeight,
                IsWidthFilterEnabled = this.IsWidthFilterEnabled,
                MinWidth = this.MinWidth,
                IsResolutionFilterEnabled = this.IsResolutionFilterEnabled,
                MinResolution = this.MinResolutionWidth * this.MinResolutionHeight
            };

            var progress = new Progress<double>(value => ProgressValue = value);
            Action<string> logAction = (message) => App.Current.Dispatcher.Invoke(() => Logs.Add(message));

            try
            {
                await Task.Run(() => ResolutionFilter.ProcessImages(InputPath, options, progress, logAction));
            }
            catch (Exception ex)
            {
                logAction($"发生严重错误: {ex.Message}");
            }
            finally
            {
                ProgressValue = 100;
                IsStartButtonEnabled = true;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
