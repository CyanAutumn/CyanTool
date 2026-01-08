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
using CyanTools.Model;
using System.Windows.Threading;

namespace CyanTools
{
    [ToolPage("数据集管理器", 130)]
    public partial class DatasetManagerView : UserControl
    {
        private DatasetManagerViewModel ViewModel => DataContext as DatasetManagerViewModel;
        private TextBox? _activeTagTextBox;
        private TagTokenViewModel? _activeEditingToken;
        private bool _suppressTagInputTextChange;
        private bool _suppressTagTextHandling;
        
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

        private void SetActiveSuggestionTarget(TextBox? textBox)
        {
            _activeTagTextBox = textBox;
            _activeEditingToken = textBox?.DataContext as TagTokenViewModel;
            if (textBox != null)
            {
                TagSuggestionPopup.PlacementTarget = textBox;
            }
        }

        private void SuppressTagTextHandlingOnce()
        {
            _suppressTagTextHandling = true;
            Dispatcher.BeginInvoke(new Action(() => _suppressTagTextHandling = false), DispatcherPriority.Input);
        }

        private void MoveSuggestionSelection(int delta)
        {
            if (!TagSuggestionPopup.IsOpen || TagSuggestionList.Items.Count == 0)
            {
                return;
            }

            int nextIndex = TagSuggestionList.SelectedIndex + delta;
            nextIndex = Math.Max(0, Math.Min(TagSuggestionList.Items.Count - 1, nextIndex));
            TagSuggestionList.SelectedIndex = nextIndex;
            TagSuggestionList.ScrollIntoView(TagSuggestionList.SelectedItem);
        }

        private bool TryApplySelectedSuggestion()
        {
            if (ViewModel == null || TagSuggestionList.Items.Count == 0)
            {
                return false;
            }

            var suggestion = TagSuggestionList.SelectedItem as TagSuggestion
                             ?? TagSuggestionList.Items[0] as TagSuggestion;
            if (suggestion == null)
            {
                return false;
            }

            ViewModel.ApplySuggestion(suggestion, _activeEditingToken);
            return true;
        }

        private void TagChip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                var deleteButton = FindAncestor<Button>(source);
                if (deleteButton?.Name == "TagDeleteButton")
                {
                    return;
                }
            }

