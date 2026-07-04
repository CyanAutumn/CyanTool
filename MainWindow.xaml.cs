using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyanTools.Localization;
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
        private static readonly IReadOnlyList<NavigationCategoryDefinition> NavigationCategories =
        [
            new NavigationCategoryDefinition(ToolPageCategory.Training, "训练", SymbolRegular.HatGraduation24),
            new NavigationCategoryDefinition(ToolPageCategory.DatasetAcquisition, "数据集获取", SymbolRegular.CloudArrowDown24),
            new NavigationCategoryDefinition(ToolPageCategory.DatasetClassification, "数据集归类", SymbolRegular.Collections24),
            new NavigationCategoryDefinition(ToolPageCategory.DatasetProcessing, "数据集处理", SymbolRegular.ImageMultiple24),
            new NavigationCategoryDefinition(ToolPageCategory.EnvironmentSettings, "环境设置", SymbolRegular.Settings24)
        ];
        private bool _downloadersUnlocked;
        private int _secretImageClickCount;
        private NavigationMenuEntry? _currentEntry;
        private bool _isUpdatingLanguageSelection;
        private bool _useDarkTheme;

        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;

            _homeView.SecretImageClicked += HomeView_SecretImageClicked;

            InitializeLanguageSelector();
            ApplyTheme(useDarkTheme: false);
            RefreshMenuItems();

            Loaded += (_, _) => ShowHome();
        }

        protected override void OnClosed(EventArgs e)
        {
            LocalizationManager.LanguageChanged -= LocalizationManager_LanguageChanged;
            base.OnClosed(e);
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
                    LocalizationManager.Format(
                        "页面加载失败: {0}\n{1}\n\n{2}",
                        LocalizationManager.Translate(page.DisplayName),
                        page.ControlType.FullName ?? string.Empty,
                        ex),
                    LocalizationManager.Translate("错误"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }

            if (control == null)
            {
                return;
            }

            _currentEntry = entry;
            RootNavigationView.Header = LocalizationManager.Translate(page.DisplayName);
            RootNavigationView.ReplaceContent(control);
            LocalizationManager.ApplyTo(control);
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
                "再敲咬你啊",
                LocalizationManager.Translate("提示"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void RefreshMenuItems()
        {
            var previousEntry = _currentEntry;
            NavigationViewItem? activeItem = null;
            RootNavigationView.MenuItems.Clear();
            _navigationItems.Clear();

            var entries = _navigationService.GetMenuEntries(_downloadersUnlocked);
            var homeEntry = entries.FirstOrDefault(entry => entry.IsHome);
            if (homeEntry != null)
            {
                var homeItem = CreateNavigationItem(homeEntry);
                RootNavigationView.MenuItems.Add(homeItem);
                _navigationItems[homeItem] = homeEntry;
                if (previousEntry != null && IsSameEntry(previousEntry, homeEntry))
                {
                    activeItem = homeItem;
                }
            }

            var pageEntries = entries.Where(entry => !entry.IsHome && entry.Metadata != null).ToList();
            var standaloneEntries = pageEntries
                .Where(IsStandaloneTopLevelEntry)
                .OrderBy(entry => entry.Metadata?.Order ?? 0)
                .ToList();
            foreach (var entry in standaloneEntries)
            {
                var item = CreateNavigationItem(entry);
                RootNavigationView.MenuItems.Add(item);
                _navigationItems[item] = entry;

                if (previousEntry != null && IsSameEntry(previousEntry, entry))
                {
                    activeItem = item;
                }
            }

            var groupedPageEntries = pageEntries
                .Where(entry => !IsStandaloneTopLevelEntry(entry))
                .ToList();
            foreach (var category in NavigationCategories)
            {
                var categoryEntries = groupedPageEntries
                    .Where(entry => entry.Metadata?.Category == category.Category)
                    .OrderBy(entry => entry.Metadata?.Order ?? 0)
                    .ToList();
                if (categoryEntries.Count == 0)
                {
                    continue;
                }

                var categoryItem = CreateCategoryItem(category);
                foreach (var entry in categoryEntries)
                {
                    var item = CreateNavigationItem(entry);
                    categoryItem.MenuItems.Add(item);
                    _navigationItems[item] = entry;

                    if (previousEntry != null && IsSameEntry(previousEntry, entry))
                    {
                        activeItem = item;
                        categoryItem.IsExpanded = true;
                    }
                }

                RootNavigationView.MenuItems.Add(categoryItem);
            }

            MarkActiveItem(activeItem);
        }

        private static bool IsStandaloneTopLevelEntry(NavigationMenuEntry entry)
        {
            return entry.Metadata?.ControlType == typeof(PipelineSchedulerView);
        }

        private static NavigationViewItem CreateCategoryItem(NavigationCategoryDefinition category)
        {
            return new NavigationViewItem
            {
                Content = LocalizationManager.Translate(category.DisplayName),
                Icon = new SymbolIcon(category.Icon),
                IsExpanded = false
            };
        }

        private NavigationViewItem CreateNavigationItem(NavigationMenuEntry entry)
        {
            var item = new NavigationViewItem
            {
                Content = LocalizationManager.Translate(entry.IsHome ? "主页" : entry.DisplayName),
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
                return SymbolRegular.Flowchart24;
            }

            if (controlType == typeof(ToolLLM))
            {
                return SymbolRegular.BotSparkle24;
            }

            if (controlType == typeof(TrainingSetBalancerView))
            {
                return SymbolRegular.Scales24;
            }

            if (controlType == typeof(LayeredLoraMergeView))
            {
                return SymbolRegular.Layer24;
            }

            if (controlType == typeof(XyzToolView))
            {
                return SymbolRegular.NumberSymbolSquare24;
            }

            if (controlType == typeof(DanbooruCrawlerView))
            {
                return SymbolRegular.ArrowDownload24;
            }

            if (controlType == typeof(GeminiImageGenerationView))
            {
                return SymbolRegular.Sparkle24;
            }

            if (controlType == typeof(ImageBucketingView))
            {
                return SymbolRegular.Table24;
            }

            if (controlType == typeof(ThreeStageView))
            {
                return SymbolRegular.SplitHorizontal24;
            }

            if (controlType == typeof(ResolutionFilterView))
            {
                return SymbolRegular.Filter24;
            }

            if (controlType == typeof(ImageClusteringView))
            {
                return SymbolRegular.ImageSearch24;
            }

            if (controlType == typeof(SameSourceImageGroupingView))
            {
                return SymbolRegular.FolderPeople24;
            }

            if (controlType == typeof(KeepFilesView))
            {
                return SymbolRegular.Folder24;
            }

            if (controlType == typeof(DatasetManagerView))
            {
                return SymbolRegular.DatabaseSearch24;
            }

            if (controlType == typeof(RealEsrganView))
            {
                return SymbolRegular.ImageSparkle24;
            }

            if (controlType == typeof(ImageScalingView))
            {
                return SymbolRegular.ResizeImage24;
            }

            if (controlType == typeof(ImageProcessingView))
            {
                return SymbolRegular.DrawImage24;
            }

            if (controlType == typeof(ExportVideoFrameView))
            {
                return SymbolRegular.VideoClipMultiple24;
            }

            if (controlType == typeof(ImageMetadataView))
            {
                return SymbolRegular.DocumentText24;
            }

            if (controlType == typeof(GaussianBlurView))
            {
                return SymbolRegular.Blur24;
            }

            if (controlType == typeof(ConvertImageFormatView))
            {
                return SymbolRegular.ImageArrowForward24;
            }

            if (controlType == typeof(ConvertTransparentChannelView))
            {
                return SymbolRegular.Layer24;
            }

            if (controlType == typeof(TaggingView))
            {
                return SymbolRegular.Tag24;
            }

            if (controlType == typeof(ImageRenameView))
            {
                return SymbolRegular.Rename24;
            }

            if (controlType == typeof(FolderProcessingView))
            {
                return SymbolRegular.AppFolder24;
            }

            if (controlType == typeof(OptionsView))
            {
                return SymbolRegular.WrenchSettings24;
            }

            return SymbolRegular.Toolbox24;
        }

        private void ShowHome(NavigationViewItem? activeItem = null)
        {
            _currentEntry = NavigationMenuEntry.Home;
            RootNavigationView.Header = LocalizationManager.Translate("主页");
            RootNavigationView.ReplaceContent(_homeView);
            LocalizationManager.ApplyTo(_homeView);
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
            var items = new List<NavigationViewItem>(_navigationItems.Keys);
            foreach (var item in items)
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

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingLanguageSelection)
            {
                return;
            }

            if (LanguageComboBox.SelectedValue is AppLanguage selectedLanguage)
            {
                LocalizationManager.SetLanguage(selectedLanguage);
                return;
            }

            if (LanguageComboBox.SelectedItem is LanguageOption option)
            {
                LocalizationManager.SetLanguage(option.Language);
            }
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
            _useDarkTheme = useDarkTheme;
            ApplicationThemeManager.Apply(
                useDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light,
                WindowBackdropType.None);
            ApplyCyanPalette(useDarkTheme);

            ThemeStatusText.Text = LocalizationManager.Translate(useDarkTheme ? "夜间" : "白天");
            ThemeIcon.Symbol = useDarkTheme ? SymbolRegular.DarkTheme24 : SymbolRegular.WeatherSunny24;
        }

        private void InitializeLanguageSelector()
        {
            _isUpdatingLanguageSelection = true;
            LanguageComboBox.ItemsSource = LocalizationManager.LanguageOptions;
            LanguageComboBox.DisplayMemberPath = nameof(LanguageOption.DisplayName);
            LanguageComboBox.SelectedValuePath = nameof(LanguageOption.Language);
            LanguageComboBox.SelectedValue = LocalizationManager.CurrentLanguage;
            _isUpdatingLanguageSelection = false;

            LocalizationManager.LanguageChanged += LocalizationManager_LanguageChanged;
        }

        private void LocalizationManager_LanguageChanged(object? sender, EventArgs e)
        {
            RefreshLocalizedText();
        }

        private void RefreshLocalizedText()
        {
            _isUpdatingLanguageSelection = true;
            LanguageComboBox.SelectedValue = LocalizationManager.CurrentLanguage;
            _isUpdatingLanguageSelection = false;

            ApplyTheme(_useDarkTheme);
            RefreshMenuItems();
            UpdateNavigationHeader();
            LocalizationManager.ApplyTo(this);
        }

        private void UpdateNavigationHeader()
        {
            RootNavigationView.Header = _currentEntry == null || _currentEntry.IsHome
                ? LocalizationManager.Translate("主页")
                : LocalizationManager.Translate(_currentEntry.DisplayName);
        }

        private static void ApplyCyanPalette(bool useDarkTheme)
        {
            if (useDarkTheme)
            {
                SetSolidBrush("CyanShellBackgroundBrush", Color.FromRgb(9, 9, 11));
                SetSolidBrush("CyanTopBarBackgroundBrush", Color.FromArgb(238, 15, 15, 17));
                SetSolidBrush("CyanNavigationShellBrush", Color.FromArgb(224, 12, 12, 14));
                SetSolidBrush("CyanSurfaceElevatedBrush", Color.FromRgb(22, 22, 24));
                SetSolidBrush("CyanSurfaceHoverBrush", Color.FromRgb(32, 32, 35));
                SetSolidBrush("CyanMutedAccentBrush", Color.FromArgb(44, 0, 194, 168));
                SetSolidBrush("CyanNodeHeaderBrush", Color.FromRgb(26, 27, 30));
                SetSolidBrush("CyanBadgeBackgroundBrush", Color.FromArgb(222, 28, 28, 31));
                SetGradientBrush(
                    "CyanShellGradientBrush",
                    (Color.FromRgb(9, 9, 11), 0),
                    (Color.FromRgb(12, 12, 14), 0.55),
                    (Color.FromRgb(18, 18, 20), 1));
                SetGradientBrush(
                    "CyanHeroBackgroundBrush",
                    (Color.FromArgb(220, 30, 30, 32), 0),
                    (Color.FromArgb(194, 18, 18, 20), 0.58),
                    (Color.FromArgb(76, 0, 194, 168), 1));
                return;
            }

            SetSolidBrush("CyanShellBackgroundBrush", Color.FromRgb(244, 248, 253));
            SetSolidBrush("CyanTopBarBackgroundBrush", Color.FromArgb(239, 255, 255, 255));
            SetSolidBrush("CyanNavigationShellBrush", Color.FromArgb(204, 255, 255, 255));
            SetSolidBrush("CyanSurfaceElevatedBrush", Color.FromRgb(255, 255, 255));
            SetSolidBrush("CyanSurfaceHoverBrush", Color.FromRgb(246, 250, 255));
            SetSolidBrush("CyanMutedAccentBrush", Color.FromArgb(31, 0, 194, 168));
            SetSolidBrush("CyanNodeHeaderBrush", Color.FromRgb(36, 50, 71));
            SetSolidBrush("CyanBadgeBackgroundBrush", Color.FromArgb(204, 31, 41, 55));
            SetGradientBrush(
                "CyanShellGradientBrush",
                (Color.FromRgb(248, 251, 255), 0),
                (Color.FromRgb(240, 248, 255), 0.55),
                (Color.FromRgb(255, 246, 251), 1));
            SetGradientBrush(
                "CyanHeroBackgroundBrush",
                (Color.FromArgb(51, 0, 194, 168), 0),
                (Color.FromArgb(36, 90, 167, 255), 0.48),
                (Color.FromArgb(40, 255, 122, 182), 1));
        }

        private static void SetSolidBrush(string key, Color color)
        {
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }

        private static void SetGradientBrush(string key, params (Color Color, double Offset)[] stops)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };

            foreach (var stop in stops)
            {
                brush.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
            }

            Application.Current.Resources[key] = brush;
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

        private sealed class NavigationCategoryDefinition
        {
            public NavigationCategoryDefinition(ToolPageCategory category, string displayName, SymbolRegular icon)
            {
                Category = category;
                DisplayName = displayName;
                Icon = icon;
            }

            public ToolPageCategory Category { get; }
            public string DisplayName { get; }
            public SymbolRegular Icon { get; }
        }
    }
}
