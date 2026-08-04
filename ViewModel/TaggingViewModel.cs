using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Localization;
using CyanTools.Pipeline;
using CyanTools.Utils;
using CyanTools;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class TaggerModel
    {
        public string DisplayName { get; set; }
        public string ModelUrl { get; set; }
        public string TagsUrl { get; set; }
        public int InputSize { get; set; }
        public string InputFormat { get; set; } = "NHWC";
        public string SeriesId { get; set; } = TaggerSeriesCatalog.OnnxMultiLabelSeriesId;
        public TaggerBackendKind BackendKind { get; set; } = TaggerBackendKind.OnnxMultiLabel;
    }

    public class ExistingTxtConflictOption
    {
        public string DisplayName { get; set; } = string.Empty;
        public ExistingTxtHandlingMode Mode { get; set; } = ExistingTxtHandlingMode.Overwrite;
    }

    public class TaggingViewModel : INotifyPropertyChanged, IPipelineDirectoryNode
    {
        private string _inputDirectory;
        private float _generalThreshold = 0.35f;
        private float _characterThreshold = 0.85f;
        private bool _isIdle = true;
        private double _progressValue;
        private TaggerModel _selectedTaggerModel;
        private string _priorityTags = string.Empty;
        private string _customTags = string.Empty;
        private bool _includeGeneral = true;
        private bool _includeArtist = false;
        private bool _includeCopyright = false;
        private bool _includeCharacter = true;
        private bool _includeMeta = false;
        private bool _includeQuality = false;
        private bool _includeRating = false;
        private bool _includeModel = false;
        private bool _preferGpu = true;
        private ExistingTxtConflictOption _selectedExistingTxtConflictOption;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;
        private TaggerSeriesDefinition _selectedTaggerSeries;
        private string _toriiGateEndpoint = TaggerSeriesCatalog.ToriiGateDefaultEndpoint;
        private string _toriiGateModelName = TaggerSeriesCatalog.ToriiGateDefaultModelName;
        private string _toriiGateApiToken = string.Empty;
        private string _toriiGateCaptionFormatId = "short";
        private double _toriiGateMaxMegapixels = 1.0;
        private int _toriiGateMaxTokens = 1024;
        private double _toriiGateTemperature = 0.5;
        private int _toriiGateMaxParallelRequests = 1;
        private readonly List<TaggerModel> _allTaggerModels = new();
        private const int MaxLogLines = 1000;

        public string InputDirectory { get => _inputDirectory; set => SetField(ref _inputDirectory, value); }
        public float GeneralThreshold { get => _generalThreshold; set => SetField(ref _generalThreshold, value); }
        public float CharacterThreshold { get => _characterThreshold; set => SetField(ref _characterThreshold, value); }
        public bool IsIdle { get => _isIdle; set => SetField(ref _isIdle, value); }
        public double ProgressValue { get => _progressValue; set => SetField(ref _progressValue, value); }

        public ObservableCollection<string> LogLines { get; }
        public ObservableCollection<TaggerSeriesDefinition> TaggerSeries { get; }
        public ObservableCollection<TaggerModel> TaggerModels { get; }
        public ObservableCollection<ExistingTxtConflictOption> ExistingTxtConflictOptions { get; }
        public TaggerModel SelectedTaggerModel { get => _selectedTaggerModel; set => SetField(ref _selectedTaggerModel, value); }
        public string SelectedTaggerSeriesId
        {
            get => SelectedTaggerSeries?.Id ?? string.Empty;
            set
            {
                var series = TaggerSeries.FirstOrDefault(item => string.Equals(item.Id, value, StringComparison.OrdinalIgnoreCase))
                             ?? TaggerSeriesCatalog.GetSeries(value);
                SelectedTaggerSeries = series;
            }
        }
        public TaggerSeriesDefinition SelectedTaggerSeries
        {
            get => _selectedTaggerSeries;
            private set
            {
                if (!SetField(ref _selectedTaggerSeries, value))
                {
                    return;
                }

                RefreshTaggerModels();
                OnPropertyChanged(nameof(SelectedTaggerSeriesId));
                OnPropertyChanged(nameof(IsOnnxTaggerSeries));
                OnPropertyChanged(nameof(IsToriiGateTaggerSeries));
            }
        }
        public ExistingTxtConflictOption SelectedExistingTxtConflictOption { get => _selectedExistingTxtConflictOption; set => SetField(ref _selectedExistingTxtConflictOption, value); }
        public string PriorityTags { get => _priorityTags; set => SetField(ref _priorityTags, value); }
        public string CustomTags { get => _customTags; set => SetField(ref _customTags, value); }
        public bool IncludeGeneral { get => _includeGeneral; set => SetField(ref _includeGeneral, value); }
        public bool IncludeArtist { get => _includeArtist; set => SetField(ref _includeArtist, value); }
        public bool IncludeCopyright { get => _includeCopyright; set => SetField(ref _includeCopyright, value); }
        public bool IncludeCharacter { get => _includeCharacter; set => SetField(ref _includeCharacter, value); }
        public bool IncludeMeta { get => _includeMeta; set => SetField(ref _includeMeta, value); }
        public bool IncludeQuality { get => _includeQuality; set => SetField(ref _includeQuality, value); }
        public bool IncludeRating { get => _includeRating; set => SetField(ref _includeRating, value); }
        public bool IncludeModel { get => _includeModel; set => SetField(ref _includeModel, value); }
        public bool PreferGpu { get => _preferGpu; set => SetField(ref _preferGpu, value); }
        public bool IsWorkflowMode { get => _isWorkflowMode; set => SetField(ref _isWorkflowMode, value); }
        public bool HasInheritedInputPath { get => _hasInheritedInputPath; set => SetField(ref _hasInheritedInputPath, value); }
        public bool IsOnnxTaggerSeries => SelectedTaggerSeries?.BackendKind == TaggerBackendKind.OnnxMultiLabel;
        public bool IsToriiGateTaggerSeries => SelectedTaggerSeries?.BackendKind == TaggerBackendKind.OpenAiCompatibleVisionCaption;
        public string ToriiGateEndpoint { get => _toriiGateEndpoint; set => SetField(ref _toriiGateEndpoint, value); }
        public string ToriiGateModelName { get => _toriiGateModelName; set => SetField(ref _toriiGateModelName, value); }
        public string ToriiGateApiToken { get => _toriiGateApiToken; set => SetField(ref _toriiGateApiToken, value); }
        public ObservableCollection<ToriiGateCaptionFormatDefinition> ToriiGateCaptionFormats { get; }
        public string ToriiGateCaptionFormatId { get => _toriiGateCaptionFormatId; set => SetField(ref _toriiGateCaptionFormatId, value); }
        public double ToriiGateMaxMegapixels { get => _toriiGateMaxMegapixels; set => SetField(ref _toriiGateMaxMegapixels, value); }
        public int ToriiGateMaxTokens { get => _toriiGateMaxTokens; set => SetField(ref _toriiGateMaxTokens, value); }
        public double ToriiGateTemperature { get => _toriiGateTemperature; set => SetField(ref _toriiGateTemperature, value); }
        public int ToriiGateMaxParallelRequests { get => _toriiGateMaxParallelRequests; set => SetField(ref _toriiGateMaxParallelRequests, value); }
        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.InPlace;
        public string PipelineInputPath { get => InputDirectory ?? string.Empty; set => InputDirectory = value ?? string.Empty; }
        public string PipelineOutputPath { get => InputDirectory ?? string.Empty; set => InputDirectory = value ?? string.Empty; }

        public ICommand BrowseInputCommand { get; }
        public ICommand StartTaggingCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand AddTaggerCommand { get; }

        public TaggingViewModel()
        {
            LogLines = new ObservableCollection<string>();
            TaggerSeries = new ObservableCollection<TaggerSeriesDefinition>(TaggerSeriesCatalog.BuiltInSeries);
            ToriiGateCaptionFormats = new ObservableCollection<ToriiGateCaptionFormatDefinition>(TaggerSeriesCatalog.ToriiGateCaptionFormats);
            TaggerModels = new ObservableCollection<TaggerModel>();
            _allTaggerModels.AddRange(new[]
            {
                new TaggerModel { DisplayName = "wd-v1-4-swinv2-tagger-v2", ModelUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-swinv2-tagger-v2/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-swinv2-tagger-v2/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "wd-v1-4-convnext-tagger-v2", ModelUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-convnext-tagger-v2/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-convnext-tagger-v2/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "wd-v1-4-moat-tagger-v2", ModelUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-moat-tagger-v2/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-moat-tagger-v2/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "wd-v1-4-convnextv2-tagger-v2", ModelUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-convnextv2-tagger-v2/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-convnextv2-tagger-v2/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "wd-v1-4-vit-tagger-v2", ModelUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-vit-tagger-v2/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-vit-tagger-v2/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "wd-swinv2-tagger-v3", ModelUrl = "https://huggingface.co/SmilingWolf/wd-swinv2-tagger-v3/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/SmilingWolf/wd-swinv2-tagger-v3/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "wd-convnext-tagger-v3", ModelUrl = "https://huggingface.co/SmilingWolf/wd-convnext-tagger-v3/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/SmilingWolf/wd-convnext-tagger-v3/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "wd-vit-tagger-v3", ModelUrl = "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "wd-vit-large-tagger-v3", ModelUrl = "https://huggingface.co/SmilingWolf/wd-vit-large-tagger-v3/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/SmilingWolf/wd-vit-large-tagger-v3/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "wd-eva02-large-tagger-v3", ModelUrl = "https://huggingface.co/SmilingWolf/wd-eva02-large-tagger-v3/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/SmilingWolf/wd-eva02-large-tagger-v3/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "idolsankaku-swinv2-tagger-v1", ModelUrl = "https://huggingface.co/deepghs/idolsankaku-swinv2-tagger-v1/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/deepghs/idolsankaku-swinv2-tagger-v1/resolve/main/selected_tags.csv", InputSize = 448, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "idolsankaku-eva02-large-tagger-v1", ModelUrl = "https://huggingface.co/deepghs/idolsankaku-eva02-large-tagger-v1/resolve/main/model.onnx", TagsUrl = "https://huggingface.co/deepghs/idolsankaku-eva02-large-tagger-v1/resolve/main/selected_tags.csv", InputSize = 480, InputFormat = "NHWC" },
                new TaggerModel { DisplayName = "cl_tagger_1_02", ModelUrl = "https://huggingface.co/cella110n/cl_tagger/resolve/main/cl_tagger_1_02/model.onnx", TagsUrl = "https://huggingface.co/cella110n/cl_tagger/raw/main/cl_tagger_1_02/tag_mapping.json", InputSize = 448, InputFormat = "NCHW" },
                new TaggerModel
                {
                    DisplayName = "ToriiGate-0.5",
                    SeriesId = TaggerSeriesCatalog.ToriiGateSeriesId,
                    BackendKind = TaggerBackendKind.OpenAiCompatibleVisionCaption
                }
            });
            SelectedTaggerSeries = TaggerSeriesCatalog.GetSeries(TaggerSeriesCatalog.OnnxMultiLabelSeriesId);
            ExistingTxtConflictOptions = new ObservableCollection<ExistingTxtConflictOption>
            {
                new ExistingTxtConflictOption { DisplayName = "跳过", Mode = ExistingTxtHandlingMode.Skip },
                new ExistingTxtConflictOption { DisplayName = "去重后添加", Mode = ExistingTxtHandlingMode.MergeUnique },
                new ExistingTxtConflictOption { DisplayName = "替换", Mode = ExistingTxtHandlingMode.Overwrite }
            };
            SelectedExistingTxtConflictOption = ExistingTxtConflictOptions.FirstOrDefault(option => option.Mode == ExistingTxtHandlingMode.Overwrite)
                                               ?? ExistingTxtConflictOptions.FirstOrDefault();

            BrowseInputCommand = new RelayCommand(_ => BrowseForFolder(path => InputDirectory = path));
            StartTaggingCommand = new RelayCommand(async _ => await StartProcessing(), _ => IsIdle && !string.IsNullOrWhiteSpace(InputDirectory));
            ClearLogCommand = new RelayCommand(_ => LogLines.Clear(), _ => IsIdle);
            AddTaggerCommand = new RelayCommand(_ => AddTagger(), _ => IsIdle);
        }

        private void RefreshTaggerModels()
        {
            string selectedName = SelectedTaggerModel?.DisplayName ?? string.Empty;
            string selectedSeriesId = SelectedTaggerSeries?.Id ?? TaggerSeriesCatalog.OnnxMultiLabelSeriesId;
            var models = _allTaggerModels
                .Where(model => string.Equals(model.SeriesId, selectedSeriesId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TaggerModels.Clear();
            foreach (var model in models)
            {
                TaggerModels.Add(model);
            }

            SelectedTaggerModel = TaggerModels.FirstOrDefault(model =>
                                      string.Equals(model.DisplayName, selectedName, StringComparison.OrdinalIgnoreCase))
                                  ?? TaggerModels.FirstOrDefault();
        }

        private void AddLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (message.StartsWith("Downloading"))
                {
                    var existingLine = LogLines.FirstOrDefault(line => line.StartsWith("Downloading"));
                    if (existingLine != null)
                    {
                        var index = LogLines.IndexOf(existingLine);
                        LogLines[index] = message;
                    }
                    else
                    {
                        LogLines.Add(message);
                    }
                }
                else
                {
                    LogLines.Add(message);
                }

                while (LogLines.Count > MaxLogLines)
                {
                    LogLines.RemoveAt(0);
                }
            });
        }

        private void BrowseForFolder(Action<string> setPathAction)
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                setPathAction(dialog.FileName);
            }
        }

        private void AddTagger()
        {
            var window = new AddTaggerWindow
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (window.ShowDialog() == true && window.ResultModel != null)
            {
                var newModel = window.ResultModel;
                newModel.SeriesId = TaggerSeriesCatalog.OnnxMultiLabelSeriesId;
                newModel.BackendKind = TaggerBackendKind.OnnxMultiLabel;
                var existing = _allTaggerModels.FirstOrDefault(model =>
                    string.Equals(model.DisplayName, newModel.DisplayName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    var result = MessageBox.Show(
                        LocalizationManager.Format("已存在同名打标器：{0}，是否覆盖？", existing.DisplayName),
                        LocalizationManager.Translate("添加打标器"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }

                    _allTaggerModels.Remove(existing);
                }

                _allTaggerModels.Add(newModel);
                SelectedTaggerSeries = TaggerSeriesCatalog.GetSeries(TaggerSeriesCatalog.OnnxMultiLabelSeriesId);
                RefreshTaggerModels();
                SelectedTaggerModel = newModel;
            }
        }

        private List<string> ParsePriorityTags()
        {
            if (string.IsNullOrWhiteSpace(PriorityTags))
            {
                return new List<string>();
            }

            var separators = new[] { ',', ';', '\n', '\r', '\uFF1B', '\uFF0C' };
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in PriorityTags.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var cleaned = NormalizeInputTag(tag);
                if (string.IsNullOrEmpty(cleaned))
                {
                    continue;
                }

                if (seen.Add(cleaned))
                {
                    result.Add(tag.Trim());
                }
            }

            return result;
        }

        private List<string> ParseCustomTags()
        {
            if (string.IsNullOrWhiteSpace(CustomTags))
            {
                return new List<string>();
            }

            var separators = new[] { ',', ';', '\n', '\r', '\uFF1B', '\uFF0C' };
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in CustomTags.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var cleaned = NormalizeInputTag(tag);
                if (string.IsNullOrEmpty(cleaned))
                {
                    continue;
                }

                if (seen.Add(cleaned))
                {
                    result.Add(tag.Trim());
                }
            }

            return result;
        }

        private static string NormalizeInputTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            var normalized = tag.Replace('_', ' ').Trim().ToLowerInvariant();
            normalized = string.Concat(normalized.Where(c => !char.IsWhiteSpace(c)));
            return normalized;
        }

        public TaggerSeriesExecutionOptions BuildSeriesExecutionOptions()
        {
            if (SelectedTaggerSeries?.BackendKind == TaggerBackendKind.OpenAiCompatibleVisionCaption)
            {
                return new TaggerSeriesExecutionOptions
                {
                    BackendKind = TaggerBackendKind.OpenAiCompatibleVisionCaption,
                    ToriiGateOptions = new ToriiGateTaggingOptions
                    {
                        InputDirectory = InputDirectory ?? string.Empty,
                        Endpoint = ToriiGateEndpoint,
                        ModelName = ToriiGateModelName,
                        ApiToken = ToriiGateApiToken,
                        CaptionFormat = TaggerSeriesCatalog.GetToriiGateCaptionFormat(ToriiGateCaptionFormatId),
                        MaxMegapixels = ToriiGateMaxMegapixels,
                        MaxTokens = ToriiGateMaxTokens,
                        Temperature = ToriiGateTemperature,
                        MaxParallelRequests = ToriiGateMaxParallelRequests,
                        CustomTags = ParseCustomTags(),
                        ExistingTxtHandlingMode = SelectedExistingTxtConflictOption?.Mode ?? ExistingTxtHandlingMode.Overwrite
                    }
                };
            }

            if (SelectedTaggerModel == null)
            {
                throw new InvalidOperationException("未选择打标模型。");
            }

            var categoryOptions = new TagCategoryOptions
            {
                IncludeGeneral = IncludeGeneral,
                IncludeArtist = IncludeArtist,
                IncludeCopyright = IncludeCopyright,
                IncludeCharacter = IncludeCharacter,
                IncludeMeta = IncludeMeta,
                IncludeQuality = IncludeQuality,
                IncludeRating = IncludeRating,
                IncludeModel = IncludeModel
            };

            return new TaggerSeriesExecutionOptions
            {
                BackendKind = TaggerBackendKind.OnnxMultiLabel,
                OnnxOptions = new TaggingPipelineExecutionOptions
                {
                    InputDirectory = InputDirectory ?? string.Empty,
                    ModelUrl = SelectedTaggerModel.ModelUrl,
                    TagsUrl = SelectedTaggerModel.TagsUrl,
                    DisplayName = SelectedTaggerModel.DisplayName,
                    PreferGpu = PreferGpu,
                    GeneralThreshold = GeneralThreshold,
                    CharacterThreshold = CharacterThreshold,
                    InputSize = SelectedTaggerModel.InputSize,
                    InputFormat = SelectedTaggerModel.InputFormat,
                    CategoryOptions = categoryOptions,
                    PriorityTags = ParsePriorityTags(),
                    CustomTags = ParseCustomTags(),
                    ExistingTxtHandlingMode = SelectedExistingTxtConflictOption?.Mode ?? ExistingTxtHandlingMode.Overwrite
                }
            };
        }

        private async Task StartProcessing()
        {
            IsIdle = false;
            (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            LogLines.Clear();
            ProgressValue = 0;

            try
            {
                await TaggerSeriesExecutor.ExecuteAsync(
                    BuildSeriesExecutionOptions(),
                    AddLog,
                    value => ProgressValue = value,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                AddLog("An error occurred: " + ex.Message);
            }
            finally
            {
                IsIdle = true;
                (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
                ProgressValue = 100;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            CommandManager.InvalidateRequerySuggested();
        }
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
