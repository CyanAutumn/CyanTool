using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public class PromptManagerViewModel : INotifyPropertyChanged
    {
        private string _currentFolderPath;
        private string _selectedPromptFile;
        private string _currentFileName;
        private string _currentPromptContent;
        private readonly Random _random = new Random();
        private static readonly string[] _imageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tiff" };

        public ObservableCollection<string> PromptFiles { get; } = new ObservableCollection<string>();

        public string SelectedPromptFile
        {
            get => _selectedPromptFile;
            set
            {
                if (SetField(ref _selectedPromptFile, value) && _selectedPromptFile != null)
                {
                    try
                    {
                        CurrentFileName = Path.GetFileName(_selectedPromptFile);
                        CurrentPromptContent = File.ReadAllText(Path.Combine(_currentFolderPath, _selectedPromptFile));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"读取文件失败: {ex.Message}");
                    }
                }
            }
        }

        public string CurrentFileName
        {
            get => _currentFileName;
            set => SetField(ref _currentFileName, value);
        }

        public string CurrentPromptContent
        {
            get => _currentPromptContent;
            set => SetField(ref _currentPromptContent, value);
        }

        public ICommand OpenFolderCommand { get; }
        public ICommand CreateOrEditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand GetRandomPromptCommand { get; }
        public ICommand AutoIncrementAddCommand { get; }

        public PromptManagerViewModel()
        {
            OpenFolderCommand = new RelayCommand(_ => OpenFolder());
            CreateOrEditCommand = new RelayCommand(_ => CreateOrEdit(), _ => !string.IsNullOrEmpty(_currentFolderPath) && !string.IsNullOrEmpty(CurrentFileName));
            DeleteCommand = new RelayCommand(_ => DeleteFile(), _ => !string.IsNullOrEmpty(SelectedPromptFile));
            GetRandomPromptCommand = new RelayCommand(_ => GetRandomPrompt(), _ => PromptFiles.Any());
            AutoIncrementAddCommand = new RelayCommand(_ => AutoIncrementAdd(), _ => !string.IsNullOrEmpty(_currentFolderPath) && !string.IsNullOrWhiteSpace(CurrentPromptContent));
        }

        private void OpenFolder()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = LocalizationManager.Translate("请选择一个文件夹") };
            if (dialog.ShowDialog() != CommonFileDialogResult.Ok) return;

            _currentFolderPath = dialog.FileName;
            try
            {
                PromptFiles.Clear();
                var files = Directory.GetFiles(_currentFolderPath, "*.txt").Select(Path.GetFileName).ToList();
                foreach (var file in files)
                {
                    PromptFiles.Add(file);
                }
                SelectedPromptFile = PromptFiles.FirstOrDefault();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件夹时出错: {ex.Message}");
            }
        }

        private void CreateOrEdit()
        {
            string fileName = CurrentFileName.Trim();
            if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".txt";
            }
            string fullPath = Path.Combine(_currentFolderPath, fileName);


            string existingFile = FindFileWithSameContent(CurrentPromptContent, fileName);
            if (!string.IsNullOrEmpty(existingFile))
            {
                MessageBox.Show($"该内容已存在于文件: {existingFile}\n跳过添加。", "内容重复", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                File.WriteAllText(fullPath, CurrentPromptContent);
                if (!PromptFiles.Contains(fileName))
                {
                    PromptFiles.Add(fileName);
                    SelectedPromptFile = fileName;
                }
                MessageBox.Show("文件已保存！");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存文件失败: {ex.Message}");
            }
        }

        private string FindFileWithSameContent(string content, string excludeFileName = null)
        {
            if (string.IsNullOrEmpty(_currentFolderPath) || string.IsNullOrEmpty(content))
                return null;

            try
            {
                var files = Directory.GetFiles(_currentFolderPath, "*.txt");
                foreach (string filePath in files)
                {
                    string fileName = Path.GetFileName(filePath);
                    

                    if (!string.IsNullOrEmpty(excludeFileName) && 
                        fileName.Equals(excludeFileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string fileContent = File.ReadAllText(filePath);
                    if (content.Equals(fileContent, StringComparison.OrdinalIgnoreCase))
                    {
                        return fileName;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"检查重复内容时出错: {ex.Message}");
            }

            return null;
        }

        private void DeleteFile()
        {
            string fullPath = Path.Combine(_currentFolderPath, SelectedPromptFile);
            if (MessageBox.Show($"确定要删除 {SelectedPromptFile} 吗？", "确认删除", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(fullPath);


                    string baseName = Path.GetFileNameWithoutExtension(SelectedPromptFile);
                    foreach (var ext in _imageExtensions)
                    {
                        string imagePath = Path.Combine(_currentFolderPath, baseName + ext);
                        if (File.Exists(imagePath))
                        {
                            try
                            {
                                File.Delete(imagePath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"删除关联图片文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }

                    int currentIndex = PromptFiles.IndexOf(SelectedPromptFile);
                    PromptFiles.Remove(SelectedPromptFile);
                    if (PromptFiles.Any())
                    {
                        SelectedPromptFile = PromptFiles[Math.Min(currentIndex, PromptFiles.Count - 1)];
                    }
                    else
                    {
                        CurrentFileName = string.Empty;
                        CurrentPromptContent = string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"删除文件失败: {ex.Message}");
                }
            }
        }

        private async void GetRandomPrompt()
        {
            if (!PromptFiles.Any()) return;
            int index = _random.Next(PromptFiles.Count);
            SelectedPromptFile = PromptFiles[index];
            if (SelectedPromptFile != null)
            {
                await ClipboardHelper.TrySetTextAsync(CurrentPromptContent ?? string.Empty);
            }
        }

        private void AutoIncrementAdd()
        {
            if (string.IsNullOrEmpty(_currentFolderPath) || string.IsNullOrWhiteSpace(CurrentPromptContent))
            {
                return;
            }

            string existingFile = FindFileWithSameContent(CurrentPromptContent);
            if (!string.IsNullOrEmpty(existingFile))
            {
                MessageBox.Show($"该内容已存在于文件: {existingFile}", "内容重复", MessageBoxButton.OK, MessageBoxImage.Information);
                if (PromptFiles.Contains(existingFile))
                {
                    SelectedPromptFile = existingFile;
                }
                return;
            }

            string newFileName;
            int index = 1;
            do
            {
                newFileName = $"{index}.txt";
                index++;
            } while (File.Exists(Path.Combine(_currentFolderPath, newFileName)));

            string fullPath = Path.Combine(_currentFolderPath, newFileName);
            try
            {
                File.WriteAllText(fullPath, CurrentPromptContent);
                if (!PromptFiles.Contains(newFileName))
                {
                    PromptFiles.Add(newFileName);
                }
                CurrentFileName = newFileName;
                SelectedPromptFile = newFileName;
                MessageBox.Show($"已创建文件: {newFileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
