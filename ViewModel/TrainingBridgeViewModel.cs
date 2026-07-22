using CyanTools.Model;
using CyanTools.Utils;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CyanTools.ViewModel
{
    public sealed class TrainingBridgeViewModel : INotifyPropertyChanged
    {
        private readonly TrainingBridgeProjectStore _projectStore;
        private CancellationTokenSource? _monitorCancellation;
        private bool _isBusy;
        private bool _hasUnsavedChanges;
        private bool _suppressDraftSelection;
        private bool _suppressParameterChanges;
        private string _projectName = string.Empty;
        private string _notes = string.Empty;
        private string _statusMessage = "可导入 JSON、TOML、TXT、ARGS 和 CFG 配置。";
        private string _logText = string.Empty;
        private TrainingBridgeBackendOption _selectedSourceBackend = null!;
        private TrainingBridgeBackendOption _selectedTargetBackend = null!;
        private TrainingBridgeProjectDraft? _selectedDraft;
        private TrainingBridgeParameterViewModel? _selectedParameter;
        private TrainingBridgeGeneratedFileViewModel? _selectedGeneratedFile;
        private string _monitorStateText = "读取中";
        private string _gpuName = "正在检测 NVIDIA GPU";
        private string _gpuTemperatureText = "--";
        private double _gpuLoadPercent;
        private string _gpuLoadText = "--";
        private double _vramLoadPercent;
        private string _vramText = "--";
        private string _cpuText = "--";
        private string _cpuDetailText = "本机处理器";
        private string _ramText = "--";
        private string _ramDetailText = "--";
        private double _diskLoadPercent;
        private string _diskPathText = "--";
        private string _diskUsageText = "--";
        private string _monitorSampleText = "正在读取本机负载...";
        private bool _showSystemMonitorWidget = SystemMonitorWidgetController.IsEnabled;

        public TrainingBridgeViewModel()
            : this(new TrainingBridgeProjectStore())
        {
        }

        internal TrainingBridgeViewModel(TrainingBridgeProjectStore projectStore)
        {
            _projectStore = projectStore;
            BackendOptions = new ObservableCollection<TrainingBridgeBackendOption>
            {
                new(TrainingBridgeConfigService.SdScriptsBackendId, "sd-scripts / Windows"),
                new(TrainingBridgeConfigService.WslSdScriptsBackendId, "wsl-sd-scripts / WSL2"),
                new(TrainingBridgeConfigService.AnimaFastLoraBackendId, "Anima Fast LoRA / Windows"),
                new(TrainingBridgeConfigService.DiffusionPipeBackendId, "diffusion-pipe / WSL2")
            };
            Drafts = new ObservableCollection<TrainingBridgeProjectDraft>();
            Parameters = new ObservableCollection<TrainingBridgeParameterViewModel>();
            GeneratedFiles = new ObservableCollection<TrainingBridgeGeneratedFileViewModel>();
            _selectedSourceBackend = BackendOptions[0];
            _selectedTargetBackend = BackendOptions[0];

            NewProjectCommand = new RelayCommand(NewProject, () => !IsBusy);
            SaveProjectCommand = new RelayCommand(SaveProject, () => !IsBusy);
            DeleteProjectCommand = new RelayCommand(DeleteProject, () => !IsBusy && SelectedDraft != null);
            ImportConfigCommand = new RelayCommand(async _ => await ImportConfigAsync(), _ => !IsBusy);
            PreviewExportCommand = new RelayCommand(PreviewExport, () => !IsBusy);
            ExportConfigCommand = new RelayCommand(ExportConfig, () => !IsBusy);
            AddParameterCommand = new RelayCommand(AddParameter, () => !IsBusy);
            RemoveParameterCommand = new RelayCommand(RemoveSelectedParameter, () => !IsBusy && SelectedParameter != null);
            FillDefaultsCommand = new RelayCommand(FillMissingDefaults, () => !IsBusy);
            CopyPreviewCommand = new RelayCommand(CopySelectedPreview, () => SelectedGeneratedFile != null);

            RefreshDrafts();
            ReplaceParameters(TrainingBridgeConfigService.CreateDefaultValues());
            AppendLog("训练桥已就绪。");
        }

        public ObservableCollection<TrainingBridgeBackendOption> BackendOptions { get; }
        public ObservableCollection<TrainingBridgeProjectDraft> Drafts { get; }
        public ObservableCollection<TrainingBridgeParameterViewModel> Parameters { get; }
        public ObservableCollection<TrainingBridgeGeneratedFileViewModel> GeneratedFiles { get; }

        public ICommand NewProjectCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand DeleteProjectCommand { get; }
        public ICommand ImportConfigCommand { get; }
        public ICommand PreviewExportCommand { get; }
        public ICommand ExportConfigCommand { get; }
        public ICommand AddParameterCommand { get; }
        public ICommand RemoveParameterCommand { get; }
        public ICommand FillDefaultsCommand { get; }
        public ICommand CopyPreviewCommand { get; }

        public string ProjectName
        {
            get => _projectName;
            set
            {
                if (SetField(ref _projectName, value))
                {
                    MarkDirty();
                }
            }
        }

        public string Notes
        {
            get => _notes;
            set
            {
                if (SetField(ref _notes, value))
                {
                    MarkDirty();
                }
            }
        }

        public TrainingBridgeBackendOption SelectedSourceBackend
        {
            get => _selectedSourceBackend;
            set
            {
                if (value != null && SetField(ref _selectedSourceBackend, value))
                {
                    MarkDirty();
                }
            }
        }

        public TrainingBridgeBackendOption SelectedTargetBackend
        {
            get => _selectedTargetBackend;
            set
            {
                if (value != null && SetField(ref _selectedTargetBackend, value))
                {
                    MarkDirty();
                    GeneratedFiles.Clear();
                    SelectedGeneratedFile = null;
                }
            }
        }

        public TrainingBridgeProjectDraft? SelectedDraft
        {
            get => _selectedDraft;
            set
            {
                if (!SetField(ref _selectedDraft, value) || _suppressDraftSelection || value == null)
                {
                    RaiseCommandStates();
                    return;
                }

                LoadDraft(value);
                RaiseCommandStates();
            }
        }

        public TrainingBridgeParameterViewModel? SelectedParameter
        {
            get => _selectedParameter;
            set
            {
                if (SetField(ref _selectedParameter, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public TrainingBridgeGeneratedFileViewModel? SelectedGeneratedFile
        {
            get => _selectedGeneratedFile;
            set
            {
                if (SetField(ref _selectedGeneratedFile, value))
                {
                    OnPropertyChanged(nameof(PreviewText));
                    RaiseCommandStates();
                }
            }
        }

        public string PreviewText => SelectedGeneratedFile?.Content ?? "点击“生成预览”查看转换后的配置文件。";

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetField(ref _statusMessage, value);
        }

        public string LogText
        {
            get => _logText;
            private set => SetField(ref _logText, value);
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set => SetField(ref _hasUnsavedChanges, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetField(ref _isBusy, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public string ParameterCountText => $"{Parameters.Count} 项";

        public bool ShowSystemMonitorWidget
        {
            get => _showSystemMonitorWidget;
            set
            {
                if (SetField(ref _showSystemMonitorWidget, value))
                {
                    SystemMonitorWidgetController.SetEnabled(value);
                }
            }
        }

        public void StartObservingSystemMonitorWidget()
        {
            SystemMonitorWidgetController.EnabledChanged -= SystemMonitorWidgetController_EnabledChanged;
            SystemMonitorWidgetController.EnabledChanged += SystemMonitorWidgetController_EnabledChanged;
            SystemMonitorWidgetController_EnabledChanged(SystemMonitorWidgetController.IsEnabled);
        }

        public void StopObservingSystemMonitorWidget()
            => SystemMonitorWidgetController.EnabledChanged -= SystemMonitorWidgetController_EnabledChanged;

        private void SystemMonitorWidgetController_EnabledChanged(bool enabled)
        {
            if (_showSystemMonitorWidget == enabled)
            {
                return;
            }

            _showSystemMonitorWidget = enabled;
            OnPropertyChanged(nameof(ShowSystemMonitorWidget));
        }

        public string MonitorStateText
        {
            get => _monitorStateText;
            private set => SetField(ref _monitorStateText, value);
        }

        public string GpuName
        {
            get => _gpuName;
            private set => SetField(ref _gpuName, value);
        }

        public string GpuTemperatureText
        {
            get => _gpuTemperatureText;
            private set => SetField(ref _gpuTemperatureText, value);
        }

        public double GpuLoadPercent
        {
            get => _gpuLoadPercent;
            private set => SetField(ref _gpuLoadPercent, value);
        }

        public string GpuLoadText
        {
            get => _gpuLoadText;
            private set => SetField(ref _gpuLoadText, value);
        }

        public double VramLoadPercent
        {
            get => _vramLoadPercent;
            private set => SetField(ref _vramLoadPercent, value);
        }

        public string VramText
        {
            get => _vramText;
            private set => SetField(ref _vramText, value);
        }

        public string CpuText
        {
            get => _cpuText;
            private set => SetField(ref _cpuText, value);
        }

        public string CpuDetailText
        {
            get => _cpuDetailText;
            private set => SetField(ref _cpuDetailText, value);
        }

        public string RamText
        {
            get => _ramText;
            private set => SetField(ref _ramText, value);
        }

        public string RamDetailText
        {
            get => _ramDetailText;
            private set => SetField(ref _ramDetailText, value);
        }

        public double DiskLoadPercent
        {
            get => _diskLoadPercent;
            private set => SetField(ref _diskLoadPercent, value);
        }

        public string DiskPathText
        {
            get => _diskPathText;
            private set => SetField(ref _diskPathText, value);
        }

        public string DiskUsageText
        {
            get => _diskUsageText;
            private set => SetField(ref _diskUsageText, value);
        }

        public string MonitorSampleText
        {
            get => _monitorSampleText;
            private set => SetField(ref _monitorSampleText, value);
        }

        public void StartMonitoring()
        {
            if (_monitorCancellation != null)
            {
                return;
            }

            _monitorCancellation = new CancellationTokenSource();
            MonitorStateText = "读取中";
            MonitorSampleText = "正在读取本机负载...";
            _ = MonitorSystemAsync(_monitorCancellation.Token);
        }

        public void StopMonitoring()
        {
            CancellationTokenSource? cancellation = _monitorCancellation;
            _monitorCancellation = null;
            if (cancellation == null)
            {
                return;
            }

            cancellation.Cancel();
            cancellation.Dispose();
        }

        private async Task MonitorSystemAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TrainingBridgeSystemMetrics metrics = await TrainingBridgeSystemMetricsService.CollectAsync(
                        FindOutputDirectory(),
                        cancellationToken);
                    ApplySystemMetrics(metrics);
                    await Task.Delay(TimeSpan.FromSeconds(2.5), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    MonitorStateText = "离线";
                    MonitorSampleText = "系统监控暂不可用";
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }

        private void ApplySystemMetrics(TrainingBridgeSystemMetrics metrics)
        {
            TrainingBridgeGpuMetrics? gpu = metrics.PrimaryGpu;
            MonitorStateText = "在线";
            GpuName = gpu?.Name ?? "未检测到 NVIDIA GPU";
            GpuTemperatureText = gpu?.TemperatureC is double temperature ? $"{temperature:0.#}°C" : "--";
            GpuLoadPercent = gpu?.LoadPercent ?? 0d;
            GpuLoadText = FormatPercent(gpu?.LoadPercent);
            VramLoadPercent = gpu?.MemoryLoadPercent ?? 0d;
            VramText = FormatGbPair(gpu?.MemoryUsedGb, gpu?.MemoryTotalGb);
            CpuText = FormatPercent(metrics.CpuLoadPercent);
            CpuDetailText = metrics.LogicalProcessors > 0 ? $"{metrics.LogicalProcessors} 线程" : "本机处理器";
            RamText = FormatGbPair(metrics.Memory.UsedGb, metrics.Memory.TotalGb);
            RamDetailText = FormatPercent(metrics.Memory.LoadPercent);
            DiskLoadPercent = metrics.Disk.LoadPercent ?? 0d;
            DiskPathText = string.IsNullOrWhiteSpace(metrics.Disk.Path) ? "--" : metrics.Disk.Path;
            DiskUsageText = FormatGbPair(metrics.Disk.UsedGb, metrics.Disk.TotalGb);
            MonitorSampleText = $"更新于 {metrics.SampledAt:HH:mm:ss}";
        }

        private string? FindOutputDirectory()
            => Parameters.FirstOrDefault(parameter => parameter.Key.Trim() == "output_dir")?.Value;

        private static string FormatPercent(double? value)
            => value.HasValue ? $"{Math.Clamp(value.Value, 0d, 100d):0.#}%" : "--";

        private static string FormatGbPair(double? used, double? total)
            => used.HasValue && total.HasValue ? $"{used.Value:0.#} GB / {total.Value:0.#} GB" : "--";

        private void NewProject()
        {
            _suppressDraftSelection = true;
            SelectedDraft = null;
            _suppressDraftSelection = false;
            ProjectName = string.Empty;
            Notes = string.Empty;
            SelectedSourceBackend = BackendOptions[0];
            SelectedTargetBackend = BackendOptions[0];
            ReplaceParameters(TrainingBridgeConfigService.CreateDefaultValues());
            GeneratedFiles.Clear();
            SelectedGeneratedFile = null;
            HasUnsavedChanges = false;
            StatusMessage = "已新建训练桥草稿。";
            AppendLog(StatusMessage);
        }

        private void SaveProject()
        {
            try
            {
                var project = new TrainingBridgeProjectDraft
                {
                    Id = SelectedDraft?.Id ?? Guid.NewGuid().ToString("D"),
                    Name = ProjectName,
                    Notes = Notes,
                    SourceBackendId = SelectedSourceBackend.Id,
                    TargetBackendId = SelectedTargetBackend.Id,
                    Values = BuildValues()
                };
                _projectStore.Save(project);
                RefreshDrafts(project.Id);
                HasUnsavedChanges = false;
                StatusMessage = $"草稿已保存：{project.Name}";
                AppendLog(StatusMessage);
            }
            catch (Exception exception)
            {
                ShowError("保存草稿失败", exception);
            }
        }

        private void DeleteProject()
        {
            if (SelectedDraft == null)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"确定删除草稿“{SelectedDraft.Name}”吗？",
                "删除训练桥草稿",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            string name = SelectedDraft.Name;
            _projectStore.Delete(SelectedDraft.Id);
            RefreshDrafts();
            NewProject();
            StatusMessage = $"草稿已删除：{name}";
            AppendLog(StatusMessage);
        }

        private async Task ImportConfigAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "导入训练配置",
                Filter = "训练配置 (*.json;*.toml;*.txt;*.args;*.cfg)|*.json;*.toml;*.txt;*.args;*.cfg|所有文件 (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var files = new List<TrainingBridgeConfigFile>();
                foreach (string path in dialog.FileNames)
                {
                    files.Add(new TrainingBridgeConfigFile(Path.GetFileName(path), await File.ReadAllTextAsync(path)));
                }

                IReadOnlyDictionary<string, string> imported = TrainingBridgeConfigService.Import(
                    SelectedSourceBackend.Id,
                    files);
                MergeParameters(imported);
                if (string.IsNullOrWhiteSpace(ProjectName))
                {
                    ProjectName = Path.GetFileNameWithoutExtension(dialog.FileNames[0]);
                }
                GeneratedFiles.Clear();
                SelectedGeneratedFile = null;
                StatusMessage = $"已导入配置：{files.Count} 个文件，映射 {imported.Count} 个参数。";
                AppendLog(StatusMessage);
            }
            catch (Exception exception)
            {
                ShowError("配置导入失败", exception);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void PreviewExport()
        {
            try
            {
                IReadOnlyList<TrainingBridgeConfigFile> files = GenerateFiles();
                StatusMessage = $"已生成 {SelectedTargetBackend.Label} 配置预览：{files.Count} 个文件。";
                AppendLog(StatusMessage);
            }
            catch (Exception exception)
            {
                ShowError("生成配置预览失败", exception);
            }
        }

        private void ExportConfig()
        {
            IReadOnlyList<TrainingBridgeConfigFile> files;
            try
            {
                files = GenerateFiles();
            }
            catch (Exception exception)
            {
                ShowError("生成导出配置失败", exception);
                return;
            }

            using var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择配置导出目录"
            };
            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            {
                return;
            }

            string[] existingFiles = files
                .Select(file => Path.Combine(dialog.FileName, file.FileName))
                .Where(File.Exists)
                .ToArray();
            if (existingFiles.Length > 0)
            {
                MessageBoxResult overwrite = MessageBox.Show(
                    $"目标目录中已有 {existingFiles.Length} 个同名文件，是否覆盖？",
                    "覆盖训练配置",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (overwrite != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            try
            {
                Directory.CreateDirectory(dialog.FileName);
                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                foreach (var file in files)
                {
                    File.WriteAllText(Path.Combine(dialog.FileName, file.FileName), file.Content, encoding);
                }
                StatusMessage = $"已导出 {SelectedTargetBackend.Label} 配置：{files.Count} 个文件。";
                AppendLog($"{StatusMessage} 目录：{dialog.FileName}");
            }
            catch (Exception exception)
            {
                ShowError("写入配置文件失败", exception);
            }
        }

        private IReadOnlyList<TrainingBridgeConfigFile> GenerateFiles()
        {
            IReadOnlyList<TrainingBridgeConfigFile> files = TrainingBridgeConfigService.Export(
                SelectedTargetBackend.Id,
                BuildValues());
            GeneratedFiles.Clear();
            foreach (var file in files)
            {
                GeneratedFiles.Add(new TrainingBridgeGeneratedFileViewModel(file.FileName, file.Content));
            }
            SelectedGeneratedFile = GeneratedFiles.FirstOrDefault();
            return files;
        }

        private void AddParameter()
        {
            string baseKey = "custom_parameter";
            string key = baseKey;
            int suffix = 2;
            var keys = Parameters.Select(parameter => parameter.Key).ToHashSet(StringComparer.Ordinal);
            while (keys.Contains(key))
            {
                key = baseKey + "_" + suffix++;
            }
            var parameter = CreateParameter(key, string.Empty);
            Parameters.Add(parameter);
            SelectedParameter = parameter;
            OnPropertyChanged(nameof(ParameterCountText));
            MarkDirty();
        }

        private void RemoveSelectedParameter()
        {
            if (SelectedParameter == null)
            {
                return;
            }
            Parameters.Remove(SelectedParameter);
            SelectedParameter = null;
            OnPropertyChanged(nameof(ParameterCountText));
            MarkDirty();
        }

        private void FillMissingDefaults()
        {
            var existing = Parameters.Select(parameter => parameter.Key.Trim()).ToHashSet(StringComparer.Ordinal);
            int added = 0;
            foreach (var pair in TrainingBridgeConfigService.CreateDefaultValues())
            {
                if (existing.Add(pair.Key))
                {
                    Parameters.Add(CreateParameter(pair.Key, pair.Value));
                    added++;
                }
            }
            OnPropertyChanged(nameof(ParameterCountText));
            MarkDirty();
            StatusMessage = added == 0 ? "常用参数已齐全。" : $"已补齐 {added} 个常用参数。";
            AppendLog(StatusMessage);
        }

        private void CopySelectedPreview()
        {
            if (SelectedGeneratedFile == null)
            {
                return;
            }
            Clipboard.SetText(SelectedGeneratedFile.Content);
            StatusMessage = $"已复制：{SelectedGeneratedFile.FileName}";
            AppendLog(StatusMessage);
        }

        private void LoadDraft(TrainingBridgeProjectDraft draft)
        {
            _suppressParameterChanges = true;
            ProjectName = draft.Name;
            Notes = draft.Notes;
            _selectedSourceBackend = FindBackend(draft.SourceBackendId);
            _selectedTargetBackend = FindBackend(draft.TargetBackendId);
            OnPropertyChanged(nameof(SelectedSourceBackend));
            OnPropertyChanged(nameof(SelectedTargetBackend));
            ReplaceParameters(draft.Values);
            _suppressParameterChanges = false;
            GeneratedFiles.Clear();
            SelectedGeneratedFile = null;
            HasUnsavedChanges = false;
            StatusMessage = $"已读取草稿：{draft.Name}";
            AppendLog(StatusMessage);
        }

        private TrainingBridgeBackendOption FindBackend(string backendId)
            => BackendOptions.FirstOrDefault(option => option.Id == backendId) ?? BackendOptions[0];

        private void RefreshDrafts(string? selectedId = null)
        {
            _suppressDraftSelection = true;
            Drafts.Clear();
            foreach (var draft in _projectStore.ListAll())
            {
                Drafts.Add(draft);
            }
            _selectedDraft = selectedId == null
                ? null
                : Drafts.FirstOrDefault(draft => draft.Id == selectedId);
            OnPropertyChanged(nameof(SelectedDraft));
            _suppressDraftSelection = false;
            RaiseCommandStates();
        }

        private void ReplaceParameters(IReadOnlyDictionary<string, string> values)
        {
            _suppressParameterChanges = true;
            Parameters.Clear();
            foreach (var pair in values.OrderBy(pair => ParameterOrder(pair.Key)).ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Parameters.Add(CreateParameter(pair.Key, pair.Value));
            }
            _suppressParameterChanges = false;
            OnPropertyChanged(nameof(ParameterCountText));
        }

        private void MergeParameters(IReadOnlyDictionary<string, string> imported)
        {
            _suppressParameterChanges = true;
            var current = Parameters
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Key))
                .GroupBy(parameter => parameter.Key.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            foreach (var pair in imported)
            {
                if (current.TryGetValue(pair.Key, out var parameter))
                {
                    parameter.Value = pair.Value;
                }
                else
                {
                    var added = CreateParameter(pair.Key, pair.Value);
                    Parameters.Add(added);
                    current[pair.Key] = added;
                }
            }
            _suppressParameterChanges = false;
            OnPropertyChanged(nameof(ParameterCountText));
            MarkDirty();
        }

        private TrainingBridgeParameterViewModel CreateParameter(string key, string value)
        {
            var parameter = new TrainingBridgeParameterViewModel(key, value);
            parameter.PropertyChanged += Parameter_PropertyChanged;
            return parameter;
        }

        private void Parameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressParameterChanges)
            {
                return;
            }
            GeneratedFiles.Clear();
            SelectedGeneratedFile = null;
            MarkDirty();
        }

        private Dictionary<string, string> BuildValues()
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var parameter in Parameters)
            {
                string key = parameter.Key.Trim();
                if (key.Length == 0)
                {
                    continue;
                }
                if (!values.TryAdd(key, parameter.Value))
                {
                    throw new InvalidOperationException($"参数键重复：{key}");
                }
            }
            return values;
        }

        private static int ParameterOrder(string key)
        {
            string[] preferred =
            [
                "model_family", "dataset_path", "output_dir", "pretrained_model_path", "transformer_path",
                "qwen3_path", "llm_path", "vae_path", "dataset_resolution", "resolution",
                "dataset_repeats", "num_repeats", "caption_extension", "keep_tokens", "enable_bucket",
                "shuffle_caption", "output_name", "save_model_as", "learning_rate", "optimizer_type",
                "lr_scheduler", "train_batch_size", "micro_batch_size_per_gpu", "max_train_epochs", "epochs",
                "gradient_accumulation_steps", "save_every_n_epochs", "network_dim", "rank", "network_alpha",
                "network_module"
            ];
            int index = Array.IndexOf(preferred, key);
            return index >= 0 ? index : preferred.Length;
        }

        private void MarkDirty()
        {
            if (!_suppressParameterChanges)
            {
                HasUnsavedChanges = true;
            }
        }

        private void ShowError(string title, Exception exception)
        {
            StatusMessage = $"{title}：{exception.Message}";
            AppendLog(StatusMessage);
            MessageBox.Show(StatusMessage, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void AppendLog(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            string combined = string.IsNullOrEmpty(LogText) ? line : LogText + Environment.NewLine + line;
            if (combined.Length > 30000)
            {
                combined = combined[^30000..];
                int firstLineBreak = combined.IndexOf(Environment.NewLine, StringComparison.Ordinal);
                if (firstLineBreak >= 0)
                {
                    combined = combined[(firstLineBreak + Environment.NewLine.Length)..];
                }
            }
            LogText = combined;
        }

        private void RaiseCommandStates()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public sealed class TrainingBridgeParameterViewModel : INotifyPropertyChanged
    {
        private static readonly IReadOnlyDictionary<string, ParameterPresentation> Presentations =
            new Dictionary<string, ParameterPresentation>(StringComparer.Ordinal)
            {
                ["model_family"] = new("模型家族", "select", "决定 sd-scripts 导出时使用的模型训练入口。", ["anima", "sd15", "sd2", "sdxl", "sd3", "flux", "lumina", "hunyuan_image"]),
                ["dataset_path"] = new("训练集根目录", "path", "图片与同名标签所在的数据集目录。"),
                ["output_dir"] = new("输出目录", "path", "配置与训练结果的保存位置。"),
                ["pretrained_model_path"] = new("基础模型路径", "path", "sd-scripts 系后端使用的基础模型。"),
                ["transformer_path"] = new("Anima transformer 模型", "path", "diffusion-pipe 使用的 Anima transformer 权重。"),
                ["qwen3_path"] = new("Qwen3 路径", "path", "Anima 文本编码器路径。"),
                ["llm_path"] = new("Qwen3 0.6B 模型", "path", "diffusion-pipe 使用的 LLM 路径。"),
                ["vae_path"] = new("VAE 路径", "path", "Anima 训练使用的 VAE 权重。"),
                ["dataset_resolution"] = new("数据集分辨率", "number", "sd-scripts 数据集分桶基准分辨率。"),
                ["resolution"] = new("训练分辨率", "number", "diffusion-pipe 数据集尺寸桶分辨率。"),
                ["dataset_repeats"] = new("默认重复次数", "number", "sd-scripts 每轮重复读取数据集的次数。"),
                ["num_repeats"] = new("数据集重复次数", "number", "diffusion-pipe 每轮重复读取数据集的次数。"),
                ["caption_extension"] = new("标签扩展名", "text", "例如 .txt。"),
                ["keep_tokens"] = new("保留前 N 个标签", "number", "打乱标签时保留开头的标签数。"),
                ["enable_bucket"] = new("启用分桶", "boolean", "按长宽比组合训练 batch。"),
                ["shuffle_caption"] = new("打乱标签", "boolean", "每轮训练打乱 caption 标签顺序。"),
                ["output_name"] = new("输出名称", "text", "训练模型的基础文件名。"),
                ["save_model_as"] = new("模型保存格式", "select", "建议使用 safetensors。", ["safetensors", "ckpt"]),
                ["learning_rate"] = new("学习率", "number", "优化器的基础学习率。"),
                ["optimizer_type"] = new("优化器", "select", "训练使用的优化器。", ["AdamW8bit", "AdamW", "Prodigy", "Adafactor"]),
                ["lr_scheduler"] = new("学习率调度器", "select", "学习率随训练进度的变化规则。", ["constant", "constant_with_warmup", "cosine", "cosine_with_restarts"]),
                ["train_batch_size"] = new("Batch Size", "number", "sd-scripts 单次迭代处理的样本数。"),
                ["micro_batch_size_per_gpu"] = new("单卡 micro batch", "number", "diffusion-pipe 每张 GPU 的微批量。"),
                ["max_train_epochs"] = new("最大 Epoch", "number", "sd-scripts 训练轮数。"),
                ["epochs"] = new("训练轮数", "number", "diffusion-pipe 训练轮数。"),
                ["gradient_accumulation_steps"] = new("梯度累积步数", "number", "增大等效 batch 而不额外占用同等显存。"),
                ["save_every_n_epochs"] = new("每 N Epoch 保存", "number", "按 Epoch 保存训练检查点。"),
                ["save_every_n_steps"] = new("每 N Step 保存", "number", "按 Step 保存训练检查点。"),
                ["network_dim"] = new("LoRA 维度", "number", "sd-scripts LoRA rank。"),
                ["rank"] = new("LoRA rank", "number", "diffusion-pipe LoRA rank。"),
                ["network_alpha"] = new("LoRA alpha", "number", "sd-scripts LoRA alpha。"),
                ["rank_alpha"] = new("Rank alpha", "number", "LoRA 缩放参数。"),
                ["network_module"] = new("网络模块", "text", "例如 networks.lora_anima。"),
                ["gradient_checkpointing"] = new("梯度检查点", "boolean", "用额外计算换取更低显存占用。"),
                ["cache_latents"] = new("缓存 Latent", "boolean", "预计算 VAE latent 以加快训练。"),
                ["blocks_to_swap"] = new("Block swap 数量", "number", "diffusion-pipe 在 CPU/GPU 间换入换出的 block 数。"),
                ["max_sequence_length"] = new("最大文本长度", "number", "Anima 文本编码的 token 上限。"),
                ["eval_every_n_epochs"] = new("每 N Epoch 评估", "number", "diffusion-pipe 评估间隔。"),
                ["conditioning_data_dir"] = new("条件数据目录", "path", "可选的配对条件数据。"),
                ["additional_args"] = new("额外训练参数", "textarea", "一行一个需要原样保留的附加参数。")
            };

        private string _key;
        private string _value;

        public TrainingBridgeParameterViewModel(string key, string value)
        {
            _key = key;
            _value = value;
        }

        public string Key
        {
            get => _key;
            set
            {
                if (SetField(ref _key, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HelpText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditorKind)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Options)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsKnownParameter)));
                }
            }
        }

        public string Value
        {
            get => _value;
            set
            {
                if (SetField(ref _value, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BooleanValue)));
                }
            }
        }

        public string DisplayLabel => Presentation.Label;

        public string HelpText => Presentation.HelpText;

        public string EditorKind => Presentation.EditorKind;

        public IReadOnlyList<string> Options => Presentation.Options;

        public bool IsKnownParameter => Presentations.ContainsKey(Key.Trim());

        public bool BooleanValue
        {
            get => Value.Equals("true", StringComparison.OrdinalIgnoreCase) || Value == "1";
            set => Value = value ? "true" : "false";
        }

        private ParameterPresentation Presentation
            => Presentations.TryGetValue(Key.Trim(), out ParameterPresentation? presentation)
                ? presentation
                : new ParameterPresentation(Key.Trim().Length > 0 ? Key.Trim() : "自定义参数", "text", "导入或手动添加的原始后端参数。");

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
        {
            value ??= string.Empty;
            if (field == value)
            {
                return false;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private sealed record ParameterPresentation(
            string Label,
            string EditorKind,
            string HelpText,
            IReadOnlyList<string>? OptionValues = null)
        {
            public IReadOnlyList<string> Options { get; } = OptionValues ?? Array.Empty<string>();
        }
    }

    public sealed record TrainingBridgeGeneratedFileViewModel(string FileName, string Content)
    {
        public override string ToString() => FileName;
    }
}