            if (sender is FrameworkElement element && element.DataContext is TagTokenViewModel token)
            {
                ViewModel?.BeginEditTag(token);
                e.Handled = true;
            }
        }
        
        private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node != null)
            {
                if (node is T match)
                {
                    return match;
                }

                node = VisualTreeHelper.GetParent(node);
            }

            return null;
        }

        private async void TagInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ViewModel == null || sender is not TextBox textBox)
            {
                return;
            }

            if (_suppressTagInputTextChange)
            {
                return;
            }

            if (_suppressTagTextHandling)
            {
                return;
            }

            if (textBox.Text.IndexOfAny(new[] { ',', '，', '\r', '\n' }) >= 0)
            {
                _suppressTagInputTextChange = true;
                bool flushAll = textBox.Text.IndexOfAny(new[] { '\r', '\n' }) >= 0;
                var result = ViewModel.CommitPendingTagsFromInput(textBox.Text, flushAll);
                if (result.AddedCount > 0 && !string.Equals(textBox.Text, result.Remainder, StringComparison.Ordinal))
                {
                    textBox.Text = result.Remainder;
                    textBox.CaretIndex = textBox.Text.Length;
                }
                _suppressTagInputTextChange = false;
            }
            else
            {
                ViewModel.PendingTagText = textBox.Text;
            }

            await ViewModel.RefreshSuggestionsAsync(ViewModel.PendingTagText);
        }

        private async void TagEditBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ViewModel == null || sender is not TextBox textBox)
            {
                return;
            }

            if (_suppressTagTextHandling)
            {
                return;
            }

            var token = textBox.DataContext as TagTokenViewModel;
            if (token != null)
            {
                int separatorIndex = textBox.Text.IndexOfAny(new[] { ',', '，', '\r', '\n' });
                if (separatorIndex >= 0)
                {
                    var trimmed = textBox.Text.Substring(0, separatorIndex);
                    textBox.Text = trimmed;
                    textBox.CaretIndex = textBox.Text.Length;
                    ViewModel.CommitEditTag(token);
                    return;
                }
            }

            await ViewModel.RefreshSuggestionsAsync(textBox.Text);
        }

        private void TagInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            if (e.Key == Key.Down)
            {
                MoveSuggestionSelection(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                MoveSuggestionSelection(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                bool allowCommit = e.Key == Key.Tab || e.ImeProcessedKey == Key.None;
                if (!allowCommit)
                {
                    return;
                }

                SuppressTagTextHandlingOnce();

                if (TagSuggestionPopup.IsOpen)
                {
                    ViewModel.SuppressSuggestionRefresh();
                    TryApplySelectedSuggestion();
                }

                if (sender is TextBox textBox)
                {
                    var inputText = ViewModel.PendingTagText;
                    if (string.IsNullOrWhiteSpace(inputText))
                    {
                        inputText = textBox.Text;
                    }

                    _suppressTagInputTextChange = true;
                    var result = ViewModel.CommitPendingTagsFromInput(inputText ?? string.Empty, true);
                    if (!string.Equals(textBox.Text, result.Remainder, StringComparison.Ordinal))
                    {
                        textBox.Text = result.Remainder;
                        textBox.CaretIndex = textBox.Text.Length;
                    }
                    if (result.AddedCount > 0)
                    {
                        textBox.Text = string.Empty;
                    }
                    _suppressTagInputTextChange = false;
                }
                else
                {
                    ViewModel.CommitPendingTags(true);
                }

                e.Handled = true;
                return;
            }
        }

        private void TagEditBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel == null || sender is not TextBox textBox)
            {
                return;
            }

            if (e.Key == Key.Down)
            {
                MoveSuggestionSelection(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                MoveSuggestionSelection(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                bool allowCommit = e.Key == Key.Tab || e.ImeProcessedKey == Key.None;
                if (!allowCommit)
                {
                    return;
                }

                SuppressTagTextHandlingOnce();

                if (TagSuggestionPopup.IsOpen)
                {
                    ViewModel.SuppressSuggestionRefresh();
                    TryApplySelectedSuggestion();
                }

                ViewModel.CommitEditTag(textBox.DataContext as TagTokenViewModel);
                ViewModel.ClearSuggestions();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                ViewModel.CancelEditTag(textBox.DataContext as TagTokenViewModel);
                ViewModel.ClearSuggestions();
                e.Handled = true;
            }
        }

        private void TagInputBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            if (TagSuggestionPopup.IsOpen && TagSuggestionPopup.IsMouseOver)
            {
                return;
            }

            if (sender is TextBox textBox)
            {
                var inputText = ViewModel.PendingTagText;
                if (string.IsNullOrWhiteSpace(inputText))
                {
                    inputText = textBox.Text;
                }

                _suppressTagInputTextChange = true;
                var result = ViewModel.CommitPendingTagsFromInput(inputText ?? string.Empty, true);
                if (!string.Equals(textBox.Text, result.Remainder, StringComparison.Ordinal))
                {
                    textBox.Text = result.Remainder;
                    textBox.CaretIndex = textBox.Text.Length;
                }
                if (result.AddedCount > 0)
                {
                    textBox.Text = string.Empty;
                }
                _suppressTagInputTextChange = false;
            }
            else
            {
                ViewModel.CommitPendingTags(true);
            }
        }

        private void TagEditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (ViewModel == null || sender is not TextBox textBox)
            {
                return;
            }

            if (TagSuggestionPopup.IsOpen && TagSuggestionPopup.IsMouseOver)
            {
                return;
            }

            ViewModel.CommitEditTag(textBox.DataContext as TagTokenViewModel);
            ViewModel.ClearSuggestions();
        }

        private void TagInputBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                SetActiveSuggestionTarget(textBox);
                _ = ViewModel?.RefreshSuggestionsAsync(textBox.Text);
            }
        }

        private void TagEditBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
                SetActiveSuggestionTarget(textBox);
                _ = ViewModel?.RefreshSuggestionsAsync(textBox.Text);
            }
        }

        private void TagSuggestionList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            ViewModel.SuppressSuggestionRefresh();
            if (TryApplySelectedSuggestion() && _activeTagTextBox != null)
            {
                _activeTagTextBox.Focus();
                _activeTagTextBox.CaretIndex = _activeTagTextBox.Text.Length;
            }
        }

        private void TagSuggestionPopup_Closed(object sender, EventArgs e)
        {
            ViewModel?.ClearSuggestions();
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
