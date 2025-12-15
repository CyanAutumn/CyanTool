using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CyanTools.Utils;
using CyanTools.Model;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class DatasetManagerViewModel : INotifyPropertyChanged
    {

        public event Action? OnCanvasResetRequired;

        private readonly Dictionary<string, List<Rect>> _cachedEdits = new Dictionary<string, List<Rect>>();
        private string? _currentFilePath;


        private BitmapImage? _originalBitmap;
        private bool _isPanning = false;
        private System.Windows.Point _panStartPoint;
        private bool _isDrawing = false;
        private System.Windows.Point _rectStartPoint;
        private System.Windows.Shapes.Rectangle? _currentDrawingRectangle;
        private readonly List<System.Windows.Shapes.Rectangle> _drawnRectangles = new List<System.Windows.Shapes.Rectangle>();
        private readonly List<Rect> _finalRects = new List<Rect>();


        private BitmapImage? _currentImageSource;
        private DatasetDataItem? _selectedDataItem;
        private string _currentTextContent = "";
        private bool _autoNext = true;
        private int _currentIndex = 0;
        private readonly Random _random = new Random();


        private string _category1Path = "";
        private string _category2Path = "";
        private string _category3Path = "";
        private string _category4Path = "";
        private string _category5Path = "";


        public ObservableCollection<DatasetDataItem> DataItemsWithOperations { get; } = new ObservableCollection<DatasetDataItem>();


        public DatasetDataItem? SelectedDataItem
        {
            get => _selectedDataItem;
            set
            {
                if (SetField(ref _selectedDataItem, value))
                {
                    SaveChangesToCache();
                    LoadSelectedData();
                    if (_selectedDataItem != null)
                    {
                        _currentIndex = DataItemsWithOperations.IndexOf(_selectedDataItem);
                    }
                }
            }
        }

        public BitmapImage? CurrentImageSource
        {
            get => _currentImageSource;
            private set => SetField(ref _currentImageSource, value);
        }

        public string CurrentTextContent
        {
            get => _currentTextContent;
            set => SetField(ref _currentTextContent, value);
        }

        public bool AutoNext
        {
            get => _autoNext;
            set => SetField(ref _autoNext, value);
        }

        public string Category1Path
        {
            get => _category1Path;
            set => SetField(ref _category1Path, value);
        }

        public string Category2Path
        {
            get => _category2Path;
            set => SetField(ref _category2Path, value);
        }

        public string Category3Path
        {
            get => _category3Path;
            set => SetField(ref _category3Path, value);
        }

        public string Category4Path
        {
            get => _category4Path;
            set => SetField(ref _category4Path, value);
        }

        public string Category5Path
        {
            get => _category5Path;
            set => SetField(ref _category5Path, value);
        }


        public ICommand SelectFolderCommand { get; }
        public ICommand SaveAllCommand { get; }
        
        public ICommand SetCategoryCommand { get; }
        public ICommand SelectCategoryFolderCommand { get; }
        public ICommand PreviousImageCommand { get; }
        public ICommand NextImageCommand { get; }
        public ICommand UndoOperationCommand { get; }
        public ICommand DeleteImageCommand { get; }
        public ICommand RandomCopyCommand { get; }
        public ICommand CopyNextDataCommand { get; }
        public ICommand SavePromptCommand { get; }
        

        public DatasetManagerViewModel()
        {
            SelectFolderCommand = new RelayCommand(_ => SelectFolder());
            SaveAllCommand = new RelayCommand(_ => SaveAllChanges());
            
            SetCategoryCommand = new RelayCommand(param => SetImageCategory(param?.ToString()));
            SelectCategoryFolderCommand = new RelayCommand(param => SelectCategoryFolder(param?.ToString()));
            PreviousImageCommand = new RelayCommand(_ => PreviousImage());
            NextImageCommand = new RelayCommand(_ => NextImage());
            UndoOperationCommand = new RelayCommand(_ => UndoOperation());
            DeleteImageCommand = new RelayCommand(_ => DeleteImage());
            RandomCopyCommand = new RelayCommand(_ => RandomCopy(), _ => DataItemsWithOperations.Any());
            CopyNextDataCommand = new RelayCommand(_ => CopyNextData(), _ => DataItemsWithOperations.Any());
            SavePromptCommand = new RelayCommand(_ => SavePrompt(), _ => SelectedDataItem != null);
            
        }

        #region 文件加载与保存

        

        private void SelectFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "请选择一个文件夹"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                string folderPath = dialog.FileName;
                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    try
                    {
                        SaveChangesToCache();

                        _cachedEdits.Clear();
                        _currentFilePath = null;
                        ResetState();
                        CurrentImageSource = null;
                        CurrentTextContent = "";
                        OnCanvasResetRequired?.Invoke();

                        var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                        var itemsByBaseName = new Dictionary<string, DatasetDataItem>(StringComparer.OrdinalIgnoreCase);

                        foreach (var file in Directory.GetFiles(folderPath))
                        {
                            string extension = System.IO.Path.GetExtension(file);
                            if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                            {
                                string baseName = System.IO.Path.GetFileNameWithoutExtension(file);
                                if (!itemsByBaseName.TryGetValue(baseName, out var item))
                                {
                                    item = new DatasetDataItem { BaseName = baseName };
                                    itemsByBaseName.Add(baseName, item);
                                }
                                item.TextPath = file;
                                continue;
                            }

                            if (imageExtensions.Contains(extension))
                            {
                                string baseName = System.IO.Path.GetFileNameWithoutExtension(file);
                                if (!itemsByBaseName.TryGetValue(baseName, out var item))
                                {
                                    item = new DatasetDataItem { BaseName = baseName };
                                    itemsByBaseName.Add(baseName, item);
                                }

                                if (string.IsNullOrEmpty(item.ImagePath))
                                {
                                    item.ImagePath = file;
                                }
                                else
                                {
                                    string uniqueKey = System.IO.Path.GetFileName(file);
                                    if (!itemsByBaseName.ContainsKey(uniqueKey))
                                    {
                                        itemsByBaseName.Add(uniqueKey, new DatasetDataItem { BaseName = uniqueKey, ImagePath = file });
                                    }
                                }
                            }
                        }

                        DataItemsWithOperations.Clear();
                        foreach (var item in itemsByBaseName.Values.OrderBy(i => i.BaseName, StringComparer.OrdinalIgnoreCase))
                        {
                            DataItemsWithOperations.Add(item);
                        }

                        if (DataItemsWithOperations.Any())
                        {
                            _currentIndex = 0;
                            SelectedDataItem = DataItemsWithOperations.First();
                        }
                        else
                        {
                            SelectedDataItem = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"加载文件夹时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }
            }
        }

        private void SaveAllChanges()
        {
            SaveChangesToCache();

            if (!DataItemsWithOperations.Any())
            {
                System.Windows.MessageBox.Show("没有需要处理的数据。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            try
            {
                int processedCount = 0;
                int errorCount = 0;

                foreach (var item in DataItemsWithOperations.ToList())
                {
                    if (string.IsNullOrEmpty(item.Operation)) continue;

                    try
                    {
                        if (item.Operation == "删")
                        {
                            DeleteDataItemFiles(item);
                        }
                        else
                        {
                            string targetFolder = GetCategoryFolder(item.Operation);
                            if (!string.IsNullOrEmpty(targetFolder) && Directory.Exists(targetFolder))
                            {
                                MoveDataItemFiles(item, targetFolder);
                            }
                            else
                            {
                                System.Windows.MessageBox.Show($"分类{item.Operation}的目标文件夹未设置或不存在。", "警告", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                                continue;
                            }
                        }

                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        System.Windows.MessageBox.Show($"处理文件 {item.DisplayName} 时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }


                DataItemsWithOperations.Clear();
                SelectedDataItem = null;
                CurrentImageSource = null;
                CurrentTextContent = "";
                _currentIndex = 0;
                _currentFilePath = null;
                _cachedEdits.Clear();
                ResetState();
                OnCanvasResetRequired?.Invoke();


                System.Windows.MessageBox.Show($"处理完成！\n\n成功处理: {processedCount} 条数据\n错误: {errorCount} 条数据", "完成", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"应用更改时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadSelectedData()
        {
            if (SelectedDataItem == null)
            {
                ClearCurrentImagePreview();
                CurrentTextContent = "";
                return;
            }

            if (!string.IsNullOrEmpty(SelectedDataItem.ImagePath) && File.Exists(SelectedDataItem.ImagePath))
            {
                LoadImageFromFile(SelectedDataItem.ImagePath);
            }
            else
            {
                ClearCurrentImagePreview();
            }

            LoadTextFromFile(SelectedDataItem.TextPath);
        }

        private void ClearCurrentImagePreview()
        {
            CurrentImageSource = null;
            _currentFilePath = null;
            ResetState();
            OnCanvasResetRequired?.Invoke();
        }

        private void LoadTextFromFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                CurrentTextContent = "";
                return;
            }

            try
            {
                CurrentTextContent = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                CurrentTextContent = "";
                System.Windows.MessageBox.Show($"读取文本失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void DeleteDataItemFiles(DatasetDataItem item)
        {
            if (!string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
            {
                if (CurrentImageSource != null && _currentFilePath == item.ImagePath)
                {
                    ClearCurrentImagePreview();
                }
                File.Delete(item.ImagePath);
            }

            if (!string.IsNullOrEmpty(item.TextPath) && File.Exists(item.TextPath))
            {
                File.Delete(item.TextPath);
            }
            else if (!string.IsNullOrEmpty(item.ImagePath))
            {
                string associatedTxtPath = System.IO.Path.ChangeExtension(item.ImagePath, ".txt");
                if (File.Exists(associatedTxtPath))
                {
                    File.Delete(associatedTxtPath);
                }
            }
        }

        private void MoveDataItemFiles(DatasetDataItem item, string targetFolder)
        {
            if (!string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
            {
                string sourceImagePath = item.ImagePath;
                _cachedEdits.TryGetValue(sourceImagePath, out List<Rect>? rects);
                List<Rect> rectsToSave = rects ?? new List<Rect>();
                bool hasModifications = rectsToSave.Any();

                string targetImagePath = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(sourceImagePath));

                if (hasModifications)
                {
                    if (!SaveImageWithRects(sourceImagePath, rectsToSave, targetImagePath))
                    {
                        throw new IOException($"保存修改后的图片失败: {System.IO.Path.GetFileName(sourceImagePath)}");
                    }

                    ProcessAssociatedTxtFile(sourceImagePath, targetImagePath, true);
                    File.Delete(sourceImagePath);

                    string sourceTxtPath = System.IO.Path.ChangeExtension(sourceImagePath, ".txt");
                    if (File.Exists(sourceTxtPath))
                    {
                        File.Delete(sourceTxtPath);
                    }

                    return;
                }

                File.Move(sourceImagePath, targetImagePath);

                string? sourceTxtToMove = item.TextPath;
                if (string.IsNullOrEmpty(sourceTxtToMove))
                {
                    string sourceTxtPath = System.IO.Path.ChangeExtension(sourceImagePath, ".txt");
                    if (File.Exists(sourceTxtPath))
                    {
                        sourceTxtToMove = sourceTxtPath;
                    }
                }

                if (!string.IsNullOrEmpty(sourceTxtToMove) && File.Exists(sourceTxtToMove))
                {
                    string targetTxtPath = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(sourceTxtToMove));
                    File.Move(sourceTxtToMove, targetTxtPath);
                }

                return;
            }

            if (!string.IsNullOrEmpty(item.TextPath) && File.Exists(item.TextPath))
            {
                string targetTxtPath = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(item.TextPath));
                File.Move(item.TextPath, targetTxtPath);
            }
        }

        private async void RandomCopy()
        {
            if (!DataItemsWithOperations.Any()) return;

            int index = _random.Next(DataItemsWithOperations.Count);
            SelectedDataItem = DataItemsWithOperations[index];

            await ClipboardHelper.TrySetTextAsync(CurrentTextContent ?? string.Empty);
        }

        private async void CopyNextData()
        {
            if (!DataItemsWithOperations.Any()) return;

            int currentIndex = SelectedDataItem != null ? DataItemsWithOperations.IndexOf(SelectedDataItem) : -1;
            int nextIndex = (currentIndex + 1) % DataItemsWithOperations.Count;
            SelectedDataItem = DataItemsWithOperations[nextIndex];

            await ClipboardHelper.TrySetTextAsync(CurrentTextContent ?? string.Empty);
        }

        private void SavePrompt()
        {
            if (SelectedDataItem == null)
            {
                return;
            }

            string? targetPath = SelectedDataItem.TextPath;
            if (string.IsNullOrEmpty(targetPath))
            {
                if (!string.IsNullOrEmpty(SelectedDataItem.ImagePath))
                {
                    targetPath = System.IO.Path.ChangeExtension(SelectedDataItem.ImagePath, ".txt");
                }
                else
                {
                    System.Windows.MessageBox.Show("当前数据没有可保存的 Prompt 文件。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }
            }

            try
            {
                File.WriteAllText(targetPath, CurrentTextContent ?? string.Empty);

                if (string.IsNullOrEmpty(SelectedDataItem.TextPath))
                {
                    SelectedDataItem.TextPath = targetPath;
                }

                System.Windows.MessageBox.Show("Prompt 已保存！", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存 Prompt 失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        #endregion

        #region 分类功能

        private void SetImageCategory(string category)
        {
            if (SelectedDataItem != null && !string.IsNullOrEmpty(category))
            {
                SelectedDataItem.Operation = category;
                
                if (AutoNext)
                {
                    NextImage();
                }
            }
        }

        private void SelectCategoryFolder(string categoryNumber)
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = $"请选择分类{categoryNumber}的目标文件夹"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                switch (categoryNumber)
                {
                    case "1": Category1Path = dialog.FileName; break;
                    case "2": Category2Path = dialog.FileName; break;
                    case "3": Category3Path = dialog.FileName; break;
                    case "4": Category4Path = dialog.FileName; break;
                    case "5": Category5Path = dialog.FileName; break;
                }
            }
        }

        private void PreviousImage()
        {
            if (DataItemsWithOperations.Any() && _currentIndex > 0)
            {
                _currentIndex--;
                SelectedDataItem = DataItemsWithOperations[_currentIndex];
            }
        }

        private void NextImage()
        {
            if (DataItemsWithOperations.Any() && _currentIndex < DataItemsWithOperations.Count - 1)
            {
                _currentIndex++;
                SelectedDataItem = DataItemsWithOperations[_currentIndex];
            }
        }

        private void UndoOperation()
        {
            if (SelectedDataItem != null)
            {
                SelectedDataItem.Operation = "";
                
                if (AutoNext)
                {
                    NextImage();
                }
            }
        }

        private void DeleteImage()
        {
            if (SelectedDataItem != null)
            {
                SelectedDataItem.Operation = "删";
                
                if (AutoNext)
                {
                    NextImage();
                }
            }
        }

        

        private string GetCategoryFolder(string category)
        {
            return category switch
            {
                "1" => Category1Path,
                "2" => Category2Path,
                "3" => Category3Path,
                "4" => Category4Path,
                "5" => Category5Path,
                _ => ""
            };
        }

        #endregion

        #region 键盘处理

        public void HandleKeyDown(Key key)
        {
            switch (key)
            {
                case Key.D1:
                    SetImageCategory("1");
                    break;
                case Key.D2:
                    SetImageCategory("2");
                    break;
                case Key.D3:
                    SetImageCategory("3");
                    break;
                case Key.D4:
                    SetImageCategory("4");
                    break;
                case Key.D5:
                    SetImageCategory("5");
                    break;
                case Key.Q:
                    RandomCopy();
                    break;
                case Key.W:
                    PreviousImage();
                    break;
                case Key.E:
                    CopyNextData();
                    break;
                case Key.Z:
                    SavePrompt();
                    break;
                case Key.S:
                    NextImage();
                    break;
                case Key.A:
                    UndoOperation();
                    break;
                case Key.D:
                    DeleteImage();
                    break;
            }
        }

        #endregion

        #region 核心逻辑 (加载、缓存、重置)

        private void LoadImageFromFile(string filePath)
        {
            try
            {
                _originalBitmap = new BitmapImage();
                _originalBitmap.BeginInit();
                _originalBitmap.UriSource = new Uri(filePath);
                _originalBitmap.CacheOption = BitmapCacheOption.OnLoad;
                _originalBitmap.EndInit();

                CurrentImageSource = _originalBitmap;
                _currentFilePath = filePath;

                ResetState();
                LoadEditsFromCache();
                

                OnCanvasResetRequired?.Invoke();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"加载图像失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                CurrentImageSource = null;
                _currentFilePath = null;
                ResetState();
            }
        }

        private void SaveChangesToCache()
        {
            if (string.IsNullOrEmpty(_currentFilePath)) return;

            if (_finalRects.Any())
            {
                _cachedEdits[_currentFilePath] = new List<Rect>(_finalRects);
            }
            else if (_cachedEdits.ContainsKey(_currentFilePath))
            {
                _cachedEdits.Remove(_currentFilePath);
            }
        }

        private void LoadEditsFromCache()
        {
            if (!string.IsNullOrEmpty(_currentFilePath) && _cachedEdits.TryGetValue(_currentFilePath, out List<Rect> cachedRects))
            {
                _finalRects.AddRange(cachedRects);
            }
        }
        

        public List<Rect> GetCurrentRects()
        {
            return new List<Rect>(_finalRects);
        }
        

        public void AddDrawnRectangle(System.Windows.Shapes.Rectangle rectangle)
        {
            if (!_drawnRectangles.Contains(rectangle))
            {
                _drawnRectangles.Add(rectangle);
            }
        }
        

        public void ClearDrawnRectangles()
        {
            _drawnRectangles.Clear();
        }

        private void ResetState()
        {
            _finalRects.Clear();
            _drawnRectangles.Clear();
            _currentDrawingRectangle = null;
            _isPanning = false;
            _isDrawing = false;
        }

        private bool SaveImageWithRects(string imagePath, List<Rect> rects, string savePath)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                DrawingVisual drawingVisual = new DrawingVisual();
                using (DrawingContext dc = drawingVisual.RenderOpen())
                {
                    dc.DrawImage(bmp, new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
                    foreach (var rect in rects)
                    {
                        dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, rect);
                    }
                }

                RenderTargetBitmap rtb = new RenderTargetBitmap(bmp.PixelWidth, bmp.PixelHeight, bmp.DpiX, bmp.DpiY, PixelFormats.Pbgra32);
                rtb.Render(drawingVisual);
                rtb.Freeze();

                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (FileStream fs = new FileStream(savePath, FileMode.Create))
                {
                    encoder.Save(fs);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存文件 {imagePath} 失败: {ex.Message}");
                return false;
            }
        }

        private void ProcessAssociatedTxtFile(string sourceImagePath, string destImagePath, bool hasModifications)
        {
            string sourceTxtPath = System.IO.Path.ChangeExtension(sourceImagePath, ".txt");
            string destTxtPath = System.IO.Path.ChangeExtension(destImagePath, ".txt");
            string promptToAdd = "black box";

            string originalContent = "";
            if (File.Exists(sourceTxtPath))
            {
                try
                {
                    originalContent = File.ReadAllText(sourceTxtPath).Trim();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"读取 .txt 文件失败: {ex.Message}");
                    return;
                }
            }

            string finalContent = originalContent;


            if (hasModifications)
            {

                if (!finalContent.Contains(promptToAdd))
                {
                    if (!string.IsNullOrEmpty(finalContent) && !finalContent.EndsWith(","))
                    {
                        finalContent += ", ";
                    }
                    finalContent += promptToAdd;
                }
            }


            if (!string.IsNullOrEmpty(finalContent))
            {
                try
                {
                    File.WriteAllText(destTxtPath, finalContent);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"写入 .txt 文件失败: {ex.Message}");
                }
            }
        }

        #endregion

        #region 事件处理 (鼠标与键盘) - 需要从View调用

        public void HandleMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e, Canvas mainCanvas, Canvas transformGroupCanvas, System.Windows.Controls.Image myImage)
        {
            if (myImage.Source == null) return;

            if (e.OriginalSource is System.Windows.Shapes.Rectangle clickedRectangle && _drawnRectangles.Contains(clickedRectangle))
            {
                int index = _drawnRectangles.IndexOf(clickedRectangle);
                if (index >= 0 && index < _finalRects.Count)
                {
                    _drawnRectangles.RemoveAt(index);
                    _finalRects.RemoveAt(index);
                    transformGroupCanvas.Children.Remove(clickedRectangle);
                }
                return;
            }

            _isDrawing = true;
            var transform = transformGroupCanvas.RenderTransform;
            _rectStartPoint = (transform?.Inverse != null)
                ? transform.Inverse.Transform(e.GetPosition(mainCanvas))
                : e.GetPosition(mainCanvas);

            _currentDrawingRectangle = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.Black,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 0, 0, 0)),
            };

            Canvas.SetLeft(_currentDrawingRectangle, _rectStartPoint.X);
            Canvas.SetTop(_currentDrawingRectangle, _rectStartPoint.Y);
            transformGroupCanvas.Children.Add(_currentDrawingRectangle);
            _drawnRectangles.Add(_currentDrawingRectangle);
            mainCanvas.CaptureMouse();
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Cross;
        }

        public void HandleMouseMove(System.Windows.Input.MouseEventArgs e, Canvas mainCanvas, Border imageContainer, TranslateTransform imagePanTransform)
        {
            if (_isPanning)
            {
                System.Windows.Point currentPoint = e.GetPosition(imageContainer);
                double deltaX = currentPoint.X - _panStartPoint.X;
                double deltaY = currentPoint.Y - _panStartPoint.Y;
                double newX = imagePanTransform.X + deltaX;
                double newY = imagePanTransform.Y + deltaY;
                double minX = 0, minY = 0, maxX = 0, maxY = 0;

                if (CurrentImageSource != null)
                {
                    if (CurrentImageSource.Width > imageContainer.ActualWidth)
                        minX = imageContainer.ActualWidth - CurrentImageSource.Width;
                    if (CurrentImageSource.Height > imageContainer.ActualHeight)
                        minY = imageContainer.ActualHeight - CurrentImageSource.Height;
                }

                imagePanTransform.X = Math.Max(minX, Math.Min(maxX, newX));
                imagePanTransform.Y = Math.Max(minY, Math.Min(maxY, newY));
                _panStartPoint = currentPoint;
            }

            if (_isDrawing && _currentDrawingRectangle != null)
            {
                var transformGroupCanvas = _currentDrawingRectangle.Parent as Canvas;
                var transform = transformGroupCanvas?.RenderTransform;
                System.Windows.Point currentPoint = (transform?.Inverse != null)
                    ? transform.Inverse.Transform(e.GetPosition(mainCanvas))
                    : e.GetPosition(mainCanvas);

                double x = Math.Min(_rectStartPoint.X, currentPoint.X);
                double y = Math.Min(_rectStartPoint.Y, currentPoint.Y);
                double width = Math.Abs(_rectStartPoint.X - currentPoint.X);
                double height = Math.Abs(_rectStartPoint.Y - currentPoint.Y);

                Canvas.SetLeft(_currentDrawingRectangle, x);
                Canvas.SetTop(_currentDrawingRectangle, y);
                _currentDrawingRectangle.Width = width;
                _currentDrawingRectangle.Height = height;
            }
        }

        public void HandleMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e, Canvas mainCanvas, Canvas transformGroupCanvas)
        {
            if (_isDrawing)
            {
                _isDrawing = false;
                if (_currentDrawingRectangle != null && _currentDrawingRectangle.Width > 0 && _currentDrawingRectangle.Height > 0)
                {
                    _finalRects.Add(new Rect(
                        Canvas.GetLeft(_currentDrawingRectangle), Canvas.GetTop(_currentDrawingRectangle),
                        _currentDrawingRectangle.Width, _currentDrawingRectangle.Height));
                    _currentDrawingRectangle.Fill = System.Windows.Media.Brushes.Black;
                    _currentDrawingRectangle.Stroke = null;
                }
                else if (_currentDrawingRectangle != null)
                {
                    _drawnRectangles.Remove(_currentDrawingRectangle);
                    transformGroupCanvas.Children.Remove(_currentDrawingRectangle);
                }
                _currentDrawingRectangle = null;
                mainCanvas.ReleaseMouseCapture();
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        public void HandleMouseRightButtonDown(System.Windows.Input.MouseButtonEventArgs e, Canvas mainCanvas, Border imageContainer)
        {
            if (CurrentImageSource == null) return;
            _isPanning = true;
            _panStartPoint = e.GetPosition(imageContainer);
            mainCanvas.CaptureMouse();
            Mouse.OverrideCursor = System.Windows.Input.Cursors.ScrollAll;
        }

        public void HandleMouseRightButtonUp(Canvas mainCanvas)
        {
            _isPanning = false;
            mainCanvas.ReleaseMouseCapture();
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
        }

        #endregion

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
