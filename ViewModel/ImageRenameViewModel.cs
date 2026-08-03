using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class ImageRenameViewModel : INotifyPropertyChanged, IPipelineDirectoryNode
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"
        };

        private string _inputPath = string.Empty;
        private string _namePrefix = "image_";
        private int _startIndex = 1;
        private int _numberPadding = 4;
        private bool _isRenaming;
        private bool _isRefreshing;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;

        public ObservableCollection<RenamePreviewItem> PreviewItems { get; } = new ObservableCollection<RenamePreviewItem>();

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
                RefreshPreview();
            }
        }

        public string NamePrefix
        {
            get => _namePrefix;
            set
            {
                var newValue = value ?? string.Empty;
                if (_namePrefix == newValue)
                {
                    return;
                }

                _namePrefix = newValue;
                OnPropertyChanged(nameof(NamePrefix));
                OnPropertyChanged(nameof(SampleFileName));
                RefreshPreview();
            }
        }

        public int StartIndex
        {
            get => _startIndex;
            set
            {
                var normalized = value < 0 ? 0 : value;
                if (_startIndex == normalized)
                {
                    return;
                }

                _startIndex = normalized;
                OnPropertyChanged(nameof(StartIndex));
                OnPropertyChanged(nameof(SampleFileName));
                RefreshPreview();
            }
        }

        public int NumberPadding
        {
            get => _numberPadding;
            set
            {
                var normalized = value < 1 ? 1 : value;
                if (_numberPadding == normalized)
                {
                    return;
                }

                _numberPadding = normalized;
                OnPropertyChanged(nameof(NumberPadding));
                OnPropertyChanged(nameof(SampleFileName));
                RefreshPreview();
            }
        }

        public bool IsRenaming
        {
            get => _isRenaming;
            private set
            {
                if (_isRenaming == value)
                {
                    return;
                }

                _isRenaming = value;
                OnPropertyChanged(nameof(IsRenaming));
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

        public int FileCount => PreviewItems.Count;

        public bool HasConflicts => PreviewItems.Any(item => item.HasConflict);

        public string SampleFileName => BuildNewFileName(StartIndex, ".png");
        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.InPlace;
        public string PipelineInputPath { get => InputPath; set => InputPath = value ?? string.Empty; }
        public string PipelineOutputPath { get => InputPath; set => InputPath = value ?? string.Empty; }

        public ICommand SelectInputPathCommand { get; }
        public ICommand RefreshPreviewCommand { get; }
        public ICommand ExecuteRenameCommand { get; }
        public ICommand ShuffleRenameCommand { get; }

        public ImageRenameViewModel()
        {
            SelectInputPathCommand = new RelayCommand(SelectInputPath);
            RefreshPreviewCommand = new RelayCommand(RefreshPreview);
            ExecuteRenameCommand = new RelayCommand(async () => await ExecuteRenameAsync(), CanExecuteRename);
            ShuffleRenameCommand = new RelayCommand(async () => await ExecuteShuffleRenameAsync(), CanExecuteShuffleRename);
        }

        private void SelectInputPath()
        {
            try
            {
                using var dialog = new CommonOpenFileDialog
                {
                    IsFolderPicker = true,
                    Title = "请选择图片所在的文件夹"
                };

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    InputPath = dialog.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开路径选择器: {ex.Message}", "错误");
            }
        }

        private bool CanExecuteRename()
        {
            return !IsRenaming && PreviewItems.Count > 0 && !HasConflicts && Directory.Exists(InputPath);
        }

        private bool CanExecuteShuffleRename()
        {
            return !IsRenaming && PreviewItems.Count > 1 && !HasConflicts && Directory.Exists(InputPath);
        }

        private void RefreshPreview()
        {
            if (_isRefreshing)
            {
                return;
            }

            _isRefreshing = true;

            try
            {
                PreviewItems.Clear();

                if (string.IsNullOrWhiteSpace(InputPath) || !Directory.Exists(InputPath))
                {
                    UpdateDerivedProperties();
                    return;
                }

                var files = Directory.EnumerateFiles(InputPath, "*", SearchOption.TopDirectoryOnly)
                                      .Where(file => SupportedExtensions.Contains(Path.GetExtension(file) ?? string.Empty))
                                      .OrderBy(file => file, StringComparer.CurrentCultureIgnoreCase)
                                      .ToList();

                int index = StartIndex;
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    var extension = Path.GetExtension(file) ?? string.Empty;
                    var newName = BuildNewFileName(index, extension);
                    var newFullPath = Path.Combine(InputPath, newName);
                    var originalFullPath = file;
                    var isSamePath = string.Equals(originalFullPath, newFullPath, StringComparison.OrdinalIgnoreCase);

                    bool duplicateName = usedNames.Contains(newName);
                    bool existsConflict = !isSamePath && File.Exists(newFullPath);
                    bool hasConflict = duplicateName || existsConflict;

                    usedNames.Add(newName);

                    var sequence = index;
                    var status = hasConflict ? (duplicateName ? "命名重复" : "文件已存在") : string.Empty;

                    PreviewItems.Add(new RenamePreviewItem(sequence, Path.GetFileName(originalFullPath), newName, originalFullPath, newFullPath, hasConflict, status));

                    index++;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成预览时出现错误: {ex.Message}", "错误");
            }
            finally
            {
                _isRefreshing = false;
                UpdateDerivedProperties();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task ExecuteRenameAsync()
        {
            if (!CanExecuteRename())
            {
                MessageBox.Show(HasConflicts ? "存在命名冲突，请调整规则后重试。" : "请先选择有效的输入路径并生成预览。", "提示");
                return;
            }

            var operations = PreviewItems
                .Where(item => !item.HasConflict)
                .Select(item => new RenameOperation(item.OriginalFullPath, item.NewFullPath))
                .Where(operation => !string.Equals(operation.Source, operation.Target, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (operations.Count == 0)
            {
                MessageBox.Show("没有需要重命名的文件。", "提示");
                return;
            }

            IsRenaming = true;
            await PerformRenameWithTempAsync(operations, $"重命名完成，共处理 {operations.Count} 个文件。");
        }

        private async Task ExecuteShuffleRenameAsync()
        {
            if (!CanExecuteShuffleRename())
            {
                MessageBox.Show(HasConflicts ? "存在命名冲突，请调整规则后重试。" : "请确保当前目录中至少包含两张图片。", "提示");
                return;
            }

            try
            {
                var files = Directory.EnumerateFiles(InputPath, "*", SearchOption.TopDirectoryOnly)
                                      .Where(file => SupportedExtensions.Contains(Path.GetExtension(file) ?? string.Empty))
                                      .ToList();

                if (files.Count <= 1)
                {
                    MessageBox.Show("图片数量不足，无法打乱。", "提示");
                    return;
                }

                var generatedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var generatedTextTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var operations = new List<RenameOperation>(files.Count * 2);

                foreach (var sourcePath in files)
                {
                    var extension = Path.GetExtension(sourcePath) ?? string.Empty;
                    var textSourcePath = Path.ChangeExtension(sourcePath, ".txt");
                    var hasTextFile = File.Exists(textSourcePath);
                    string newName;
                    string newBaseName;
                    string targetPath;
                    string textTargetPath = string.Empty;

                    do
                    {
                        newBaseName = Guid.NewGuid().ToString("N");
                        newName = newBaseName + extension;
                        targetPath = Path.Combine(InputPath, newName);
                        if (hasTextFile)
                        {
                            textTargetPath = Path.Combine(InputPath, newBaseName + ".txt");
                        }
                    } while (generatedNames.Contains(newName)
                             || File.Exists(targetPath)
                             || string.Equals(Path.GetFileName(sourcePath), newName, StringComparison.OrdinalIgnoreCase)
                             || (hasTextFile && (generatedTextTargets.Contains(textTargetPath) || File.Exists(textTargetPath))));

                    generatedNames.Add(newName);
                    operations.Add(new RenameOperation(sourcePath, targetPath));

                    if (hasTextFile)
                    {
                        generatedTextTargets.Add(textTargetPath);
                        operations.Add(new RenameOperation(textSourcePath, textTargetPath));
                    }
                }

                IsRenaming = true;
                await PerformRenameWithTempAsync(operations, $"打乱完成，共处理 {files.Count} 个图片文件。");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打乱过程中出现错误: {ex.Message}", "错误");
            }
        }

        private async Task PerformRenameWithTempAsync(List<RenameOperation> operations, string successMessage)
        {
            try
            {
                var directory = InputPath;

                await Task.Run(() =>
                {
                    var tempMappings = new List<(string TempPath, string TargetPath)>(operations.Count);

                    foreach (var operation in operations)
                    {
                        var tempPath = CreateTemporaryPath(directory);
                        File.Move(operation.Source, tempPath);
                        tempMappings.Add((tempPath, operation.Target));
                    }

                    foreach (var mapping in tempMappings)
                    {
                        File.Move(mapping.TempPath, mapping.TargetPath);
                    }
                });

                MessageBox.Show(successMessage, "完成");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重命名失败: {ex.Message}", "错误");
            }
            finally
            {
                IsRenaming = false;
                RefreshPreview();
            }
        }

        private static string CreateTemporaryPath(string directory)
        {
            string tempPath;
            do
            {
                tempPath = Path.Combine(directory, Path.GetRandomFileName());
            } while (File.Exists(tempPath));

            return tempPath;
        }

        private readonly struct RenameOperation
        {
            public RenameOperation(string source, string target)
            {
                Source = source;
                Target = target;
            }

            public string Source { get; }
            public string Target { get; }
        }

        private string BuildNewFileName(int index, string extension)
        {
            var padded = index.ToString().PadLeft(NumberPadding, '0');
            return string.Concat(NamePrefix, padded, extension ?? string.Empty);
        }

        private void UpdateDerivedProperties()
        {
            OnPropertyChanged(nameof(FileCount));
            OnPropertyChanged(nameof(HasConflicts));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RenamePreviewItem
    {
        public RenamePreviewItem(int sequenceNumber, string originalName, string newName, string originalFullPath, string newFullPath, bool hasConflict, string status)
        {
            SequenceNumber = sequenceNumber;
            OriginalName = originalName;
            NewName = newName;
            OriginalFullPath = originalFullPath;
            NewFullPath = newFullPath;
            HasConflict = hasConflict;
            Status = status;
        }

        public int SequenceNumber { get; }
        public string OriginalName { get; }
        public string NewName { get; }
        public string OriginalFullPath { get; }
        public string NewFullPath { get; }
        public bool HasConflict { get; }
        public string Status { get; }
    }
}
