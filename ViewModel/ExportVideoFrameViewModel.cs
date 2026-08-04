using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public class ExportVideoFrameViewModel : INotifyPropertyChanged, IDisposable
    {
        private string _videoPath;
        private string _outputFolder;
        private string _frameIntervalText = "1";
        private BitmapSource _currentFrame;
        private double _maxFrameIndex;
        private int _currentFrameIndex;
        private bool _isVideoLoaded;
        private bool _isUpdatingFrameIndex;
        private VideoCapture _videoCapture;
        private Mat _currentMat;
        private double _videoFps = 30;

        public ExportVideoFrameViewModel()
        {
            SelectVideoCommand = new RelayCommand(SelectVideo);
            SelectOutputCommand = new RelayCommand(SelectOutputFolder);
            PreviousFrameCommand = new RelayCommand(() => ChangeFrame(-GetFrameIntervalInFrames()), () => IsVideoLoaded);
            NextFrameCommand = new RelayCommand(() => ChangeFrame(GetFrameIntervalInFrames()), () => IsVideoLoaded);
            ExportCurrentFrameCommand = new RelayCommand(ExportCurrentFrame, CanExportFrames);
            ExportAllFramesCommand = new RelayCommand(ExportAllFrames, CanExportFrames);
            ExportGifCommand = new RelayCommand(ExportGif, CanExportFrames);
        }

        public string VideoPath
        {
            get => _videoPath;
            set
            {
                if (_videoPath != value)
                {
                    _videoPath = value;
                    OnPropertyChanged(nameof(VideoPath));
                }
            }
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                if (_outputFolder != value)
                {
                    _outputFolder = value;
                    OnPropertyChanged(nameof(OutputFolder));
                    RaiseCommandStates();
                }
            }
        }

        public BitmapSource CurrentFrame
        {
            get => _currentFrame;
            private set
            {
                if (_currentFrame != value)
                {
                    _currentFrame = value;
                    OnPropertyChanged(nameof(CurrentFrame));
                }
            }
        }

        public double MaxFrameIndex
        {
            get => _maxFrameIndex;
            private set
            {
                if (Math.Abs(_maxFrameIndex - value) > double.Epsilon)
                {
                    _maxFrameIndex = value;
                    OnPropertyChanged(nameof(MaxFrameIndex));
                }
            }
        }

        public int CurrentFrameIndex
        {
            get => _currentFrameIndex;
            set
            {
                if (_currentFrameIndex != value)
                {
                    _currentFrameIndex = value;
                    OnPropertyChanged(nameof(CurrentFrameIndex));
                    if (!_isUpdatingFrameIndex)
                    {
                        DisplayFrame(_currentFrameIndex);
                    }
                }
            }
        }

        public bool IsVideoLoaded
        {
            get => _isVideoLoaded;
            private set
            {
                if (_isVideoLoaded != value)
                {
                    _isVideoLoaded = value;
                    OnPropertyChanged(nameof(IsVideoLoaded));
                    RaiseCommandStates();
                }
            }
        }

        public string FrameIntervalText
        {
            get => _frameIntervalText;
            set
            {
                if (_frameIntervalText != value)
                {
                    _frameIntervalText = value;
                    OnPropertyChanged(nameof(FrameIntervalText));
                }
            }
        }

        public ICommand SelectVideoCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand PreviousFrameCommand { get; }
        public ICommand NextFrameCommand { get; }
        public ICommand ExportCurrentFrameCommand { get; }
        public ICommand ExportAllFramesCommand { get; }
        public ICommand ExportGifCommand { get; }

        private void SelectVideo()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Video files (*.mp4;*.avi;*.mov;*.mkv)|*.mp4;*.avi;*.mov;*.mkv|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                VideoPath = dialog.FileName;
                InitializeVideoCapture();
            }
        }

        private void SelectOutputFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputFolder = dialog.FileName;
            }
        }

        private void InitializeVideoCapture()
        {
            ReleaseResources();

            if (string.IsNullOrEmpty(VideoPath))
            {
                IsVideoLoaded = false;
                return;
            }

            _videoCapture = new VideoCapture(VideoPath);
            if (!_videoCapture.IsOpened())
            {
                MessageBox.Show("无法打开视频文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                IsVideoLoaded = false;
                return;
            }

            _videoFps = _videoCapture.Fps;
            if (_videoFps <= 0)
            {
                _videoFps = 30;
                MessageBox.Show("无法获取视频帧率，将使用默认值 30 FPS。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            MaxFrameIndex = Math.Max(0, _videoCapture.FrameCount - 1);
            _isUpdatingFrameIndex = true;
            CurrentFrameIndex = 0;
            _isUpdatingFrameIndex = false;
            IsVideoLoaded = true;
            DisplayFrame(0);
        }

        private void DisplayFrame(int frameIndex)
        {
            if (!IsVideoLoaded || _videoCapture == null || !_videoCapture.IsOpened())
            {
                return;
            }

            frameIndex = Math.Max(0, Math.Min(frameIndex, (int)MaxFrameIndex));
            _videoCapture.PosFrames = frameIndex;
            using var frame = _videoCapture.RetrieveMat();

            if (frame == null || frame.Empty())
            {
                MessageBox.Show("无法读取帧。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentMat?.Dispose();
            _currentMat = frame.Clone();

            var bitmap = BitmapSourceConverter.ToBitmapSource(frame);
            bitmap.Freeze();
            CurrentFrame = bitmap;

            _isUpdatingFrameIndex = true;
            _currentFrameIndex = frameIndex;
            OnPropertyChanged(nameof(CurrentFrameIndex));
            _isUpdatingFrameIndex = false;
        }

        private void ChangeFrame(int offset)
        {
            if (!IsVideoLoaded)
            {
                return;
            }
            int newIndex = Math.Max(0, Math.Min((int)MaxFrameIndex, CurrentFrameIndex + offset));
            DisplayFrame(newIndex);
        }

        private int GetFrameIntervalInFrames()
        {
            if (!int.TryParse(FrameIntervalText, out int intervalMs) || intervalMs <= 0)
            {
                intervalMs = 1;
            }

            double intervalSeconds = intervalMs / 1000.0;
            int framesToSkip = (int)Math.Round(intervalSeconds * _videoFps);
            return Math.Max(1, framesToSkip);
        }

        private bool CanExportFrames()
        {
            return IsVideoLoaded && !string.IsNullOrWhiteSpace(OutputFolder);
        }

        private void ExportCurrentFrame()
        {
            if (_currentMat == null || string.IsNullOrEmpty(OutputFolder))
            {
                MessageBox.Show("请先选择视频并确保已选择输出路径。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Directory.CreateDirectory(OutputFolder);
                string fileName = $"frame_{CurrentFrameIndex:D5}.png";
                string fullPath = Path.Combine(OutputFolder, fileName);
                _currentMat.SaveImage(fullPath);
                MessageBox.Show($"当前帧已保存到: {fullPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存当前帧失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportAllFrames()
        {
            if (!CanExportFrames())
            {
                MessageBox.Show("请先选择视频并确保已选择输出路径。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int frameInterval = GetFrameIntervalInFrames();
            Directory.CreateDirectory(OutputFolder);

            try
            {
                _videoCapture.PosFrames = 0;
                for (int i = 0; i <= MaxFrameIndex; i += frameInterval)
                {
                    _videoCapture.PosFrames = i;
                    using var frame = _videoCapture.RetrieveMat();
                    if (frame != null && !frame.Empty())
                    {
                        string fileName = $"frame_{i:D5}.png";
                        string fullPath = Path.Combine(OutputFolder, fileName);
                        frame.SaveImage(fullPath);
                    }
                }

                MessageBox.Show($"所有帧已根据帧间隔保存到: {OutputFolder}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存所有帧失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportGif()
        {
            if (!CanExportFrames())
            {
                MessageBox.Show("请先选择视频并确保已选择输出路径。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int frameInterval = GetFrameIntervalInFrames();
            Directory.CreateDirectory(OutputFolder);
            string fileName = Path.GetFileNameWithoutExtension(VideoPath) + ".gif";
            string fullPath = Path.Combine(OutputFolder, fileName);
            double fps = _videoFps > 0 ? _videoFps : 30;

            try
            {
                var encoder = new GifBitmapEncoder();
                _videoCapture.PosFrames = 0;

                for (int i = 0; i <= MaxFrameIndex; i += frameInterval)
                {
                    _videoCapture.PosFrames = i;
                    using var frame = _videoCapture.RetrieveMat();
                    if (frame != null && !frame.Empty())
                    {
                        var bitmapSource = BitmapSourceConverter.ToBitmapSource(frame);
                        bitmapSource.Freeze();
                        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                    }
                }

                using var fs = new FileStream(fullPath, FileMode.Create);
                encoder.Save(fs);

                MessageBox.Show($"GIF 已保存到: {fullPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出 GIF 失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RaiseCommandStates()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        private void ReleaseResources()
        {
            _videoCapture?.Release();
            _videoCapture?.Dispose();
            _videoCapture = null;

            _currentMat?.Dispose();
            _currentMat = null;
        }

        public void Dispose()
        {
            ReleaseResources();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
