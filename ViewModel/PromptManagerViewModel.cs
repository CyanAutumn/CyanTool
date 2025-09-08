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
                        System.Windows.MessageBox.Show($"读取文件失败: {ex.Message}");
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

        public PromptManagerViewModel()
        {
            OpenFolderCommand = new RelayCommand(_ => OpenFolder());
            CreateOrEditCommand = new RelayCommand(_ => CreateOrEdit(), _ => !string.IsNullOrEmpty(_currentFolderPath) && !string.IsNullOrEmpty(CurrentFileName));
            DeleteCommand = new RelayCommand(_ => DeleteFile(), _ => !string.IsNullOrEmpty(SelectedPromptFile));
            GetRandomPromptCommand = new RelayCommand(_ => GetRandomPrompt(), _ => PromptFiles.Any());
        }

        private void OpenFolder()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = "请选择一个文件夹" };
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
                System.Windows.MessageBox.Show($"加载文件夹时出错: {ex.Message}");
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
                System.Windows.MessageBox.Show($"该内容已存在于文件: {existingFile}\n跳过添加。", "内容重复", MessageBoxButton.OK, MessageBoxImage.Information);
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
                System.Windows.MessageBox.Show("文件已保存！");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存文件失败: {ex.Message}");
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
                System.Windows.MessageBox.Show($"检查重复内容时出错: {ex.Message}");
            }

            return null;
        }

        private void DeleteFile()
        {
            string fullPath = Path.Combine(_currentFolderPath, SelectedPromptFile);
            if (System.Windows.MessageBox.Show($"确定要删除 {SelectedPromptFile} 吗？", "确认删除", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(fullPath);
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
                    System.Windows.MessageBox.Show($"删除文件失败: {ex.Message}");
                }
            }
        }

        private void GetRandomPrompt()
        {
            if (!PromptFiles.Any()) return;
            int index = _random.Next(PromptFiles.Count);
            SelectedPromptFile = PromptFiles[index];
            if(SelectedPromptFile != null)
            {
                System.Windows.Clipboard.SetText(CurrentPromptContent);
                System.Windows.MessageBox.Show($"已随机抽取并复制: {CurrentFileName}");
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