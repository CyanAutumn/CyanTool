using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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

        private readonly TaggingProcessor _taggingProcessor;
        private const int MaxLogLines = 1000;

        public string InputDirectory { get => _inputDirectory; set => SetField(ref _inputDirectory, value); }
        public float GeneralThreshold { get => _generalThreshold; set => SetField(ref _generalThreshold, value); }
        public float CharacterThreshold { get => _characterThreshold; set => SetField(ref _characterThreshold, value); }
        public bool IsIdle { get => _isIdle; set => SetField(ref _isIdle, value); }
        public double ProgressValue { get => _progressValue; set => SetField(ref _progressValue, value); }

        public ObservableCollection<string> LogLines { get; }
        public ObservableCollection<TaggerModel> TaggerModels { get; }
        public ObservableCollection<ExistingTxtConflictOption> ExistingTxtConflictOptions { get; }
        public TaggerModel SelectedTaggerModel { get => _selectedTaggerModel; set => SetField(ref _selectedTaggerModel, value); }
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
        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.InPlace;
        public string PipelineInputPath { get => InputDirectory ?? string.Empty; set => InputDirectory = value ?? string.Empty; }
        public string PipelineOutputPath { get => InputDirectory ?? string.Empty; set => InputDirectory = value ?? string.Empty; }

        public ICommand BrowseInputCommand { get; }
        public ICommand StartTaggingCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand CheckCudaCommand { get; }
        public ICommand AddTaggerCommand { get; }

        public TaggingViewModel()
        {
            _taggingProcessor = new TaggingProcessor();
            LogLines = new ObservableCollection<string>();
            TaggerModels = new ObservableCollection<TaggerModel>
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
                new TaggerModel { DisplayName = "cl_tagger_1_02", ModelUrl = "https://huggingface.co/cella110n/cl_tagger/resolve/main/cl_tagger_1_02/model.onnx", TagsUrl = "https://huggingface.co/cella110n/cl_tagger/raw/main/cl_tagger_1_02/tag_mapping.json", InputSize = 448, InputFormat = "NCHW" }
            };
            SelectedTaggerModel = TaggerModels.FirstOrDefault();
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
            CheckCudaCommand = new RelayCommand(async _ => await CheckOrInstallCudaAsync(), _ => IsIdle);
            AddTaggerCommand = new RelayCommand(_ => AddTagger(), _ => IsIdle);
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
                var existing = TaggerModels.FirstOrDefault(model =>
                    string.Equals(model.DisplayName, newModel.DisplayName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    var result = MessageBox.Show(
                        $"已存在同名打标器：{existing.DisplayName}，是否覆盖？",
                        "添加打标器",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }

                    TaggerModels.Remove(existing);
                }

                TaggerModels.Add(newModel);
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

        private async Task CheckOrInstallCudaAsync()
        {
            await Task.Run(() =>
            {
                var check = TaggingProcessor.CheckCudaEnvironment();
                AddLog($"CUDA 检查结果: {check.Details}");

                if (check.NvidiaDriverDetected && check.CudaExecutionProviderAvailable)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("CUDA 环境可用，已可以优先使用 GPU(CUDA) 打标。", "CUDA 检查", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    return;
                }

                if (!check.CudaAppendApiSupported)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            $"{check.Details}\n\n检测到当前程序的 onnxruntime.dll 不支持 CUDA 执行提供器。\n这不是安装 CUDA Toolkit 能解决的问题，请更新/重新发布带 `Microsoft.ML.OnnxRuntime.Gpu` 的版本。",
                            "CUDA 检查",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    });
                    return;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var result = MessageBox.Show(
                        $"{check.Details}\n\n是否尝试使用 winget 安装 CUDA Toolkit 12.x（推荐 12.4）？\n（需要联网，可能需要管理员权限；安装完成后请重启软件。注意：cuDNN 需要另外安装。）",
                        "CUDA 检查/安装",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        TryLaunchWingetCudaInstall();
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        OpenCudaInstallPages(check.NvidiaDriverDetected);
                    }
                });
            });
        }

        private static void TryLaunchWingetCudaInstall()
        {
            const string wingetArgs =
                "/k winget install -e --id Nvidia.CUDA -s winget --version 12.4 --accept-source-agreements --accept-package-agreements || winget show --id Nvidia.CUDA -s winget --versions || winget search cuda -s winget";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = wingetArgs,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"启动 winget 失败：{ex.Message}\n\n将为你打开 CUDA 下载页面，请手动安装。",
                    "CUDA 安装",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                OpenCudaInstallPages(nvidiaDriverDetected: true);
            }
        }

        private static void OpenCudaInstallPages(bool nvidiaDriverDetected)
        {
            try
            {
                if (!nvidiaDriverDetected)
                {
                    Process.Start(new ProcessStartInfo("https://www.nvidia.com/Download/index.aspx") { UseShellExecute = true });
                }

                Process.Start(new ProcessStartInfo("https://developer.nvidia.com/cuda-downloads") { UseShellExecute = true });
                Process.Start(new ProcessStartInfo("https://developer.nvidia.com/cudnn") { UseShellExecute = true });
            }
            catch
            {

            }
        }

        private async Task StartProcessing()
        {
            IsIdle = false;
            (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            LogLines.Clear();
            ProgressValue = 0;

            var progress = new Progress<string>(update =>
            {
                AddLog(update);
                if (update.StartsWith("Processed"))
                {
                    try
                    {
                        var parts = update.Split('[', '/', ']');
                        if (parts.Length > 2)
                        {
                            double current = double.Parse(parts[1]);
                            double total = double.Parse(parts[2]);
                            ProgressValue = (current / total) * 100;
                        }
                    }
                    catch {                             }
                }
            });

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

            try
            {
                var priorityTags = ParsePriorityTags();
                var customTags = ParseCustomTags();

                await Task.Run(async () =>
                {
                    await _taggingProcessor.InitializeAsync(SelectedTaggerModel.ModelUrl, SelectedTaggerModel.TagsUrl, SelectedTaggerModel.DisplayName, PreferGpu, progress);
                    await _taggingProcessor.ProcessDirectoryAsync(
                        InputDirectory,
                        GeneralThreshold,
                        CharacterThreshold,
                        SelectedTaggerModel.InputSize,
                        SelectedTaggerModel.InputFormat,
                        categoryOptions,
                        priorityTags,
                        customTags,
                        SelectedExistingTxtConflictOption?.Mode ?? ExistingTxtHandlingMode.Overwrite,
                        progress);
                });
                AddLog("Tagging complete.");
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            CommandManager.InvalidateRequerySuggested();
        }
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
