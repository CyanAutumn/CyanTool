using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class ImageClusteringViewModel : PipelineNodeViewModelBase, IPipelineDirectoryNode
    {
        public override string DisplayName => "图片聚类";
        private const string FeatureTypeHybridDisplay = "Style+Content（偏画风+偏内容）";
        private const string FeatureTypeStyleDisplay = "Style（偏画风）";
        private const string FeatureTypeOriginalCsdStyleDisplay = "Style（原版 CSD）";
        private const string FeatureTypeGrayStyleDisplay = "Style（灰度去色，更偏笔触）";
        private const string FeatureTypeStrictGrayStyleDisplay = "Style（灰度去色，严格画风）";
        private const string FeatureTypeHeadLineStyleDisplay = "Style（头部画法/线条，训练推荐）";
        private const string FeatureTypeStyleColorDownweightDisplay = "Style（颜色降权，保留少量色彩）";
        private const string FeatureTypeContentDisplay = "Content（偏内容）";
        private const string ClusteringSpaceRawDisplay = "高维向量（推荐）";
        private const string ClusteringSpaceUmapDisplay = "UMAP 2D（兼容参考）";
        private const string ClusteringSpaceStyleBrushDisplay = "高维 Style（更偏笔触）";
        private const int DefaultNumClusters = 40;
        private const int DefaultMinClusterSize = 10;
        private const int DefaultUmapNNeighbors = 15;
        private const double DefaultUmapMinDist = 0.1d;
        private const double DefaultUmapSpread = 1.0d;
        private const double OutlierIqrMultiplier = 3.0d;
        private const int OutlierFilterMinClusterSize = 8;
        private const double DefaultColorDownweightStyleRetention = 0.35d;
        private const double DefaultMinCentroidSimilarity = 0.82d;
        private const string DefaultSelectedFeatureType = FeatureTypeStyleDisplay;
        private const string DefaultSelectedClusteringSpace = ClusteringSpaceUmapDisplay;
        private const string DefaultSelectedAlgorithm = "KMeans";
        private const string DefaultSelectedComputeDevice = "GPU";
        private const string CsdModelRelativePath = "Models/csd/csd_clip_model.onnx";
        private const string HeadDetectionModelRelativePath = "Models/anime_head_detection/head_detect_v0_s.onnx";
        private const string EyeDetectionModelRelativePath = "Models/anime_eye_detection/eye_detect_v1.0_s.onnx";
        private const string CsdModelDownloadUrl = "https://huggingface.co/yuxi-liu-wired/CSD/resolve/main/csd_clip_model.onnx";
        private const string CsdModelPageUrl = "https://huggingface.co/yuxi-liu-wired/CSD";
        private const string ClusterDistributionFileName = "cluster_distribution.png";
        private static readonly string SettingsDirectory = Path.Combine(AppContext.BaseDirectory, "Config");
        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "image_clustering.json");
        private static readonly JsonSerializerOptions SettingsSerializerOptions = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };
        private static readonly HttpClient CsdModelHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        private string _inputPath = string.Empty;
        public string InputPath
        {
            get => _inputPath;
            set
            {
                string previousInput = _inputPath;
                if (SetField(ref _inputPath, value))
                {
                    OutputPath = ToolOutputDirectoryDefaults.RefreshForInputChange(OutputPath, previousInput, value, DisplayName);
                    RefreshCommandStates();
                }
            }
        }

        private string _outputPath = string.Empty;
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (SetField(ref _outputPath, value))
                {
                    RefreshCommandStates();
                }
            }
        }


        private int _numClusters = DefaultNumClusters;
        public int NumClusters
        {
            get => _numClusters;
            set => SetField(ref _numClusters, value);
        }

        private bool _useBestKBySilhouette;
        public bool UseBestKBySilhouette
        {
            get => _useBestKBySilhouette;
            set => SetField(ref _useBestKBySilhouette, value);
        }

        private int _minClusterSize = DefaultMinClusterSize;
        public int MinClusterSize
        {
            get => _minClusterSize;
            set => SetField(ref _minClusterSize, value);
        }

        private bool _filterOutliersForExport;
        public bool FilterOutliersForExport
        {
            get => _filterOutliersForExport;
            set => SetField(ref _filterOutliersForExport, value);
        }

        private double _colorDownweightStyleRetention = DefaultColorDownweightStyleRetention;
        public double ColorDownweightStyleRetention
        {
            get => _colorDownweightStyleRetention;
            set
            {
                double clamped = Math.Clamp(value, 0d, 1d);
                if (SetField(ref _colorDownweightStyleRetention, clamped))
                {
                    OnPropertyChanged(nameof(ColorDownweightStyleRetentionPercentText));
                }
            }
        }

        public string ColorDownweightStyleRetentionPercentText => $"{ColorDownweightStyleRetention * 100d:F0}%";

        private bool _enableTwoStageStyleClustering;
        public bool EnableTwoStageStyleClustering
        {
            get => _enableTwoStageStyleClustering;
            set => SetField(ref _enableTwoStageStyleClustering, value);
        }

        private bool _useMinSimilarityForUncertain;
        public bool UseMinSimilarityForUncertain
        {
            get => _useMinSimilarityForUncertain;
            set => SetField(ref _useMinSimilarityForUncertain, value);
        }

        private double _minCentroidSimilarity = DefaultMinCentroidSimilarity;
        public double MinCentroidSimilarity
        {
            get => _minCentroidSimilarity;
            set
            {
                double clamped = Math.Clamp(value, 0d, 0.999d);
                if (SetField(ref _minCentroidSimilarity, clamped))
                {
                    OnPropertyChanged(nameof(MinCentroidSimilarityPercentText));
                }
            }
        }

        public string MinCentroidSimilarityPercentText => $"{MinCentroidSimilarity * 100d:F0}%";

        private int _umapNNeighbors = DefaultUmapNNeighbors;
        public int UmapNNeighbors
        {
            get => _umapNNeighbors;
            set => SetField(ref _umapNNeighbors, value);
        }

        private double _umapMinDist = DefaultUmapMinDist;
        public double UmapMinDist
        {
            get => _umapMinDist;
            set => SetField(ref _umapMinDist, value);
        }

        private double _umapSpread = DefaultUmapSpread;
        public double UmapSpread
        {
            get => _umapSpread;
            set => SetField(ref _umapSpread, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetField(ref _progress, value);
        }

        public List<string> FeatureTypes { get; } = new()
        {
            FeatureTypeHybridDisplay,
            FeatureTypeStyleDisplay,
            FeatureTypeOriginalCsdStyleDisplay,
            FeatureTypeGrayStyleDisplay,
            FeatureTypeStrictGrayStyleDisplay,
            FeatureTypeHeadLineStyleDisplay,
            FeatureTypeStyleColorDownweightDisplay,
            FeatureTypeContentDisplay
        };

        private string _selectedFeatureType = DefaultSelectedFeatureType;
        public string SelectedFeatureType
        {
            get => _selectedFeatureType;
            set
            {
                if (SetField(ref _selectedFeatureType, ToFeatureTypeDisplay(value)))
                {
                    OnPropertyChanged(nameof(IsColorDownweightSelected));
                }
            }
        }

        public bool IsColorDownweightSelected =>
            GetFeatureTypeKey(SelectedFeatureType) == "StyleColorDownweight";

        public List<string> ClusteringSpaces { get; } = new()
        {
            ClusteringSpaceRawDisplay,
            ClusteringSpaceUmapDisplay,
            ClusteringSpaceStyleBrushDisplay
        };

        private string _selectedClusteringSpace = DefaultSelectedClusteringSpace;
        public string SelectedClusteringSpace
        {
            get => _selectedClusteringSpace;
            set => SetField(ref _selectedClusteringSpace, value);
        }

        public List<string> Algorithms { get; } = new() { "KMeans", "SphericalKMeans", "HDBSCAN" };

        private string _selectedAlgorithm = DefaultSelectedAlgorithm;
        public string SelectedAlgorithm
        {
            get => _selectedAlgorithm;
            set
            {
                if (SetField(ref _selectedAlgorithm, value))
                {
                    OnPropertyChanged(nameof(IsKMeansSelected));
                    OnPropertyChanged(nameof(IsHdbscanSelected));
                }
            }
        }

        public bool IsKMeansSelected => SelectedAlgorithm == "KMeans" || SelectedAlgorithm == "SphericalKMeans";
        public bool IsHdbscanSelected => SelectedAlgorithm == "HDBSCAN";

        public List<string> ComputeDevices { get; } = new() { "CPU", "GPU" };

        private string _selectedComputeDevice = DefaultSelectedComputeDevice;
        public string SelectedComputeDevice
        {
            get => _selectedComputeDevice;
            set => SetField(ref _selectedComputeDevice, value);
        }

        private string _log = string.Empty;
        public string Log
        {
            get => _log;
            set => SetField(ref _log, value);
        }

        private bool _isClustering;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;
        private bool _shouldClearInputDirectoryAfterCompletion;
        public bool IsClustering
        {
            get => _isClustering;
            set
            {
                if (SetField(ref _isClustering, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set => SetField(ref _isWorkflowMode, value);
        }

        public bool HasInheritedInputPath
        {
            get => _hasInheritedInputPath;
            set => SetField(ref _hasInheritedInputPath, value);
        }

        public bool ShouldClearInputDirectoryAfterCompletion
        {
            get => _shouldClearInputDirectoryAfterCompletion;
            set => SetField(ref _shouldClearInputDirectoryAfterCompletion, value);
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;
        public string PipelineInputPath { get => InputPath; set => InputPath = value ?? string.Empty; }
        public string PipelineOutputPath { get => OutputPath; set => OutputPath = value ?? string.Empty; }

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand MoveFilesToTopLevelCommand { get; }
        public ICommand ResetParametersCommand { get; }

        public ImageClusteringViewModel()
        {
            SelectInputCommand = new RelayCommand(_ => SelectInputFolder());
            SelectOutputCommand = new RelayCommand(_ => SelectOutputFolder());
            StartCommand = new RelayCommand(async () => await RunClusteringAsync(showDialogs: true, CancellationToken.None),
                () => !string.IsNullOrEmpty(InputPath) && !IsClustering);
            MoveFilesToTopLevelCommand = new RelayCommand(async () => await MoveFilesToTopLevelAsync(), () => !string.IsNullOrEmpty(InputPath) && !IsClustering);
            ResetParametersCommand = new RelayCommand(_ => ResetParametersToDefaults(), _ => !IsClustering);

            LoadSettings();
        }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            _ = context;

            return RunClusteringAsync(showDialogs: false, token);
        }

        public override PipelineNodeViewModelBase Clone()
        {
            return new ImageClusteringViewModel
            {
                InputPath = InputPath,
                OutputPath = OutputPath,
                NumClusters = NumClusters,
                UseBestKBySilhouette = UseBestKBySilhouette,
                MinClusterSize = MinClusterSize,
                FilterOutliersForExport = FilterOutliersForExport,
                ColorDownweightStyleRetention = ColorDownweightStyleRetention,
                EnableTwoStageStyleClustering = EnableTwoStageStyleClustering,
                UseMinSimilarityForUncertain = UseMinSimilarityForUncertain,
                MinCentroidSimilarity = MinCentroidSimilarity,
                UmapNNeighbors = UmapNNeighbors,
                UmapMinDist = UmapMinDist,
                UmapSpread = UmapSpread,
                SelectedAlgorithm = SelectedAlgorithm,
                SelectedFeatureType = SelectedFeatureType,
                SelectedClusteringSpace = SelectedClusteringSpace,
                SelectedComputeDevice = SelectedComputeDevice,
                ShouldClearInputDirectoryAfterCompletion = ShouldClearInputDirectoryAfterCompletion
            };
        }

        private void RefreshCommandStates()
        {
            (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveFilesToTopLevelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ResetParametersCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ResetParametersToDefaults()
        {
            SelectedComputeDevice = DefaultSelectedComputeDevice;
            SelectedFeatureType = DefaultSelectedFeatureType;
            SelectedAlgorithm = DefaultSelectedAlgorithm;
            SelectedClusteringSpace = DefaultSelectedClusteringSpace;
            NumClusters = DefaultNumClusters;
            UseBestKBySilhouette = false;
            MinClusterSize = DefaultMinClusterSize;
            FilterOutliersForExport = false;
            ColorDownweightStyleRetention = DefaultColorDownweightStyleRetention;
            EnableTwoStageStyleClustering = false;
            UseMinSimilarityForUncertain = false;
            MinCentroidSimilarity = DefaultMinCentroidSimilarity;
            UmapNNeighbors = DefaultUmapNNeighbors;
            UmapMinDist = DefaultUmapMinDist;
            UmapSpread = DefaultUmapSpread;
            AppendLog("参数已初始化为默认值。");
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return;
                }

                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<ImageClusteringSettings>(json, SettingsSerializerOptions);
                if (settings == null)
                {
                    return;
                }

                InputPath = settings.InputPath ?? string.Empty;
                OutputPath = settings.OutputPath ?? string.Empty;
                NumClusters = settings.NumClusters <= 0 ? DefaultNumClusters : settings.NumClusters;
                UseBestKBySilhouette = settings.UseBestKBySilhouette;
                MinClusterSize = settings.MinClusterSize <= 0 ? DefaultMinClusterSize : settings.MinClusterSize;
                FilterOutliersForExport = settings.FilterOutliersForExport;
                ColorDownweightStyleRetention =
                    settings.ColorDownweightStyleRetention < 0d || settings.ColorDownweightStyleRetention > 1d
                        ? DefaultColorDownweightStyleRetention
                        : settings.ColorDownweightStyleRetention;
                EnableTwoStageStyleClustering = settings.EnableTwoStageStyleClustering;
                UseMinSimilarityForUncertain = settings.UseMinSimilarityForUncertain;
                MinCentroidSimilarity =
                    settings.MinCentroidSimilarity < 0d || settings.MinCentroidSimilarity >= 1d
                        ? DefaultMinCentroidSimilarity
                        : settings.MinCentroidSimilarity;
                UmapNNeighbors = settings.UmapNNeighbors <= 0 ? DefaultUmapNNeighbors : settings.UmapNNeighbors;
                UmapMinDist = settings.UmapMinDist;
                UmapSpread = settings.UmapSpread <= 0d ? DefaultUmapSpread : settings.UmapSpread;
                SelectedFeatureType = ToFeatureTypeDisplay(settings.SelectedFeatureType);
                SelectedAlgorithm = Algorithms.Contains(settings.SelectedAlgorithm ?? string.Empty)
                    ? settings.SelectedAlgorithm!
                    : DefaultSelectedAlgorithm;
                SelectedClusteringSpace = ClusteringSpaces.Contains(settings.SelectedClusteringSpace ?? string.Empty)
                    ? settings.SelectedClusteringSpace!
                    : DefaultSelectedClusteringSpace;
                SelectedComputeDevice = ComputeDevices.Contains(settings.SelectedComputeDevice ?? string.Empty)
                    ? settings.SelectedComputeDevice!
                    : DefaultSelectedComputeDevice;
            }
            catch (Exception ex)
            {
                AppendLog($"加载图片聚类参数失败: {ex.Message}");
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                var settings = new ImageClusteringSettings
                {
                    InputPath = InputPath,
                    OutputPath = OutputPath,
                    NumClusters = NumClusters,
                    UseBestKBySilhouette = UseBestKBySilhouette,
                    MinClusterSize = MinClusterSize,
                    FilterOutliersForExport = FilterOutliersForExport,
                    ColorDownweightStyleRetention = ColorDownweightStyleRetention,
                    EnableTwoStageStyleClustering = EnableTwoStageStyleClustering,
                    UseMinSimilarityForUncertain = UseMinSimilarityForUncertain,
                    MinCentroidSimilarity = MinCentroidSimilarity,
                    UmapNNeighbors = UmapNNeighbors,
                    UmapMinDist = UmapMinDist,
                    UmapSpread = UmapSpread,
                    SelectedFeatureType = SelectedFeatureType,
                    SelectedAlgorithm = SelectedAlgorithm,
                    SelectedClusteringSpace = SelectedClusteringSpace,
                    SelectedComputeDevice = SelectedComputeDevice
                };

                var json = JsonSerializer.Serialize(settings, SettingsSerializerOptions);
                var tempPath = SettingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, SettingsFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                AppendLog($"保存图片聚类参数失败: {ex.Message}");
            }
        }

        public async Task SaveStateOnCloseAsync()
        {
            await SaveSettingsAsync();
        }

        private sealed class ImageClusteringSettings
        {
            public string? InputPath { get; set; }
            public string? OutputPath { get; set; }
            public int NumClusters { get; set; } = DefaultNumClusters;
            public bool UseBestKBySilhouette { get; set; }
            public int MinClusterSize { get; set; } = DefaultMinClusterSize;
            public bool FilterOutliersForExport { get; set; }
            public double ColorDownweightStyleRetention { get; set; } = DefaultColorDownweightStyleRetention;
            public bool EnableTwoStageStyleClustering { get; set; }
            public bool UseMinSimilarityForUncertain { get; set; }
            public double MinCentroidSimilarity { get; set; } = DefaultMinCentroidSimilarity;
            public int UmapNNeighbors { get; set; } = DefaultUmapNNeighbors;
            public double UmapMinDist { get; set; } = DefaultUmapMinDist;
            public double UmapSpread { get; set; } = DefaultUmapSpread;
            public string? SelectedFeatureType { get; set; } = DefaultSelectedFeatureType;
            public string? SelectedAlgorithm { get; set; } = DefaultSelectedAlgorithm;
            public string? SelectedClusteringSpace { get; set; } = DefaultSelectedClusteringSpace;
            public string? SelectedComputeDevice { get; set; } = DefaultSelectedComputeDevice;
        }

        private void AppendLog(string message)
        {
            void Update() => Log += $"{DateTime.Now:HH:mm:ss} {message}\n";

            if (Application.Current.Dispatcher.CheckAccess())
            {
                Update();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(Update);
            }

            LogToPipeline($"[图片聚类] {message}");
        }

        private async Task RunClusteringAsync(bool showDialogs, CancellationToken token)
        {
            OutputPath = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputPath, InputPath, DisplayName);
            if (IsClustering)
            {
                return;
            }

            IsClustering = true;
            Progress = 0;
            Log = string.Empty;
            bool success = false;

            try
            {
                if (!await EnsureCsdModelReadyAsync(showDialogs, token))
                {
                    return;
                }

                success = await Task.Run(() => RunClusteringWork(showDialogs, token), token);
            }
            finally
            {
                if (success)
                {
                    TryClearInputDirectoryAfterCompletion();
                }

                IsClustering = false;
            }
        }

        private void TryClearInputDirectoryAfterCompletion()
        {
            if (!ShouldClearInputDirectoryAfterCompletion)
            {
                return;
            }

            InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.TryClearContents(InputPath, new[] { OutputPath });
            AppendLog(result.Message);
        }

        private bool RunClusteringWork(bool showDialogs, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                AppendLog("开始聚类");

                string modelPath = Path.GetFullPath(CsdModelRelativePath);

                string inputPathFull = NormalizeDirectoryPath(InputPath);
                string outputPathFull = NormalizeDirectoryPath(OutputPath);
                if (inputPathFull.Equals(outputPathFull, StringComparison.OrdinalIgnoreCase))
                {
                    const string samePathMessage = "输入路径与输出路径不能相同，请将输出路径设置为其它路径。";
                    AppendLog(samePathMessage);
                    if (showDialogs)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show(samePathMessage, "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    return false;
                }

                if (IsSameOrSubDirectory(outputPathFull, inputPathFull))
                {
                    AppendLog("检测到输出路径位于输入路径内，已自动在特征提取阶段排除输出路径，避免旧结果污染聚类。");
                }

                bool useGpu = SelectedComputeDevice.Equals("GPU", StringComparison.OrdinalIgnoreCase);
                AppendLog($"选择计算设备: {SelectedComputeDevice}");
                bool useKMeansFamily = SelectedAlgorithm == "KMeans" || SelectedAlgorithm == "SphericalKMeans";
                string selectedFeatureTypeKey = GetFeatureTypeKey(SelectedFeatureType);
                bool useHeadLineStyleFeature = UsesHeadLineStyleVectors(SelectedFeatureType);
                bool useStrictGrayStyleFeature = selectedFeatureTypeKey == "StrictGrayStyle";
                bool requestedTwoStageStyleClustering = EnableTwoStageStyleClustering;
                bool useTwoStageStyleClustering = requestedTwoStageStyleClustering && useKMeansFamily && !useHeadLineStyleFeature;
                if (requestedTwoStageStyleClustering && !useKMeansFamily)
                {
                    AppendLog("二阶段风格聚类当前仅支持 KMeans / SphericalKMeans，已忽略该选项。");
                }
                else if (requestedTwoStageStyleClustering && useHeadLineStyleFeature)
                {
                    AppendLog("头部渲染/线条模式已内置局部风格描述子组合，已忽略二阶段聚类选项。");
                }

                bool includeGrayStyle = UsesGrayStyleVectors(SelectedFeatureType, useTwoStageStyleClustering);
                if (includeGrayStyle)
                {
                    AppendLog("已启用灰度 Style 分支：特征提取将额外进行一次灰度图推理，速度会变慢。");
                }
                if (useHeadLineStyleFeature)
                {
                    AppendLog("已启用头部渲染/线条实验模式：先裁剪头部区域，仅用裁剪结果判断画风，再复制裁剪前原图到最终文件夹。");
                    if (SelectedAlgorithm != "SphericalKMeans" || !SelectedClusteringSpace.Equals(ClusteringSpaceStyleBrushDisplay, StringComparison.Ordinal) || !UseBestKBySilhouette)
                    {
                        AppendLog("复现实验推荐设置：算法=SphericalKMeans，聚类空间=高维 Style（更偏笔触），勾选“最优”。");
                    }
                }

                var processor = new Utils.ImageClusteringProcessor(modelPath, useGpu, AppendLog);
                var processResult = processor.ProcessFolderWithSummary(InputPath,
                    p => Application.Current.Dispatcher.Invoke(() => Progress = p * 50),
                    error =>
                    {
                        AppendLog(error);
                        if (showDialogs)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                                MessageBox.Show(error, "处理失败", MessageBoxButton.OK, MessageBoxImage.Warning));
                        }
                    },
                    new ImageClusteringExtractionOptions
                    {
                        IncludeBaseEmbeddings = !useHeadLineStyleFeature,
                        IncludeGrayStyle = includeGrayStyle,
                        IncludeGrayStructureStyle = UsesGrayStructureStyleVectors(SelectedFeatureType),
                        IncludeHeadLineStyle = useHeadLineStyleFeature,
                        HeadDetectionModelPath = Path.GetFullPath(HeadDetectionModelRelativePath),
                        HeadValidationEyeModelPath = Path.GetFullPath(EyeDetectionModelRelativePath)
                    },
                    new[] { outputPathFull });
                var imageResults = processResult.Results;
                if (useHeadLineStyleFeature)
                {
                    var summary = processResult.Summary;
                    AppendLog(
                        $"头部渲染/线条统计：成功处理 {summary.ProcessedImages}/{summary.TotalImages} 张；可靠头部 {summary.HeadDetectedCrops} 张，低可信/兜底 {summary.HeadFallbackCrops} 张，失败 {summary.FailedImages} 张。");
                }

                token.ThrowIfCancellationRequested();

                if (imageResults.Count == 0)
                {
                    AppendLog("未成功处理任何图片");
                    if (showDialogs)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show("未成功处理任何图片，请检查图片格式或模型是否正确。", "信息",
                                MessageBoxButton.OK, MessageBoxImage.Information));
                    }
                    return false;
                }

                bool useStyleBrushSpace = SelectedClusteringSpace.Equals(ClusteringSpaceStyleBrushDisplay, StringComparison.Ordinal);
                bool isSelectedStyleLike = IsStyleLikeFeatureType(SelectedFeatureType);
                float colorRetention = (float)ColorDownweightStyleRetention;
                List<float[]> dataPoints = BuildFeatureVectors(imageResults, SelectedFeatureType, colorRetention);
                string effectiveFeatureType;
                List<float[]> clusteringSourcePoints;
                List<float[]>? headStyleMergePoints = null;
                List<float[]>? coarseStagePoints = null;

                if (useTwoStageStyleClustering)
                {
                    effectiveFeatureType = ResolveTwoStageFineFeatureType(SelectedFeatureType);
                    clusteringSourcePoints = BuildFeatureVectors(imageResults, effectiveFeatureType, colorRetention);
                    coarseStagePoints = BuildFeatureVectors(imageResults, FeatureTypeGrayStyleDisplay, colorRetention);

                    if (!isSelectedStyleLike)
                    {
                        AppendLog("二阶段风格聚类仅支持 Style 类特征，已忽略当前非 Style 特征选择，改用灰度 Style -> 彩色 Style。");
                    }
                    else if (UsesGrayStructureStyleVectors(SelectedFeatureType))
                    {
                        AppendLog("当前使用二阶段风格聚类：先按灰度 Style 粗分，再用彩色 Style 细分，避免颜色完全丢失。");
                    }
                    else
                    {
                        AppendLog($"当前使用二阶段风格聚类：第一阶段灰度 Style，第二阶段 {effectiveFeatureType}。");
                    }

                    if (SelectedClusteringSpace.Equals(ClusteringSpaceUmapDisplay, StringComparison.Ordinal))
                    {
                        AppendLog("二阶段风格聚类固定使用高维 Style 向量分簇，UMAP 仅用于生成分布图。");
                    }
                }
                else
                {
                    effectiveFeatureType = useStyleBrushSpace
                        ? (isSelectedStyleLike ? SelectedFeatureType : FeatureTypeStyleDisplay)
                        : SelectedFeatureType;
                    clusteringSourcePoints = effectiveFeatureType.Equals(SelectedFeatureType, StringComparison.Ordinal)
                        ? dataPoints
                        : BuildFeatureVectors(imageResults, effectiveFeatureType, colorRetention);

                    if (useStyleBrushSpace &&
                        !isSelectedStyleLike)
                    {
                        AppendLog("高维 Style 模式会强制使用 Style 类特征进行聚类，忽略当前非 Style 特征选择。");
                    }
                    else if (effectiveFeatureType.Equals(FeatureTypeGrayStyleDisplay, StringComparison.Ordinal))
                    {
                        AppendLog("当前使用灰度 Style 分支聚类：颜色信息会被尽量压掉。");
                    }
                    else if (effectiveFeatureType.Equals(FeatureTypeOriginalCsdStyleDisplay, StringComparison.Ordinal))
                    {
                        AppendLog("当前使用原版 CSD Style：仅使用模型 style_output，不叠加灰度、线条或同源分页增强。");
                    }
                    else if (effectiveFeatureType.Equals(FeatureTypeStyleColorDownweightDisplay, StringComparison.Ordinal))
                    {
                        AppendLog($"当前使用颜色降权 Style：原彩色 Style 保留系数 {ColorDownweightStyleRetention:F2}。");
                    }
                }

                if (useHeadLineStyleFeature)
                {
                    headStyleMergePoints = BuildHeadStyleMergeVectors(imageResults);
                }

                if (SelectedAlgorithm == "SphericalKMeans")
                {
                    AppendLog("当前使用 SphericalKMeans：按向量方向（余弦相似）聚类，更适合归一化 embedding。");
                }

                bool useUmapSpace = !useTwoStageStyleClustering &&
                    (SelectedAlgorithm == "HDBSCAN" ||
                     SelectedClusteringSpace.Equals(ClusteringSpaceUmapDisplay, StringComparison.Ordinal));

                if (SelectedAlgorithm == "HDBSCAN" &&
                    SelectedClusteringSpace.Equals(ClusteringSpaceRawDisplay, StringComparison.Ordinal))
                {
                    AppendLog("HDBSCAN 当前仅支持 2D 输入，已自动切换到 UMAP 2D 聚类空间。");
                }
                else if (SelectedAlgorithm == "HDBSCAN" && useStyleBrushSpace)
                {
                    AppendLog("HDBSCAN 当前不支持高维 Style 聚类，已自动切换到 Style 的 UMAP 2D。");
                }

                List<float[]> clusteringSpace = clusteringSourcePoints;
                if (useUmapSpace)
                {
                    if (clusteringSourcePoints.Count < 3)
                    {
                        AppendLog($"样本数量过少（{clusteringSourcePoints.Count}），跳过 UMAP，直接使用特征前两维。");
                        clusteringSpace = ProjectToTwoDimensions(clusteringSourcePoints);
                    }
                    else
                    {
                        try
                        {
                            var umapOptions = BuildUmapOptions(clusteringSourcePoints.Count);
                            AppendLog(
                                $"开始 UMAP 降维（metric=cosine, seed=42, n_neighbors={umapOptions.NNeighbors}, n_components=2, min_dist={umapOptions.MinDist:F3}, spread={umapOptions.Spread:F3}）: {effectiveFeatureType}");
                            AppendUmapImplementationLog(umapOptions);

                            clusteringSpace = Utils.UmapReducer.ReduceToTwoDimensions(
                                clusteringSourcePoints,
                                umapOptions,
                                token,
                                p => Application.Current.Dispatcher.Invoke(() => Progress = 50 + p * 25));
                        }
                        catch (Exception ex)
                        {

                            AppendLog($"UMAP 失败，回退 PCA。原因: {ex.Message}");
                            clusteringSpace = Utils.FeatureSpaceReducer.ReduceToTwoDimensionsPca(clusteringSourcePoints);
                        }
                    }

                    if (clusteringSpace.Count != dataPoints.Count || clusteringSpace.Any(v => v == null || v.Length != 2))
                    {
                        throw new InvalidOperationException("降维输出异常：UMAP/PCA 未生成有效 2D 特征。");
                    }
                }
                else
                {
                    if (useTwoStageStyleClustering)
                    {
                        AppendLog($"使用二阶段高维 Style 聚类（维度={clusteringSourcePoints[0].Length}），分布图会单独生成 UMAP 2D。");
                    }
                    else if (useStyleBrushSpace)
                    {
                        AppendLog($"使用高维 Style 向量直接聚类（维度={clusteringSourcePoints[0].Length}），分布图会单独生成 Style 的 UMAP 2D。");
                    }
                    else
                    {
                        AppendLog($"使用高维向量直接聚类（维度={clusteringSourcePoints[0].Length}）。");
                    }
                }

                int resolvedKMeansK = NumClusters;
                bool mergeKMeansClustersAfterClustering = false;
                bool useMacroStyleOptimalMode = false;
                int macroStyleMinClusterCount = 0;
                int macroStyleMaxClusterCount = 0;
                if (useKMeansFamily)
                {
                    if (UseBestKBySilhouette)
                    {
                        if (clusteringSpace.Count < 3)
                        {
                            resolvedKMeansK = Math.Clamp(clusteringSpace.Count, 1, 2);
                            AppendLog($"样本数量过少（{clusteringSpace.Count}），自动使用 K={resolvedKMeansK}。");
                        }
                        else
                        {
                            resolvedKMeansK = ResolveAutoMergeInitialClusterCount(clusteringSpace.Count);
                            mergeKMeansClustersAfterClustering = true;
                            useMacroStyleOptimalMode = IsStyleLikeFeatureType(effectiveFeatureType);
                            if (useMacroStyleOptimalMode)
                            {
                                (macroStyleMinClusterCount, macroStyleMaxClusterCount) =
                                    ResolveMacroStyleClusterRange(clusteringSpace.Count);
                                string styleMode = useHeadLineStyleFeature
                                    ? "头部画法/线条"
                                    : useStrictGrayStyleFeature
                                        ? "严格灰度画风"
                                        : "画风";
                                AppendLog(
                                    $"最优模式：{styleMode}先过分割为 K={resolvedKMeansK}，再做宏观层次合并；将在 {macroStyleMinClusterCount}-{macroStyleMaxClusterCount} 类范围内根据合并距离跃升自动停止，而不是合并到固定数量。");
                            }
                            else
                            {
                                AppendLog(
                                    $"最优模式：先自动过分割为 K={resolvedKMeansK}，随后根据簇中心邻近距离断点合并，最终文件夹数量由数据决定。");
                            }
                        }
                    }
                    else
                    {
                        AppendLog($"使用手动 K 值: {resolvedKMeansK}");
                    }
                }

                int[] assignments;
                if (useTwoStageStyleClustering)
                {
                    var twoStageResult = Utils.TwoStageStyleClusterer.Cluster(
                        coarseStagePoints ?? throw new InvalidOperationException("二阶段聚类缺少灰度 Style 特征。"),
                        clusteringSourcePoints,
                        resolvedKMeansK,
                        useSphericalKMeans: SelectedAlgorithm == "SphericalKMeans",
                        randomState: 42);
                    assignments = twoStageResult.Labels;
                    AppendLog(
                        $"二阶段聚类完成：粗分 {twoStageResult.CoarseClusterCount} 类，细分后得到 {twoStageResult.ProducedClusterCount} 类。");
                    AppendLog($"二阶段分配: {DescribeTwoStageClusterResult(twoStageResult)}");
                }
                else
                {
                    assignments = SelectedAlgorithm switch
                    {
                        "KMeans" => Utils.SklearnKMeansClusterer.Cluster(clusteringSpace, resolvedKMeansK, randomState: 42),
                        "SphericalKMeans" => Utils.SphericalKMeansClusterer.Cluster(clusteringSpace, resolvedKMeansK, randomState: 42),
                        "HDBSCAN" => Utils.HdbscanClusterer.Cluster(clusteringSpace, MinClusterSize),
                        _ => new int[clusteringSourcePoints.Count]
                    };
                }

                if (mergeKMeansClustersAfterClustering)
                {
                    bool mergeInCosineSpace = useTwoStageStyleClustering ||
                        !useUmapSpace ||
                        SelectedAlgorithm == "SphericalKMeans";
                    var mergeReferenceSpace = useUmapSpace && !useTwoStageStyleClustering
                        ? clusteringSpace
                        : clusteringSourcePoints;
                    var mergeResult = useMacroStyleOptimalMode
                        ? Utils.AdaptiveClusterMerger.MergeToMacroClusters(
                            useHeadLineStyleFeature ? headStyleMergePoints ?? mergeReferenceSpace : mergeReferenceSpace,
                            assignments,
                            macroStyleMinClusterCount,
                            macroStyleMaxClusterCount,
                            useCosineDistance: mergeInCosineSpace)
                        : Utils.AdaptiveClusterMerger.MergeNearbyClusters(
                            mergeReferenceSpace,
                            assignments,
                            useCosineDistance: mergeInCosineSpace);
                    assignments = mergeResult.Labels;
                    if (useMacroStyleOptimalMode && mergeResult.StableDistanceGapFound)
                    {
                        AppendLog(
                            $"宏观画风断点已找到：初始 {mergeResult.OriginalClusterCount} 簇 -> 最终 {mergeResult.FinalClusterCount} 个文件夹；相邻合并距离由 {mergeResult.PreviousMergeDistance:F4} 跃升到 {mergeResult.CutDistance:F4}。");
                    }
                    else if (useMacroStyleOptimalMode)
                    {
                        AppendLog(
                            $"宏观画风未发现足够稳定的距离断点，保守保留 {mergeResult.FinalClusterCount} 个文件夹，避免强行合并不同画风。");
                    }
                    else if (mergeResult.Changed)
                    {
                        AppendLog(
                            $"最优模式合并完成：初始 {mergeResult.OriginalClusterCount} 簇 -> 最终 {mergeResult.FinalClusterCount} 个文件夹，断点距离 {mergeResult.CutDistance:F4}。");
                    }
                    else
                    {
                        AppendLog($"最优模式未发现稳定的簇间距离断点，保留 {mergeResult.FinalClusterCount} 个文件夹。");
                    }
                }

                if (useKMeansFamily &&
                    UseBestKBySilhouette &&
                    !useMacroStyleOptimalMode &&
                    !useUmapSpace &&
                    IsStyleLikeFeatureType(effectiveFeatureType) &&
                    !(useHeadLineStyleFeature && UseBestKBySilhouette))
                {
                    var purityResult = useHeadLineStyleFeature
                        ? Utils.StyleClusterPurityRefiner.SplitLooseClusters(
                            clusteringSourcePoints,
                            assignments,
                            useCosineDistance: true,
                            minClusterSizeToSplit: 4,
                            minJumpRatio: 1.22d,
                            minJumpDistance: 0.003d)
                        : Utils.StyleClusterPurityRefiner.SplitLooseClusters(
                            clusteringSourcePoints,
                            assignments,
                            useCosineDistance: SelectedAlgorithm == "SphericalKMeans" || useStyleBrushSpace || useTwoStageStyleClustering);
                    assignments = purityResult.Labels;
                    if (purityResult.Changed)
                    {
                        AppendLog(
                            $"风格可靠性细分完成：拆分 {purityResult.SplitClusterCount} 个松散簇，文件夹数 {purityResult.OriginalClusterCount} -> {purityResult.FinalClusterCount}，完全链接阈值 {purityResult.CompleteLinkThreshold:F4}。");
                    }
                }
                else if (useMacroStyleOptimalMode)
                {
                    AppendLog("宏观画风模式已跳过全局松散簇细分，保留相近画风的大类结构。");
                }
                else if (useKMeansFamily && !UseBestKBySilhouette)
                {
                    AppendLog("手动 K 模式已跳过自动风格细分，导出文件夹数将尽量尊重手动 K 值。");
                }

                if (useHeadLineStyleFeature && UseBestKBySilhouette)
                {
                    AppendLog("头部画法/线条最优模式已保留大型同画风簇及低可信/兜底头部的合并结果，不再因文件夹图片较多或构图差异而拆分。");
                }

                bool shouldApplySameSourcePageCohesion =
                    useKMeansFamily &&
                    IsStyleLikeFeatureType(effectiveFeatureType) &&
                    GetFeatureTypeKey(effectiveFeatureType) != "OriginalCsdStyle";
                if (shouldApplySameSourcePageCohesion)
                {
                    var cohesionResult = ApplySameSourcePageCohesion(assignments, imageResults);
                    assignments = cohesionResult.Labels;
                    if (cohesionResult.ChangedImageCount > 0)
                    {
                        AppendLog(
                            $"同源分页一致性合并完成：修正 {cohesionResult.ChangedImageCount} 张跨文件夹分页图，涉及 {cohesionResult.ChangedGroupCount} 个源图组；当前文件夹数 {cohesionResult.FinalClusterCount}。");
                    }
                }

                assignments = RelabelAssignmentsSequentially(assignments);

                Application.Current.Dispatcher.Invoke(() => Progress = 75);

                string featureLower = useTwoStageStyleClustering
                    ? $"{GetFeatureTypeSlug(FeatureTypeGrayStyleDisplay)}2{GetFeatureTypeSlug(effectiveFeatureType)}"
                    : GetFeatureTypeSlug(effectiveFeatureType);
                string spaceLower = useTwoStageStyleClustering
                    ? "style2stage"
                    : GetClusteringSpaceSlug(useUmapSpace, useStyleBrushSpace);
                string title = $"{SelectedAlgorithm}_{featureLower}_{spaceLower}";
                string outputRoot = Path.Combine(OutputPath, title);
                PrepareOutputRoot(outputRoot);

                string? distributionImagePath = null;
                try
                {
                    var distributionSpace = BuildDistributionSpace(clusteringSourcePoints, clusteringSpace, useUmapSpace, token);
                    distributionImagePath = Utils.ClusterDistributionPlotter.SaveScatterPlot(
                        distributionSpace,
                        assignments,
                        outputRoot,
                        ClusterDistributionFileName,
                        imageResults.Select(r => r.FilePath).ToList());
                    AppendLog($"已生成聚类分布图: {distributionImagePath}");
                }
                catch (Exception ex)
                {
                    AppendLog($"生成聚类分布图失败: {ex.Message}");
                }

                var exportFilterOptions = new ClusterExportFilterOptions
                {
                    FilterByIqr = FilterOutliersForExport,
                    FilterByMinSimilarity = UseMinSimilarityForUncertain,
                    MinSimilarity = MinCentroidSimilarity
                };
                var exportPlan = BuildClusterExportPlan(clusteringSourcePoints, assignments, exportFilterOptions);
                var exportOrder = exportPlan.ClusterIndices;
                var clusterLabels = exportOrder.Keys.Where(id => id >= 0).OrderBy(id => id).ToList();
                bool hasNoise = exportOrder.ContainsKey(-1);
                bool hasUncertain = exportPlan.UncertainIndices.Count > 0;

                if (FilterOutliersForExport || UseMinSimilarityForUncertain)
                {
                    string filterSummary = BuildExportFilterSummary(FilterOutliersForExport, UseMinSimilarityForUncertain, MinCentroidSimilarity);
                    if (hasUncertain)
                    {
                        AppendLog($"导出过滤（{filterSummary}）：已将 {exportPlan.UncertainIndices.Count} 张样本移动到 uncertain。");
                    }
                    else
                    {
                        AppendLog($"导出过滤（{filterSummary}）：未检测到需要剔除的样本。");
                    }
                }

                foreach (int clusterId in clusterLabels)
                {
                    Directory.CreateDirectory(Path.Combine(outputRoot, $"class_{clusterId}"));
                }
                if (hasNoise)
                {
                    Directory.CreateDirectory(Path.Combine(outputRoot, "noise"));
                }
                if (hasUncertain)
                {
                    Directory.CreateDirectory(Path.Combine(outputRoot, "uncertain"));
                }

                int copiedCount = 0;
                int totalToCopy = imageResults.Count;
                foreach (int clusterId in clusterLabels)
                {
                    token.ThrowIfCancellationRequested();

                    string targetDir = Path.Combine(outputRoot, $"class_{clusterId}");
                    var orderedIndices = exportOrder[clusterId];
                    for (int rank = 0; rank < orderedIndices.Count; rank++)
                    {
                        token.ThrowIfCancellationRequested();

                        int sourceIndex = orderedIndices[rank];
                        string sourceFilePath = imageResults[sourceIndex].FilePath;
                        string targetFilePath = BuildRankedTargetFilePath(targetDir, sourceFilePath, rank, sourceIndex);
                        CopyFileWithRetry(sourceFilePath, targetFilePath);
                        CopyCompanionTextFileWithRetry(sourceFilePath, targetFilePath);

                        copiedCount++;
                        Application.Current.Dispatcher.Invoke(() =>
                            Progress = 75 + (double)copiedCount / totalToCopy * 25);
                    }
                }

                if (hasNoise)
                {
                    string noiseDir = Path.Combine(outputRoot, "noise");
                    var noiseIndices = exportOrder[-1];
                    for (int rank = 0; rank < noiseIndices.Count; rank++)
                    {
                        token.ThrowIfCancellationRequested();

                        int sourceIndex = noiseIndices[rank];
                        string sourceFilePath = imageResults[sourceIndex].FilePath;
                        string targetFilePath = BuildRankedTargetFilePath(noiseDir, sourceFilePath, rank, sourceIndex);
                        CopyFileWithRetry(sourceFilePath, targetFilePath);
                        CopyCompanionTextFileWithRetry(sourceFilePath, targetFilePath);

                        copiedCount++;
                        Application.Current.Dispatcher.Invoke(() =>
                            Progress = 75 + (double)copiedCount / totalToCopy * 25);
                    }
                }

                if (hasUncertain)
                {
                    string uncertainDir = Path.Combine(outputRoot, "uncertain");
                    var uncertainIndices = exportPlan.UncertainIndices;
                    for (int rank = 0; rank < uncertainIndices.Count; rank++)
                    {
                        token.ThrowIfCancellationRequested();

                        int sourceIndex = uncertainIndices[rank];
                        string sourceFilePath = imageResults[sourceIndex].FilePath;
                        string targetFilePath = BuildRankedTargetFilePath(uncertainDir, sourceFilePath, rank, sourceIndex);
                        CopyFileWithRetry(sourceFilePath, targetFilePath);
                        CopyCompanionTextFileWithRetry(sourceFilePath, targetFilePath);

                        copiedCount++;
                        Application.Current.Dispatcher.Invoke(() =>
                            Progress = 75 + (double)copiedCount / totalToCopy * 25);
                    }
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AppendLog("图片聚类完成");
                    Progress = 100;
                    if (showDialogs)
                    {
                        ShowClusteringCompletionDialog(outputRoot, distributionImagePath, imageResults.Count, assignments, exportPlan.UncertainIndices.Count);
                    }
                });
                return true;
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() => RefreshCommandStates());
            }
        }

        private List<float[]> BuildDistributionSpace(
            List<float[]> dataPoints,
            List<float[]> clusteringSpace,
            bool useUmapSpace,
            CancellationToken token)
        {
            if (useUmapSpace && IsValidTwoDimensionalSpace(clusteringSpace))
            {
                return clusteringSpace.Select(v => new[] { v[0], v[1] }).ToList();
            }

            if (dataPoints.Count < 3)
            {
                return ProjectToTwoDimensions(dataPoints);
            }

            try
            {
                var umapOptions = BuildUmapOptions(dataPoints.Count);
                AppendLog(
                    $"开始生成分布图坐标（UMAP 2D, n_neighbors={umapOptions.NNeighbors}, min_dist={umapOptions.MinDist:F3}, spread={umapOptions.Spread:F3}）");
                AppendUmapImplementationLog(umapOptions);

                return Utils.UmapReducer.ReduceToTwoDimensions(dataPoints, umapOptions, token);
            }
            catch (Exception ex)
            {
                AppendLog($"分布图 UMAP 失败，回退 PCA。原因: {ex.Message}");
                return Utils.FeatureSpaceReducer.ReduceToTwoDimensionsPca(dataPoints);
            }
        }

        private Utils.UmapReducer.Options BuildUmapOptions(int sampleCount)
        {
            int safeSampleCount = Math.Max(sampleCount, 3);
            int requestedNeighbors = UmapNNeighbors;
            if (requestedNeighbors < 2)
            {
                requestedNeighbors = 2;
            }

            int resolvedNeighbors = Math.Clamp(requestedNeighbors, 2, Math.Max(2, safeSampleCount - 1));
            if (resolvedNeighbors != UmapNNeighbors)
            {
                AppendLog($"UMAP 参数调整: n_neighbors={UmapNNeighbors} -> {resolvedNeighbors}（样本数限制）");
            }

            float minDist = (float)Math.Max(0d, UmapMinDist);
            if (Math.Abs(minDist - UmapMinDist) > 1e-9)
            {
                AppendLog($"UMAP 参数调整: min_dist={UmapMinDist:F3} -> {minDist:F3}");
            }

            float spread = (float)Math.Max(1e-4d, UmapSpread);
            if (Math.Abs(spread - UmapSpread) > 1e-9)
            {
                AppendLog($"UMAP 参数调整: spread={UmapSpread:F3} -> {spread:F3}");
            }

            return new Utils.UmapReducer.Options
            {
                Metric = "cosine",
                RandomState = 42,
                NNeighbors = resolvedNeighbors,
                NComponents = 2,
                MinDist = minDist,
                Spread = spread
            };
        }

        private void AppendUmapImplementationLog(Utils.UmapReducer.Options options)
        {
            bool useReferenceCompatiblePath =
                options.NComponents == 2 &&
                Math.Abs(options.MinDist - 0.1f) <= 1e-6f &&
                Math.Abs(options.Spread - 1.0f) <= 1e-6f;

            if (useReferenceCompatiblePath)
            {
                AppendLog("UMAP 实现: 使用 UMAP.dll 路径，对齐参考脚本的默认 2D 配置。");
                return;
            }

            AppendLog("UMAP 实现: 当前参数已偏离参考默认值，将回退到内置近似实现。");
        }

        private static int ResolveAutoKUpperBound(int sampleCount)
        {
            int bySqrt = (int)Math.Round(Math.Sqrt(sampleCount));
            return Math.Clamp(bySqrt, 3, 20);
        }

        private static int ResolveAutoMergeInitialClusterCount(int sampleCount)
        {
            if (sampleCount <= 1)
            {
                return Math.Max(sampleCount, 1);
            }

            int sqrtScaled = (int)Math.Round(Math.Sqrt(sampleCount) * 2.0d);
            int byDatasetSize = sampleCount < 120
                ? Math.Max(4, sampleCount / 8)
                : sqrtScaled;
            return Math.Clamp(Math.Max(4, byDatasetSize), 2, Math.Min(80, sampleCount - 1));
        }

        private static (int MinClusters, int MaxClusters) ResolveMacroStyleClusterRange(int sampleCount)
        {
            if (sampleCount <= 2)
            {
                int count = Math.Max(sampleCount, 1);
                return (count, count);
            }

            if (sampleCount < 40)
            {
                return (2, Math.Min(8, sampleCount));
            }

            return sampleCount switch
            {
                < 120 => (3, 12),
                < 240 => (4, 18),
                _ => (6, 24)
            };
        }

        private static bool IsValidTwoDimensionalSpace(IReadOnlyList<float[]> points)
        {
            return points.Count > 0 && points.All(v => v != null && v.Length == 2);
        }

        private void ShowClusteringCompletionDialog(string outputRoot, string? distributionImagePath, int totalCount, IReadOnlyList<int> assignments, int uncertainCount)
        {
            int clusterCount = assignments.Where(a => a >= 0).Distinct().Count();
            int noiseCount = assignments.Count(a => a < 0);
            string summary = $"图片聚类完成：共 {totalCount} 张，聚类簇 {clusterCount} 个";
            if (noiseCount > 0)
            {
                summary += $"，噪声 {noiseCount} 张";
            }
            if (uncertainCount > 0)
            {
                summary += $"，离群 {uncertainCount} 张";
            }

            if (!string.IsNullOrWhiteSpace(distributionImagePath) && File.Exists(distributionImagePath))
            {
                var window = new ClusterDistributionWindow(distributionImagePath, outputRoot, summary);
                if (Application.Current?.MainWindow != null && Application.Current.MainWindow != window)
                {
                    window.Owner = Application.Current.MainWindow;
                }

                window.ShowDialog();
                return;
            }

            MessageBox.Show($"{summary}\n\n未能生成分布图。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static float[] BuildFeatureVector(ImageStyleResult result, string featureType, float colorRetention)
        {
            if (result == null)
            {
                throw new InvalidOperationException("特征提取结果为空。");
            }

            return GetFeatureTypeKey(featureType) switch
            {
                "Style" => NormalizeVectorCopy(result.StyleVector),
                "OriginalCsdStyle" => NormalizeVectorCopy(result.StyleVector),
                "GrayStyle" => BuildGrayStructureStyleVector(result),
                "StrictGrayStyle" => BuildStrictGrayStructureStyleVector(result),
                "HeadLineStyle" => ConcatWeightedNormalizedVectors(
                    (result.HeadGrayStyleVector, 0.72f),
                    (result.HeadRenderStyleVector, 0.55f),
                    (result.HeadLineStyleVector, 0.48f),
                    (result.HeadUpperLineStyleVector, 0.34f),
                    (result.HeadCenterLineStyleVector, 0.46f),
                    (result.HeadFaceLineStyleVector, 0.58f),
                    (result.HeadEyeLineStyleVector, 0.52f),
                    (result.HeadToneEdgeStyleVector, 0.50f),
                    (result.HeadFaceToneEdgeStyleVector, 0.42f)),
                "StyleColorDownweight" => BlendNormalizedVectors(result.StyleVector, result.GrayStyleVector, colorRetention),
                "Content" => NormalizeVectorCopy(result.ContentVector),
                "Style+Content" => ConcatNormalizedVectors(result.StyleVector, result.ContentVector),
                _ => throw new InvalidOperationException($"未知特征类型: {featureType}")
            };
        }

        private static float[] BuildHeadStyleMergeVector(ImageStyleResult result)
        {
            return ConcatWeightedNormalizedVectors(
                (result.HeadGrayStyleVector, 0.82f),
                (result.HeadRenderStyleVector, 0.58f),
                (result.HeadLineStyleVector, 0.42f),
                (result.HeadUpperLineStyleVector, 0.26f),
                (result.HeadCenterLineStyleVector, 0.34f),
                (result.HeadFaceLineStyleVector, 0.28f),
                (result.HeadEyeLineStyleVector, 0.24f),
                (result.HeadToneEdgeStyleVector, 0.52f),
                (result.HeadFaceToneEdgeStyleVector, 0.32f));
        }

        private static float[] BuildGrayStructureStyleVector(ImageStyleResult result)
        {
            var parts = new List<(float[] Vector, float Weight)>
            {
                (result.GrayStyleVector, 0.70f)
            };

            if (HasVector(result.GrayRenderStyleVector))
            {
                parts.Add((result.GrayRenderStyleVector, 0.45f));
            }

            if (HasVector(result.GrayLineStyleVector))
            {
                parts.Add((result.GrayLineStyleVector, 0.55f));
            }

            if (HasVector(result.GrayToneEdgeStyleVector))
            {
                parts.Add((result.GrayToneEdgeStyleVector, 0.40f));
            }

            return ConcatWeightedNormalizedVectors(parts.ToArray());
        }

        private static float[] BuildStrictGrayStructureStyleVector(ImageStyleResult result)
        {
            var parts = new List<(float[] Vector, float Weight)>
            {
                (result.GrayStyleVector, 0.55f)
            };

            if (HasVector(result.GrayRenderStyleVector))
            {
                parts.Add((result.GrayRenderStyleVector, 0.75f));
            }

            if (HasVector(result.GrayLineStyleVector))
            {
                parts.Add((result.GrayLineStyleVector, 0.95f));
            }

            if (HasVector(result.GrayToneEdgeStyleVector))
            {
                parts.Add((result.GrayToneEdgeStyleVector, 0.80f));
            }

            if (HasVector(result.GraySketchStyleVector))
            {
                parts.Add((result.GraySketchStyleVector, 1.10f));
            }

            return ConcatWeightedNormalizedVectors(parts.ToArray());
        }

        private static bool HasVector(float[]? vector)
        {
            return vector != null && vector.Length > 0;
        }

        private static float[] ConcatNormalizedVectors(float[] first, float[] second)
        {
            var a = NormalizeVectorCopy(first);
            var b = NormalizeVectorCopy(second);

            var merged = new float[a.Length + b.Length];
            Array.Copy(a, 0, merged, 0, a.Length);
            Array.Copy(b, 0, merged, a.Length, b.Length);
            return NormalizeVectorCopy(merged);
        }

        private static float[] ConcatWeightedNormalizedVectors(params (float[] Vector, float Weight)[] parts)
        {
            int length = parts.Sum(part => part.Vector?.Length ?? 0);
            if (length == 0)
            {
                throw new InvalidOperationException("用于拼接的特征为空。");
            }

            var merged = new float[length];
            int offset = 0;
            foreach (var (vector, weight) in parts)
            {
                var normalized = NormalizeVectorCopy(vector);
                for (int i = 0; i < normalized.Length; i++)
                {
                    merged[offset + i] = normalized[i] * weight;
                }

                offset += normalized.Length;
            }

            return NormalizeVectorCopy(merged);
        }

        private static float[] BlendNormalizedVectors(float[] first, float[] second, float firstWeight)
        {
            var a = NormalizeVectorCopy(first);
            var b = NormalizeVectorCopy(second);

            if (a.Length != b.Length)
            {
                throw new InvalidOperationException("向量维度不一致，无法进行颜色降权融合。");
            }

            float clampedFirstWeight = Math.Clamp(firstWeight, 0f, 1f);
            float secondWeight = 1f - clampedFirstWeight;
            var merged = new float[a.Length];
            for (int i = 0; i < merged.Length; i++)
            {
                merged[i] = a[i] * clampedFirstWeight + b[i] * secondWeight;
            }

            return NormalizeVectorCopy(merged);
        }

        private static float[] NormalizeVectorCopy(float[] vector)
        {
            if (vector == null || vector.Length == 0)
            {
                throw new InvalidOperationException("模型输出特征为空。");
            }

            var copy = new float[vector.Length];
            Array.Copy(vector, copy, vector.Length);

            double sum = 0;
            for (int i = 0; i < copy.Length; i++)
            {
                float value = copy[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new InvalidOperationException("模型输出包含非法数值（NaN/Infinity）。");
                }

                sum += value * value;
            }

            if (sum <= 1e-12)
            {
                return copy;
            }

            float invNorm = (float)(1.0 / Math.Sqrt(sum));
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] *= invNorm;
            }

            return copy;
        }

        private static string GetFeatureTypeSlug(string featureType)
        {
            return GetFeatureTypeKey(featureType) switch
            {
                "Style" => "style",
                "OriginalCsdStyle" => "csdstyle",
                "GrayStyle" => "graystyle",
                "StrictGrayStyle" => "graystrictstyle",
                "HeadLineStyle" => "headlinestyle",
                "StyleColorDownweight" => "stylelite",
                "Content" => "content",
                "Style+Content" => "hybrid",
                _ => "unknown"
            };
        }

        private static string GetClusteringSpaceSlug(bool useUmapSpace, bool useStyleBrushSpace)
        {
            if (useUmapSpace)
            {
                return "umap2d";
            }

            return useStyleBrushSpace ? "stylehd" : "raw";
        }

        private static string GetFeatureTypeKey(string? featureType)
        {
            if (string.IsNullOrWhiteSpace(featureType))
            {
                return "Style+Content";
            }

            if (featureType.StartsWith("OriginalCsdStyle", StringComparison.OrdinalIgnoreCase) ||
                featureType.StartsWith("Style（原版 CSD", StringComparison.OrdinalIgnoreCase) ||
                featureType.StartsWith("Style（原版CSD", StringComparison.OrdinalIgnoreCase))
            {
                return "OriginalCsdStyle";
            }

            if (featureType.StartsWith("StrictGrayStyle", StringComparison.OrdinalIgnoreCase) ||
                featureType.StartsWith("Style（灰度去色，严格", StringComparison.OrdinalIgnoreCase))
            {
                return "StrictGrayStyle";
            }

            if (featureType.StartsWith("GrayStyle", StringComparison.OrdinalIgnoreCase) ||
                featureType.StartsWith("Style（灰度", StringComparison.OrdinalIgnoreCase))
            {
                return "GrayStyle";
            }

            if (featureType.StartsWith("HeadLineStyle", StringComparison.OrdinalIgnoreCase) ||
                featureType.StartsWith("Style（头部线条", StringComparison.OrdinalIgnoreCase) ||
                featureType.StartsWith("Style（头部渲染", StringComparison.OrdinalIgnoreCase) ||
                featureType.StartsWith("Style（头部画法", StringComparison.OrdinalIgnoreCase))
            {
                return "HeadLineStyle";
            }

            if (featureType.StartsWith("StyleColorDownweight", StringComparison.OrdinalIgnoreCase) ||
                featureType.StartsWith("Style（颜色降权", StringComparison.OrdinalIgnoreCase))
            {
                return "StyleColorDownweight";
            }

            if (featureType.StartsWith("Style+Content", StringComparison.OrdinalIgnoreCase))
            {
                return "Style+Content";
            }

            if (featureType.StartsWith("Style", StringComparison.OrdinalIgnoreCase))
            {
                return "Style";
            }

            if (featureType.StartsWith("Content", StringComparison.OrdinalIgnoreCase))
            {
                return "Content";
            }

            return "Style+Content";
        }

        private static string ToFeatureTypeDisplay(string? featureType)
        {
            return GetFeatureTypeKey(featureType) switch
            {
                "Style" => FeatureTypeStyleDisplay,
                "OriginalCsdStyle" => FeatureTypeOriginalCsdStyleDisplay,
                "GrayStyle" => FeatureTypeGrayStyleDisplay,
                "StrictGrayStyle" => FeatureTypeStrictGrayStyleDisplay,
                "HeadLineStyle" => FeatureTypeHeadLineStyleDisplay,
                "StyleColorDownweight" => FeatureTypeStyleColorDownweightDisplay,
                "Content" => FeatureTypeContentDisplay,
                _ => FeatureTypeHybridDisplay
            };
        }

        private static bool UsesGrayStyleVectors(string featureType, bool useTwoStageStyleClustering = false)
        {
            if (useTwoStageStyleClustering)
            {
                return true;
            }

            return GetFeatureTypeKey(featureType) switch
            {
                "GrayStyle" => true,
                "StrictGrayStyle" => true,
                "StyleColorDownweight" => true,
                _ => false
            };
        }

        private static bool UsesGrayStructureStyleVectors(string featureType)
        {
            return GetFeatureTypeKey(featureType) switch
            {
                "GrayStyle" => true,
                "StrictGrayStyle" => true,
                _ => false
            };
        }

        private static bool UsesHeadLineStyleVectors(string featureType)
        {
            return GetFeatureTypeKey(featureType) == "HeadLineStyle";
        }

        private static bool IsStyleLikeFeatureType(string featureType)
        {
            return GetFeatureTypeKey(featureType) switch
            {
                "Style" => true,
                "OriginalCsdStyle" => true,
                "GrayStyle" => true,
                "StrictGrayStyle" => true,
                "HeadLineStyle" => true,
                "StyleColorDownweight" => true,
                _ => false
            };
        }

        private static string ResolveTwoStageFineFeatureType(string featureType)
        {
            return GetFeatureTypeKey(featureType) switch
            {
                "StyleColorDownweight" => FeatureTypeStyleColorDownweightDisplay,
                "HeadLineStyle" => FeatureTypeHeadLineStyleDisplay,
                "GrayStyle" => FeatureTypeStyleDisplay,
                "StrictGrayStyle" => FeatureTypeStyleDisplay,
                "OriginalCsdStyle" => FeatureTypeOriginalCsdStyleDisplay,
                "Style" => FeatureTypeStyleDisplay,
                _ => FeatureTypeStyleDisplay
            };
        }

        private static string DescribeTwoStageClusterResult(Utils.TwoStageStyleClusterer.Result result)
        {
            if (result.CoarseClusterCount == 0)
            {
                return "无有效样本。";
            }

            var parts = new List<string>(result.CoarseClusterCount);
            for (int i = 0; i < result.CoarseClusterCount; i++)
            {
                int size = i < result.CoarseClusterSizes.Count ? result.CoarseClusterSizes[i] : 0;
                int fine = i < result.FineClusterCounts.Count ? result.FineClusterCounts[i] : 0;
                parts.Add($"粗类{i}: {size}张 -> {fine}类");
            }

            const int maxParts = 8;
            if (parts.Count > maxParts)
            {
                return string.Join("，", parts.Take(maxParts)) + $"，其余 {parts.Count - maxParts} 个粗类已省略";
            }

            return string.Join("，", parts);
        }

        private static string BuildExportFilterSummary(bool filterByIqr, bool filterByMinSimilarity, double minSimilarity)
        {
            if (filterByIqr && filterByMinSimilarity)
            {
                return $"IQR + 最小相似度 {minSimilarity:F2}";
            }

            if (filterByMinSimilarity)
            {
                return $"最小相似度 {minSimilarity:F2}";
            }

            return "IQR";
        }

        private static List<float[]> BuildFeatureVectors(IReadOnlyList<ImageStyleResult> results, string featureType, float colorRetention)
        {
            var vectors = new List<float[]>(results.Count);
            for (int i = 0; i < results.Count; i++)
            {
                vectors.Add(BuildFeatureVector(results[i], featureType, colorRetention));
            }

            return vectors;
        }

        private static List<float[]> BuildHeadStyleMergeVectors(IReadOnlyList<ImageStyleResult> results)
        {
            var vectors = new List<float[]>(results.Count);
            for (int i = 0; i < results.Count; i++)
            {
                vectors.Add(BuildHeadStyleMergeVector(results[i]));
            }

            return vectors;
        }

        private static List<float[]> ProjectToTwoDimensions(IReadOnlyList<float[]> dataPoints)
        {
            var projected = new List<float[]>(dataPoints.Count);
            for (int i = 0; i < dataPoints.Count; i++)
            {
                var vector = dataPoints[i];
                float x = vector.Length > 0 ? vector[0] : 0f;
                float y = vector.Length > 1 ? vector[1] : 0f;
                projected.Add(new[] { x, y });
            }

            return projected;
        }

        private static int[] RelabelAssignmentsSequentially(IReadOnlyList<int> assignments)
        {
            var labelMap = new Dictionary<int, int>();
            int nextLabel = 0;
            var relabeled = new int[assignments.Count];
            for (int i = 0; i < assignments.Count; i++)
            {
                int label = assignments[i];
                if (label < 0)
                {
                    relabeled[i] = label;
                    continue;
                }

                if (!labelMap.TryGetValue(label, out int mappedLabel))
                {
                    mappedLabel = nextLabel++;
                    labelMap[label] = mappedLabel;
                }

                relabeled[i] = mappedLabel;
            }

            return relabeled;
        }

        private static HeadReliabilitySplitResult SplitMixedHeadReliabilityClusters(
            IReadOnlyList<int> assignments,
            IReadOnlyList<ImageStyleResult> results)
        {
            if (assignments.Count != results.Count)
            {
                throw new InvalidOperationException("聚类标签数量与图片数量不一致。");
            }

            var labels = assignments.ToArray();
            int nextLabel = labels.Where(label => label >= 0).DefaultIfEmpty(-1).Max() + 1;
            int splitCount = 0;
            var groups = Enumerable.Range(0, labels.Length)
                .Where(i => labels[i] >= 0)
                .GroupBy(i => labels[i])
                .ToArray();

            foreach (var group in groups)
            {
                var indices = group.ToList();
                bool hasReliable = indices.Any(i => !results[i].UsedHeadFallbackCrop);
                bool hasFallback = indices.Any(i => results[i].UsedHeadFallbackCrop);
                if (hasReliable && hasFallback)
                {
                    int fallbackLabel = nextLabel++;
                    foreach (int index in indices.Where(i => results[i].UsedHeadFallbackCrop))
                    {
                        labels[index] = fallbackLabel;
                    }

                    splitCount++;
                }
            }

            int finalCount = labels.Where(label => label >= 0).Distinct().Count();
            return new HeadReliabilitySplitResult(labels, splitCount, finalCount);
        }

        private sealed record HeadReliabilitySplitResult(
            int[] Labels,
            int SplitClusterCount,
            int FinalClusterCount);

        private static SameSourceCohesionResult ApplySameSourcePageCohesion(
            IReadOnlyList<int> assignments,
            IReadOnlyList<ImageStyleResult> results)
        {
            if (assignments.Count != results.Count)
            {
                throw new InvalidOperationException("聚类标签数量与图片数量不一致。");
            }

            var labels = assignments.ToArray();
            int changedImages = 0;
            int changedGroups = 0;
            var groups = Enumerable.Range(0, results.Count)
                .Select(index => new
                {
                    Index = index,
                    Key = TryGetSameSourcePageKey(results[index].FilePath)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && labels[item.Index] >= 0)
                .GroupBy(item => item.Key!)
                .Where(group => group.Count() >= 2);

            foreach (var group in groups)
            {
                var indices = group.Select(item => item.Index).ToArray();
                var labelCounts = indices
                    .GroupBy(index => labels[index])
                    .Select(labelGroup => new
                    {
                        Label = labelGroup.Key,
                        Count = labelGroup.Count()
                    })
                    .OrderByDescending(item => item.Count)
                    .ThenBy(item => item.Label)
                    .ToArray();
                if (labelCounts.Length <= 1 || labelCounts[0].Count < 2)
                {
                    continue;
                }

                int targetLabel = labelCounts[0].Label;
                bool changedThisGroup = false;
                foreach (int index in indices)
                {
                    if (labels[index] == targetLabel)
                    {
                        continue;
                    }

                    labels[index] = targetLabel;
                    changedImages++;
                    changedThisGroup = true;
                }

                if (changedThisGroup)
                {
                    changedGroups++;
                }
            }

            int finalClusterCount = labels.Where(label => label >= 0).Distinct().Count();
            return new SameSourceCohesionResult(labels, changedImages, changedGroups, finalClusterCount);
        }

        private static string? TryGetSameSourcePageKey(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);
            int pageMarker = name.LastIndexOf("_p", StringComparison.OrdinalIgnoreCase);
            if (pageMarker <= 0 || pageMarker + 2 >= name.Length)
            {
                return null;
            }

            bool hasPageNumber = true;
            for (int i = pageMarker + 2; i < name.Length; i++)
            {
                if (!char.IsDigit(name[i]))
                {
                    hasPageNumber = false;
                    break;
                }
            }

            if (!hasPageNumber)
            {
                return null;
            }

            int idStart = pageMarker - 1;
            while (idStart >= 0 && char.IsDigit(name[idStart]))
            {
                idStart--;
            }

            int idLength = pageMarker - idStart - 1;
            if (idLength < 6)
            {
                return null;
            }

            return name.Substring(idStart + 1, idLength);
        }

        private sealed record SameSourceCohesionResult(
            int[] Labels,
            int ChangedImageCount,
            int ChangedGroupCount,
            int FinalClusterCount);

        private sealed class ClusterExportPlan
        {
            public Dictionary<int, List<int>> ClusterIndices { get; } = new();
            public List<int> UncertainIndices { get; } = new();
        }

        private sealed class ClusterExportFilterOptions
        {
            public bool FilterByIqr { get; init; }
            public bool FilterByMinSimilarity { get; init; }
            public double MinSimilarity { get; init; }
        }

        private static ClusterExportPlan BuildClusterExportPlan(
            IReadOnlyList<float[]> referenceSpace,
            IReadOnlyList<int> assignments,
            ClusterExportFilterOptions filterOptions)
        {
            if (referenceSpace.Count != assignments.Count)
            {
                throw new InvalidOperationException("特征数量与聚类标签数量不一致。");
            }

            var clusterIndices = new Dictionary<int, List<int>>();
            for (int i = 0; i < assignments.Count; i++)
            {
                int clusterId = assignments[i];
                if (!clusterIndices.TryGetValue(clusterId, out var indices))
                {
                    indices = new List<int>();
                    clusterIndices[clusterId] = indices;
                }

                indices.Add(i);
            }

            var exportPlan = new ClusterExportPlan();
            foreach (var entry in clusterIndices)
            {
                int clusterId = entry.Key;
                var indices = entry.Value;
                if (clusterId < 0)
                {
                    indices.Sort();
                    exportPlan.ClusterIndices[clusterId] = indices;
                    continue;
                }

                List<int> retainedIndices = new(indices);
                if (filterOptions.FilterByIqr || filterOptions.FilterByMinSimilarity)
                {
                    var (retained, outliers) = SplitClusterMembers(referenceSpace, indices, filterOptions);
                    retainedIndices = retained;
                    exportPlan.UncertainIndices.AddRange(outliers);
                }

                if (retainedIndices.Count == 0)
                {
                    continue;
                }

                float[] centroid = ComputeCentroid(referenceSpace, retainedIndices);
                retainedIndices.Sort((a, b) =>
                {
                    double da = ComputeSquaredDistance(referenceSpace[a], centroid);
                    double db = ComputeSquaredDistance(referenceSpace[b], centroid);
                    int cmp = da.CompareTo(db);
                    return cmp != 0 ? cmp : a.CompareTo(b);
                });
                exportPlan.ClusterIndices[clusterId] = retainedIndices;
            }

            exportPlan.UncertainIndices.Sort();
            return exportPlan;
        }

        private static (List<int> retainedIndices, List<int> outlierIndices) SplitClusterMembers(
            IReadOnlyList<float[]> referenceSpace,
            IReadOnlyList<int> indices,
            ClusterExportFilterOptions filterOptions)
        {
            if (indices.Count == 0)
            {
                return (new List<int>(), new List<int>());
            }

            float[] centroid = ComputeCentroid(referenceSpace, indices);
            float[] normalizedCentroid = NormalizeVectorCopy(centroid);
            var ranked = indices
                .Select(index => new
                {
                    Index = index,
                    Distance = Math.Sqrt(ComputeSquaredDistance(referenceSpace[index], centroid)),
                    Similarity = ComputeCosineSimilarity(referenceSpace[index], normalizedCentroid)
                })
                .OrderBy(item => item.Distance)
                .ThenBy(item => item.Index)
                .ToList();

            double distanceThreshold = double.PositiveInfinity;
            if (filterOptions.FilterByIqr && indices.Count >= OutlierFilterMinClusterSize)
            {
                double[] distances = ranked.Select(item => item.Distance).ToArray();
                double q1 = ComputePercentile(distances, 0.25d);
                double q3 = ComputePercentile(distances, 0.75d);
                double iqr = q3 - q1;
                if (iqr > 1e-9d)
                {
                    distanceThreshold = q3 + OutlierIqrMultiplier * iqr;
                }
            }

            var retained = ranked
                .Where(item =>
                    item.Distance <= distanceThreshold &&
                    (!filterOptions.FilterByMinSimilarity || item.Similarity >= filterOptions.MinSimilarity))
                .Select(item => item.Index)
                .ToList();
            var outliers = ranked
                .Where(item =>
                    item.Distance > distanceThreshold ||
                    (filterOptions.FilterByMinSimilarity && item.Similarity < filterOptions.MinSimilarity))
                .Select(item => item.Index)
                .ToList();

            if (outliers.Count == 0)
            {
                return (indices.ToList(), new List<int>());
            }

            return (retained, outliers);
        }

        private static double ComputePercentile(IReadOnlyList<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0)
            {
                return 0d;
            }

            if (sortedValues.Count == 1)
            {
                return sortedValues[0];
            }

            double clamped = Math.Clamp(percentile, 0d, 1d);
            double position = clamped * (sortedValues.Count - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
            {
                return sortedValues[lower];
            }

            double weight = position - lower;
            return sortedValues[lower] * (1d - weight) + sortedValues[upper] * weight;
        }

        private static float[] ComputeCentroid(IReadOnlyList<float[]> vectors, IReadOnlyList<int> indices)
        {
            if (indices.Count == 0)
            {
                return Array.Empty<float>();
            }

            int dimension = vectors[indices[0]].Length;
            var sum = new double[dimension];
            for (int i = 0; i < indices.Count; i++)
            {
                var vector = vectors[indices[i]];
                for (int d = 0; d < dimension; d++)
                {
                    sum[d] += vector[d];
                }
            }

            var centroid = new float[dimension];
            double invCount = 1.0 / indices.Count;
            for (int d = 0; d < dimension; d++)
            {
                centroid[d] = (float)(sum[d] * invCount);
            }

            return centroid;
        }

        private static double ComputeSquaredDistance(IReadOnlyList<float> vector, IReadOnlyList<float> centroid)
        {
            int dimension = Math.Min(vector.Count, centroid.Count);
            double sum = 0;
            for (int d = 0; d < dimension; d++)
            {
                double delta = vector[d] - centroid[d];
                sum += delta * delta;
            }

            return sum;
        }

        private static double ComputeCosineSimilarity(IReadOnlyList<float> vector, IReadOnlyList<float> centroid)
        {
            int dimension = Math.Min(vector.Count, centroid.Count);
            double dot = 0;
            for (int d = 0; d < dimension; d++)
            {
                dot += vector[d] * centroid[d];
            }

            return dot;
        }

        private static string BuildRankedTargetFilePath(string targetDir, string sourceFilePath, int rank, int fallbackIndex)
        {
            string sourceName = Path.GetFileNameWithoutExtension(sourceFilePath);
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                sourceName = $"image_{fallbackIndex}";
            }

            string extension = Path.GetExtension(sourceFilePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".jpg";
            }

            string prefix = $"{Math.Max(rank, 0) + 1:D5}_";
            string candidate = Path.Combine(targetDir, prefix + sourceName + extension);
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            for (int i = 1; ; i++)
            {
                candidate = Path.Combine(targetDir, $"{prefix}{sourceName}_{i}{extension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        private async Task<bool> EnsureCsdModelReadyAsync(bool showDialogs, CancellationToken token)
        {
            string modelPath = Path.GetFullPath(CsdModelRelativePath);
            if (File.Exists(modelPath))
            {
                return true;
            }

            AppendLog($"模型文件不存在: {modelPath}");

            bool shouldDownload = !showDialogs;
            if (showDialogs)
            {
                MessageBoxResult choice = MessageBoxResult.None;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    choice = MessageBox.Show(
                        $"检测到缺少 CSD 模型文件：\n{modelPath}\n\n是否自动下载该模型？",
                        "缺少模型",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                });

                if (choice == MessageBoxResult.No)
                {
                    AppendLog("已取消自动下载，准备打开 CSD Hugging Face 页面。");
                    OpenCsdModelPage();
                    return false;
                }

                shouldDownload = choice == MessageBoxResult.Yes;
            }

            if (!shouldDownload)
            {
                return false;
            }

            try
            {
                string? directory = Path.GetDirectoryName(modelPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AppendLog("开始下载 CSD 模型...");
                await DownloadCsdModelAsync(CsdModelDownloadUrl, modelPath, token);
                AppendLog("CSD 模型下载完成。");
                return true;
            }
            catch (OperationCanceledException)
            {
                AppendLog("CSD 模型下载已取消。");
                throw;
            }
            catch (Exception ex)
            {
                AppendLog($"CSD 模型下载失败: {ex.Message}");
                if (showDialogs)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        MessageBox.Show(
                            $"CSD 模型下载失败：{ex.Message}\n\n将为你打开模型页面，请手动下载后重试。",
                            "下载失败",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning));
                    OpenCsdModelPage();
                }
                return false;
            }
        }

        private async Task DownloadCsdModelAsync(string url, string destinationPath, CancellationToken token)
        {
            string tempPath = destinationPath + ".download";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            try
            {
                using var response = await CsdModelHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                long totalBytesRead = 0;
                long reportStep = totalBytes.HasValue && totalBytes.Value > 0
                    ? Math.Max(1, totalBytes.Value / 20)
                    : 10L * 1024 * 1024;
                long nextReport = reportStep;

                await using var contentStream = await response.Content.ReadAsStreamAsync(token);
                await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                    totalBytesRead += bytesRead;

                    if (totalBytesRead < nextReport)
                    {
                        continue;
                    }

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double percentage = (double)totalBytesRead / totalBytes.Value * 100;
                        AppendLog($"CSD 模型下载进度: {totalBytesRead / 1024.0 / 1024.0:F2}MB / {totalBytes.Value / 1024.0 / 1024.0:F2}MB ({percentage:F1}%)");
                    }
                    else
                    {
                        AppendLog($"CSD 模型已下载: {totalBytesRead / 1024.0 / 1024.0:F2}MB");
                    }

                    nextReport += reportStep;
                }

                await fileStream.FlushAsync(token);
                File.Move(tempPath, destinationPath, true);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                throw;
            }
        }

        private void OpenCsdModelPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(CsdModelPageUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendLog($"打开模型页面失败: {ex.Message}");
            }
        }

        private async Task MoveFilesToTopLevelAsync()
        {
            IsClustering = true;
            Log = "开始移动文件...\n";
            Progress = 0;

            await Task.Run(() =>
            {
                try
                {
                    var subDirectories = Directory.GetDirectories(InputPath, "*", SearchOption.AllDirectories);
                    var filesToMove = new List<string>();
                    foreach (var dir in subDirectories)
                    {
                        filesToMove.AddRange(Directory.GetFiles(dir));
                    }

                    if (filesToMove.Count == 0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Log += "没有在子文件夹中找到需要移动的文件。\n";
                            MessageBox.Show("没有在子文件夹中找到需要移动的文件。", "操作完成", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                        return;
                    }

                    int movedCount = 0;
                    int skippedCount = 0;
                    for (int i = 0; i < filesToMove.Count; i++)
                    {
                        string sourcePath = filesToMove[i];
                        string fileName = Path.GetFileName(sourcePath);
                        string destPath = Path.Combine(InputPath, fileName);

                        if (File.Exists(destPath))
                        {
                            skippedCount++;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Log += $"警告: 文件 {fileName} 已存在于目标路径，已跳过。\n";
                            });
                        }
                        else
                        {
                            File.Move(sourcePath, destPath);
                            movedCount++;
                        }

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Progress = (double)(i + 1) / filesToMove.Count * 100;
                        });
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Log += "操作完成。成功移动 " + movedCount + " 个文件，跳过 " + skippedCount + " 个重复文件。\n";
                        MessageBox.Show($"操作完成。\n成功移动 {movedCount} 个文件。\n跳过 {skippedCount} 个重复文件。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        Progress = 100;
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Log += "发生错误: " + ex.Message + "\n";
                        MessageBox.Show($"发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {

                    try
                    {
                        var subDirectories = Directory.GetDirectories(InputPath);
                        foreach (var dir in subDirectories)
                        {
                            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            {
                                Directory.Delete(dir);
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    Log += $"已删除空目录: {Path.GetFileName(dir)}\n";
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Log += "清理空目录时发生错误: " + ex.Message + "\n";
                        });
                    }
                }
            });

            IsClustering = false;
        }

        private void SelectInputFolder()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                InputPath = dialog.FileName;
            }
        }

        private void SelectOutputFolder()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputPath = dialog.FileName;
            }
        }

        private void CopyFileWithRetry(string source, string destination, int maxRetries = 3)
        {

            if (File.Exists(destination))
            {
                return;
            }

            if (!File.Exists(source))
            {
                AppendLog($"警告: 源文件不存在，已跳过: {source}");
                return;
            }

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                    sourceStream.CopyTo(destStream);
                    return;
                }
                catch (IOException) when (attempt < maxRetries - 1)
                {
                    Thread.Sleep(500 * (attempt + 1));
                }
                catch (Exception ex)
                {
                    AppendLog($"警告: 无法复制文件 {Path.GetFileName(source)}: {ex.Message}");
                    return;
                }
            }
        }

        private void CopyCompanionTextFileWithRetry(string sourceImagePath, string destinationImagePath, int maxRetries = 3)
        {
            string sourceTxtPath = Path.ChangeExtension(sourceImagePath, ".txt");
            if (!File.Exists(sourceTxtPath))
            {
                return;
            }

            string destinationTxtPath = Path.ChangeExtension(destinationImagePath, ".txt");
            CopyFileWithRetry(sourceTxtPath, destinationTxtPath, maxRetries);
        }

        private void PrepareOutputRoot(string outputRoot)
        {
            if (Directory.Exists(outputRoot))
            {
                AppendLog($"检测到已有输出路径，先清理旧结果: {outputRoot}");

                if (!TryDeleteDirectoryWithRetry(outputRoot, maxRetries: 6, delayMilliseconds: 400, out Exception? deleteError))
                {
                    var backupPath = $"{outputRoot}_old_{DateTime.Now:yyyyMMdd_HHmmss}";
                    try
                    {
                        Directory.Move(outputRoot, backupPath);
                        AppendLog($"旧输出路径被占用，已重命名为: {backupPath}");
                    }
                    catch (Exception moveEx)
                    {
                        throw new IOException(
                            $"无法清理旧输出路径: {outputRoot}，请关闭占用该路径的程序后重试。",
                            new AggregateException(deleteError ?? moveEx, moveEx));
                    }
                }
            }

            Directory.CreateDirectory(outputRoot);
        }

        private static bool TryDeleteDirectoryWithRetry(string path, int maxRetries, int delayMilliseconds, out Exception? lastError)
        {
            lastError = null;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        return true;
                    }

                    ClearReadOnlyAttributes(path);
                    Directory.Delete(path, recursive: true);

                    if (!Directory.Exists(path))
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    lastError = ex;
                }

                Thread.Sleep(delayMilliseconds * (attempt + 1));
            }

            return false;
        }

        private static void ClearReadOnlyAttributes(string rootPath)
        {
            foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(dir);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(dir, attributes & ~FileAttributes.ReadOnly);
                }
            }
        }

        private static bool IsSameOrSubDirectory(string candidatePath, string parentPath)
        {
            if (candidatePath.Equals(parentPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return candidatePath.StartsWith(parentPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectoryPath(string path)
        {
            var full = Path.GetFullPath(path)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var root = Path.GetPathRoot(full)?
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                ?? string.Empty;
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }

            return full.TrimEnd(Path.DirectorySeparatorChar);
        }
    }
}
