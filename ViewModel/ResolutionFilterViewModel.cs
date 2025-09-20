
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public class ResolutionFilterViewModel : INotifyPropertyChanged
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

        public ICommand StartCommand { get; }

        public ResolutionFilterViewModel()
        {
            StartCommand = new RelayCommand(async () => await StartProcessing(), () => IsStartButtonEnabled);
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
