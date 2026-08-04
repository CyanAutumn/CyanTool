using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class ImageProcessingViewModel : INotifyPropertyChanged
    {
        private string _targetDirectory;
        private bool _isMirrorEnabled = true;
        private bool _isIdle = true;
        private readonly AdvancedImageProcessor _processor;
        
        private const int MaxLogLines = 1000;

        public string TargetDirectory { get => _targetDirectory; set => SetField(ref _targetDirectory, value); }
        public bool IsMirrorEnabled { get => _isMirrorEnabled; set => SetField(ref _isMirrorEnabled, value); }
        public bool IsIdle { get => _isIdle; set => SetField(ref _isIdle, value); }
        
        public ObservableCollection<string> LogLines { get; }

        public ICommand BrowseDirectoryCommand { get; }
        public ICommand StartProcessingCommand { get; }
        public ICommand ClearLogCommand { get; }

        public ImageProcessingViewModel()
        {
            _processor = new AdvancedImageProcessor();
            LogLines = new ObservableCollection<string>();
            BrowseDirectoryCommand = new RelayCommand(_ => BrowseForFolder(path => TargetDirectory = path));
            StartProcessingCommand = new RelayCommand(async _ => await StartProcessing(), _ => IsIdle && (IsMirrorEnabled));
            ClearLogCommand = new RelayCommand(_ => LogLines.Clear(), _ => IsIdle);
        }

        private void AddLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add(message);
                if (LogLines.Count > MaxLogLines)
                {
                    LogLines.RemoveAt(0);
                }
            });
        }

        private void BrowseForFolder(Action<string> setPathAction)
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                setPathAction(dialog.FileName);
            }
        }

        private async Task StartProcessing()
        {
            IsIdle = false;
            (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            LogLines.Clear();
            var progress = new Progress<string>(AddLog);

            try
            {
                if (IsMirrorEnabled)
                {
                    await _processor.MirrorImagesAsync(TargetDirectory, progress);
                }

                AddLog("Processing complete.");
            }
            catch (Exception ex)
            {
                AddLog($"An error occurred: {ex.Message}");
            }
            finally
            {
                IsIdle = true;
                (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}