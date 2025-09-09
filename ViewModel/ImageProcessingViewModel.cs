using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class ImageProcessingViewModel : INotifyPropertyChanged
    {
        private string _targetDirectory;
        private bool _isMirrorEnabled = true;
        private string _logOutput;
        private bool _isIdle = true;
        private readonly AdvancedImageProcessor _processor;

        public string TargetDirectory { get => _targetDirectory; set => SetField(ref _targetDirectory, value); }
        public bool IsMirrorEnabled { get => _isMirrorEnabled; set => SetField(ref _isMirrorEnabled, value); }
        public string LogOutput { get => _logOutput; set => SetField(ref _logOutput, value); }
        public bool IsIdle { get => _isIdle; set => SetField(ref _isIdle, value); }

        public ICommand BrowseDirectoryCommand { get; }
        public ICommand StartProcessingCommand { get; }

        public ImageProcessingViewModel()
        {
            _processor = new AdvancedImageProcessor();
            BrowseDirectoryCommand = new RelayCommand(_ => BrowseForFolder(path => TargetDirectory = path));
            StartProcessingCommand = new RelayCommand(async _ => await StartProcessing(), _ => IsIdle && (IsMirrorEnabled));
        }

        private void BrowseForFolder(Action<string> setPathAction)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                setPathAction(dialog.FileName);
            }
        }

        private async Task StartProcessing()
        {
            IsIdle = false;
            var logBuilder = new StringBuilder();
            LogOutput = "";
            var progress = new Progress<string>(update =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    logBuilder.AppendLine(update);
                    LogOutput = logBuilder.ToString();
                });
            });

            try
            {
                if (IsMirrorEnabled)
                {
                    await _processor.MirrorImagesAsync(TargetDirectory, progress);
                }

            }
            catch (Exception ex)
            {
                ((IProgress<string>)progress).Report($"发生严重错误: {ex.Message}");
            }
            finally
            {
                IsIdle = true;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}

