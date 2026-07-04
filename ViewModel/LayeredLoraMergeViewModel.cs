using CyanTools.Model;
using CyanTools.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
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
        private const int DefaultAnimaBlockCount = 28;
        private const double DefaultAnimaBlockWeight = 0.5d;
        private const string SafetensorsFilter = "LoRA safetensors (*.safetensors)|*.safetensors|所有文件 (*.*)|*.*";
        private const string ReportExampleWeights = "1,1,0.8,0.6,0.4,0.2,0.1,0,0,0,0,0,0,0.3,0.5,0.6,0.7,0.8,0.9,1,1,1,1,1,1";

        private readonly StringBuilder _logBuilder = new StringBuilder();
        private string _modelAPath = string.Empty;
        private string _modelBPath = string.Empty;
        private string _outputPath = string.Empty;
        private string _baseAlphaText = "0";
        private string _logText = string.Empty;
        private string _statusText = "准备就绪";
        private bool _isBusy;
        private bool _isSdxlWeightEditorVisible = true;
        private bool _isAnimaWeightEditorVisible;
        private LayeredLoraArchitectureOption _selectedArchitectureOption;

        public LayeredLoraMergeViewModel()
        {
            ArchitectureOptions = new ObservableCollection<LayeredLoraArchitectureOption>
            {
                new LayeredLoraArchitectureOption(LayeredLoraArchitecture.Auto, "自动识别"),
                new LayeredLoraArchitectureOption(LayeredLoraArchitecture.Sdxl, "SDXL / Kohya"),
                new LayeredLoraArchitectureOption(LayeredLoraArchitecture.Anima, "Anima / diffusion-pipe"),
                new LayeredLoraArchitectureOption(LayeredLoraArchitecture.AnimaSdxlCompatible, "Anima / SDXL 25层习惯")
            };
            _selectedArchitectureOption = ArchitectureOptions[0];
            var reportExample = LayeredLoraWeightParser.ParseReportWeights(ReportExampleWeights, baseAlpha: 0d);
            LayerWeights = new ObservableCollection<LayeredLoraLayerWeight>(
                LayeredLoraWeights.CreateReportLayerItems(reportExample.LayerWeights));
            AnimaBlockWeights = new ObservableCollection<LayeredLoraBlockWeight>(
                LayeredLoraWeights.CreateAnimaBlockItems(Enumerable.Range(0, DefaultAnimaBlockCount), DefaultAnimaBlockWeight));

            BrowseModelACommand = new RelayCommand(BrowseModelA, () => !IsBusy);
            BrowseModelBCommand = new RelayCommand(BrowseModelB, () => !IsBusy);
            BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
            MergeCommand = new RelayCommand(async () => await MergeAsync(), () => !IsBusy);
            UseAllACommand = new RelayCommand(() => ApplyPreset(0d, 0d), () => !IsBusy);
            UseAllBCommand = new RelayCommand(() => ApplyPreset(1d, 1d), () => !IsBusy);
            UseBalancedCommand = new RelayCommand(() => ApplyPreset(0.5d, 0.5d), () => !IsBusy);
            UseReportExampleCommand = new RelayCommand(ApplyReportExample, () => !IsBusy && IsSdxlWeightEditorVisible);
        }

        public ObservableCollection<LayeredLoraArchitectureOption> ArchitectureOptions { get; }

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
                }
            }
        }

        public bool IsReportExampleVisible => IsSdxlWeightEditorVisible;

        public string WeightEditorLabel
        {
            get
            {
                if (IsAnimaWeightEditorVisible)
                {
                    return "Anima Block 权重";
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
        public ICommand UseAllACommand { get; }
        public ICommand UseAllBCommand { get; }
        public ICommand UseBalancedCommand { get; }
        public ICommand UseReportExampleCommand { get; }

        private void BrowseModelA()
        {
            SelectInputFile("选择 LoRA A", path => ModelAPath = path);
        }

        private void BrowseModelB()
        {
            SelectInputFile("选择 LoRA B", path => ModelBPath = path);
        }

        private void BrowseOutput()
        {
            var dialog = new SaveFileDialog
            {
                Title = "选择输出 LoRA",
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
                AppendLog($"LoRA A: {options.ModelAPath}");
                AppendLog($"LoRA B: {options.ModelBPath}");
                AppendLog($"输出: {options.OutputPath}");

                var report = await Task.Run(() => LayeredLoraMerger.Merge(options));
                AppendLog($"完成: 架构识别为 {report.Architecture}。");
                AppendLog($"张量: 插值/缩放 {report.MergedTensorCount}，直接复制 {report.CopiedTensorCount}。");
                if (report.Architecture == LayeredLoraArchitecture.Anima)
                {
                    AppendLog($"Anima Block 权重: {options.Weights.AnimaBlockWeights.Count} 个滑动条。");
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
                MessageBox.Show("LoRA 分层融合完成。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
                throw new InvalidOperationException("请先选择有效的 LoRA A 文件。");
            }

            if (string.IsNullOrWhiteSpace(ModelBPath) || !File.Exists(ModelBPath))
            {
                throw new InvalidOperationException("请先选择有效的 LoRA B 文件。");
            }

            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                throw new InvalidOperationException("请设置输出 LoRA 路径。");
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
            var weights = effectiveArchitecture == LayeredLoraArchitecture.Anima
                ? LayeredLoraWeights.CreateFromAnimaBlockItems(baseAlpha, AnimaBlockWeights)
                : LayeredLoraWeights.CreateFromLayerItems(baseAlpha, LayerWeights);
            return new LayeredLoraMergeOptions(
                ModelAPath,
                ModelBPath,
                OutputPath,
                SelectedArchitectureOption.Architecture,
                weights,
                overwrite: true);
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
            IsSdxlWeightEditorVisible = architecture != LayeredLoraArchitecture.Anima;
            IsAnimaWeightEditorVisible = architecture == LayeredLoraArchitecture.Anima;

            if (IsAnimaWeightEditorVisible)
            {
                RefreshAnimaBlockWeights();
            }

            OnPropertyChanged(nameof(IsReportExampleVisible));
            OnPropertyChanged(nameof(WeightEditorLabel));
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
}
