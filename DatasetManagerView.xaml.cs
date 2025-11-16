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
            ViewModel?.HandleKeyDown(e.Key);
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
