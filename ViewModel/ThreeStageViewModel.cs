using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CyanTools.ViewModel
{
    public class ThreeStageViewModel : INotifyPropertyChanged
    {
        private string _inputFolder = "";
        private string _outputFolder = "";
        private string _progressText = "就绪";
        private double _progressPercentage = 0;
        private bool _isProcessing = false;

        private CancellationTokenSource? _cancellationTokenSource;
        private ThreeStageProcessor _processor;
        
        private const int MaxLogLines = 1000;

        public ThreeStageViewModel()
        {
            _processor = new ThreeStageProcessor();
            LogLines = new ObservableCollection<string>();

            BrowseInputFolderCommand = new RelayCommand(BrowseInputFolder);
            BrowseOutputFolderCommand = new RelayCommand(BrowseOutputFolder);
            StartProcessingCommand = new RelayCommand(StartProcessing, () => !_isProcessing && !string.IsNullOrEmpty(InputFolder) && !string.IsNullOrEmpty(OutputFolder));
            StopProcessingCommand = new RelayCommand(StopProcessing, () => _isProcessing);
            ClearLogCommand = new RelayCommand(ClearLog);


            PersonConfThreshold = 0.3f;
            HalfbodyConfThreshold = 0.5f;
            HeadConfThreshold = 0.4f;
            EyeConfThreshold = 0.3f;
            HeadScale = 1.5f;
            EyeScale = 2.4f;
            SplitPerson = true;
            SplitEyes = false;
            KeepOriginTags = false;
        }

        #region Properties

        public ObservableCollection<string> LogLines { get; }

        public string InputFolder
        {
            get => _inputFolder;
            set
            {
                _inputFolder = value;
                OnPropertyChanged();
                (StartProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                _outputFolder = value;
                OnPropertyChanged();
                (StartProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
        
        public string ProgressText
        {
            get => _progressText;
            set
            {
                _progressText = value;
                OnPropertyChanged();
            }
        }

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set
            {
                _progressPercentage = value;
                OnPropertyChanged();
            }
        }


        public bool SplitPerson { get => _processor.SplitPerson; set { _processor.SplitPerson = value; OnPropertyChanged(); } }
        public bool SplitEyes { get => _processor.SplitEyes; set { _processor.SplitEyes = value; OnPropertyChanged(); } }
        public bool KeepOriginTags { get => _processor.KeepOriginTags; set { _processor.KeepOriginTags = value; OnPropertyChanged(); } }
        public float PersonConfThreshold { get => _processor.PersonConfThreshold; set { _processor.PersonConfThreshold = value; OnPropertyChanged(); } }
        public float HalfbodyConfThreshold { get => _processor.HalfbodyConfThreshold; set { _processor.HalfbodyConfThreshold = value; OnPropertyChanged(); } }
        public float HeadConfThreshold { get => _processor.HeadConfThreshold; set { _processor.HeadConfThreshold = value; OnPropertyChanged(); } }
        public float EyeConfThreshold { get => _processor.EyeConfThreshold; set { _processor.EyeConfThreshold = value; OnPropertyChanged(); } }
        public float HeadScale { get => _processor.HeadScale; set { _processor.HeadScale = value; OnPropertyChanged(); } }
        public float EyeScale { get => _processor.EyeScale; set { _processor.EyeScale = value; OnPropertyChanged(); } }

        #endregion

        #region Commands

        public ICommand BrowseInputFolderCommand { get; }
        public ICommand BrowseOutputFolderCommand { get; }
        public ICommand StartProcessingCommand { get; }
        public ICommand StopProcessingCommand { get; }
        public ICommand ClearLogCommand { get; }

        #endregion

        #region Command Methods

        private void BrowseInputFolder()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                InputFolder = dialog.FileName;
            }
        }

        private void BrowseOutputFolder()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputFolder = dialog.FileName;
            }
        }

        private async void StartProcessing()
        {
            try
            {
                _isProcessing = true;
                _cancellationTokenSource = new CancellationTokenSource();

                (StartProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StopProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();

                ClearLog();
                ProgressPercentage = 0;
                ProgressText = "准备处理...";

                AddLog("开始三分法处理...");
                AddLog($"输入文件夹: {InputFolder}");
                AddLog($"输出文件夹: {OutputFolder}");

                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
                var imageFiles = Directory.GetFiles(InputFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLower()))
                    .ToList();

                if (imageFiles.Count == 0)
                {
                    AddLog("在输入文件夹中未找到图片文件！");
                    return;
                }

                AddLog($"找到 {imageFiles.Count} 个图片文件");
                Directory.CreateDirectory(OutputFolder);

                var progress = new Progress<string>(AddLog);
                int processedCount = 0;

                await Task.Run(async () =>
                {
                    foreach (var imageFile in imageFiles)
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested) break;

                        try
                        {
                            Application.Current.Dispatcher.Invoke(() => AddLog($"处理文件: {Path.GetFileName(imageFile)}"));
                            using var image = Image.Load<Rgb24>(imageFile);
                            var meta = new Dictionary<string, object> { ["filename"] = Path.GetFileName(imageFile) };
                            var inputItem = new ImageItem(image, meta);
                            var results = await _processor.ProcessImageAsync(inputItem, progress, _cancellationTokenSource.Token);

                            foreach (var result in results)
                            {
                                if (_cancellationTokenSource.Token.IsCancellationRequested) break;
                                var outputFilename = result.Meta.TryGetValue("filename", out var fn) ? fn.ToString() : $"{Path.GetFileNameWithoutExtension(imageFile)}_processed{Path.GetExtension(imageFile)}";
                                var outputPath = Path.Combine(OutputFolder, outputFilename!);
                                result.Image.Save(outputPath);
                                result.Image.Dispose();
                            }

                            processedCount++;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ProgressPercentage = (double)processedCount / imageFiles.Count * 100;
                                ProgressText = $"已处理 {processedCount}/{imageFiles.Count} 个文件";
                                AddLog($"完成处理: {Path.GetFileName(imageFile)}");
                            });
                        }
                        catch (Exception ex)
                        {
                            Application.Current.Dispatcher.Invoke(() => AddLog($"处理文件 {Path.GetFileName(imageFile)} 时出错: {ex.Message}"));
                        }
                    }
                });

                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    AddLog("处理已取消");
                    ProgressText = "处理已取消";
                }
                else
                {
                    AddLog($"处理完成！共处理 {processedCount} 个文件");
                    ProgressText = $"处理完成 ({processedCount}/{imageFiles.Count})";
                    ProgressPercentage = 100;
                }
            }
            catch (Exception ex)
            {
                AddLog($"处理过程中发生错误: {ex.Message}");
                MessageBox.Show($"处理过程中发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isProcessing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                (StartProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StopProcessingCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void StopProcessing()
        {
            _cancellationTokenSource?.Cancel();
            AddLog("正在停止处理...");
        }

        private void ClearLog()
        {
            LogLines.Clear();
        }

        #endregion

        private void AddLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                LogLines.Add($"[{timestamp}] {message}");
                if (LogLines.Count > MaxLogLines)
                {
                    LogLines.RemoveAt(0);
                }
            });
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
