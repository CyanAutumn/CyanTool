using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CyanTools.Model.Training;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel.TrainingSet
{
    public class TrainingSetBalancerViewModel : INotifyPropertyChanged, IPipelineDirectoryNode
    {
        private string _selectedFolderPath;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;
        public string SelectedFolderPath
        {
            get => _selectedFolderPath;
            set
            {
                if (SetField(ref _selectedFolderPath, value))
                {
                    LoadFolderData();
                }
            }
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set => SetField(ref _isWorkflowMode, value);
        }

        public bool HasInheritedInputPath
        {
            get => _hasInheritedInputPath;
            set => SetField(ref _hasInheritedInputPath, value);
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.InPlace;
        public string PipelineInputPath { get => SelectedFolderPath ?? string.Empty; set => SelectedFolderPath = value ?? string.Empty; }
        public string PipelineOutputPath { get => SelectedFolderPath ?? string.Empty; set => SelectedFolderPath = value ?? string.Empty; }

        public ObservableCollection<FolderInfo> FolderItems { get; } = new ObservableCollection<FolderInfo>();

        public ICommand BrowseFolderCommand { get; }
        public ICommand BalanceCommand { get; }
        public ICommand ApplyRenamingCommand { get; }

        public TrainingSetBalancerViewModel()
        {
            BrowseFolderCommand = new RelayCommand(_ => SelectFolder());
            BalanceCommand = new RelayCommand(_ => BalanceRepetitions());
            ApplyRenamingCommand = new RelayCommand(_ => ApplyRenaming());
        }

        private void ApplyRenaming()
        {
            if (string.IsNullOrEmpty(SelectedFolderPath) || !Directory.Exists(SelectedFolderPath) || !FolderItems.Any())
            {
                MessageBox.Show("请先选择一个包含子目录的有效输入路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show("您确定要根据表格中的重复次数重命名文件夹吗？\n此操作将直接修改您的文件系统，且不可逆。", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                int renamedCount = 0;
                foreach (var item in FolderItems)
                {
                    var originalFolderName = item.FolderName;
                    var parts = originalFolderName.Split('_');
                    string baseName;

                    if (parts.Length > 1 && int.TryParse(parts[0], out _))
                    {
                        baseName = string.Join("_", parts.Skip(1));
                    }
                    else
                    {
                        baseName = originalFolderName;
                    }

                    var newFolderName = $"{item.Repetitions}_{baseName}";

                    if (originalFolderName.Equals(newFolderName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var originalFullPath = Path.Combine(SelectedFolderPath, originalFolderName);
                    var newFullPath = Path.Combine(SelectedFolderPath, newFolderName);

                    if (Directory.Exists(newFullPath))
                    {
                        MessageBox.Show($"无法重命名 '{originalFolderName}' 为 '{newFolderName}'，因为目标路径已存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
                    }

                    Directory.Move(originalFullPath, newFullPath);
                    item.FolderName = newFolderName;
                    renamedCount++;
                }

                MessageBox.Show($"操作完成！成功重命名 {renamedCount} 个文件夹。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重命名过程中发生错误：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BalanceRepetitions()
        {
            if (FolderItems == null || !FolderItems.Any()) return;

            int maxImageCount = FolderItems.Max(item => item.ImageCount);
            if (maxImageCount == 0) return;

            foreach (var item in FolderItems)
            {
                if (item.ImageCount > 0)
                {
                    double idealRepetitions = (double)maxImageCount / item.ImageCount;
                    item.Repetitions = Math.Max(1, (int)Math.Round(idealRepetitions));
                }
                else
                {
                    item.Repetitions = 1;
                }
            }
        }

        private void SelectFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择输入路径"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                SelectedFolderPath = dialog.FileName;
            }
        }

        private void LoadFolderData()
        {
            FolderItems.Clear();
            if (string.IsNullOrEmpty(SelectedFolderPath) || !Directory.Exists(SelectedFolderPath)) return;

            var subDirectories = Directory.GetDirectories(SelectedFolderPath);
            var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

            foreach (var dir in subDirectories)
            {
                var dirInfo = new DirectoryInfo(dir);
                var parts = dirInfo.Name.Split('_');
                int repetitions = 1;
                if (parts.Length > 1 && int.TryParse(parts[0], out int parsedRepetitions))
                {
                    repetitions = parsedRepetitions;
                }

                var imageCount = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                                          .Count(f => imageExtensions.Contains(Path.GetExtension(f).ToLower()));

                var folderInfo = new FolderInfo
                {
                    FolderName = dirInfo.Name,
                    ImageCount = imageCount,
                    Repetitions = repetitions
                };
                folderInfo.PropertyChanged += (s, e) => RecalculatePercentages();
                FolderItems.Add(folderInfo);
            }
            RecalculatePercentages();
        }

        private void RecalculatePercentages()
        {
            double totalSteps = FolderItems.Sum(item => (double)item.ImageCount * item.Repetitions);
            if (totalSteps == 0) return;

            foreach (var item in FolderItems)
            {
                item.StepPercentage = ((double)item.ImageCount * item.Repetitions / totalSteps) * 100;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
