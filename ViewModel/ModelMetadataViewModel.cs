using CyanTools.Utils;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CyanTools.ViewModel
{
    public sealed class ModelMetadataViewModel : INotifyPropertyChanged
    {
        private const string SafetensorsFilter = "Safetensors 模型 (*.safetensors)|*.safetensors|所有文件 (*.*)|*.*";

        private static readonly string[] PreferredSummaryFields =
        [
            "modelspec.title",
            "ss_output_name",
            "modelspec.architecture",
            "ss_base_model_version",
            "ss_sd_model_name",
            "ss_network_module",
            "ss_network_dim",
            "ss_network_alpha",
            "ss_epoch",
            "ss_num_epochs",
            "ss_steps",
            "ss_max_train_steps",
            "ss_learning_rate",
            "ss_dataset_dirs"
        ];

        private string _inputPath = string.Empty;
        private string _outputPath = string.Empty;
        private string _metadataJson = "{}";
        private string _fileInfoText = "尚未加载模型文件。";
        private string _statusText = "选择或拖入 Safetensors 模型后读取其嵌入元数据。";
        private bool _isBusy;
        private bool _isSettingSuggestedOutput;
        private bool _outputPathWasSuggested;

        public ModelMetadataViewModel()
        {
            SummaryItems = new ObservableCollection<ModelMetadataSummaryItem>();
            BrowseInputCommand = new RelayCommand(async () => await BrowseInputAsync(), () => !IsBusy);
            BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
            LoadCommand = new RelayCommand(async () => await LoadCurrentInputAsync(), () => !IsBusy);
            SaveMetadataCommand = new RelayCommand(async () => await SaveAsync(purgeMetadata: false), () => !IsBusy);
            PurgeMetadataCommand = new RelayCommand(async () => await SaveAsync(purgeMetadata: true), () => !IsBusy);
        }

        public ObservableCollection<ModelMetadataSummaryItem> SummaryItems { get; }

        public ICommand BrowseInputCommand { get; }

        public ICommand BrowseOutputCommand { get; }

        public ICommand LoadCommand { get; }

        public ICommand SaveMetadataCommand { get; }

        public ICommand PurgeMetadataCommand { get; }

        public string InputPath
        {
            get => _inputPath;
            set => SetField(ref _inputPath, value ?? string.Empty, nameof(InputPath));
        }

        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (SetField(ref _outputPath, value ?? string.Empty, nameof(OutputPath)) && !_isSettingSuggestedOutput)
                {
                    _outputPathWasSuggested = false;
                }
            }
        }

        public string MetadataJson
        {
            get => _metadataJson;
            set => SetField(ref _metadataJson, value ?? string.Empty, nameof(MetadataJson));
        }

        public string FileInfoText
        {
            get => _fileInfoText;
            private set => SetField(ref _fileInfoText, value, nameof(FileInfoText));
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value, nameof(StatusText));
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetField(ref _isBusy, value, nameof(IsBusy));
        }

        public async Task LoadModelAsync(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                ShowInfo("请选择 Safetensors 模型文件。");
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(modelPath);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("找不到模型文件。", fullPath);
                }

                if (!string.Equals(Path.GetExtension(fullPath), ".safetensors", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("仅支持 Safetensors（.safetensors）模型文件。");
                }
            }
            catch (Exception ex)
            {
                ShowError($"模型路径无效：{ex.Message}");
                return;
            }

            IsBusy = true;
            StatusText = "正在读取 Safetensors 文件头…";
            try
            {
                SafetensorsMetadataDocument document = await Task.Run(() => SafetensorsMetadataService.Read(fullPath));
                InputPath = fullPath;
                SuggestOutputPath(fullPath);
                MetadataJson = SerializeMetadata(document.Metadata);
                BuildSummary(document.Metadata);
                FileInfoText =
                    $"{Path.GetFileName(fullPath)}  ·  {FormatFileSize(document.FileSize)}  ·  " +
                    $"{document.TensorCount.ToString(CultureInfo.InvariantCulture)} 个张量  ·  " +
                    $"{document.Metadata.Count.ToString(CultureInfo.InvariantCulture)} 项元数据";
                StatusText = "已加载。编辑 JSON 后可生成新的 Safetensors 文件。";
            }
            catch (Exception ex)
            {
                StatusText = "读取失败。";
                ShowError($"读取模型元数据失败：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task BrowseInputAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.Translate("选择 Safetensors 模型"),
                Filter = SafetensorsFilter,
                Multiselect = false,
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                await LoadModelAsync(dialog.FileName);
            }
        }

        private async Task LoadCurrentInputAsync()
        {
            await LoadModelAsync(InputPath);
        }

        private void BrowseOutput()
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationManager.Translate("选择输出 Safetensors 文件"),
                Filter = SafetensorsFilter,
                DefaultExt = ".safetensors",
                AddExtension = true,
                FileName = string.IsNullOrWhiteSpace(OutputPath)
                    ? "model_metadata.safetensors"
                    : Path.GetFileName(OutputPath),
                InitialDirectory = GetInitialDirectory(OutputPath, InputPath)
            };

            if (dialog.ShowDialog() == true)
            {
                OutputPath = dialog.FileName;
            }
        }

        private async Task SaveAsync(bool purgeMetadata)
        {
            if (string.IsNullOrWhiteSpace(InputPath) || !File.Exists(InputPath))
            {
                ShowInfo("请先加载有效的 Safetensors 模型文件。");
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                SuggestOutputPath(InputPath);
            }

            IReadOnlyDictionary<string, string> metadata;
            try
            {
                metadata = purgeMetadata
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : ParseMetadataJson();
            }
            catch (Exception ex)
            {
                ShowError($"元数据 JSON 无法保存：{ex.Message}");
                return;
            }

            string fullInputPath = Path.GetFullPath(InputPath);
            string fullOutputPath = Path.GetFullPath(OutputPath);
            bool replacesSource = string.Equals(fullInputPath, fullOutputPath, StringComparison.OrdinalIgnoreCase);
            if (File.Exists(fullOutputPath))
            {
                string prompt = replacesSource
                    ? "这会用已修改元数据的新文件替换原模型；张量数据会原样流式复制。是否继续？"
                    : $"输出文件已存在：\n{fullOutputPath}\n\n是否覆盖？";
                if (MessageBox.Show(prompt, "确认覆盖", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            IsBusy = true;
            StatusText = purgeMetadata ? "正在移除元数据并复制张量数据…" : "正在写入元数据并复制张量数据…";
            try
            {
                SafetensorsMetadataWriteResult result = await Task.Run(
                    () => SafetensorsMetadataService.Write(fullInputPath, fullOutputPath, metadata));
                OutputPath = result.OutputPath;
                _outputPathWasSuggested = false;
                MetadataJson = SerializeMetadata(metadata);
                BuildSummary(metadata);
                StatusText = purgeMetadata
                    ? $"已清除元数据并保存到：{result.OutputPath}"
                    : $"已保存 {result.MetadataCount.ToString(CultureInfo.InvariantCulture)} 项元数据到：{result.OutputPath}";
                MessageBox.Show(
                    "模型元数据已保存。张量数据未被解析或修改。",
                    "完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText = "保存失败。";
                ShowError($"保存模型元数据失败：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private IReadOnlyDictionary<string, string> ParseMetadataJson()
        {
            using JsonDocument document = JsonDocument.Parse(MetadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("根节点必须是 JSON 对象。");
            }

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name))
                {
                    throw new InvalidDataException("元数据字段名不能为空。");
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException($"字段“{property.Name}”必须是字符串。");
                }

                metadata[property.Name] = property.Value.GetString() ?? string.Empty;
            }

            return metadata;
        }

        private void SuggestOutputPath(string inputPath)
        {
            if (!string.IsNullOrWhiteSpace(OutputPath) && !_outputPathWasSuggested)
            {
                return;
            }

            string directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
            string fileName = $"{Path.GetFileNameWithoutExtension(inputPath)}_metadata.safetensors";
            _isSettingSuggestedOutput = true;
            try
            {
                OutputPath = Path.Combine(directory, fileName);
                _outputPathWasSuggested = true;
            }
            finally
            {
                _isSettingSuggestedOutput = false;
            }
        }

        private void BuildSummary(IReadOnlyDictionary<string, string> metadata)
        {
            SummaryItems.Clear();
            var included = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in PreferredSummaryFields)
            {
                if (metadata.TryGetValue(key, out string? value))
                {
                    SummaryItems.Add(new ModelMetadataSummaryItem(key, value));
                    included.Add(key);
                }
            }

            foreach (var item in metadata
                .Where(item => !included.Contains(item.Key))
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Take(8))
            {
                SummaryItems.Add(new ModelMetadataSummaryItem(item.Key, item.Value));
            }
        }

        private static string SerializeMetadata(IReadOnlyDictionary<string, string> metadata)
        {
            var sortedMetadata = metadata
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            return JsonSerializer.Serialize(sortedMetadata, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });
        }

        private static string GetInitialDirectory(string outputPath, string inputPath)
        {
            string? candidate = !string.IsNullOrWhiteSpace(outputPath)
                ? Path.GetDirectoryName(outputPath)
                : Path.GetDirectoryName(inputPath);
            return !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)
                ? candidate
                : Environment.CurrentDirectory;
        }

        private static string FormatFileSize(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double size = bytes;
            int unitIndex = 0;
            while (size >= 1024d && unitIndex < units.Length - 1)
            {
                size /= 1024d;
                unitIndex++;
            }

            return $"{size.ToString(size >= 100d || unitIndex == 0 ? "0" : "0.##", CultureInfo.InvariantCulture)} {units[unitIndex]}";
        }

        private static void ShowInfo(string message)
        {
            MessageBox.Show(message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    public sealed record ModelMetadataSummaryItem(string Key, string Value);
}
