using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace CyanTools
{
    public partial class ClusterDistributionWindow : FluentWindow
    {
        private readonly string _outputRoot;
        private readonly ScaleTransform _imageScaleTransform = new(1d, 1d);
        private const double MinZoom = 0.2d;
        private const double MaxZoom = 6.0d;
        private const double ZoomStep = 1.15d;
        private double _zoomScale = 1d;
        private bool _isPanning;
        private MouseButton _panButton = MouseButton.Left;
        private Point _panStartPoint;
        private double _panStartHorizontalOffset;
        private double _panStartVerticalOffset;

        public ClusterDistributionWindow(string imagePath, string outputRoot, string summary)
        {
            InitializeComponent();
            _outputRoot = outputRoot;
            DistributionImage.LayoutTransform = _imageScaleTransform;
            SummaryTextBlock.Text = summary;
            ImagePathTextBlock.Text = imagePath;
            UpdateZoomText();
            LoadDistributionImage(imagePath);
        }

        private void LoadDistributionImage(string imagePath)
        {
            if (!File.Exists(imagePath))
            {
                SummaryTextBlock.Text += "\n分布图文件不存在。";
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                DistributionImage.Source = bitmap;
                ResetZoom();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载分布图失败: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DistributionScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _ = sender;

            if (DistributionImage.Source == null)
            {
                return;
            }

            double factor = e.Delta > 0 ? ZoomStep : 1d / ZoomStep;
            double newScale = Math.Clamp(_zoomScale * factor, MinZoom, MaxZoom);
            if (Math.Abs(newScale - _zoomScale) < 1e-6)
            {
                return;
            }

            _zoomScale = newScale;
            _imageScaleTransform.ScaleX = _zoomScale;
            _imageScaleTransform.ScaleY = _zoomScale;
            UpdateZoomText();
            e.Handled = true;
        }

        private void DistributionScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _ = sender;
            if (DistributionImage.Source == null)
            {
                return;
            }

            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right)
            {
                return;
            }

            _isPanning = true;
            _panButton = e.ChangedButton;
            _panStartPoint = e.GetPosition(DistributionScrollViewer);
            _panStartHorizontalOffset = DistributionScrollViewer.HorizontalOffset;
            _panStartVerticalOffset = DistributionScrollViewer.VerticalOffset;
            DistributionScrollViewer.Cursor = Cursors.SizeAll;
            DistributionScrollViewer.CaptureMouse();
            e.Handled = true;
        }

        private void DistributionScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            _ = sender;
            if (!_isPanning || !DistributionScrollViewer.IsMouseCaptured)
            {
                return;
            }

            Point currentPoint = e.GetPosition(DistributionScrollViewer);
            double deltaX = currentPoint.X - _panStartPoint.X;
            double deltaY = currentPoint.Y - _panStartPoint.Y;

            DistributionScrollViewer.ScrollToHorizontalOffset(_panStartHorizontalOffset - deltaX);
            DistributionScrollViewer.ScrollToVerticalOffset(_panStartVerticalOffset - deltaY);
            e.Handled = true;
        }

        private void DistributionScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _ = sender;
            if (!_isPanning)
            {
                return;
            }

            if (e.ChangedButton != _panButton &&
                e.ChangedButton != MouseButton.Left &&
                e.ChangedButton != MouseButton.Right)
            {
                return;
            }

            EndPanning();
            e.Handled = true;
        }

        private void DistributionScrollViewer_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _ = sender;
            _ = e;
            EndPanning();
        }

        private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _outputRoot,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开结果目录失败: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            Close();
        }

        private void ResetZoom()
        {
            _zoomScale = 1d;
            _imageScaleTransform.ScaleX = _zoomScale;
            _imageScaleTransform.ScaleY = _zoomScale;
            DistributionScrollViewer.ScrollToHorizontalOffset(0);
            DistributionScrollViewer.ScrollToVerticalOffset(0);
            UpdateZoomText();
        }

        private void UpdateZoomText()
        {
            ZoomTextBlock.Text = $"滚轮缩放：{_zoomScale * 100:F0}%";
        }

        private void EndPanning()
        {
            _isPanning = false;
            DistributionScrollViewer.Cursor = Cursors.Arrow;
            if (DistributionScrollViewer.IsMouseCaptured)
            {
                DistributionScrollViewer.ReleaseMouseCapture();
            }
        }
    }
}
