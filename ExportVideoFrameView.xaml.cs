using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace CyanTools
{



    public partial class ExportVideoFrameView : UserControl
    {
        private VideoCapture _videoCapture;
        private Mat _currentFrame;
        private string _videoPath;
        private int _currentFrameIndex = 0;
        private double _videoFps = 0.0;

        public ExportVideoFrameView()
        {
            InitializeComponent();
        }

        private void SelectVideoButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Video files (*.mp4;*.avi;*.mov;*.mkv)|*.mp4;*.avi;*.mov;*.mkv|All files (*.*)|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                _videoPath = openFileDialog.FileName;
                VideoPathTextBox.Text = _videoPath;
                InitializeVideoCapture();
            }
        }

        private void SelectOutputFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                if (dialog.ShowDialog() == Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok)
                {
                    OutputPathTextBox.Text = dialog.FileName;
                }
            }
        }

        private void InitializeVideoCapture()
        {
            if (_videoCapture != null)
            {
                _videoCapture.Release();
                _videoCapture.Dispose();
            }

            if (!string.IsNullOrEmpty(_videoPath))
            {
                _videoCapture = new VideoCapture(_videoPath);
                if (!_videoCapture.IsOpened())
                {
                    MessageBox.Show("无法打开视频文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _videoFps = _videoCapture.Fps;
                if (_videoFps == 0)
                {
                    _videoFps = 30;
                    MessageBox.Show("无法获取视频帧率，将使用默认值 30 FPS。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                FrameSlider.Maximum = _videoCapture.FrameCount - 1;
                _currentFrameIndex = 0;
                DisplayFrame(_currentFrameIndex);
            }
        }

        private void DisplayFrame(int frameIndex)
        {
            if (_videoCapture != null && _videoCapture.IsOpened())
            {
                _videoCapture.PosFrames = frameIndex;
                _currentFrame = _videoCapture.RetrieveMat();

                if (_currentFrame != null && !_currentFrame.Empty())
                {
                    FrameImage.Source = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(_currentFrame);
                    _currentFrameIndex = frameIndex;
                    FrameSlider.Value = frameIndex;
                }
                else
                {

                    MessageBox.Show("无法读取帧。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void FrameSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_videoCapture != null && _videoCapture.IsOpened())
            {
                int newFrameIndex = (int)FrameSlider.Value;
                if (newFrameIndex != _currentFrameIndex)
                {
                    DisplayFrame(newFrameIndex);
                }
            }
        }

        private void PreviousFrameButton_Click(object sender, RoutedEventArgs e)
        {
            int frameInterval = GetFrameInterval();
            int newIndex = Math.Max(0, _currentFrameIndex - frameInterval);
            DisplayFrame(newIndex);
        }

        private void NextFrameButton_Click(object sender, RoutedEventArgs e)
        {
            int frameInterval = GetFrameInterval();
            int newIndex = Math.Min((int)FrameSlider.Maximum, _currentFrameIndex + frameInterval);
            DisplayFrame(newIndex);
        }

        private int GetFrameInterval()
        {
            if (int.TryParse(FrameIntervalTextBox.Text, out int intervalMs) && intervalMs > 0)
            {

                double intervalSeconds = intervalMs / 1000.0;
                int framesToSkip = (int)Math.Round(intervalSeconds * _videoFps);
                return Math.Max(1, framesToSkip);
            }
            return 1;
        }

        private void FrameIntervalTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {

            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void OutputCurrentFrameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFrame != null && !string.IsNullOrEmpty(OutputPathTextBox.Text))
            {
                try
                {
                    string outputPath = OutputPathTextBox.Text;
                    string fileName = $"frame_{_currentFrameIndex:D5}.png";
                    string fullPath = Path.Combine(outputPath, fileName);
                    _currentFrame.SaveImage(fullPath);
                    MessageBox.Show($"当前帧已保存到: {fullPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存当前帧失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("请先选择视频并确保已选择输出路径。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OutputAllFramesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_videoCapture == null || !_videoCapture.IsOpened() || string.IsNullOrEmpty(OutputPathTextBox.Text))
            {
                MessageBox.Show("请先选择视频并确保已选择输出路径。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string outputPath = OutputPathTextBox.Text;
            int frameInterval = GetFrameInterval();
            int totalFrames = (int)_videoCapture.FrameCount;

            try
            {
                _videoCapture.PosFrames = 0;
                for (int i = 0; i < totalFrames; i += frameInterval)
                {
                    _videoCapture.PosFrames = i;
                    Mat frame = _videoCapture.RetrieveMat();
                    if (frame != null && !frame.Empty())
                    {
                        string fileName = $"frame_{i:D5}.png";
                        string fullPath = Path.Combine(outputPath, fileName);
                        frame.SaveImage(fullPath);
                    }
                    frame.Dispose();
                }
                MessageBox.Show($"所有帧已根据帧间隔保存到: {outputPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存所有帧失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_videoCapture != null)
            {
                _videoCapture.Release();
                _videoCapture.Dispose();
            }
            if (_currentFrame != null)
            {
                _currentFrame.Dispose();
            }
        }
    }
}