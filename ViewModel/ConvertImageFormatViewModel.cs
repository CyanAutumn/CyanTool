
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class ConvertImageFormatViewModel : INotifyPropertyChanged
    {
        private string _inputPath;
        private string _outputPath;
        private string _selectedFormat;
        private double _progressValue;
        private bool _isConverting;

        public string InputPath
        {
            get => _inputPath;
            set { _inputPath = value; OnPropertyChanged(nameof(InputPath)); }
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

        private async Task StartConversion()
        {
            if (string.IsNullOrEmpty(InputPath) || string.IsNullOrEmpty(OutputPath))
            {
                System.Windows.MessageBox.Show("请选择输入和输出路径。", "提示");
                return;
            }

            IsConverting = true;
            ProgressValue = 0;

            var progress = new Progress<double>(value => ProgressValue = value);

            try
            {
                await ImageFormatConverter.ConvertAsync(InputPath, OutputPath, SelectedFormat, progress);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"转换过程中发生错误: {ex.Message}", "错误");
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
