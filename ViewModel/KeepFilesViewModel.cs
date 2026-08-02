using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public class KeepFilesViewModel : INotifyPropertyChanged, IPipelineDirectoryNode
    {
        private string _folderPath;
        private string _logText = string.Empty;
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;

        public KeepFilesViewModel()
        {
            KeepJpg = true;
            KeepPng = true;
            KeepWebp = true;
            KeepBmp = true;
            KeepJpeg = true;
            KeepNpz = true;
            KeepTxt = true;

            SelectFolderCommand = new RelayCommand(SelectFolder);
            DeleteFilesCommand = new RelayCommand(DeleteFiles);
        }

        public string FolderPath
        {
            get => _folderPath;
            set
            {
                if (_folderPath != value)
                {
                    _folderPath = value;
                    OnPropertyChanged(nameof(FolderPath));
                }
            }
        }

        public string LogText
        {
            get => _logText;
            private set
            {
                if (_logText != value)
                {
                    _logText = value;
                    OnPropertyChanged(nameof(LogText));
                }
            }
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set
            {
                if (_isWorkflowMode != value)
                {
                    _isWorkflowMode = value;
                    OnPropertyChanged(nameof(IsWorkflowMode));
                }
            }
        }

        public bool HasInheritedInputPath
        {
            get => _hasInheritedInputPath;
            set
            {
                if (_hasInheritedInputPath != value)
                {
                    _hasInheritedInputPath = value;
                    OnPropertyChanged(nameof(HasInheritedInputPath));
                }
            }
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.InPlace;
        public string PipelineInputPath { get => FolderPath ?? string.Empty; set => FolderPath = value ?? string.Empty; }
        public string PipelineOutputPath { get => FolderPath ?? string.Empty; set => FolderPath = value ?? string.Empty; }

        private bool _keepJpg = true;
        private bool _keepPng = true;
        private bool _keepWebp = true;
        private bool _keepBmp = true;
        private bool _keepJpeg = true;
        private bool _keepNpz = true;
        private bool _keepTxt = true;
        private bool _keepChineseTxt = true;

        public bool KeepJpg
        {
            get => _keepJpg;
            set
            {
                if (_keepJpg != value)
                {
                    _keepJpg = value;
                    OnPropertyChanged(nameof(KeepJpg));
                }
            }
        }

        public bool KeepPng
        {
            get => _keepPng;
            set
            {
                if (_keepPng != value)
                {
                    _keepPng = value;
                    OnPropertyChanged(nameof(KeepPng));
                }
            }
        }

        public bool KeepWebp
        {
            get => _keepWebp;
            set
            {
                if (_keepWebp != value)
                {
                    _keepWebp = value;
                    OnPropertyChanged(nameof(KeepWebp));
                }
            }
        }

        public bool KeepBmp
        {
            get => _keepBmp;
            set
            {
                if (_keepBmp != value)
                {
                    _keepBmp = value;
                    OnPropertyChanged(nameof(KeepBmp));
                }
            }
        }

        public bool KeepJpeg
        {
            get => _keepJpeg;
            set
            {
                if (_keepJpeg != value)
                {
                    _keepJpeg = value;
                    OnPropertyChanged(nameof(KeepJpeg));
                }
            }
        }

        public bool KeepNpz
        {
            get => _keepNpz;
            set
            {
                if (_keepNpz != value)
                {
                    _keepNpz = value;
                    OnPropertyChanged(nameof(KeepNpz));
                }
            }
        }

        public bool KeepTxt
        {
            get => _keepTxt;
            set
            {
                if (_keepTxt != value)
                {
                    _keepTxt = value;
                    OnPropertyChanged(nameof(KeepTxt));
                }
            }
        }

        public bool KeepChineseTxt
        {
            get => _keepChineseTxt;
            set
            {
                if (_keepChineseTxt != value)
                {
                    _keepChineseTxt = value;
                    OnPropertyChanged(nameof(KeepChineseTxt));
                }
            }
        }

        public ICommand SelectFolderCommand { get; }
        public ICommand DeleteFilesCommand { get; }

        private void SelectFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = LocalizationManager.Translate("选择输入路径")
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                FolderPath = dialog.FileName;
            }
        }

        private void DeleteFiles()
        {
            if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
            {
                MessageBox.Show("请先输入有效的输入路径。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var options = BuildOptions();
            var extensionsToKeep = KeepFilesRetentionPolicy.BuildExtensionsToKeep(options);
            if (extensionsToKeep.Count == 0)
            {
                MessageBox.Show("请至少保留一种文件类型。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var files = Directory.GetFiles(FolderPath);
                int deletedCount = 0;
                _logBuilder.Clear();
                AppendLog($"开始处理输入路径: {FolderPath}");

                foreach (var file in files)
                {
                    var deleteReason = KeepFilesRetentionPolicy.GetDeleteReason(file, options);
                    if (deleteReason != KeepFilesDeleteReason.None)
                    {
                        File.Delete(file);
                        AppendLog(BuildDeleteLog(file, deleteReason));
                        deletedCount++;
                    }
                }

                AppendLog($"操作完成。共删除了 {deletedCount} 个文件。");
                MessageBox.Show($"操作完成。共删除了 {deletedCount} 个文件。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"错误: {ex.Message}");
                MessageBox.Show($"处理文件时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private KeepFilesOptions BuildOptions()
        {
            return new KeepFilesOptions
            {
                KeepJpg = KeepJpg,
                KeepPng = KeepPng,
                KeepWebp = KeepWebp,
                KeepBmp = KeepBmp,
                KeepJpeg = KeepJpeg,
                KeepNpz = KeepNpz,
                KeepTxt = KeepTxt,
                KeepChineseTxt = KeepChineseTxt
            };
        }

        private static string BuildDeleteLog(string filePath, KeepFilesDeleteReason deleteReason)
        {
            return deleteReason == KeepFilesDeleteReason.ChineseContentNotAllowed
                ? $"已删除(含中文内容 txt): {filePath}"
                : $"已删除: {filePath}";
        }

        private void AppendLog(string message)
        {
            _logBuilder.AppendLine(message);
            LogText = _logBuilder.ToString();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
