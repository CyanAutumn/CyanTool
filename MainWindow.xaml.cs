using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CyanTools.Navigation;
using CyanTools.Utils;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace CyanTools
{
    public partial class MainWindow : FluentWindow
    {
        private readonly ToolNavigationService _navigationService = new();
        private readonly Dictionary<NavigationViewItem, NavigationMenuEntry> _navigationItems = new();
        private readonly HomeView _homeView = new();
        private bool _downloadersUnlocked;
        private int _secretImageClickCount;
        private NavigationMenuEntry? _currentEntry;

        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;

            _homeView.SecretImageClicked += HomeView_SecretImageClicked;

            ApplyTheme(useDarkTheme: false);
            RefreshMenuItems();

            Loaded += (_, _) => ShowHome();
        }

        private void RootNavigationView_SelectionChanged(NavigationView sender, RoutedEventArgs args)
        {
            if (sender.SelectedItem is not NavigationViewItem item ||
                !_navigationItems.TryGetValue(item, out var entry))
            {
                return;
            }

            NavigateToEntry(entry, item);
        }

        private void NavigationItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not NavigationViewItem item ||
                !_navigationItems.TryGetValue(item, out var entry))
            {
                return;
            }

            NavigateToEntry(entry, item);
            e.Handled = true;
        }

        private void NavigateToEntry(NavigationMenuEntry entry, NavigationViewItem activeItem)
        {
            if (entry.IsHome)
            {
                ShowHome(activeItem);
                return;
            }

            var page = entry.Metadata;
            if (page == null || page.RequiresUnlock && !_downloadersUnlocked)
            {
                return;
            }

            UserControl? control = null;
            try
            {
                control = _navigationService.GetOrCreateControl(page);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"页面加载失败: {page.DisplayName}\n{page.ControlType.FullName}\n\n{ex}",
                    "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }

            if (control == null)
            {
                return;
            }

            _currentEntry = entry;
            RootNavigationView.Header = page.DisplayName;
            RootNavigationView.ReplaceContent(control);
            MarkActiveItem(activeItem);
        }

        private void HomeView_SecretImageClicked(object? sender, EventArgs e)
        {
            if (_downloadersUnlocked)
            {
                return;
            }

            _secretImageClickCount++;
            if (_secretImageClickCount < 5)
            {
                return;
            }

            _downloadersUnlocked = true;
            UnlockState.DownloadersUnlocked = true;
            _homeView.ShowUnlockedImage();
            RefreshMenuItems();
            System.Windows.MessageBox.Show(
                "隐藏下载器已解锁。",
                "提示",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void RefreshMenuItems()
        {
            var previousEntry = _currentEntry;
            RootNavigationView.MenuItems.Clear();
            _navigationItems.Clear();

            foreach (var entry in _navigationService.GetMenuEntries(_downloadersUnlocked))
            {
                var item = CreateNavigationItem(entry);
                RootNavigationView.MenuItems.Add(item);
                _navigationItems[item] = entry;

                if (previousEntry != null && IsSameEntry(previousEntry, entry))
                {
                    MarkActiveItem(item);
                }
            }
        }

        private NavigationViewItem CreateNavigationItem(NavigationMenuEntry entry)
        {
            var item = new NavigationViewItem
            {
                Content = entry.IsHome ? "主页" : entry.DisplayName,
                Icon = new SymbolIcon(GetIconForEntry(entry)),
                Tag = entry
            };

            item.Click += NavigationItem_Click;
            return item;
        }

        private static SymbolRegular GetIconForEntry(NavigationMenuEntry entry)
        {
            if (entry.IsHome)
            {
                return SymbolRegular.Home24;
            }

            var controlType = entry.Metadata?.ControlType;
            if (controlType == typeof(PipelineSchedulerView))
            {
                return SymbolRegular.BranchFork24;
            }

            if (controlType == typeof(DatasetManagerView) ||
                controlType == typeof(DanbooruCrawlerView))
            {
                return SymbolRegular.DatabaseSearch24;
            }

            if (controlType == typeof(GeminiImageGenerationView))
            {
                return SymbolRegular.Sparkle24;
            }

            if (controlType == typeof(ToolLLM))
            {
                return SymbolRegular.Bot24;
            }

            if (controlType == typeof(RealEsrganView) ||
                controlType == typeof(ImageScalingView) ||
                controlType == typeof(ImageProcessingView) ||
                controlType == typeof(ImageClusteringView))
            {
                return SymbolRegular.ImageMultiple24;
            }

            return SymbolRegular.Toolbox24;
        }

        private void ShowHome(NavigationViewItem? activeItem = null)
        {
            _currentEntry = NavigationMenuEntry.Home;
            RootNavigationView.Header = "主页";
            RootNavigationView.ReplaceContent(_homeView);
            MarkActiveItem(activeItem ?? FindItemForEntry(NavigationMenuEntry.Home));
        }

        private NavigationViewItem? FindItemForEntry(NavigationMenuEntry entry)
        {
            foreach (var pair in _navigationItems)
            {
                if (IsSameEntry(pair.Value, entry))
                {
                    return pair.Key;
                }
            }

            return null;
        }

        private void MarkActiveItem(NavigationViewItem? activeItem)
        {
            foreach (var item in _navigationItems.Keys)
            {
                item.IsActive = ReferenceEquals(item, activeItem);
            }
        }

        private static bool IsSameEntry(NavigationMenuEntry first, NavigationMenuEntry second)
        {
            if (first.IsHome || second.IsHome)
            {
                return first.IsHome && second.IsHome;
            }

            return first.Metadata?.ControlType == second.Metadata?.ControlType;
        }

        private void ThemeToggleSwitch_Checked(object sender, RoutedEventArgs e)
        {
            ApplyTheme(useDarkTheme: true);
        }

        private void ThemeToggleSwitch_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplyTheme(useDarkTheme: false);
        }

        private void AppTitleBar_MinimizeClicked(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
            e.Handled = true;
        }

        private void AppTitleBar_MaximizeClicked(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
            e.Handled = true;
        }

        private void AppTitleBar_CloseClicked(object sender, RoutedEventArgs e)
        {
            Close();
            e.Handled = true;
        }

        private void TitleDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
                e.Handled = true;
                return;
            }

            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {

            }
        }

        private void ToggleWindowState()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void ApplyTheme(bool useDarkTheme)
        {
            ApplicationThemeManager.Apply(
                useDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light,
                WindowBackdropType.None);

            ThemeStatusText.Text = useDarkTheme ? "夜间" : "白天";
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_navigationService.TryGetCachedControl(typeof(DanbooruCrawlerView), out var danbooruView) &&
                danbooruView.DataContext is ViewModel.DatasetDownloaderViewModel viewModel)
            {
                await viewModel.SaveStateOnCloseAsync();
            }

            if (_navigationService.TryGetCachedControl(typeof(ImageClusteringView), out var imageClusteringView) &&
                imageClusteringView.DataContext is ViewModel.ImageClusteringViewModel imageClusteringViewModel)
            {
                await imageClusteringViewModel.SaveStateOnCloseAsync();
            }
        }
    }
}
