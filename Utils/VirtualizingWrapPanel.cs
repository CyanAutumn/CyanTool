using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CyanTools.Utils
{
    public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        public static readonly DependencyProperty ItemWidthProperty =
            DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ItemHeightProperty =
            DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        private Size _extent = new Size(0, 0);
        private Size _viewport = new Size(0, 0);
        private Point _offset;
        private Size _childSize = Size.Empty;
        private int _itemsPerRow = 1;

        public double ItemWidth
        {
            get => (double)GetValue(ItemWidthProperty);
            set => SetValue(ItemWidthProperty, value);
        }

        public double ItemHeight
        {
            get => (double)GetValue(ItemHeightProperty);
            set => SetValue(ItemHeightProperty, value);
        }

        public int ItemsPerRow => _itemsPerRow;

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs e)
        {
            base.OnItemsChanged(sender, e);

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _childSize = Size.Empty;
            }

            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var itemsControl = ItemsControl.GetItemsOwner(this);
            if (itemsControl == null)
            {
                return availableSize;
            }

            int itemCount = itemsControl.HasItems ? itemsControl.Items.Count : 0;
            UpdateChildSize(itemCount);

            _itemsPerRow = CalculateItemsPerRow(availableSize, _childSize);
            if (_itemsPerRow <= 0)
            {
                _itemsPerRow = 1;
            }

            int rowCount = itemCount == 0 ? 0 : (int)Math.Ceiling((double)itemCount / _itemsPerRow);
            UpdateScrollInfo(availableSize, rowCount);

            if (itemCount == 0 || _childSize.Width <= 0 || _childSize.Height <= 0)
            {
                return availableSize;
            }

            int firstVisibleRow = (int)Math.Floor(VerticalOffset / _childSize.Height);
            int visibleRowCount = (int)Math.Ceiling(ViewportHeight / _childSize.Height) + 1;

            int startIndex = Math.Max(0, firstVisibleRow * _itemsPerRow);
            int endIndex = Math.Min(itemCount - 1, (firstVisibleRow + visibleRowCount) * _itemsPerRow - 1);

            var generator = ItemContainerGenerator;
            var startPos = generator.GeneratorPositionFromIndex(startIndex);
            int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

            using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
            {
                for (int itemIndex = startIndex; itemIndex <= endIndex; itemIndex++, childIndex++)
                {
                    bool newlyRealized;
                    var child = generator.GenerateNext(out newlyRealized) as UIElement;
                    if (child == null)
                    {
                        continue;
                    }

                    if (newlyRealized)
                    {
                        if (childIndex >= InternalChildren.Count)
                        {
                            AddInternalChild(child);
                        }
                        else
                        {
                            InsertInternalChild(childIndex, child);
                        }

                        generator.PrepareItemContainer(child);
                    }

                    child.Measure(_childSize);
                }
            }

            CleanUpItems(startIndex, endIndex);

            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_childSize.Width <= 0 || _childSize.Height <= 0)
            {
                return finalSize;
            }

            for (int i = 0; i < InternalChildren.Count; i++)
            {
                var child = InternalChildren[i];
                var position = new GeneratorPosition(i, 0);
                int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(position);
                var rect = GetChildRect(itemIndex);
                rect.X -= HorizontalOffset;
                rect.Y -= VerticalOffset;
                child.Arrange(rect);
            }

            return finalSize;
        }

        private void UpdateChildSize(int itemCount)
        {
            if (ItemWidth > 0 && ItemHeight > 0)
            {
                _childSize = new Size(ItemWidth, ItemHeight);
                return;
            }

            if (!_childSize.IsEmpty)
            {
                return;
            }

            if (itemCount == 0)
            {
                _childSize = new Size(Math.Max(1, ItemWidth), Math.Max(1, ItemHeight));
                return;
            }

            var generator = ItemContainerGenerator;
            var startPos = generator.GeneratorPositionFromIndex(0);
            int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

            using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
            {
                bool newlyRealized;
                var child = generator.GenerateNext(out newlyRealized) as UIElement;
                if (child == null)
                {
                    _childSize = new Size(1, 1);
                    return;
                }

                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _childSize = child.DesiredSize;
            }
        }

        private int CalculateItemsPerRow(Size availableSize, Size childSize)
        {
            if (childSize.Width <= 0)
            {
                return 1;
            }

            double availableWidth = availableSize.Width;
            if (double.IsInfinity(availableWidth) || availableWidth <= 0)
            {
                availableWidth = _viewport.Width > 0 ? _viewport.Width : childSize.Width;
            }

            return Math.Max(1, (int)Math.Floor(availableWidth / childSize.Width));
        }

        private void CleanUpItems(int startIndex, int endIndex)
        {
            var generator = ItemContainerGenerator;

            for (int i = InternalChildren.Count - 1; i >= 0; i--)
            {
                var position = new GeneratorPosition(i, 0);
                int itemIndex = generator.IndexFromGeneratorPosition(position);
                if (itemIndex < startIndex || itemIndex > endIndex)
                {
                    generator.Remove(position, 1);
                    RemoveInternalChildRange(i, 1);
                }
            }
        }

        private Rect GetChildRect(int itemIndex)
        {
            int row = itemIndex / _itemsPerRow;
            int column = itemIndex % _itemsPerRow;
            double x = column * _childSize.Width;
            double y = row * _childSize.Height;
            return new Rect(x, y, _childSize.Width, _childSize.Height);
        }

        private void UpdateScrollInfo(Size availableSize, int rowCount)
        {
            var extent = new Size(_itemsPerRow * _childSize.Width, rowCount * _childSize.Height);

            double viewportWidth = double.IsInfinity(availableSize.Width) ? extent.Width : availableSize.Width;
            double viewportHeight = double.IsInfinity(availableSize.Height) ? extent.Height : availableSize.Height;

            var viewport = new Size(viewportWidth, viewportHeight);
            if (!extent.Equals(_extent) || !viewport.Equals(_viewport))
            {
                _extent = extent;
                _viewport = viewport;
                ScrollOwner?.InvalidateScrollInfo();
            }

            _offset.X = CoerceOffset(_offset.X, _extent.Width, _viewport.Width);
            _offset.Y = CoerceOffset(_offset.Y, _extent.Height, _viewport.Height);
        }

        private static double CoerceOffset(double offset, double extent, double viewport)
        {
            if (offset < 0)
            {
                return 0;
            }

            if (offset + viewport > extent)
            {
                return Math.Max(0, extent - viewport);
            }

            return offset;
        }

        private double HorizontalLineSize => _childSize.Width > 0 ? _childSize.Width : 16;
        private double VerticalLineSize => _childSize.Height > 0 ? _childSize.Height : 16;

        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public double ExtentWidth => _extent.Width;
        public double ExtentHeight => _extent.Height;
        public double ViewportWidth => _viewport.Width;
        public double ViewportHeight => _viewport.Height;
        public double HorizontalOffset => _offset.X;
        public double VerticalOffset => _offset.Y;
        public ScrollViewer? ScrollOwner { get; set; }

        public void LineDown() => SetVerticalOffset(VerticalOffset + VerticalLineSize);
        public void LineUp() => SetVerticalOffset(VerticalOffset - VerticalLineSize);
        public void LineLeft() => SetHorizontalOffset(HorizontalOffset - HorizontalLineSize);
        public void LineRight() => SetHorizontalOffset(HorizontalOffset + HorizontalLineSize);
        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + VerticalLineSize * SystemParameters.WheelScrollLines);
        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - VerticalLineSize * SystemParameters.WheelScrollLines);
        public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - HorizontalLineSize * SystemParameters.WheelScrollLines);
        public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + HorizontalLineSize * SystemParameters.WheelScrollLines);
        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
        public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
        public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);

        public void SetHorizontalOffset(double offset)
        {
            double coerced = CoerceOffset(offset, _extent.Width, _viewport.Width);
            if (Math.Abs(coerced - _offset.X) < 0.1)
            {
                return;
            }

            _offset.X = coerced;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }

        public void SetVerticalOffset(double offset)
        {
            double coerced = CoerceOffset(offset, _extent.Height, _viewport.Height);
            if (Math.Abs(coerced - _offset.Y) < 0.1)
            {
                return;
            }

            _offset.Y = coerced;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            if (visual == null)
            {
                return Rect.Empty;
            }

            var itemsControl = ItemsControl.GetItemsOwner(this);
            if (itemsControl == null)
            {
                return Rect.Empty;
            }

            int index = itemsControl.ItemContainerGenerator.IndexFromContainer(visual);
            if (index < 0)
            {
                return Rect.Empty;
            }

            var target = GetChildRect(index);
            if (target.Left < HorizontalOffset)
            {
                SetHorizontalOffset(target.Left);
            }
            else if (target.Right > HorizontalOffset + ViewportWidth)
            {
                SetHorizontalOffset(target.Right - ViewportWidth);
            }

            if (target.Top < VerticalOffset)
            {
                SetVerticalOffset(target.Top);
            }
            else if (target.Bottom > VerticalOffset + ViewportHeight)
            {
                SetVerticalOffset(target.Bottom - ViewportHeight);
            }

            target.X -= HorizontalOffset;
            target.Y -= VerticalOffset;
            return target;
        }
    }
}
