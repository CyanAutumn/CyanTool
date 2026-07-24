using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CyanTools.Pipeline;

namespace CyanTools.ViewModel
{
    public enum PipelineParameterEditorKind
    {
        Text,
        Password,
        Boolean,
        Choice
    }

    public sealed class PipelineNodeParameterOption
    {
        public PipelineNodeParameterOption(object? value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public object? Value { get; }
        public string DisplayName { get; }
    }

    public sealed class PipelineNodeParameterViewModel : INotifyPropertyChanged
    {
        private readonly object _target;
        private readonly PropertyInfo _property;
        private readonly bool _isReadOnlyWhenInputInherited;
        private readonly string? _isEnabledPropertyName;
        private string _textValue = string.Empty;

        internal PipelineNodeParameterViewModel(
            object target,
            PropertyInfo property,
            string label,
            IReadOnlyList<PipelineNodeParameterOption> options,
            ICommand? browseCommand,
            bool isPassword,
            bool isReadOnlyWhenInputInherited,
            string? isEnabledPropertyName)
        {
            _target = target;
            _property = property;
            _isReadOnlyWhenInputInherited = isReadOnlyWhenInputInherited;
            _isEnabledPropertyName = isEnabledPropertyName;
            Label = label;
            Options = options;
            BrowseCommand = browseCommand;
            EditorKind = isPassword
                ? PipelineParameterEditorKind.Password
                : options.Count > 0
                    ? PipelineParameterEditorKind.Choice
                    : GetUnderlyingType(property.PropertyType) == typeof(bool)
                        ? PipelineParameterEditorKind.Boolean
                        : PipelineParameterEditorKind.Text;
            Refresh();
        }

        public string PropertyName => _property.Name;
        public string Label { get; }
        public PipelineParameterEditorKind EditorKind { get; }
        public IReadOnlyList<PipelineNodeParameterOption> Options { get; }
        public object? CurrentValue => _property.GetValue(_target);
        public ICommand? BrowseCommand { get; }
        public bool HasBrowseCommand => BrowseCommand != null;
        public bool IsReadOnly => _isReadOnlyWhenInputInherited && IsInputInherited();
        public bool IsBrowseEnabled => !IsReadOnly && IsEnabled;
        public bool IsEnabled => string.IsNullOrWhiteSpace(_isEnabledPropertyName)
            || _target.GetType().GetProperty(_isEnabledPropertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(_target) is not bool enabled
            || enabled;

        public string TextValue
        {
            get => _textValue;
            set
            {
                if (_textValue == value)
                {
                    return;
                }

                _textValue = value ?? string.Empty;
                OnPropertyChanged();
                TrySetTextValue(_textValue);
            }
        }

        public bool BooleanValue
        {
            get => _property.GetValue(_target) as bool? ?? false;
            set
            {
                if (BooleanValue == value)
                {
                    return;
                }

                _property.SetValue(_target, value);
                OnPropertyChanged();
            }
        }

        public PipelineNodeParameterOption? SelectedOption
        {
            get
            {
                object? current = _property.GetValue(_target);
                return Options.FirstOrDefault(option => Equals(option.Value, current));
            }
            set
            {
                if (value == null || Equals(_property.GetValue(_target), value.Value))
                {
                    return;
                }

                _property.SetValue(_target, value.Value);
                OnPropertyChanged();
            }
        }

        public void Refresh()
        {
            object? value = _property.GetValue(_target);
            _textValue = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
            OnPropertyChanged(nameof(TextValue));
            OnPropertyChanged(nameof(BooleanValue));
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(IsBrowseEnabled));
            OnPropertyChanged(nameof(IsEnabled));
        }

        private bool IsInputInherited()
        {
            return _target.GetType().GetProperty("HasInheritedInputPath", BindingFlags.Public | BindingFlags.Instance)?.GetValue(_target) is true;
        }

