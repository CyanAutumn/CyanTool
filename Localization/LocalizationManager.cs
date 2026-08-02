using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace CyanTools.Localization
{
    public enum AppLanguage
    {
        Chinese,
        English,
        Japanese
    }

    public sealed class LanguageOption
    {
        public LanguageOption(AppLanguage language, string displayName)
        {
            Language = language;
            DisplayName = displayName;
        }

        public AppLanguage Language { get; }
        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }

    public static class LocalizationManager
    {
        private static readonly DependencyProperty OriginalTextKeyProperty =
            DependencyProperty.RegisterAttached("OriginalTextKey", typeof(string), typeof(LocalizationManager));

        private static readonly DependencyProperty OriginalContentKeyProperty =
            DependencyProperty.RegisterAttached("OriginalContentKey", typeof(string), typeof(LocalizationManager));

        private static readonly DependencyProperty OriginalHeaderKeyProperty =
            DependencyProperty.RegisterAttached("OriginalHeaderKey", typeof(string), typeof(LocalizationManager));

        private static readonly DependencyProperty OriginalToolTipKeyProperty =
            DependencyProperty.RegisterAttached("OriginalToolTipKey", typeof(string), typeof(LocalizationManager));

        private static bool _classHandlerRegistered;
        private static AppLanguage _currentLanguage = AppLanguage.Chinese;
        private static List<DynamicTranslationPattern>? _dynamicTranslationPatterns;

        static LocalizationManager()
        {
            LocalizationSupplement.AddTo(Translations);
            LocalizationCompletenessSupplement.AddTo(Translations);
            LocalizationDialogsSupplement.AddTo(Translations);
        }

        public static event EventHandler? LanguageChanged;

        public static IReadOnlyList<LanguageOption> LanguageOptions { get; } =
        [
            new LanguageOption(AppLanguage.Chinese, "中文"),
            new LanguageOption(AppLanguage.English, "English"),
            new LanguageOption(AppLanguage.Japanese, "日本語")
        ];

        public static AppLanguage CurrentLanguage => _currentLanguage;

        public static void InitializeApplication()
        {
            ApplyCulture(_currentLanguage);

            if (_classHandlerRegistered)
            {
                return;
            }

            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    if (sender is DependencyObject element)
                    {
                        ApplyElement(element);
                    }
                }),
                true);

            _classHandlerRegistered = true;
        }

        public static void SetLanguage(AppLanguage language)
        {
            if (_currentLanguage == language)
            {
                return;
            }

            _currentLanguage = language;
            ApplyCulture(language);
            ApplyToOpenWindows();
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string Translate(string? source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source ?? string.Empty;
            }

            if (!Translations.TryGetValue(source, out var text))
            {
                return TranslateDynamic(source);
            }

            return _currentLanguage switch
            {
                AppLanguage.English => text.English,
                AppLanguage.Japanese => text.Japanese,
                _ => source
            };
        }

        public static bool HasTranslation(string? source)
            => !string.IsNullOrEmpty(source) && Translations.ContainsKey(source);

        public static string Format(string source, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Translate(source), args);
        }

        private static string TranslateDynamic(string source)
        {
            foreach (var pattern in GetDynamicTranslationPatterns())
            {
                var match = pattern.Regex.Match(source);
                if (!match.Success)
                {
                    continue;
                }

                var args = new object[match.Groups.Count - 1];
                for (var i = 1; i < match.Groups.Count; i++)
                {
                    args[i - 1] = Translate(match.Groups[i].Value);
                }

                var template = _currentLanguage switch
                {
                    AppLanguage.English => pattern.Text.English,
                    AppLanguage.Japanese => pattern.Text.Japanese,
                    _ => pattern.Source
                };

                return string.Format(CultureInfo.CurrentCulture, template, args);
            }

            return source;
        }

        private static IReadOnlyList<DynamicTranslationPattern> GetDynamicTranslationPatterns()
        {
            if (_dynamicTranslationPatterns != null)
            {
                return _dynamicTranslationPatterns;
            }

            var patterns = new List<DynamicTranslationPattern>();
            foreach (var (source, text) in Translations)
            {
                if (!source.Contains('{') || !source.Contains('}'))
                {
                    continue;
                }

                var regexPattern = BuildDynamicRegexPattern(source);
                if (regexPattern != null)
                {
                    patterns.Add(new DynamicTranslationPattern(
                        source,
                        text,
                        new Regex(regexPattern, RegexOptions.CultureInvariant)));
                }
            }

            _dynamicTranslationPatterns = patterns;
            return patterns;
        }

        private static string? BuildDynamicRegexPattern(string source)
        {
            var matches = Regex.Matches(source, @"\{[^{}]+\}");
            if (matches.Count == 0)
            {
                return null;
            }

            var pattern = "^";
            var position = 0;
            foreach (Match match in matches)
            {
                var placeholder = match.Value[1..^1].Split(':')[0];
                if (!int.TryParse(placeholder, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    return null;
                }

                pattern += Regex.Escape(source[position..match.Index]);
                pattern += "(.*?)";
                position = match.Index + match.Length;
            }

            pattern += Regex.Escape(source[position..]);
            pattern += "$";
            return pattern;
        }

        public static void ApplyTo(DependencyObject? root)
        {
            if (root == null)
            {
                return;
            }

            var children = GetVisualChildrenSnapshot(root);
            ApplyElement(root);

            foreach (var child in children)
            {
                ApplyTo(child);
            }
        }

        private static void ApplyElement(DependencyObject element)
        {
            if (element is FrameworkElement frameworkElement)
            {
                frameworkElement.Language = XmlLanguage.GetLanguage(GetCulture(_currentLanguage).IetfLanguageTag);
                LocalizeToolTip(frameworkElement);
            }

            if (element is Window window)
            {
                LocalizeWindowTitle(window);
            }

            if (element is TextBlock textBlock)
            {
                LocalizeTextBlock(textBlock);
            }

            if (element is HeaderedContentControl headeredContentControl)
            {
                LocalizeHeader(headeredContentControl);
            }

            if (element is HeaderedItemsControl headeredItemsControl)
            {
                LocalizeHeader(headeredItemsControl);
            }

            if (element is ContentControl contentControl)
            {
                LocalizeContent(contentControl);
            }

            if (element is DataGrid dataGrid)
            {
                LocalizeDataGridColumns(dataGrid);
            }

            if (element is ListView { View: GridView gridView })
            {
                LocalizeGridViewColumns(gridView);
            }
        }

        private static void LocalizeTextBlock(TextBlock textBlock)
        {
            if (LocalizeBoundString(textBlock, TextBlock.TextProperty))
            {
                return;
            }

            var hasLocalText = textBlock.ReadLocalValue(TextBlock.TextProperty) != DependencyProperty.UnsetValue ||
                textBlock.GetValue(OriginalTextKeyProperty) is string;
            if (hasLocalText)
            {
                var key = GetOrCreateOriginalKey(textBlock, OriginalTextKeyProperty, textBlock.Text);
                if (key != null)
                {
                    textBlock.Text = Translate(key);
                }

                return;
            }

            foreach (var inline in GetInlinesSnapshot(textBlock))
            {
                if (inline is not Run run)
                {
                    continue;
                }

                if (LocalizeBoundString(run, Run.TextProperty))
                {
                    continue;
                }

                if (!BindingOperations.IsDataBound(run, Run.TextProperty))
                {
                    var inlineKey = GetOrCreateOriginalKey(run, OriginalTextKeyProperty, run.Text);
                    if (inlineKey != null)
                    {
                        run.Text = Translate(inlineKey);
                    }
                }
            }
        }

        private static void LocalizeContent(ContentControl contentControl)
        {
            if (LocalizeBoundString(contentControl, ContentControl.ContentProperty))
            {
                return;
            }

            if (contentControl.Content is not string content)
            {
                return;
            }

            var key = GetOrCreateOriginalKey(contentControl, OriginalContentKeyProperty, content);
            if (key != null)
            {
                contentControl.Content = Translate(key);
            }
        }

        private static void LocalizeHeader(HeaderedContentControl control)
        {
            if (LocalizeBoundString(control, HeaderedContentControl.HeaderProperty))
            {
                return;
            }

            if (control.Header is not string header)
            {
                return;
            }

            var key = GetOrCreateOriginalKey(control, OriginalHeaderKeyProperty, header);
            if (key != null)
            {
                control.Header = Translate(key);
            }
        }

        private static void LocalizeHeader(HeaderedItemsControl control)
        {
            if (LocalizeBoundString(control, HeaderedItemsControl.HeaderProperty))
            {
                return;
            }

            if (control.Header is not string header)
            {
                return;
            }

            var key = GetOrCreateOriginalKey(control, OriginalHeaderKeyProperty, header);
            if (key != null)
            {
                control.Header = Translate(key);
            }
        }

        private static void LocalizeToolTip(FrameworkElement element)
        {
            if (LocalizeBoundString(element, FrameworkElement.ToolTipProperty))
            {
                return;
            }

            if (element.ToolTip is not string tooltip)
            {
                return;
            }

            var key = GetOrCreateOriginalKey(element, OriginalToolTipKeyProperty, tooltip);
            if (key != null)
            {
                element.ToolTip = Translate(key);
            }
        }

        private static void LocalizeWindowTitle(Window window)
        {
            if (LocalizeBoundString(window, Window.TitleProperty))
            {
                return;
            }

            var key = GetOrCreateOriginalKey(window, OriginalHeaderKeyProperty, window.Title);
            if (key != null)
            {
                window.Title = Translate(key);
            }
        }

        private static void LocalizeDataGridColumns(DataGrid dataGrid)
        {
            var columns = new List<DataGridColumn>(dataGrid.Columns);
            foreach (var column in columns)
            {
                if (LocalizeBoundString(column, DataGridColumn.HeaderProperty))
                {
                    continue;
                }

                if (column.Header is not string header)
                {
                    continue;
                }

                var key = GetOrCreateOriginalKey(column, OriginalHeaderKeyProperty, header);
                if (key != null)
                {
                    column.Header = Translate(key);
                }
            }
        }

        private static void LocalizeGridViewColumns(GridView gridView)
        {
            var columns = new List<GridViewColumn>(gridView.Columns);
            foreach (var column in columns)
            {
                if (LocalizeBoundString(column, GridViewColumn.HeaderProperty))
                {
                    continue;
                }

                if (column.Header is not string header)
                {
                    continue;
                }

                var key = GetOrCreateOriginalKey(column, OriginalHeaderKeyProperty, header);
                if (key != null)
                {
                    column.Header = Translate(key);
                }
            }
        }

        private static string? GetOrCreateOriginalKey(DependencyObject owner, DependencyProperty property, string? current)
        {
            if (owner.GetValue(property) is string existing)
            {
                return existing;
            }

            if (string.IsNullOrWhiteSpace(current))
            {
                return null;
            }

            owner.SetValue(property, current);
            return current;
        }

        private static bool LocalizeBoundString(DependencyObject owner, DependencyProperty property)
        {
            if (!BindingOperations.IsDataBound(owner, property))
            {
                return false;
            }

            UpdateBindingTarget(owner, property);
            return true;
        }

        private static void UpdateBindingTarget(DependencyObject owner, DependencyProperty property)
        {
            if (BindingOperations.GetBindingExpression(owner, property) is BindingExpression binding)
            {
                binding.UpdateTarget();
                return;
            }

            if (BindingOperations.GetMultiBindingExpression(owner, property) is MultiBindingExpression multiBinding)
            {
                multiBinding.UpdateTarget();
                return;
            }

            BindingOperations.GetPriorityBindingExpression(owner, property)?.UpdateTarget();
        }

        private static void ApplyToOpenWindows()
        {
            Application? application = Application.Current;
            if (application == null)
            {
                return;
            }

            void Apply()
            {
                var windows = new List<Window>();
                foreach (Window window in application.Windows)
                {
                    windows.Add(window);
                }

                foreach (Window window in windows)
                {
                    ApplyTo(window);
                }
            }

            if (application.Dispatcher.CheckAccess())
            {
                Apply();
            }
            else
            {
                application.Dispatcher.Invoke(Apply);
            }
        }

        private static List<DependencyObject> GetVisualChildrenSnapshot(DependencyObject root)
        {
            var children = new List<DependencyObject>();
            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                children.Add(VisualTreeHelper.GetChild(root, i));
            }

            return children;
        }

        private static List<Inline> GetInlinesSnapshot(TextBlock textBlock)
        {
            var inlines = new List<Inline>();
            foreach (var inline in textBlock.Inlines)
            {
                inlines.Add(inline);
            }

            return inlines;
        }

        private static void ApplyCulture(AppLanguage language)
        {
            var culture = GetCulture(language);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        private static CultureInfo GetCulture(AppLanguage language)
        {
            return language switch
            {
                AppLanguage.English => CultureInfo.GetCultureInfo("en-US"),
                AppLanguage.Japanese => CultureInfo.GetCultureInfo("ja-JP"),
                _ => CultureInfo.GetCultureInfo("zh-CN")
            };
        }

        internal readonly record struct LocalizedText(string English, string Japanese);

        private sealed record DynamicTranslationPattern(string Source, LocalizedText Text, Regex Regex);

        private static readonly Dictionary<string, LocalizedText> Translations = new(StringComparer.Ordinal)
        {
            ["语言"] = new("Language", "言語"),
            ["白天"] = new("Day", "昼"),
            ["夜间"] = new("Night", "夜"),
            ["切换白天/夜间模式"] = new("Switch day/night mode", "昼/夜モードを切り替え"),
            ["最小化"] = new("Minimize", "最小化"),
            ["最大化/还原"] = new("Maximize/restore", "最大化/元に戻す"),
            ["关闭"] = new("Close", "閉じる"),
            ["提示"] = new("Notice", "通知"),
            ["错误"] = new("Error", "エラー"),
            ["页面加载失败: {0}\n{1}\n\n{2}"] = new("Failed to load page: {0}\n{1}\n\n{2}", "ページの読み込みに失敗しました: {0}\n{1}\n\n{2}"),
            ["AI 图像与数据集工作台"] = new("AI image and dataset workbench", "AI画像とデータセットの作業台"),
            ["管道就绪"] = new("Pipeline Ready", "パイプライン準備完了"),
            ["Modernized with WPF UI"] = new("Modernized with WPF UI", "WPF UIでモダン化"),
            ["本地图片、数据集、标签与训练准备的一站式工作台"] = new("A local workbench for images, datasets, tagging, and training prep", "画像、データセット、タグ付け、学習準備のためのローカル作業台"),
            ["图像批处理"] = new("Image Batch Processing", "画像バッチ処理"),
            ["数据集管理"] = new("Dataset Management", "データセット管理"),
            ["Cyan Core"] = new("Cyan Core", "Cyan Core"),
            ["支持白天 / 夜间"] = new("Light / dark ready", "昼/夜モード対応"),
            ["图片增强"] = new("Image Enhancement", "画像強化"),
            ["超分、缩放、格式转换、透明通道处理"] = new("Upscaling, resizing, format conversion, and transparent-channel processing", "アップスケール、リサイズ、形式変換、透明チャンネル処理"),
            ["数据集"] = new("Dataset", "データセット"),
            ["下载、整理、打标、Prompt 文件管理"] = new("Download, organize, tag, and manage prompt files", "ダウンロード、整理、タグ付け、Promptファイル管理"),
            ["自动化"] = new("Automation", "自動化"),
            ["节点管道、执行顺序、日志追踪"] = new("Node pipelines, run order, and log tracing", "ノードパイプライン、実行順序、ログ追跡"),
            ["AI 辅助"] = new("AI Assistance", "AI支援"),
            ["Gemini 图像生成与 LLM 工具联动"] = new("Gemini image generation and LLM tool workflows", "Gemini画像生成とLLMツールの連携"),
            ["工作流方向"] = new("Workflow Direction", "ワークフローの方向"),
            ["从输入目录到输出目录，各工具可被管道串联，减少重复点选。"] = new("Connect tools from input folders to output folders in pipelines to reduce repeated clicks.", "入力フォルダーから出力フォルダーまでツールをパイプラインでつなぎ、繰り返し操作を減らします。"),
            ["视觉状态"] = new("Visual State", "表示状態"),
            ["白天模式更清爽，夜间模式压低亮度，长时间整理图片时更舒服。"] = new("Light mode stays crisp; dark mode lowers brightness for longer image-sorting sessions.", "昼モードは明るく、夜モードは輝度を抑えて長時間の画像整理を楽にします。"),
            ["本地优先"] = new("Local First", "ローカル優先"),
            ["核心处理仍在本地完成，界面升级不改变原有处理逻辑。"] = new("Core processing still runs locally; the UI refresh does not change existing processing logic.", "主要処理は引き続きローカルで実行され、UI更新は既存ロジックを変更しません。"),
            ["免责声明"] = new("Disclaimer", "免責事項"),
            ["本工具仅供学习与研究使用，使用过程中产生的任何后果均由使用者自行承担，与作者无关。"] = new("This tool is for learning and research only. Users are responsible for any consequences of use.", "本ツールは学習と研究目的のみです。使用による結果は利用者自身の責任です。"),
            ["鸣谢"] = new("Credits", "謝辞"),
            [" - 视频教程制作与分辨率归类思路"] = new(" - Video tutorials and resolution bucketing ideas", " - 動画チュートリアルと解像度分類のアイデア"),
            [" - 打标模型来源"] = new(" - Tagging model source", " - タグ付けモデルの提供元"),
            [" - 图片三分法与打标模型来源"] = new(" - Three-stage image method and tagging model source", " - 三段階画像処理とタグ付けモデルの提供元"),
            [" - 图片放大项目"] = new(" - Image upscaling project", " - 画像拡大プロジェクト"),
            [" - 图片超分项目"] = new(" - Image super-resolution project", " - 画像超解像プロジェクト"),
            [" - 图片聚类模型来源"] = new(" - Image clustering model source", " - 画像クラスタリングモデルの提供元"),
            [" - 高斯模糊反下毒思路"] = new(" - Gaussian-blur anti-poisoning idea", " - ガウスぼかしによる対策アイデア"),

            ["主页"] = new("Home", "ホーム"),
            ["训练"] = new("Training", "学習"),
            ["数据集获取"] = new("Dataset Acquisition", "データセット取得"),
            ["数据集归类"] = new("Dataset Classification", "データセット分類"),
            ["数据集处理"] = new("Dataset Processing", "データセット処理"),
            ["环境设置"] = new("Environment Settings", "環境設定"),
            ["LLM工具"] = new("LLM Tools", "LLMツール"),
            ["图片超分"] = new("Image Upscaling", "画像アップスケール"),
            ["图片上下采"] = new("Image Scaling", "画像スケーリング"),
            ["图片处理"] = new("Image Processing", "画像処理"),
            ["数据集管理器"] = new("Dataset Manager", "データセット管理"),
            ["模型训练"] = new("Model Training", "モデル学習"),
            ["LoRA训练"] = new("LoRA Training", "LoRA学習"),
            ["图片分辨率归类"] = new("Resolution Bucketing", "解像度分類"),
            ["图片三分法"] = new("Three-stage Images", "三段階画像処理"),
            ["分辨率筛选"] = new("Resolution Filter", "解像度フィルター"),
            ["手指完整性筛选"] = new("Hand Integrity Filter", "手指完全性フィルター"),
            ["保留指定文件"] = new("Keep Selected Files", "指定ファイル保持"),
            ["文件夹处理"] = new("Folder Processing", "フォルダー処理"),
            ["导出视频帧"] = new("Export Video Frames", "動画フレーム書き出し"),
            ["图片元数据"] = new("Image Metadata", "画像メタデータ"),
            ["高斯模糊"] = new("Gaussian Blur", "ガウスぼかし"),
            ["图片聚类"] = new("Image Clustering", "画像クラスタリング"),
            ["同源图片归类"] = new("Same-source Grouping", "同一ソース画像分類"),
            ["格式转换"] = new("Format Conversion", "形式変換"),
            ["转换透明通道"] = new("Transparent Channel Conversion", "透明チャンネル変換"),
            ["图片打标"] = new("Image Tagging", "画像タグ付け"),
            ["训练集配平"] = new("Training Set Balancer", "学習セット均衡化"),
            ["XYZ工具"] = new("XYZ Tool", "XYZツール"),
            ["图片重命名"] = new("Image Rename", "画像リネーム"),
            ["数据集下载器"] = new("Dataset Downloader", "データセットダウンローダー"),
            ["Gemini图像生成"] = new("Gemini Image Generation", "Gemini画像生成"),
            ["选项"] = new("Options", "オプション"),
            ["训练输出整理"] = new("Training Output Organizer", "学習出力整理"),

            ["路径"] = new("Paths", "パス"),
            ["路径选择"] = new("Path Selection", "パス選択"),
            ["路径设置"] = new("Path Settings", "パス設定"),
            ["输入"] = new("Input", "入力"),
            ["输出"] = new("Output", "出力"),
            ["输入路径"] = new("Input Path", "入力パス"),
            ["输出路径"] = new("Output Path", "出力パス"),
            ["输入目录"] = new("Input Folder", "入力フォルダー"),
            ["输出目录"] = new("Output Folder", "出力フォルダー"),
            ["输入文件夹"] = new("Input Folder", "入力フォルダー"),
            ["输出文件夹"] = new("Output Folder", "出力フォルダー"),
            ["选择输入路径"] = new("Choose input path", "入力パスを選択"),
            ["选择输出路径"] = new("Choose output path", "出力パスを選択"),
            ["选择工作目录"] = new("Choose working folder", "作業フォルダーを選択"),
            ["选择目录"] = new("Choose Folder", "フォルダーを選択"),
            ["选择文件夹"] = new("Choose Folder", "フォルダーを選択"),
            ["选择视频"] = new("Choose Video", "動画を選択"),
            ["浏览"] = new("Browse", "参照"),
            ["浏览..."] = new("Browse...", "参照..."),
            ["自动识别"] = new("Auto Detect", "自動検出"),
            ["添加"] = new("Add", "追加"),
            ["取消"] = new("Cancel", "キャンセル"),
            ["确定"] = new("OK", "OK"),
            ["确认"] = new("Confirm", "確認"),
            ["应用"] = new("Apply", "適用"),
            ["保存"] = new("Save", "保存"),
            ["删除"] = new("Delete", "削除"),
            ["移除"] = new("Remove", "削除"),
            ["配置"] = new("Configure", "設定"),
            ["清空"] = new("Clear", "クリア"),
            ["清空结果"] = new("Clear Results", "結果をクリア"),
            ["清空日志"] = new("Clear Log", "ログをクリア"),
            ["恢复默认"] = new("Restore Defaults", "既定値に戻す"),
            ["开始"] = new("Start", "開始"),
            ["停止"] = new("Stop", "停止"),
            ["运行"] = new("Run", "実行"),
            ["刷新"] = new("Refresh", "更新"),
            ["复制"] = new("Copy", "コピー"),
            ["关闭窗口"] = new("Close Window", "ウィンドウを閉じる"),
            ["打开文件夹"] = new("Open Folder", "フォルダーを開く"),
            ["打开结果目录"] = new("Open Result Folder", "結果フォルダーを開く"),
            ["新建/修改"] = new("Create/Update", "新規/変更"),
            ["随机复制"] = new("Random Copy", "ランダムコピー"),
            ["自增添加"] = new("Add Incrementally", "連番で追加"),

            ["执行"] = new("Run", "実行"),
            ["日志"] = new("Log", "ログ"),
            ["操作日志"] = new("Operation Log", "操作ログ"),
            ["处理日志"] = new("Processing Log", "処理ログ"),
            ["执行日志"] = new("Run Log", "実行ログ"),
            ["输出日志"] = new("Output Log", "出力ログ"),
            ["日志和结果"] = new("Log and Results", "ログと結果"),
            ["功能"] = new("Function", "機能"),
            ["功能设置"] = new("Function Settings", "機能設定"),
            ["参数"] = new("Parameters", "パラメーター"),
            ["模糊参数"] = new("Blur Parameters", "ぼかしパラメーター"),
            ["聚类参数"] = new("Clustering Parameters", "クラスタリングパラメーター"),
            ["UMAP 参数"] = new("UMAP Parameters", "UMAPパラメーター"),
            ["筛选条件"] = new("Filter Conditions", "フィルター条件"),
            ["模型设置"] = new("Model Settings", "モデル設定"),
            ["筛选设置"] = new("Filter Settings", "フィルター設定"),
            ["处理选项"] = new("Processing Options", "処理オプション"),
            ["缩放选项"] = new("Scaling Options", "スケーリングオプション"),
            ["格式"] = new("Format", "形式"),
            ["透明通道填充"] = new("Transparent Channel Fill", "透明チャンネル塗りつぶし"),
            ["计算设备"] = new("Compute Device", "計算デバイス"),
            ["设备"] = new("Device", "デバイス"),
            ["引擎"] = new("Engine", "エンジン"),
            ["选择工具"] = new("Choose Tool", "ツールを選択"),
            ["选择模型"] = new("Choose Model", "モデルを選択"),
            ["模型"] = new("Model", "モデル"),
            ["打标模型"] = new("Tagging Model", "タグ付けモデル"),
            ["模型连接"] = new("Model Connection", "モデル接続"),
            ["生成设置"] = new("Generation Settings", "生成設定"),
            ["配置与模型"] = new("Config and Model", "設定とモデル"),
            ["提示词"] = new("Prompt", "プロンプト"),
            ["配置名"] = new("Config Name", "設定名"),
            ["状态"] = new("Status", "状態"),
            ["素材图片"] = new("Source Images", "素材画像"),
            ["生成结果"] = new("Generated Results", "生成結果"),
            ["添加图片"] = new("Add Images", "画像を追加"),
            ["拖入素材或点击添加图片"] = new("Drop images here or click Add Images", "素材をドロップ、または画像を追加"),
            ["还没有生成结果"] = new("No generated results yet", "生成結果はまだありません"),
            ["移除"] = new("Remove", "削除"),
            ["尺寸"] = new("Size", "サイズ"),
            ["宽高比"] = new("Aspect Ratio", "アスペクト比"),
            ["批量间隔"] = new("Batch Interval", "バッチ間隔"),
            ["秒"] = new("sec", "秒"),
            ["张数"] = new("Count", "枚数"),
            ["开始生成"] = new("Generate", "生成"),
            ["批量生成"] = new("Batch Generate", "一括生成"),

            ["开始处理"] = new("Start Processing", "処理開始"),
            ["停止处理"] = new("Stop Processing", "処理停止"),
            ["开始转换"] = new("Start Conversion", "変換開始"),
            ["开始归类"] = new("Start Grouping", "分類開始"),
            ["开始筛选"] = new("Start Filtering", "フィルター開始"),
            ["开始超分"] = new("Start Upscaling", "アップスケール開始"),
            ["开始打标"] = new("Start Tagging", "タグ付け開始"),
            ["开始整理"] = new("Start Organizing", "整理開始"),
            ["一键配平"] = new("Balance", "均衡化"),
            ["添加节点"] = new("Add Node", "ノードを追加"),
            ["前移"] = new("Move Up", "上へ"),
            ["后移"] = new("Move Down", "下へ"),
            ["工作流"] = new("Workflow", "ワークフロー"),
            ["可用节点"] = new("Available Nodes", "利用可能なノード"),
            ["执行顺序"] = new("Run Order", "実行順序"),

            ["执行完成后清空输入路径文件夹"] = new("Clear the input folder after a successful run", "実行完了後に入力フォルダーを空にする"),
            ["执行成功后会删除输入路径文件夹中的内容，并自动保留输出路径。"] = new("After success, contents in the input folder are deleted while the output path is kept.", "成功後、入力フォルダー内の内容を削除し、出力パスは保持します。"),
            ["工作流模式下输出路径自动生成：父级目录/NN-节点名"] = new("In workflow mode, output is generated as: parent folder/NN-node name.", "ワークフローモードでは、出力パスは 親フォルダー/NN-ノード名 で自動生成されます。"),
            ["工作流模式下输出路径自动生成。"] = new("In workflow mode, the output path is generated automatically.", "ワークフローモードでは出力パスが自動生成されます。"),
            ["输出路径留空时默认为输入路径。"] = new("When empty, output defaults to the input path.", "空の場合、出力は入力パスになります。"),
            ["工作流中此输入路径固定为上一个节点的输出路径；如果上一个节点没有输出路径，则沿用其输入路径。"] = new("In workflow mode, this input path is fixed to the previous node output; if unavailable, it uses that node's input path.", "ワークフローでは、この入力パスは前ノードの出力パスに固定されます。出力がない場合は前ノードの入力パスを使います。"),
            ["工作流中此输入路径固定为上一个节点的输出路径。"] = new("In workflow mode, this input path is fixed to the previous node output.", "ワークフローでは、この入力パスは前ノードの出力パスに固定されます。"),
            ["覆盖已存在文件"] = new("Overwrite existing files", "既存ファイルを上書き"),
            ["包含子目录"] = new("Include subfolders", "サブフォルダーを含める"),
            ["同时处理同名txt"] = new("Process matching txt files", "同名txtも処理"),
            ["完成后打开目录"] = new("Open folder when done", "完了後にフォルダーを開く"),
            ["启用调试输出"] = new("Enable debug output", "デバッグ出力を有効化"),
            ["启用"] = new("Enable", "有効"),
            ["启动"] = new("Enable", "有効化"),
            ["保留原始标签"] = new("Keep original tags", "元のタグを保持"),

            ["视频路径"] = new("Video Path", "動画パス"),
            ["视频与输出"] = new("Video and Output", "動画と出力"),
            ["帧预览"] = new("Frame Preview", "フレームプレビュー"),
            ["时间轴"] = new("Timeline", "タイムライン"),
            ["导出"] = new("Export", "書き出し"),
            ["帧间隔"] = new("Frame Interval", "フレーム間隔"),
            ["上一帧"] = new("Previous Frame", "前のフレーム"),
            ["下一帧"] = new("Next Frame", "次のフレーム"),
            ["输出当前帧"] = new("Export Current Frame", "現在のフレームを書き出し"),
            ["输出所有帧"] = new("Export All Frames", "全フレームを書き出し"),
            ["导出为 GIF"] = new("Export as GIF", "GIFとして書き出し"),

            ["数据列表"] = new("Data List", "データ一覧"),
            ["分类目录"] = new("Category Folders", "分類フォルダー"),
            ["内容"] = new("Content", "内容"),
            ["控制"] = new("Controls", "操作"),
            ["自动下一张"] = new("Auto Next", "自動で次へ"),
            ["浏览模式"] = new("Browse Mode", "閲覧モード"),
            ["保存所有更改"] = new("Save All Changes", "すべての変更を保存"),
            ["勾选后“保存所有更改”才会处理对应分类"] = new("Checked categories are processed when saving all changes.", "チェックした分類は「すべての変更を保存」で処理されます。"),
            ["翻译"] = new("Translation", "翻訳"),
            ["翻译未翻译"] = new("Translate Missing", "未翻訳を翻訳"),
            ["移到最前"] = new("Move to First", "先頭へ移動"),
            ["移到最后"] = new("Move to Last", "最後へ移動"),
            ["移到最前(数据集)"] = new("Move to First (Dataset)", "先頭へ移動(データセット)"),
            ["移到最后(数据集)"] = new("Move to Last (Dataset)", "最後へ移動(データセット)"),
            ["删除(数据集)"] = new("Delete (Dataset)", "削除(データセット)"),
            ["添加(数据集)"] = new("Add (Dataset)", "追加(データセット)"),
            ["打乱提示词（数据集）"] = new("Shuffle Prompts (Dataset)", "プロンプトをシャッフル(データセット)"),

            ["标签与阈值"] = new("Tags and Thresholds", "タグとしきい値"),
            ["自定义标签（逗号分隔）"] = new("Custom tags (comma separated)", "カスタムタグ(カンマ区切り)"),
            ["前置标签（逗号分隔）"] = new("Priority tags (comma separated)", "前置タグ(カンマ区切り)"),
            ["同名 txt"] = new("Existing txt", "同名txt"),
            ["通用阈值"] = new("General Threshold", "一般しきい値"),
            ["角色阈值"] = new("Character Threshold", "キャラクターしきい値"),
            ["优先使用 GPU(CUDA)"] = new("Prefer GPU (CUDA)", "GPU(CUDA)を優先"),
            ["评级标签"] = new("Rating Tags", "レーティングタグ"),
            ["质量标签"] = new("Quality Tags", "品質タグ"),
            ["通用标签"] = new("General Tags", "一般タグ"),
            ["角色标签"] = new("Character Tags", "キャラクタータグ"),
            ["作者标签"] = new("Artist Tags", "作者タグ"),
            ["版权标签"] = new("Copyright Tags", "著作権タグ"),
            ["元信息标签"] = new("Meta Tags", "メタタグ"),
            ["模型标签"] = new("Model Tags", "モデルタグ"),
            ["检查/安装 CUDA"] = new("Check/Install CUDA", "CUDAを確認/インストール"),
            ["检查/安装CUDA"] = new("Check/Install CUDA", "CUDAを確認/インストール"),
            ["安装 CUDA Toolkit"] = new("Install CUDA Toolkit", "CUDA Toolkitをインストール"),
            ["CUDA/cuDNN 页面"] = new("CUDA/cuDNN Pages", "CUDA/cuDNNページ"),
            ["CUDA / cuDNN"] = new("CUDA / cuDNN", "CUDA / cuDNN"),
            ["管理本机运行环境与共享工具设置。"] = new("Manage local runtime and shared tool settings.", "ローカル実行環境と共有ツール設定を管理します。"),
            ["GPU 推理依赖 NVIDIA 驱动、CUDA 12.x、cuDNN 9，以及带 CUDA Provider 的 ONNX Runtime。"] = new("GPU inference depends on the NVIDIA driver, CUDA 12.x, cuDNN 9, and ONNX Runtime with the CUDA Provider.", "GPU推論にはNVIDIAドライバー、CUDA 12.x、cuDNN 9、CUDA Provider付きONNX Runtimeが必要です。"),
            ["GPU 推理依赖 NVIDIA 驱动、CUDA 12.x、cuDNN 9，以及带 CUDA Provider 的 ONNX Runtime。cuDNN 可拖入 zip 自动复制到程序目录或 CUDA 12 bin。"] = new("GPU inference depends on the NVIDIA driver, CUDA 12.x, cuDNN 9, and ONNX Runtime with the CUDA Provider. Drop a cuDNN zip here to copy it into the app folder or CUDA 12 bin.", "GPU推論にはNVIDIAドライバー、CUDA 12.x、cuDNN 9、CUDA Provider付きONNX Runtimeが必要です。cuDNN zipをドロップするとアプリフォルダーまたはCUDA 12 binへコピーします。"),
            ["拖入 cuDNN zip 安装包"] = new("Drop cuDNN zip package", "cuDNN zipパッケージをドロップ"),
            ["选择 zip"] = new("Choose zip", "zipを選択"),
            ["NVIDIA 驱动应用"] = new("NVIDIA Driver App", "NVIDIAドライバーアプリ"),
            ["重新检测"] = new("Check Again", "再検出"),
            ["添加打标器"] = new("Add Tagger", "タグ付けモデルを追加"),

            ["名称："] = new("Name:", "名前:"),
            ["模型文件（.onnx）："] = new("Model file (.onnx):", "モデルファイル(.onnx):"),
            ["标签映射（.csv/.json）："] = new("Tag mapping (.csv/.json):", "タグマッピング(.csv/.json):"),
            ["输入通道："] = new("Input channels:", "入力チャンネル:"),
            ["输入尺寸："] = new("Input size:", "入力サイズ:"),

            ["模型连接"] = new("Model Connection", "モデル接続"),
            ["API 地址"] = new("API URL", "APIアドレス"),
            ["工作目录"] = new("Working Folder", "作業フォルダー"),
            ["路径 1"] = new("Path 1", "パス1"),
            ["路径 2"] = new("Path 2", "パス2"),
            ["路径 3"] = new("Path 3", "パス3"),
            ["路径 4"] = new("Path 4", "パス4"),
            ["路径 5"] = new("Path 5", "パス5"),
            ["当前任务：{0}"] = new("Current task: {0}", "現在のタスク: {0}"),

            ["分辨率筛选"] = new("Resolution Filter", "解像度フィルター"),
            ["最小高度"] = new("Minimum Height", "最小高さ"),
            ["最小宽度"] = new("Minimum Width", "最小幅"),
            ["最小分辨率"] = new("Minimum Resolution", "最小解像度"),
            ["删除指定高度以下的图片"] = new("Delete images below the specified height", "指定高さ未満の画像を削除"),
            ["删除指定宽度以下的图片"] = new("Delete images below the specified width", "指定幅未満の画像を削除"),
            ["删除指定分辨率以下的图片"] = new("Delete images below the specified resolution", "指定解像度未満の画像を削除"),

            ["放大倍数"] = new("Scale", "拡大倍率"),
            ["选择 GPU"] = new("Choose GPU", "GPUを選択"),
            ["瓦片大小"] = new("Tile Size", "タイルサイズ"),
            ["0 为不分块"] = new("0 means no tiling", "0は分割なし"),
            ["降噪等级"] = new("Denoise Level", "ノイズ除去レベル"),
            ["裁剪大小"] = new("Crop Size", "クロップサイズ"),
            ["使用照片模型"] = new("Use photo model", "写真モデルを使用"),
            ["Real-ESRGAN 与 waifu2x 共用同一执行入口，当前选择会决定下方参数面板。"] = new("Real-ESRGAN and waifu2x share the same run entry; the current choice controls the parameter panel below.", "Real-ESRGAN と waifu2x は同じ実行入口を共有し、選択に応じて下のパラメーターパネルが変わります。"),

            ["图片三分法处理"] = new("Three-stage Image Processing", "三段階画像処理"),
            ["人物、半身、头部与眼部裁切参数集中配置。"] = new("Configure person, half-body, head, and eye crop parameters in one place.", "人物、半身、頭部、目のクロップ設定をまとめて調整します。"),
            ["人物检测"] = new("Person Detection", "人物検出"),
            ["半身检测"] = new("Half-body Detection", "半身検出"),
            ["头部检测"] = new("Head Detection", "頭部検出"),
            ["眼部检测"] = new("Eye Detection", "目検出"),
            ["其他选项"] = new("Other Options", "その他のオプション"),
            ["置信度阈值"] = new("Confidence Threshold", "信頼度しきい値"),
            ["追加Tag"] = new("Append Tag", "タグを追加"),
            ["头部缩放"] = new("Head Scale", "頭部スケール"),
            ["眼部缩放"] = new("Eye Scale", "目スケール"),
            ["启用眼部检测"] = new("Enable eye detection", "目検出を有効化"),

            ["保留的文件类型"] = new("File Types to Keep", "保持するファイル種別"),
            [".txt(中文内容)"] = new(".txt (Chinese content)", ".txt(中国語内容)"),
            ["命名规则"] = new("Naming Rules", "命名規則"),
            ["重命名预览"] = new("Rename Preview", "リネームプレビュー"),
            ["序号"] = new("No.", "番号"),
            ["原文件名"] = new("Original Name", "元のファイル名"),
            ["新文件名"] = new("New Name", "新しいファイル名"),
            ["状态"] = new("Status", "状態"),
            ["训练集路径与操作"] = new("Training Set Path and Actions", "学習セットのパスと操作"),
            ["子文件夹配平"] = new("Subfolder Balancing", "サブフォルダー均衡化"),
            ["子文件夹名"] = new("Subfolder Name", "サブフォルダー名"),
            ["图片数量"] = new("Image Count", "画像数"),
            ["重复次数 (Repet)"] = new("Repeats (Repet)", "繰り返し回数(Repet)"),
            ["训练步数占比 (%)"] = new("Training Step Share (%)", "学習ステップ比率(%)"),
            ["脚本与配置"] = new("Scripts and Config", "スクリプトと設定"),
            ["重置默认参数"] = new("Reset Defaults", "既定値に戻す"),
            ["仅生成 TOML"] = new("Generate TOML Only", "TOMLのみ生成"),
            ["生成并开始训练"] = new("Generate and Start Training", "生成して学習開始"),
            ["开始训练后关闭本软件"] = new("Close this app after training starts", "学習開始後にこのアプリを閉じる"),
            ["LoRA epoch 文件整理"] = new("LoRA Epoch File Organizer", "LoRA epochファイル整理"),
            ["处理日志"] = new("Processing Log", "処理ログ"),
            ["XYZ 序号提示词"] = new("XYZ Sequence Prompt", "XYZ連番プロンプト"),
            ["开始"] = new("Start", "開始"),
            ["结束"] = new("End", "終了"),
            ["间隔"] = new("Interval", "間隔")
        };
    }
}
