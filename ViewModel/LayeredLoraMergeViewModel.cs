using CyanTools.Model;
using CyanTools.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CyanTools.ViewModel
{
    public sealed class LayeredLoraMergeViewModel : INotifyPropertyChanged
    {
        private const int DefaultAnimaBlockCount = 32;
        private const double DefaultAnimaBlockWeight = 0.5d;
        private const string SafetensorsFilter = "safetensors (*.safetensors)|*.safetensors|所有文件 (*.*)|*.*";
        private const string ReportExampleWeights = "1,1,0.8,0.6,0.4,0.2,0.1,0,0,0,0,0,0,0.3,0.5,0.6,0.7,0.8,0.9,1,1,1,1,1,1";

        private readonly LayeredLoraPresetStore _presetStore = new LayeredLoraPresetStore();
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private string _modelAPath = string.Empty;
        private string _modelBPath = string.Empty;
        private string _outputPath = string.Empty;
        private string _baseAlphaText = "0";
        private string _presetConfigName = string.Empty;
        private string _layerWeightsText = string.Empty;
        private string _logText = string.Empty;
        private string _statusText = "准备就绪";
        private bool _isBusy;
        private bool _isSdxlWeightEditorVisible = true;
        private bool _isAnimaWeightEditorVisible;
        private bool _isAnimaFullModelKeyRepairVisible;
        private bool _isAnimaFullModelKeyRepairEnabled;
        private bool _isApplyingLayerWeightsText;
        private bool _isSynchronizingLayerWeightsText;
        private LayeredLoraArchitectureOption _selectedArchitectureOption;
        private LayeredLoraPresetOption? _selectedPresetOption;

        public LayeredLoraMergeViewModel()
        {
            ArchitectureOptions = new ObservableCollection<LayeredLoraArchitectureOption>
            {
                new LayeredLoraArchitectureOption(LayeredLoraArchitecture.Auto, "自动识别"),
                new LayeredLoraArchitectureOption(LayeredLoraArchitecture.Sdxl, "SDXL / Kohya"),
                new LayeredLoraArchitectureOption(LayeredLoraArchitecture.Anima, "Anima / diffusion-pipe"),
                new LayeredLoraArchitectureOption(LayeredLoraArchitecture.AnimaFullModel, "Anima 大模型 / diffusion-pipe"),
                new LayeredLoraArchitectureOption(LayeredLoraArchitecture.AnimaSdxlCompatible, "Anima / SDXL 25层习惯")
            };
            _selectedArchitectureOption = ArchitectureOptions[0];
            var reportExample = LayeredLoraWeightParser.ParseReportWeights(ReportExampleWeights, baseAlpha: 0d);
            LayerWeights = new ObservableCollection<LayeredLoraLayerWeight>(
                LayeredLoraWeights.CreateReportLayerItems(reportExample.LayerWeights));
            AnimaBlockWeights = new ObservableCollection<LayeredLoraBlockWeight>(
                LayeredLoraWeights.CreateAnimaBlockItems(Enumerable.Range(0, DefaultAnimaBlockCount), DefaultAnimaBlockWeight));
            LayerWeights.CollectionChanged += OnWeightCollectionChanged;
            AnimaBlockWeights.CollectionChanged += OnWeightCollectionChanged;
            SubscribeWeightItems(LayerWeights);
            SubscribeWeightItems(AnimaBlockWeights);
            UpdateLayerWeightsTextFromCurrentEditor();

            BrowseModelACommand = new RelayCommand(BrowseModelA, () => !IsBusy);
            BrowseModelBCommand = new RelayCommand(BrowseModelB, () => !IsBusy);
            BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
            MergeCommand = new RelayCommand(async () => await MergeAsync(), () => !IsBusy);
            SavePresetConfigCommand = new RelayCommand(SavePresetConfig, CanSavePresetConfig);
            DeletePresetConfigCommand = new RelayCommand(DeletePresetConfig, CanDeletePresetConfig);
            PresetOptions = new ObservableCollection<LayeredLoraPresetOption>();
            RefreshPresetOptions();
        }

        public ObservableCollection<LayeredLoraArchitectureOption> ArchitectureOptions { get; }

        public ObservableCollection<LayeredLoraPresetOption> PresetOptions { get; }

        public ObservableCollection<LayeredLoraLayerWeight> LayerWeights { get; }

        public ObservableCollection<LayeredLoraBlockWeight> AnimaBlockWeights { get; }

        public LayeredLoraArchitectureOption SelectedArchitectureOption
        {
            get => _selectedArchitectureOption;
            set
            {
                if (SetField(ref _selectedArchitectureOption, value ?? ArchitectureOptions[0], nameof(SelectedArchitectureOption)))
                {
                    RefreshWeightEditorForInputs();
                }
            }
        }

        public string ModelAPath
        {
            get => _modelAPath;
            set
            {
                if (SetField(ref _modelAPath, value ?? string.Empty, nameof(ModelAPath)))
                {
                    SuggestOutputPath();
                    RefreshWeightEditorForInputs();
                }
            }
        }

        public string ModelBPath
        {
            get => _modelBPath;
            set
            {
                if (SetField(ref _modelBPath, value ?? string.Empty, nameof(ModelBPath)))
                {
                    RefreshWeightEditorForInputs();
                }
            }
        }

        public string OutputPath
        {
            get => _outputPath;
            set => SetField(ref _outputPath, value ?? string.Empty, nameof(OutputPath));
        }

        public string BaseAlphaText
        {
            get => _baseAlphaText;
            set => SetField(ref _baseAlphaText, value ?? string.Empty, nameof(BaseAlphaText));
        }

        public string PresetConfigName
        {
            get => _presetConfigName;
            set
            {
                if (SetField(ref _presetConfigName, value ?? string.Empty, nameof(PresetConfigName)))
                {
                    RaisePresetCommandStates();
                }
            }
        }

        public LayeredLoraPresetOption? SelectedPresetOption
        {
            get => _selectedPresetOption;
            set
            {
                if (SetField(ref _selectedPresetOption, value, nameof(SelectedPresetOption)) && value != null)
                {
                    PresetConfigName = value.Name;
                    ApplyPresetOption(value);
                }
            }
        }

        public string LayerWeightsText
        {
            get => _layerWeightsText;
            set
            {
                if (!SetField(ref _layerWeightsText, value ?? string.Empty, nameof(LayerWeightsText))
                    || _isSynchronizingLayerWeightsText)
                {
                    return;
                }

                ApplyLayerWeightsText(_layerWeightsText);
            }
        }

        public string LogText
        {
            get => _logText;
            private set => SetField(ref _logText, value, nameof(LogText));
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value, nameof(StatusText));
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetField(ref _isBusy, value, nameof(IsBusy)))
                {
                    CommandManager.InvalidateRequerySuggested();
                    RaisePresetCommandStates();
                }
            }
        }

        public bool IsSdxlWeightEditorVisible
        {
            get => _isSdxlWeightEditorVisible;
            private set
            {
                if (SetField(ref _isSdxlWeightEditorVisible, value, nameof(IsSdxlWeightEditorVisible)))
                {
                    OnPropertyChanged(nameof(IsReportExampleVisible));
                    OnPropertyChanged(nameof(WeightEditorLabel));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsAnimaWeightEditorVisible
        {
            get => _isAnimaWeightEditorVisible;
            private set
            {
                if (SetField(ref _isAnimaWeightEditorVisible, value, nameof(IsAnimaWeightEditorVisible)))
                {
                    OnPropertyChanged(nameof(WeightEditorLabel));
                    OnPropertyChanged(nameof(IsAnimaStylePresetVisible));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsAnimaFullModelKeyRepairVisible
        {
            get => _isAnimaFullModelKeyRepairVisible;
            private set => SetField(ref _isAnimaFullModelKeyRepairVisible, value, nameof(IsAnimaFullModelKeyRepairVisible));
        }

        public bool IsAnimaFullModelKeyRepairEnabled
        {
            get => _isAnimaFullModelKeyRepairEnabled;
            set => SetField(ref _isAnimaFullModelKeyRepairEnabled, value, nameof(IsAnimaFullModelKeyRepairEnabled));
        }

        public bool IsReportExampleVisible => IsSdxlWeightEditorVisible;

        public bool IsAnimaStylePresetVisible => IsAnimaWeightEditorVisible;

        public string WeightEditorLabel
        {
            get
            {
                if (IsAnimaWeightEditorVisible)
                {
                    return SelectedArchitectureOption?.Architecture == LayeredLoraArchitecture.AnimaFullModel
                        ? "Anima 大模型 Block 权重"
                        : "Anima Block 权重";
                }

                return SelectedArchitectureOption?.Architecture == LayeredLoraArchitecture.AnimaSdxlCompatible
                    ? "Anima 25 层权重"
                    : "25 层权重";
            }
        }

        public ICommand BrowseModelACommand { get; }
        public ICommand BrowseModelBCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand MergeCommand { get; }
        public ICommand SavePresetConfigCommand { get; }
        public ICommand DeletePresetConfigCommand { get; }

        private void BrowseModelA()
        {
            SelectInputFile("选择模型 A", path => ModelAPath = path);
        }

        private void BrowseModelB()
        {
            SelectInputFile("选择模型 B", path => ModelBPath = path);
        }

        private void BrowseOutput()
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationManager.Translate("选择输出文件"),
                Filter = SafetensorsFilter,
                DefaultExt = ".safetensors",
                AddExtension = true,
                FileName = string.IsNullOrWhiteSpace(OutputPath) ? BuildDefaultOutputFileName() : Path.GetFileName(OutputPath)
            };

            var initialDirectory = GetExistingDirectory(OutputPath);
            if (string.IsNullOrWhiteSpace(initialDirectory))
            {
                initialDirectory = GetExistingDirectory(ModelAPath);
            }

            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            if (dialog.ShowDialog() == true)
            {
                OutputPath = dialog.FileName;
            }
        }

        private static void SelectInputFile(string title, Action<string> assignPath)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = SafetensorsFilter,
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                assignPath(dialog.FileName);
            }
        }

        private async Task MergeAsync()
        {
            if (IsBusy)
            {
                return;
            }

            try
            {
                var options = BuildMergeOptions();
                IsBusy = true;
                StatusText = "正在融合...";
                ClearLog();
                AppendLog("开始分层融合。");
                AppendLog($"架构: {SelectedArchitectureOption.DisplayName}");
                AppendLog($"模型 A: {options.ModelAPath}");
                AppendLog($"模型 B: {options.ModelBPath}");
                AppendLog($"输出: {options.OutputPath}");
                if (options.RepairAnimaFullModelKeys)
                {
                    AppendLog("Key 修复: 已启用，将以模型 A 的 shape 顺序修复模型 B 的 Anima 大模型 key。");
                }

                var report = await Task.Run(() => LayeredLoraMerger.Merge(options));
                AppendLog($"完成: 架构识别为 {report.Architecture}。");
                AppendLog($"张量: 插值/缩放 {report.MergedTensorCount}，直接复制 {report.CopiedTensorCount}。");
                if (report.AnimaFullModelKeyRepairTensorCount > 0)
                {
                    AppendLog($"Key 修复: 已修复模型 B 的 {report.AnimaFullModelKeyRepairTensorCount} 个 tensor key。");
                }

                if (report.Architecture == LayeredLoraArchitecture.Anima ||
                    report.Architecture == LayeredLoraArchitecture.AnimaFullModel)
                {
                    AppendLog($"Anima Block 权重: {options.Weights.AnimaBlockWeights.Count} 个滑动条，命中 {report.AnimaBlockTensorCount} 个 block 张量。");
                    if (report.AnimaBlockTensorCount == 0)
                    {
                        AppendLog("警告: 未命中 Anima block 张量，本次融合会退回 base_alpha 或非 block 层权重。");
                    }
                }
                else
                {
                    AppendLog($"层分布: {FormatLayerCounts(report.LayerTensorCounts)}");
                }

                foreach (var warning in report.Warnings.Take(20))
                {
                    AppendLog($"警告: {warning}");
                }

                if (report.Warnings.Count > 20)
                {
                    AppendLog($"警告: 还有 {report.Warnings.Count - 20} 条同类提示未显示。");
                }

                StatusText = "融合完成";
                MessageBox.Show("分层融合完成。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText = "融合失败";
                AppendLog($"失败: {ex.Message}");
                MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private LayeredLoraMergeOptions BuildMergeOptions()
        {
            if (string.IsNullOrWhiteSpace(ModelAPath) || !File.Exists(ModelAPath))
            {
                throw new InvalidOperationException("请先选择有效的模型 A 文件。");
            }

            if (string.IsNullOrWhiteSpace(ModelBPath) || !File.Exists(ModelBPath))
            {
                throw new InvalidOperationException("请先选择有效的模型 B 文件。");
            }

            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                throw new InvalidOperationException("请设置输出文件路径。");
            }

            var outputDirectory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            if (!double.TryParse(BaseAlphaText, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseAlpha))
            {
                throw new FormatException("base_alpha 必须是数字，例如 0、0.5 或 1。");
            }

            var effectiveArchitecture = ResolveWeightEditorArchitecture();
            var weights = effectiveArchitecture == LayeredLoraArchitecture.Anima ||
                effectiveArchitecture == LayeredLoraArchitecture.AnimaFullModel
                ? LayeredLoraWeights.CreateFromAnimaBlockItems(baseAlpha, AnimaBlockWeights)
                : LayeredLoraWeights.CreateFromLayerItems(baseAlpha, LayerWeights);
            return new LayeredLoraMergeOptions(
                ModelAPath,
                ModelBPath,
                OutputPath,
                SelectedArchitectureOption.Architecture,
                weights,
                overwrite: true,
                repairAnimaFullModelKeys: effectiveArchitecture == LayeredLoraArchitecture.AnimaFullModel &&
                    IsAnimaFullModelKeyRepairEnabled);
        }

        private void RefreshPresetOptions()
        {
            var selectedName = SelectedPresetOption?.Name;
            PresetOptions.Clear();
            PresetOptions.Add(LayeredLoraPresetOption.CreateBuiltIn("全 A", LayeredLoraPresetKind.AllA));
            PresetOptions.Add(LayeredLoraPresetOption.CreateBuiltIn("全 B", LayeredLoraPresetKind.AllB));
            PresetOptions.Add(LayeredLoraPresetOption.CreateBuiltIn("均衡 0.5", LayeredLoraPresetKind.Balanced));

            if (IsSdxlWeightEditorVisible)
            {
                PresetOptions.Add(LayeredLoraPresetOption.CreateBuiltIn("报告示例", LayeredLoraPresetKind.ReportExample));
            }

            if (IsAnimaWeightEditorVisible)
            {
                PresetOptions.Add(LayeredLoraPresetOption.CreateBuiltIn("A画风到B构图", LayeredLoraPresetKind.AnimaStyleToComposition));
            }

            try
            {
                foreach (var name in _presetStore.LoadNames())
                {
                    PresetOptions.Add(LayeredLoraPresetOption.CreateCustom(name));
                }
            }
            catch (Exception ex)
            {
                StatusText = $"加载配置列表失败: {ex.Message}";
            }

            if (!string.IsNullOrWhiteSpace(selectedName))
            {
                _selectedPresetOption = PresetOptions.FirstOrDefault(item => string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase));
                OnPropertyChanged(nameof(SelectedPresetOption));
            }

            RaisePresetCommandStates();
        }

        private void ApplyPresetOption(LayeredLoraPresetOption option)
        {
            if (IsBusy)
            {
                return;
            }

            try
            {
                switch (option.Kind)
                {
                    case LayeredLoraPresetKind.AllA:
                        ApplyPreset(0d, 0d);
                        StatusText = "已加载配置: 全 A";
                        break;
                    case LayeredLoraPresetKind.AllB:
                        ApplyPreset(1d, 1d);
                        StatusText = "已加载配置: 全 B";
                        break;
                    case LayeredLoraPresetKind.Balanced:
                        ApplyPreset(0.5d, 0.5d);
                        StatusText = "已加载配置: 均衡 0.5";
                        break;
                    case LayeredLoraPresetKind.ReportExample:
                        ApplyReportExample();
                        StatusText = "已加载配置: 报告示例";
                        break;
                    case LayeredLoraPresetKind.AnimaStyleToComposition:
                        ApplyAnimaStyleToCompositionPreset();
                        StatusText = "已加载配置: A画风到B构图";
                        break;
                    case LayeredLoraPresetKind.Custom:
                        LoadPresetConfig(option.Name);
                        break;
                }
            }
            finally
            {
                RaisePresetCommandStates();
            }
        }

        private void SavePresetConfig()
        {
            var name = LayeredLoraPresetStore.SanitizeFileName(PresetConfigName);
            if (string.IsNullOrWhiteSpace(name))
            {
                StatusText = "配置名不能为空。";
                return;
            }

            try
            {
                _presetStore.Save(CreateCurrentPresetConfig(name));
                RefreshPresetOptions();
                SelectedPresetOption = PresetOptions.FirstOrDefault(item =>
                    item.Kind == LayeredLoraPresetKind.Custom &&
                    string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                StatusText = $"配置已保存: {name}";
                AppendLog(StatusText);
            }
            catch (Exception ex)
            {
                StatusText = $"保存配置失败: {ex.Message}";
                AppendLog(StatusText);
            }
            finally
            {
                RaisePresetCommandStates();
            }
        }

        private void DeletePresetConfig()
        {
            var name = LayeredLoraPresetStore.SanitizeFileName(PresetConfigName);
            if (string.IsNullOrWhiteSpace(name))
            {
                StatusText = "配置名不能为空。";
                return;
            }

            try
            {
                _presetStore.Delete(name);
                RefreshPresetOptions();
                SelectedPresetOption = null;
                StatusText = $"配置已删除: {name}";
                AppendLog(StatusText);
            }
            catch (Exception ex)
            {
                StatusText = $"删除配置失败: {ex.Message}";
                AppendLog(StatusText);
            }
            finally
            {
                RaisePresetCommandStates();
            }
        }

        private bool CanSavePresetConfig()
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(PresetConfigName);
        }

        private bool CanDeletePresetConfig()
        {
            return !IsBusy && _presetStore.Exists(PresetConfigName);
        }

        private void RaisePresetCommandStates()
        {
            (SavePresetConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeletePresetConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private LayeredLoraPresetConfig CreateCurrentPresetConfig(string name)
        {
            if (!double.TryParse(BaseAlphaText, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseAlpha))
            {
                throw new FormatException("base_alpha 必须是数字，例如 0、0.5 或 1。");
            }

            return new LayeredLoraPresetConfig
            {
                Name = name,
                Architecture = SelectedArchitectureOption?.Architecture ?? LayeredLoraArchitecture.Auto,
                BaseAlpha = baseAlpha,
                LayerWeights = LayerWeights.Select(item => item.Value).ToList(),
                AnimaBlockWeights = AnimaBlockWeights
                    .Select(item => new LayeredLoraBlockPresetWeight
                    {
                        BlockIndex = item.BlockIndex,
                        Value = item.Value
                    })
                    .ToList()
            };
        }

        private void LoadPresetConfig(string name)
        {
            var config = _presetStore.Load(name);
            if (config == null)
            {
                StatusText = "配置文件不存在。";
                RaisePresetCommandStates();
                return;
            }

            ApplyPresetConfig(config);
            StatusText = $"已加载配置: {config.Name}";
            AppendLog(StatusText);
        }

        private void ApplyPresetConfig(LayeredLoraPresetConfig config)
        {
            var architectureOption = ArchitectureOptions.FirstOrDefault(item => item.Architecture == config.Architecture)
                ?? ArchitectureOptions[0];
            SelectedArchitectureOption = architectureOption;
            BaseAlphaText = config.BaseAlpha.ToString("0.###", CultureInfo.InvariantCulture);

            if (config.LayerWeights.Count == LayeredLoraWeights.ReportLayerCount)
            {
                ApplyLayerValues(config.LayerWeights);
            }

            if (config.AnimaBlockWeights.Count > 0 &&
                (config.Architecture == LayeredLoraArchitecture.Anima ||
                 config.Architecture == LayeredLoraArchitecture.AnimaFullModel))
            {
                ApplyAnimaBlockPresetWeights(config.AnimaBlockWeights);
            }
        }

        private void ApplyAnimaBlockPresetWeights(IEnumerable<LayeredLoraBlockPresetWeight> weights)
        {
            var values = weights
                .Where(item => item.BlockIndex >= 0)
                .GroupBy(item => item.BlockIndex)
                .ToDictionary(group => group.Key, group => group.Last().Value);
            if (values.Count == 0)
            {
                return;
            }

            var refreshedItems = LayeredLoraWeights.CreateAnimaBlockItems(values.Keys, DefaultAnimaBlockWeight);
            AnimaBlockWeights.Clear();
            foreach (var item in refreshedItems)
            {
                if (values.TryGetValue(item.BlockIndex, out var value))
                {
                    item.Value = value;
                }

                AnimaBlockWeights.Add(item);
            }
        }

        private void ApplyPreset(double baseAlpha, double layerWeight)
        {
            BaseAlphaText = baseAlpha.ToString("0.###", CultureInfo.InvariantCulture);
            if (IsAnimaWeightEditorVisible)
            {
                ApplyBlockValue(layerWeight);
            }
            else
            {
                ApplyLayerValues(Enumerable.Repeat(layerWeight, LayeredLoraWeights.ReportLayerCount));
            }
        }

        private void ApplyReportExample()
        {
            if (!IsSdxlWeightEditorVisible)
            {
                return;
            }

            BaseAlphaText = "0";
            var reportExample = LayeredLoraWeightParser.ParseReportWeights(ReportExampleWeights, baseAlpha: 0d);
            ApplyLayerValues(reportExample.LayerWeights);
        }

        private void ApplyAnimaStyleToCompositionPreset()
        {
            if (!IsAnimaWeightEditorVisible)
            {
                return;
            }

            BaseAlphaText = "1";
            var presetValues = LayeredLoraWeights
                .CreateAnimaStyleToCompositionPresetItems(AnimaBlockWeights.Select(item => item.BlockIndex))
                .ToDictionary(item => item.BlockIndex, item => item.Value);
            foreach (var item in AnimaBlockWeights)
            {
                if (presetValues.TryGetValue(item.BlockIndex, out var value))
                {
                    item.Value = value;
                }
            }
        }

        private void ApplyLayerValues(IEnumerable<double> values)
        {
            var layerValues = values.ToArray();
            if (layerValues.Length != LayerWeights.Count)
            {
                throw new ArgumentException($"Layer weight count must be {LayerWeights.Count}.", nameof(values));
            }

            for (int i = 0; i < LayerWeights.Count; i++)
            {
                LayerWeights[i].Value = layerValues[i];
            }
        }

        private void ApplyBlockValue(double value)
        {
            foreach (var item in AnimaBlockWeights)
            {
                item.Value = value;
            }
        }

        private void RefreshWeightEditorForInputs()
        {
            var architecture = ResolveWeightEditorArchitecture();
            IsSdxlWeightEditorVisible = architecture != LayeredLoraArchitecture.Anima &&
                architecture != LayeredLoraArchitecture.AnimaFullModel;
            IsAnimaWeightEditorVisible = architecture == LayeredLoraArchitecture.Anima ||
                architecture == LayeredLoraArchitecture.AnimaFullModel;
            IsAnimaFullModelKeyRepairVisible = architecture == LayeredLoraArchitecture.AnimaFullModel;
            if (!IsAnimaFullModelKeyRepairVisible)
            {
                IsAnimaFullModelKeyRepairEnabled = false;
            }

            if (IsAnimaWeightEditorVisible)
            {
                RefreshAnimaBlockWeights();
            }

            OnPropertyChanged(nameof(IsReportExampleVisible));
            OnPropertyChanged(nameof(IsAnimaStylePresetVisible));
            OnPropertyChanged(nameof(WeightEditorLabel));
            UpdateLayerWeightsTextFromCurrentEditor();
            RefreshPresetOptions();
            CommandManager.InvalidateRequerySuggested();
        }

        private LayeredLoraArchitecture ResolveWeightEditorArchitecture()
        {
            var requested = SelectedArchitectureOption?.Architecture ?? LayeredLoraArchitecture.Auto;
            if (requested != LayeredLoraArchitecture.Auto)
            {
                return requested;
            }

            foreach (var key in ReadSelectedTensorKeys())
            {
                var guess = LoraLayerMapper.GuessArchitectureFromKey(key);
                if (guess == LayeredLoraArchitecture.AnimaFullModel)
                {
                    return LayeredLoraArchitecture.AnimaFullModel;
                }

                if (guess == LayeredLoraArchitecture.Anima)
                {
                    return LayeredLoraArchitecture.Anima;
                }
            }

            return LayeredLoraArchitecture.Sdxl;
        }

        private void RefreshAnimaBlockWeights()
        {
            var indices = new SortedSet<int>();
            foreach (var key in ReadSelectedTensorKeys())
            {
                var index = LoraLayerMapper.TryGetAnimaBlockIndex(key);
                if (index.HasValue)
                {
                    indices.Add(index.Value);
                }
            }

            if (indices.Count == 0)
            {
                indices.UnionWith(Enumerable.Range(0, DefaultAnimaBlockCount));
            }

            var existingValues = AnimaBlockWeights.ToDictionary(item => item.BlockIndex, item => item.Value);
            var refreshedItems = LayeredLoraWeights.CreateAnimaBlockItems(indices, DefaultAnimaBlockWeight);
            AnimaBlockWeights.Clear();
            foreach (var item in refreshedItems)
            {
                if (existingValues.TryGetValue(item.BlockIndex, out var existingValue))
                {
                    item.Value = existingValue;
                }

                AnimaBlockWeights.Add(item);
            }
        }

        private void OnWeightCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<INotifyPropertyChanged>())
                {
                    item.PropertyChanged -= OnWeightItemPropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<INotifyPropertyChanged>())
                {
                    item.PropertyChanged += OnWeightItemPropertyChanged;
                }
            }

            UpdateLayerWeightsTextFromCurrentEditor();
        }

        private void SubscribeWeightItems(IEnumerable<INotifyPropertyChanged> items)
        {
            foreach (var item in items)
            {
                item.PropertyChanged += OnWeightItemPropertyChanged;
            }
        }

        private void OnWeightItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isApplyingLayerWeightsText || e.PropertyName != nameof(LayeredLoraLayerWeight.Value))
            {
                return;
            }

            UpdateLayerWeightsTextFromCurrentEditor();
        }

        private void ApplyLayerWeightsText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)
                || !LayeredLoraWeightText.TryParseValues(text, out var values)
                || values.Count == 0)
            {
                return;
            }

            _isApplyingLayerWeightsText = true;
            try
            {
                if (IsAnimaWeightEditorVisible)
                {
                    int count = Math.Min(values.Count, AnimaBlockWeights.Count);
                    for (int i = 0; i < count; i++)
                    {
                        AnimaBlockWeights[i].Value = values[i];
                    }
                }
                else
                {
                    int count = Math.Min(values.Count, LayerWeights.Count);
                    for (int i = 0; i < count; i++)
                    {
                        LayerWeights[i].Value = values[i];
                    }
                }
            }
            finally
            {
                _isApplyingLayerWeightsText = false;
            }
        }

        private void UpdateLayerWeightsTextFromCurrentEditor()
        {
            if (_isApplyingLayerWeightsText)
            {
                return;
            }

            var values = IsAnimaWeightEditorVisible
                ? AnimaBlockWeights.Select(item => item.Value)
                : LayerWeights.Select(item => item.Value);
            var text = LayeredLoraWeightText.ToCommaText(values);
            if (_layerWeightsText == text)
            {
                return;
            }

            _isSynchronizingLayerWeightsText = true;
            try
            {
                _layerWeightsText = text;
                OnPropertyChanged(nameof(LayerWeightsText));
            }
            finally
            {
                _isSynchronizingLayerWeightsText = false;
            }
        }

        private IEnumerable<string> ReadSelectedTensorKeys()
        {
            foreach (var path in new[] { ModelAPath, ModelBPath })
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                IReadOnlyList<string> keys;
                try
                {
                    keys = SafetensorsFile.ReadTensorKeys(path);
                }
                catch
                {
                    continue;
                }

                foreach (var key in keys)
                {
                    yield return key;
                }
            }
        }

        private void SuggestOutputPath()
        {
            if (!string.IsNullOrWhiteSpace(OutputPath) || string.IsNullOrWhiteSpace(ModelAPath))
            {
                return;
            }

            var directory = GetExistingDirectory(ModelAPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            OutputPath = Path.Combine(directory, BuildDefaultOutputFileName());
        }

        private string BuildDefaultOutputFileName()
        {
            var baseName = string.IsNullOrWhiteSpace(ModelAPath)
                ? "layered_lora_merge"
                : Path.GetFileNameWithoutExtension(ModelAPath);
            return $"{baseName}_layered_merge.safetensors";
        }

        private static string GetExistingDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (Directory.Exists(path))
            {
                return path;
            }

            var directory = Path.GetDirectoryName(path);
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
                ? directory
                : string.Empty;
        }

        private void ClearLog()
        {
            _logBuilder.Clear();
            LogText = string.Empty;
        }

        private void AppendLog(string message)
        {
            _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            LogText = _logBuilder.ToString();
        }

        private static string FormatLayerCounts(IReadOnlyDictionary<LoraLayerSlot, int> counts)
        {
            var parts = new List<string>();
            AppendCount(parts, counts, LoraLayerSlot.Base);
            foreach (var slot in LayeredLoraWeights.ReportLayerSlots)
            {
                AppendCount(parts, counts, slot);
            }

            AppendCount(parts, counts, LoraLayerSlot.Unknown);
            return parts.Count > 0 ? string.Join(", ", parts) : "无";
        }

        private static void AppendCount(List<string> parts, IReadOnlyDictionary<LoraLayerSlot, int> counts, LoraLayerSlot slot)
        {
            if (counts.TryGetValue(slot, out int count) && count > 0)
            {
                parts.Add($"{slot}:{count}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public sealed class LayeredLoraArchitectureOption
    {
        public LayeredLoraArchitectureOption(LayeredLoraArchitecture architecture, string displayName)
        {
            Architecture = architecture;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        }

        public LayeredLoraArchitecture Architecture { get; }

        public string DisplayName { get; }
    }

    public enum LayeredLoraPresetKind
    {
        Custom,
        AllA,
        AllB,
        Balanced,
        ReportExample,
        AnimaStyleToComposition
    }

    public sealed class LayeredLoraPresetOption
    {
        private LayeredLoraPresetOption(string name, LayeredLoraPresetKind kind)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kind = kind;
        }

        public string Name { get; }

        public string DisplayName => Name;

        public LayeredLoraPresetKind Kind { get; }

        public bool IsCustom => Kind == LayeredLoraPresetKind.Custom;

        public static LayeredLoraPresetOption CreateBuiltIn(string name, LayeredLoraPresetKind kind)
        {
            if (kind == LayeredLoraPresetKind.Custom)
            {
                throw new ArgumentException("Use CreateCustom for custom presets.", nameof(kind));
            }

            return new LayeredLoraPresetOption(name, kind);
        }

        public static LayeredLoraPresetOption CreateCustom(string name)
        {
            return new LayeredLoraPresetOption(name, LayeredLoraPresetKind.Custom);
        }
    }
}
