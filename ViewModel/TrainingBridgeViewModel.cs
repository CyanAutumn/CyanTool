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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CyanTools.ViewModel
{
    public sealed class TrainingBridgeViewModel : INotifyPropertyChanged
    {
        private readonly TrainingBridgeProjectStore _projectStore;
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
            set => SetField(ref _key, value);
        }

        public string Value
        {
            get => _value;
            set => SetField(ref _value, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
        {
            value ??= string.Empty;
            if (field == value)
            {
                return;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed record TrainingBridgeGeneratedFileViewModel(string FileName, string Content)
    {
        public override string ToString() => FileName;
    }
}
