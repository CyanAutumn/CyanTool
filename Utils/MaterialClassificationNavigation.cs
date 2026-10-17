using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CyanTools.ViewModel;

namespace CyanTools.Utils;

public static class MaterialClassificationNavigation
{
    public static readonly DependencyProperty IsIndexModeProperty = DependencyProperty.RegisterAttached(
        "IsIndexMode", typeof(bool), typeof(MaterialClassificationNavigation), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetIsIndexMode(DependencyObject target) => (bool)target.GetValue(IsIndexModeProperty);
    public static void SetIsIndexMode(DependencyObject target, bool value) => target.SetValue(IsIndexModeProperty, value);
    public static Type PageType(MaterialClassificationTool tool) => tool switch
    {
        MaterialClassificationTool.SameSource => typeof(SameSourceImageGroupingView),
        MaterialClassificationTool.ResolutionBuckets => typeof(ImageBucketingView),
        MaterialClassificationTool.ResolutionFilter => typeof(ResolutionFilterView),
        MaterialClassificationTool.Clustering => typeof(ImageClusteringView),
        MaterialClassificationTool.Aesthetic => typeof(AestheticScoringView),
        MaterialClassificationTool.FarScene => typeof(FarSceneDetectionView),
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };

    public static void Open(UserControl page, string materialRoot, MaterialClassificationTool tool, Action goBack)
    {

        if (page.IsLoaded) Attach();
        else page.Loaded += FirstLoad;
        void FirstLoad(object sender, RoutedEventArgs e) { page.Loaded -= FirstLoad; Attach(); }
        void Attach()
        {
            object model = page.DataContext ?? throw new InvalidOperationException("功能页面尚未初始化。");
            bool busy = model switch
            {
                SameSourceImageGroupingViewModel m => m.IsProcessing,
                ImageBucketingViewModel m => m.IsProcessing,
                ResolutionFilterViewModel m => !m.IsStartButtonEnabled,
                ImageClusteringViewModel m => m.IsClustering,
                AestheticScoringViewModel m => m.IsBusy,
                FarSceneDetectionViewModel m => m.IsBusy,
                _ => true
            };
            if (busy || MaterialClassificationSession.IsRunning(model)) { MessageBox.Show("该功能正在处理任务，请等待完成后再从素材库进入。"); return; }
            var session = new MaterialClassificationSession(model, materialRoot, tool, goBack);
            SetIsIndexMode(page, true);
            var body = (UIElement)page.Content;
            page.Content = null;
            var dock = new DockPanel();
            var bar = new StackPanel { Margin = new Thickness(16, 8, 16, 8), DataContext = session };
            var status = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 140,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 8) };
            status.SetBinding(TextBox.TextProperty, new Binding(nameof(session.Status)) { Mode = BindingMode.OneWay });
            bar.Children.Add(status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (label, command) in new[] { ("查看", session.ViewCommand), ("还原", session.RestoreCommand), ("返回素材库", session.ReturnCommand), ("取消", session.CancelCommand) })
                buttons.Children.Add(new Button { Content = label, Command = command, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 5, 16, 5) });
            bar.Children.Add(buttons);
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar); dock.Children.Add(body); page.Content = dock;
            session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(session.IsWorking)) body.IsEnabled = !session.IsWorking; };
            session.Attach();
            page.Unloaded += Unload;
            void Unload(object sender, RoutedEventArgs e)
            {
                if (!ReferenceEquals(e.OriginalSource, page)) return;
                page.Unloaded -= Unload;
                session.Detach();
                SetIsIndexMode(page, false);
                dock.Children.Remove(body); page.Content = body;
                body.IsEnabled = true;
            }
        }
    }
}
