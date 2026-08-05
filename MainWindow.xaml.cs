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
        private static readonly IReadOnlyDictionary<Type, string> PageDescriptions = new Dictionary<Type, string>
        {
            [typeof(ToolLLM)] = "集中管理模型、提示词、工作目录和分类路径，右侧持续显示执行日志。",
            [typeof(RealEsrganView)] = "统一管理超分工具、模型参数、输出选项和运行日志。",
            [typeof(ImageScalingView)] = "按目标分辨率批量上采样或下采样图片。",
            [typeof(ImageProcessingView)] = "对目标路径中的图片执行镜像等轻量批处理操作。",
            [typeof(DatasetManagerView)] = "浏览图片、编辑提示词标签、分配分类并保存数据集改动。",
            [typeof(TrainingSetManagerView)] = "用 Git/LFS 管理图片训练集历史、节点配置、笔记、日志和训练产物。",
            [typeof(TrainingBridgeView)] = "在 Windows 与 WSL 训练后端之间导入、映射、预览和导出训练配置。",
            [typeof(ModelTrainingView)] = "管理训练脚本、TOML 输出和参数分组，生成配置后可直接启动训练。",
            [typeof(LoraTrainingView)] = "管理 LoRA 训练脚本、TOML 输出和参数分组，生成配置后可直接启动训练。",
            [typeof(LayeredLoraMergeView)] = "按 SDXL 25 层、Anima LoRA Block 或 Anima 大模型 Block 融合。",
            [typeof(ModelMetadataView)] = "查看、编辑或清除 Safetensors 模型嵌入的源数据，并保持张量不变。",
            [typeof(ImageBucketingView)] = "根据图片宽高比和尺寸规则，将图片归入对应分辨率分组。",
            [typeof(ThreeStageView)] = "人物、半身、头部与眼部裁切参数集中配置。",
            [typeof(ResolutionFilterView)] = "按高度、宽度或分辨率阈值筛掉不符合要求的图片。",
            [typeof(HandIntegrityFilterView)] = "按模型检测手部关键点，将疑似异常图片分流到输出路径。",
            [typeof(KeepFilesView)] = "按扩展名保留需要的文件类型，其余文件会被清理。",
            [typeof(FolderProcessingView)] = "对整批文件执行移动、清理、整理和训练输出转换等目录级操作。",
            [typeof(ExportVideoFrameView)] = "预览视频帧并导出当前帧、全部帧或 GIF。",
            [typeof(ImageMetadataView)] = "拖入图片后查看并编辑元数据 JSON。",
            [typeof(GaussianBlurView)] = "批量生成指定半径的模糊图片。",
            [typeof(ImageClusteringView)] = "按特征空间、聚类算法和 UMAP 参数整理图片分组。",
            [typeof(SameSourceImageGroupingView)] = "识别原图、缩略图、副本、被包含图和差分图，并导出清晰的分组结果。",
            [typeof(ConvertImageFormatView)] = "批量转换图片格式并保留原有目录结构。",
            [typeof(ConvertTransparentChannelView)] = "将透明像素填充为白色，支持 PNG / WEBP / BMP / JPEG。",
            [typeof(TaggingView)] = "选择输入路径、打标模型和阈值后批量生成标签文件。",
            [typeof(TrainingSetBalancerView)] = "按子文件夹图片数量计算重复次数，让训练步数占比更均衡。",
            [typeof(TrainingOutputOrganizerView)] = "将 diffusion-pipe epoch 输出整理为 sd-script 可识别的 adapter_model 序列。",
            [typeof(XyzToolView)] = "按开始、结束和间隔生成可复制的序号提示词。",
            [typeof(ImageRenameView)] = "按前缀、起始编号和位数批量预览并重命名图片。",
            [typeof(DanbooruCrawlerView)] = "集中配置来源、令牌、标签和输出目录，下载日志在右侧持续追踪。",
            [typeof(GeminiImageGenerationView)] = "素材、提示词、模型参数和生成结果集中在一个更清爽的工作台里。",
            [typeof(PipelineSchedulerView)] = "在节点画布中编排处理流程，保存工作流并查看执行日志。",
            [typeof(OptionsView)] = "管理本机运行环境与共享工具设置。",
            [typeof(ProxySettingsView)] = "管理 Clash 订阅、本地代理端口和数据集下载节点。",
            [typeof(InferenceProjectsView)] = "启动 ComfyUI 与 Webui Forge Neo，查看实时日志并快捷管理模型目录。",
            [typeof(PromptManagerView)] = "维护本地提示词文件，快速创建、编辑、随机复制和自增添加。"
        };
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
                MessageBox.Show(
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
            UpdateShellPageHeader();
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
            MessageBox.Show(
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

            if (controlType == typeof(TrainingSetManagerView))
            {
                return SymbolRegular.DatabaseLink24;
            }

            if (controlType == typeof(TrainingBridgeView))
            {
                return SymbolRegular.ArrowSync24;
            }

            if (controlType == typeof(LayeredLoraMergeView))
            {
                return SymbolRegular.Layer24;
            }

            if (controlType == typeof(ModelMetadataView))
            {
                return SymbolRegular.DocumentText24;
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

            if (controlType == typeof(ProxySettingsView))
            {
                return SymbolRegular.ArrowRouting24;
            }

            if (controlType == typeof(InferenceProjectsView))
            {
                return SymbolRegular.Play24;
            }

            return SymbolRegular.Toolbox24;
        }

        private void ShowHome(NavigationViewItem? activeItem = null)
        {
            _currentEntry = NavigationMenuEntry.Home;
            RootNavigationView.Header = LocalizationManager.Translate("主页");
            UpdateShellPageHeader();
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
            UpdateShellPageHeader();
        }

        private void UpdateNavigationHeader()
        {
            RootNavigationView.Header = _currentEntry == null || _currentEntry.IsHome
                ? LocalizationManager.Translate("主页")
                : LocalizationManager.Translate(_currentEntry.DisplayName);
            UpdateShellPageHeader();
        }

        private void UpdateShellPageHeader()
        {
            if (_currentEntry == null || _currentEntry.IsHome || _currentEntry.Metadata == null)
            {
                ShellPageTitleText.Text = "Cyan Tools";
                ShellPageDescriptionText.Text = LocalizationManager.Translate("AI 图像与数据集工作台");
                return;
            }

            var metadata = _currentEntry.Metadata;
            ShellPageTitleText.Text = LocalizationManager.Translate(metadata.DisplayName);
            ShellPageDescriptionText.Text = PageDescriptions.TryGetValue(metadata.ControlType, out var description)
                ? LocalizationManager.Translate(description)
                : LocalizationManager.Translate("AI 图像与数据集工作台");
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
