using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class ConvertTransparentChannelViewModel : INotifyPropertyChanged
    {
        private string _inputPath;
        private string _outputPath;
        private double _progressValue;
        private bool _isProcessing;

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public string InputPath
        {
            get => _inputPath;
            set { _inputPath = value; OnPropertyChanged(); }
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

        private async Task StartConversionAsync()
        {
            if (string.IsNullOrWhiteSpace(InputPath) || string.IsNullOrWhiteSpace(OutputPath))
            {
                System.Windows.MessageBox.Show("请选择输入和输出路径。", "提示");
                return;
            }

            if (!Directory.Exists(InputPath))
            {
                System.Windows.MessageBox.Show("输入路径不存在。", "提示");
                return;
            }

            IsProcessing = true;
            ProgressValue = 0;
            Logs.Clear();

            var progress = new Progress<double>(value => ProgressValue = value);
            Action<string> logAction = message => App.Current.Dispatcher.Invoke(() => Logs.Add(message));

            try
            {
                await Task.Run(() => TransparentChannelConverter.Convert(InputPath, OutputPath, progress, logAction));
            }
            catch (Exception ex)
            {
                logAction($"转换失败: {ex.Message}");
            }
            finally
            {
                ProgressValue = 100;
                IsProcessing = false;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
