using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class AddTaggerViewModel : INotifyPropertyChanged
    {
        private string _displayName = string.Empty;
        private string _localModelPath = string.Empty;
        private string _localTagsPath = string.Empty;
        private int _inputSize = 448;
        private string _inputFormat = "NHWC";
        private string _statusMessage = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<bool> RequestClose;

        public AddTaggerViewModel()
        {
            BrowseModelCommand = new RelayCommand(_ => BrowseModelFile());
            BrowseTagsCommand = new RelayCommand(_ => BrowseTagsFile());
            AutoDetectCommand = new RelayCommand(_ => AutoDetectInputConfig());
            ConfirmCommand = new RelayCommand(_ => Confirm());
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        }

        public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }
        public string LocalModelPath { get => _localModelPath; set => SetField(ref _localModelPath, value); }
        public string LocalTagsPath { get => _localTagsPath; set => SetField(ref _localTagsPath, value); }
        public int InputSize { get => _inputSize; set => SetField(ref _inputSize, value); }
        public string InputFormat { get => _inputFormat; set => SetField(ref _inputFormat, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public string[] InputFormats { get; } = { "NHWC", "NCHW" };

        public ICommand BrowseModelCommand { get; }
        public ICommand BrowseTagsCommand { get; }
        public ICommand AutoDetectCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public TaggerModel BuildTaggerModel()
        {
            return new TaggerModel
            {
                DisplayName = DisplayName.Trim(),
                ModelUrl = LocalModelPath.Trim(),
                TagsUrl = LocalTagsPath.Trim(),
                InputSize = InputSize,
                InputFormat = (InputFormat ?? string.Empty).Trim().ToUpperInvariant()
            };
        }

        private void BrowseModelFile()
        {
            var dialog = new CommonOpenFileDialog
            {
                Title = LocalizationManager.Translate("选择 ONNX 模型文件"),
                IsFolderPicker = false
            };
            dialog.Filters.Add(new CommonFileDialogFilter("ONNX 模型", "*.onnx"));
            dialog.Filters.Add(new CommonFileDialogFilter("所有文件", "*.*"));

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                LocalModelPath = dialog.FileName;
                if (string.IsNullOrWhiteSpace(DisplayName))
                {
                    DisplayName = Path.GetFileNameWithoutExtension(LocalModelPath);
                }
                AutoDetectInputConfig();
            }
        }

        private void BrowseTagsFile()
        {
            var dialog = new CommonOpenFileDialog
            {
                Title = LocalizationManager.Translate("选择标签映射文件"),
                IsFolderPicker = false
            };
            dialog.Filters.Add(new CommonFileDialogFilter("标签映射", "*.csv;*.json"));
            dialog.Filters.Add(new CommonFileDialogFilter("所有文件", "*.*"));

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                LocalTagsPath = dialog.FileName;
            }
        }

        private void AutoDetectInputConfig()
        {
            if (string.IsNullOrWhiteSpace(LocalModelPath) || !File.Exists(LocalModelPath))
            {
                StatusMessage = "未找到本地模型文件，无法识别。";
                return;
            }

            if (TaggingProcessor.TryDetectInputConfig(LocalModelPath, out var detectedSize, out var detectedFormat, out var detectMessage))
            {
                if (detectedSize > 0)
                {
                    InputSize = detectedSize;
                }

                if (!string.IsNullOrWhiteSpace(detectedFormat))
                {
                    InputFormat = detectedFormat;
                }

                StatusMessage = string.IsNullOrWhiteSpace(detectMessage) ? "已自动识别输入参数。" : detectMessage;
                return;
            }

            StatusMessage = string.IsNullOrWhiteSpace(detectMessage) ? "未能识别输入参数，请手动选择。" : detectMessage;
        }

        private void Confirm()
        {
            var error = ValidateInputs();
            if (!string.IsNullOrWhiteSpace(error))
            {
                MessageBox.Show(error, "添加打标器", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RequestClose?.Invoke(true);
        }

        private string ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                return "请填写打标器名称。";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            if (DisplayName.IndexOfAny(invalidChars) >= 0)
            {
                return "名称包含非法字符，请修改。";
            }

            if (InputSize <= 0)
            {
                return "请输入正确的输入尺寸（正整数）。";
            }

            if (!InputFormats.Contains((InputFormat ?? string.Empty).Trim().ToUpperInvariant()))
            {
                return "请输入正确的输入通道格式（NCHW 或 NHWC）。";
            }

            if (string.IsNullOrWhiteSpace(LocalModelPath) || !File.Exists(LocalModelPath))
            {
                return "请选择有效的本地模型文件。";
            }

            if (string.IsNullOrWhiteSpace(LocalTagsPath) || !File.Exists(LocalTagsPath))
            {
                return "请选择有效的标签映射文件。";
            }

            return string.Empty;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            CommandManager.InvalidateRequerySuggested();
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
