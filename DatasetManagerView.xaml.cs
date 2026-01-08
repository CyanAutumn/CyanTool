using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CyanTools.Navigation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CyanTools.ViewModel;
using System.Windows.Controls.Primitives;

namespace CyanTools
{
    [ToolPage("数据集管理器", 130)]
    public partial class DatasetManagerView : UserControl
    {
        private DatasetManagerViewModel ViewModel => DataContext as DatasetManagerViewModel;
        
        public DatasetManagerView()
        {
            InitializeComponent();
            this.Loaded += DatasetManagerView_Loaded;
            this.DataContextChanged += DatasetManagerView_DataContextChanged;
        }
        
        private void DatasetManagerView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {

            if (e.OldValue is DatasetManagerViewModel oldViewModel)
            {
                oldViewModel.OnCanvasResetRequired -= ResetCanvas;
            }
            

            if (ViewModel != null)
            {
                ViewModel.OnCanvasResetRequired += ResetCanvas;
            }
        }
        
        private void ResetCanvas()
        {

            ViewModel?.ClearDrawnRectangles();
            

            var rectanglesToRemove = TransformGroupCanvas.Children.OfType<System.Windows.Shapes.Rectangle>().ToList();
            foreach (var rect in rectanglesToRemove)
            {
                TransformGroupCanvas.Children.Remove(rect);
            }
            

            var currentRects = ViewModel?.GetCurrentRects();
            if (currentRects != null)
            {
                foreach (var rectData in currentRects)
                {
                    var rectangle = new System.Windows.Shapes.Rectangle
                    {
                        Width = rectData.Width,
                        Height = rectData.Height,
                        Fill = System.Windows.Media.Brushes.Black,
                        Opacity = 0.3
                    };
                    
                    System.Windows.Controls.Canvas.SetLeft(rectangle, rectData.X);
                    System.Windows.Controls.Canvas.SetTop(rectangle, rectData.Y);
                    TransformGroupCanvas.Children.Add(rectangle);
                    

                    ViewModel?.AddDrawnRectangle(rectangle);
                }
            }
        }
        
        private void DatasetManagerView_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus();
            

            if (ViewModel != null)
            {
                ViewModel.OnCanvasResetRequired += ResetCanvas;
            }
        }

        private void DatasetManagerView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBoxBase tb && !tb.IsReadOnly)
            {
                return;
            }
            if (TryHandleThumbnailNavigation(e.Key))
            {
                e.Handled = true;
                return;
            }
            ViewModel?.HandleKeyDown(e.Key);
        }

        private void ThumbnailMoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag
                && Enum.TryParse(tag, true, out ThumbnailMoveDirection direction))
            {
                MoveThumbnailSelection(direction);
            }
        }

        private bool TryHandleThumbnailNavigation(Key key)
        {
            switch (key)
            {
                case Key.W:
                    return MoveThumbnailSelection(ThumbnailMoveDirection.Up);
                case Key.S:
                    return MoveThumbnailSelection(ThumbnailMoveDirection.Down);
                case Key.A:
                    return MoveThumbnailSelection(ThumbnailMoveDirection.Left);
                case Key.D:
                    return MoveThumbnailSelection(ThumbnailMoveDirection.Right);
                default:
                    return false;
            }
        }

        private bool MoveThumbnailSelection(ThumbnailMoveDirection direction)
        {
            if (ViewModel == null || ViewModel.DataItemsWithOperations.Count == 0)
            {
                return true;
            }

            int itemsPerRow = GetItemsPerRow();
            int itemCount = ViewModel.DataItemsWithOperations.Count;
            int currentIndex = ViewModel.SelectedDataItem != null
                ? ViewModel.DataItemsWithOperations.IndexOf(ViewModel.SelectedDataItem)
                : -1;

            if (currentIndex < 0)
            {
                ViewModel.SelectedDataItem = ViewModel.DataItemsWithOperations[0];
                BringItemIntoView(0);
                return true;
            }

            int targetIndex = currentIndex;
            int column = currentIndex % itemsPerRow;

            switch (direction)
            {
                case ThumbnailMoveDirection.Left:
                    if (column > 0)
                    {
                        targetIndex = currentIndex - 1;
                    }
                    else
                    {
                        return true;
                    }
                    break;
                case ThumbnailMoveDirection.Right:
                    if (column < itemsPerRow - 1 && currentIndex + 1 < itemCount)
                    {
                        targetIndex = currentIndex + 1;
                    }
                    else
                    {
                        return true;
                    }
                    break;
                case ThumbnailMoveDirection.Up:
                    if (currentIndex - itemsPerRow >= 0)
                    {
                        targetIndex = currentIndex - itemsPerRow;
                    }
                    else
                    {
                        return true;
                    }
                    break;
                case ThumbnailMoveDirection.Down:
                    if (currentIndex + itemsPerRow < itemCount)
                    {
                        targetIndex = currentIndex + itemsPerRow;
                    }
                    else
                    {
                        return true;
                    }
                    break;
            }

            if (targetIndex != currentIndex)
            {
                ViewModel.SelectedDataItem = ViewModel.DataItemsWithOperations[targetIndex];
                BringItemIntoView(targetIndex);
            }

            return true;
        }

        private int GetItemsPerRow()
        {
            var panel = FindVisualChild<WrapPanel>(DataItemsList);
            if (panel == null || panel.Children.Count == 0)
            {
                return 1;
            }

            double? firstRowTop = null;
            int count = 0;

            foreach (UIElement child in panel.Children)
            {
                if (child is FrameworkElement element)
                {
                    Point position = element.TranslatePoint(new Point(0, 0), panel);
                    if (firstRowTop == null)
                    {
                        firstRowTop = position.Y;
                    }

                    if (Math.Abs(position.Y - firstRowTop.Value) < 0.5)
                    {
                        count++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return count > 0 ? count : 1;
        }

        private void BringItemIntoView(int index)
        {
            var panel = FindVisualChild<WrapPanel>(DataItemsList);
            if (panel != null && index >= 0 && index < panel.Children.Count)
            {
                if (panel.Children[index] is FrameworkElement element)
                {
                    element.BringIntoView();
                    return;
                }
            }

            if (DataItemsList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                T? descendant = FindVisualChild<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private enum ThumbnailMoveDirection
        {
            Up,
            Down,
            Left,
            Right
        }

        private void MainCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewModel?.HandleMouseLeftButtonDown(e, MainCanvas, TransformGroupCanvas, MyImage);
        }

        private void MainCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ViewModel?.HandleMouseMove(e, MainCanvas, ImageContainer, ImagePanTransform);
        }

        private void MainCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewModel?.HandleMouseLeftButtonUp(e, MainCanvas, TransformGroupCanvas);
        }

        private void MainCanvas_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewModel?.HandleMouseRightButtonDown(e, MainCanvas, ImageContainer);
        }

        private void MainCanvas_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewModel?.HandleMouseRightButtonUp(MainCanvas);
        }
    }
}