        private void TrySetTextValue(string value)
        {
            Type targetType = GetUnderlyingType(_property.PropertyType);
            try
            {
                object? converted = targetType == typeof(string)
                    ? value
                    : TypeDescriptor.GetConverter(targetType).ConvertFromString(null, CultureInfo.CurrentCulture, value);

                if (converted != null)
                {
                    _property.SetValue(_target, converted);
                }
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
            {

            }
        }

        private static Type GetUnderlyingType(Type type)
        {
            return Nullable.GetUnderlyingType(type) ?? type;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class PipelineNodeActionViewModel
    {
        internal PipelineNodeActionViewModel(string label, ICommand command)
        {
            Label = label;
            Command = command;
        }

        public string Label { get; }
        public ICommand Command { get; }
    }





    public static class PipelineNodeParameterFactory
    {
        private static readonly IReadOnlyDictionary<Type, IReadOnlyList<PipelineNodeParameterDefinition>> Definitions =
            new Dictionary<Type, IReadOnlyList<PipelineNodeParameterDefinition>>
            {
                [typeof(FolderPickerPipelineNode)] =
                [
                    P("FolderPath", "输入文件夹", browseCommand: "SelectFolderCommand")
                ],
                [typeof(OutputFolderPickerPipelineNode)] =
                [
                    P("OutputFolderPath", "输出文件夹", browseCommand: "SelectOutputFolderCommand")
                ],
                [typeof(ImageClusteringViewModel)] =
                [
                    P("SelectedFeatureType", "特征类型", "FeatureTypes"),
                    P("SelectedAlgorithm", "聚类算法", "Algorithms"),
                    P("SelectedClusteringSpace", "聚类空间", "ClusteringSpaces"),
                    P("SelectedComputeDevice", "计算设备", "ComputeDevices"),
                    P("NumClusters", "聚类数量"),
                    P("UseBestKBySilhouette", "自动选择最佳 K"),
                    P("MinClusterSize", "最小聚类大小"),
                    P("FilterOutliersForExport", "导出时过滤离群点"),
                    P("EnableTwoStageStyleClustering", "两阶段风格聚类"),
                    P("UseMinSimilarityForUncertain", "最低相似度兜底"),
                    P("MinCentroidSimilarity", "最低中心相似度"),
                    P("UmapNNeighbors", "UMAP 邻居数"),
                    P("UmapMinDist", "UMAP 最小距离"),
                    P("UmapSpread", "UMAP 展开度"),
                    P("ShouldClearInputDirectoryAfterCompletion", "完成后清空输入")
                ],
                [typeof(RealEsrganViewModel)] =
                [
                    P("SelectedTool", "超分工具", "AvailableTools"),
                    P("SelectedRealEsrganModel", "Real-ESRGAN 模型", "RealEsrganModels"),
                    P("SelectedScale", "放大倍数", "Scales"),
                    P("SelectedGpu", "GPU", "Gpus"),
                    P("TileSize", "分块大小"),
                    P("SelectedWaifu2xNoiseLevel", "降噪等级", "Waifu2xNoiseLevels"),
                    P("Waifu2xUsePhotoModel", "使用照片模型"),
                    P("Waifu2xCropSize", "裁切大小"),
                    P("ShouldOpenOutputDirectory", "完成后打开输出"),
                    P("ShouldClearInputDirectoryAfterCompletion", "完成后清空输入")
                ],
                [typeof(ImageScalingViewModel)] =
                [
                    P("IsUpsampleEnabled", "启用上采样"),
                    P("UpsampleTargetWidth", "上采样宽度"),
                    P("UpsampleTargetHeight", "上采样高度"),
                    P("IsDownsampleEnabled", "启用下采样"),
                    P("DownsampleTargetWidth", "下采样宽度"),
                    P("DownsampleTargetHeight", "下采样高度"),
                    P("ShouldClearInputDirectoryAfterCompletion", "完成后清空输入")
                ],
                [typeof(ImageBucketingViewModel)] =
                [
                    P("DivisibleText", "尺寸整除值"),
                    P("MaxArErrorText", "最大宽高比误差"),
                    P("DimLimitText", "尺寸上限"),
                    P("IsDebugEnabled", "调试输出"),
                    P("ShouldClearInputDirectoryAfterCompletion", "完成后清空输入")
                ],
                [typeof(ThreeStageViewModel)] =
                [
                    P("SplitPerson", "裁切人物"),
                    P("SplitHalfbody", "裁切半身"),
                    P("SplitHead", "裁切头部"),
                    P("SplitEyes", "裁切眼部"),
                    P("KeepOriginTags", "保留原标签"),
                    P("PersonConfThreshold", "人物置信度"),
                    P("HalfbodyConfThreshold", "半身置信度"),
                    P("HeadConfThreshold", "头部置信度"),
                    P("EyeConfThreshold", "眼部置信度"),
                    P("HeadScale", "头部缩放"),
                    P("EyeScale", "眼部缩放"),
                    P("HalfbodyAppendTag", "半身追加标签"),
                    P("HeadAppendTag", "头部追加标签"),
                    P("EyeAppendTag", "眼部追加标签"),
                    P("ShouldClearInputDirectoryAfterCompletion", "完成后清空输入")
                ],
                [typeof(ResolutionFilterViewModel)] =
                [
                    P("IsHeightFilterEnabled", "启用高度筛选"),
                    P("MinHeight", "最小高度"),
                    P("IsWidthFilterEnabled", "启用宽度筛选"),
                    P("MinWidth", "最小宽度"),
                    P("IsResolutionFilterEnabled", "启用分辨率筛选"),
                    P("MinResolutionWidth", "最小分辨率宽度"),
                    P("MinResolutionHeight", "最小分辨率高度")
                ],
                [typeof(KeepFilesViewModel)] =
                [
                    P("KeepJpg", "保留 JPG"),
                    P("KeepJpeg", "保留 JPEG"),
                    P("KeepPng", "保留 PNG"),
                    P("KeepWebp", "保留 WEBP"),
                    P("KeepBmp", "保留 BMP"),
                    P("KeepNpz", "保留 NPZ"),
                    P("KeepTxt", "保留 TXT"),
                    P("KeepChineseTxt", "保留中文 TXT")
                ],
                [typeof(FolderProcessingViewModel)] =
                [
                    P("SelectedOperation", "处理操作"),
                    P("ParameterText", "操作参数"),
                    P("OverwriteExisting", "覆盖已有文件")
                ],
                [typeof(ExportVideoFrameViewModel)] =
                [
                    P("FrameIntervalText", "帧间隔（毫秒）")
                ],
                [typeof(GaussianBlurViewModel)] =
                [
                    P("BlurRadius", "模糊半径"),
                    P("ShouldClearInputDirectoryAfterCompletion", "完成后清空输入")
                ],
                [typeof(ConvertImageFormatViewModel)] =
                [
                    P("SelectedFormat", "目标格式", "ImageFormats"),
                    P("ShouldClearInputDirectoryAfterCompletion", "完成后清空输入")
                ],
                [typeof(ConvertTransparentChannelViewModel)] =
                [
                    P("ShouldClearInputDirectoryAfterCompletion", "完成后清空输入")
                ],
                [typeof(SameSourceImageGroupingViewModel)] =
                [
                    P("ComputeBackend", "计算后端", "ComputeBackendOptions"),
                    P("ShouldClearInputDirectoryAfterCompletion", "完成后清空输入")
                ],
                [typeof(TaggingViewModel)] =
                [
                    P("SelectedTaggerModel", "打标模型", "TaggerModels"),
                    P("SelectedExistingTxtConflictOption", "已有 TXT 处理", "ExistingTxtConflictOptions"),
                    P("GeneralThreshold", "通用标签阈值"),
                    P("CharacterThreshold", "角色标签阈值"),
                    P("PriorityTags", "优先标签"),
                    P("CustomTags", "自定义标签"),
                    P("PreferGpu", "优先使用 GPU"),
                    P("IncludeGeneral", "包含通用标签"),
                    P("IncludeArtist", "包含画师标签"),
                    P("IncludeCopyright", "包含版权标签"),
                    P("IncludeCharacter", "包含角色标签"),
                    P("IncludeMeta", "包含元标签"),
                    P("IncludeQuality", "包含质量标签"),
                    P("IncludeRating", "包含分级标签"),
                    P("IncludeModel", "包含模型标签")
                ],
                [typeof(TrainingSet.TrainingSetBalancerViewModel)] = Array.Empty<PipelineNodeParameterDefinition>(),
                [typeof(ImageRenameViewModel)] =
                [
                    P("NamePrefix", "文件名前缀"),
                    P("StartIndex", "起始编号"),
                    P("NumberPadding", "编号位数")
                ],
                [typeof(DatasetDownloaderViewModel)] =
                [
                    P("SelectedMode", "下载方式"),
                    P("EnableDanbooru", "Danbooru"),
                    P("EnablePixiv", "Pixiv", isEnabledPropertyName: "CanSelectAllSources"),
                    P("EnableKemono", "Kemono", isEnabledPropertyName: "CanSelectAllSources"),
                    P("InputText", "ID / 标签"),
                    P("IncludeArtist", "Artist"),
                    P("IncludeCopyright", "Copyright"),
                    P("IncludeCharacter", "Character"),
                    P("IncludeGeneral", "General"),
                    P("IncludeMeta", "Meta"),
                    P("OutputDirectory", "输出目录", browseCommand: "BrowseOutputFolderCommand"),
                    P("DownloadIntervalSeconds", "请求间隔（秒）")
                ]
            };

        private static readonly IReadOnlyDictionary<Type, IReadOnlyList<PipelineNodeParameterDefinition>> WorkflowOnlyDefinitions =
            new Dictionary<Type, IReadOnlyList<PipelineNodeParameterDefinition>>
            {
                [typeof(TrainingOutputOrganizerViewModel)] =
                [
                    P("OverwriteExisting", "覆盖已存在文件")
                ]
            };

        private static readonly IReadOnlyDictionary<Type, IReadOnlyList<PipelineNodeActionDefinition>> WorkflowActionDefinitions =
            new Dictionary<Type, IReadOnlyList<PipelineNodeActionDefinition>>();

        public static ObservableCollection<PipelineNodeParameterViewModel> Create(PipelineNodeViewModelBase node)
        {
            return CreateParameters(node, includeWorkflowOnly: false);
        }

        public static ObservableCollection<PipelineNodeParameterViewModel> CreateWorkflowParameters(PipelineNodeViewModelBase node)
        {
            return CreateParameters(node, includeWorkflowOnly: true);
        }

        public static ObservableCollection<PipelineNodeActionViewModel> CreateWorkflowActions(PipelineNodeViewModelBase node)
        {
            object target = ResolveTarget(node);
            if (!WorkflowActionDefinitions.TryGetValue(target.GetType(), out IReadOnlyList<PipelineNodeActionDefinition>? definitions))
            {
                return new ObservableCollection<PipelineNodeActionViewModel>();
            }

            var actions = new ObservableCollection<PipelineNodeActionViewModel>();
            foreach (PipelineNodeActionDefinition definition in definitions)
            {
                ICommand? command = ResolveCommand(target, node, definition.CommandPropertyName);
                if (command != null)
                {
                    actions.Add(new PipelineNodeActionViewModel(definition.Label, command));
                }
            }

            return actions;
        }

        private static ObservableCollection<PipelineNodeParameterViewModel> CreateParameters(
            PipelineNodeViewModelBase node,
            bool includeWorkflowOnly)
        {
            object target = ResolveTarget(node);
            var definitions = new List<PipelineNodeParameterDefinition>();
            if (Definitions.TryGetValue(target.GetType(), out IReadOnlyList<PipelineNodeParameterDefinition>? agentDefinitions))
            {
                definitions.AddRange(agentDefinitions);
            }

            if (includeWorkflowOnly && WorkflowOnlyDefinitions.TryGetValue(target.GetType(), out IReadOnlyList<PipelineNodeParameterDefinition>? workflowDefinitions))
            {
                definitions.AddRange(workflowDefinitions);
            }

            var parameters = new ObservableCollection<PipelineNodeParameterViewModel>();
            foreach (PipelineNodeParameterDefinition definition in definitions
                         .GroupBy(definition => definition.PropertyName)
                         .Select(group => group.Last()))
            {
                PropertyInfo? property = target.GetType().GetProperty(definition.PropertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanRead || !property.CanWrite)
                {
                    continue;
                }

                IReadOnlyList<PipelineNodeParameterOption> options = CreateOptions(target, property, definition.OptionsPropertyName);
                ICommand? browseCommand = ResolveCommand(target, node, definition.BrowseCommandPropertyName);
                parameters.Add(new PipelineNodeParameterViewModel(
                    target,
                    property,
                    definition.Label,
                    options,
                    browseCommand,
                    definition.IsPassword,
                    definition.IsReadOnlyWhenInputInherited,
                    definition.IsEnabledPropertyName));
            }

            return parameters;
        }

        private static object ResolveTarget(PipelineNodeViewModelBase node)
        {
            PropertyInfo? innerProperty = node.GetType().GetProperty("Inner", BindingFlags.Public | BindingFlags.Instance);
            return innerProperty?.GetValue(node) ?? node;
        }

        private static IReadOnlyList<PipelineNodeParameterOption> CreateOptions(
            object target,
            PropertyInfo property,
            string? optionsPropertyName)
        {
            IEnumerable? source = null;
            if (!string.IsNullOrWhiteSpace(optionsPropertyName))
            {
                source = target.GetType()
                    .GetProperty(optionsPropertyName, BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(target) as IEnumerable;
            }

            if (source == null && (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).IsEnum)
            {
                source = Enum.GetValues(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
            }

            return source == null
                ? Array.Empty<PipelineNodeParameterOption>()
                : source.Cast<object?>()
                    .Select(value => new PipelineNodeParameterOption(NormalizeOptionValue(property.PropertyType, value), GetOptionDisplayName(value)))
                    .ToList();
        }

        private static object? NormalizeOptionValue(Type propertyType, object? value)
        {
            if (value == null || propertyType.IsInstanceOfType(value))
            {
                return value;
            }

            PropertyInfo? operationProperty = value.GetType().GetProperty("Operation", BindingFlags.Public | BindingFlags.Instance);
            object? operation = operationProperty?.GetValue(value);
            return operation != null && propertyType.IsInstanceOfType(operation) ? operation : value;
        }

        private static string GetOptionDisplayName(object? value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            PropertyInfo? displayNameProperty = value.GetType().GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
            if (displayNameProperty?.GetValue(value) is string displayName && !string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            PropertyInfo? nameProperty = value.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            if (nameProperty?.GetValue(value) is string name && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return value.ToString() switch
            {
                "ArtistId" => "画师 ID",
                "Tag" => "标签",
                "PostId" => "帖子 ID",
                "MoveFilesToTopLevel" => "移动子目录文件到顶层",
                "DeleteEmptyDirectories" => "删除空文件夹",
                "RetainDirectoriesByKeyword" => "按关键词保留文件夹",
                "ExtractArchives" => "解压所有压缩包",
                "ConvertDiffusionPipeOutputToSdScript" => "转换 diffusion-pipe 输出",
                string text => text
            };
        }

        private static ICommand? ResolveCommand(
            object target,
            PipelineNodeViewModelBase node,
            string? commandPropertyName)
        {
            if (string.IsNullOrWhiteSpace(commandPropertyName))
            {
                return null;
            }

            return target.GetType().GetProperty(commandPropertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) as ICommand
                   ?? node.GetType().GetProperty(commandPropertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(node) as ICommand;
        }

        private static PipelineNodeParameterDefinition P(
            string propertyName,
            string label,
            string? options = null,
            string? browseCommand = null,
            bool isPassword = false,
            bool isReadOnlyWhenInputInherited = false,
            string? isEnabledPropertyName = null)
        {
            return new PipelineNodeParameterDefinition(
                propertyName,
                label,
                options,
                browseCommand,
                isPassword,
                isReadOnlyWhenInputInherited,
                isEnabledPropertyName);
        }

        private static PipelineNodeActionDefinition A(string label, string commandPropertyName)
        {
            return new PipelineNodeActionDefinition(label, commandPropertyName);
        }

        private sealed record PipelineNodeParameterDefinition(
            string PropertyName,
            string Label,
            string? OptionsPropertyName,
            string? BrowseCommandPropertyName,
            bool IsPassword,
            bool IsReadOnlyWhenInputInherited,
            string? IsEnabledPropertyName);

        private sealed record PipelineNodeActionDefinition(
            string Label,
            string CommandPropertyName);
    }
}
