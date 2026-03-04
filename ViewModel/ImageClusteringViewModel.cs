using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class ImageClusteringViewModel : PipelineNodeViewModelBase
    {
        public override string DisplayName => "图片聚类";
        private const string FeatureTypeHybridDisplay = "Style+Content（偏画风+偏内容）";
        private const string FeatureTypeStyleDisplay = "Style（偏画风）";
        private const string FeatureTypeContentDisplay = "Content（偏内容）";
        private const string CsdModelRelativePath = "Models/csd/csd_clip_model.onnx";
        private const string CsdModelDownloadUrl = "https://huggingface.co/yuxi-liu-wired/CSD/resolve/main/csd_clip_model.onnx";
        private const string CsdModelPageUrl = "https://huggingface.co/yuxi-liu-wired/CSD";
        private const string ClusterDistributionFileName = "cluster_distribution.png";
        private static readonly HttpClient CsdModelHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        private string _inputPath = string.Empty;
        public string InputPath
        {
            get => _inputPath;
            set
            {
                if (SetField(ref _inputPath, value))
                {
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


        private int _numClusters = 40;
        public int NumClusters
        {
            get => _numClusters;
            set => SetField(ref _numClusters, value);
        }

        private int _minClusterSize = 10;
        public int MinClusterSize
        {
            get => _minClusterSize;
            set => SetField(ref _minClusterSize, value);
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
            FeatureTypeContentDisplay
        };

        private string _selectedFeatureType = FeatureTypeHybridDisplay;
        public string SelectedFeatureType
        {
            get => _selectedFeatureType;
            set => SetField(ref _selectedFeatureType, ToFeatureTypeDisplay(value));
        }

        public List<string> ClusteringSpaces { get; } = new() { "高维向量（推荐）", "UMAP 2D（兼容参考）" };

        private string _selectedClusteringSpace = "高维向量（推荐）";
        public string SelectedClusteringSpace
        {
            get => _selectedClusteringSpace;
            set => SetField(ref _selectedClusteringSpace, value);
        }

        public List<string> Algorithms { get; } = new() { "KMeans", "HDBSCAN" };

        private string _selectedAlgorithm = "KMeans";
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

        public bool IsKMeansSelected => SelectedAlgorithm == "KMeans";
        public bool IsHdbscanSelected => SelectedAlgorithm == "HDBSCAN";

        public List<string> ComputeDevices { get; } = new() { "CPU", "GPU" };

        private string _selectedComputeDevice = "GPU";
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

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand MoveFilesToTopLevelCommand { get; }
        public ICommand CheckCudaCommand { get; }

        public ImageClusteringViewModel()
        {
            SelectInputCommand = new RelayCommand(_ => SelectInputFolder());
            SelectOutputCommand = new RelayCommand(_ => SelectOutputFolder());
            StartCommand = new RelayCommand(async () => await RunClusteringAsync(showDialogs: true, CancellationToken.None),
                () => !string.IsNullOrEmpty(InputPath) && !string.IsNullOrEmpty(OutputPath) && !IsClustering);
            MoveFilesToTopLevelCommand = new RelayCommand(async () => await MoveFilesToTopLevelAsync(), () => !string.IsNullOrEmpty(InputPath) && !IsClustering);
            CheckCudaCommand = new RelayCommand(async () => await CheckOrInstallCudaAsync(), () => !IsClustering);
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
                MinClusterSize = MinClusterSize,
                SelectedAlgorithm = SelectedAlgorithm,
                SelectedFeatureType = SelectedFeatureType,
                SelectedClusteringSpace = SelectedClusteringSpace,
                SelectedComputeDevice = SelectedComputeDevice
            };
        }

        private void RefreshCommandStates()
        {
            (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveFilesToTopLevelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CheckCudaCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

        private async Task CheckOrInstallCudaAsync()
        {
            var check = await Task.Run(TaggingProcessor.CheckCudaEnvironment);
            AppendLog($"CUDA 检查结果: {check.Details}");

            if (check.NvidiaDriverDetected && check.CudaExecutionProviderAvailable)
            {
                MessageBox.Show("CUDA 环境可用，已可以使用 GPU(CUDA) 进行图片聚类。", "CUDA 检查", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!check.CudaAppendApiSupported)
            {
                MessageBox.Show(
                    $"{check.Details}\n\n检测到当前程序的 onnxruntime.dll 不支持 CUDA 执行提供器。\n这不是安装 CUDA Toolkit 能解决的问题，请更新/重新发布带 `Microsoft.ML.OnnxRuntime.Gpu` 的版本。",
                    "CUDA 检查",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

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

        private async Task RunClusteringAsync(bool showDialogs, CancellationToken token)
        {
            if (IsClustering)
            {
                return;
            }

            IsClustering = true;
            Progress = 0;
            Log = string.Empty;

            try
            {
                if (!await EnsureCsdModelReadyAsync(showDialogs, token))
                {
                    return;
                }

                await Task.Run(() => RunClusteringWork(showDialogs, token), token);
            }
            finally
            {
                IsClustering = false;
            }
        }

        private void RunClusteringWork(bool showDialogs, CancellationToken token)
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
                    const string samePathMessage = "输入目录与输出目录不能相同，请将输出目录设置为其它路径。";
                    AppendLog(samePathMessage);
                    if (showDialogs)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show(samePathMessage, "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    return;
                }

                if (IsSameOrSubDirectory(outputPathFull, inputPathFull))
                {
                    AppendLog("检测到输出目录位于输入目录内，已自动在特征提取阶段排除输出目录，避免旧结果污染聚类。");
                }

                bool useGpu = SelectedComputeDevice.Equals("GPU", StringComparison.OrdinalIgnoreCase);
                AppendLog($"选择计算设备: {SelectedComputeDevice}");

                var processor = new Utils.ImageClusteringProcessor(modelPath, useGpu, AppendLog);
                var imageResults = processor.ProcessFolder(InputPath,
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
                    new[] { outputPathFull });

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
                    return;
                }

                List<float[]> dataPoints = new(imageResults.Count);
                foreach (var result in imageResults)
                {
                    dataPoints.Add(BuildFeatureVector(result, SelectedFeatureType));
                }

                bool useUmapSpace = SelectedAlgorithm == "HDBSCAN" ||
                    SelectedClusteringSpace.Equals("UMAP 2D（兼容参考）", StringComparison.Ordinal);

                if (SelectedAlgorithm == "HDBSCAN" &&
                    SelectedClusteringSpace.Equals("高维向量（推荐）", StringComparison.Ordinal))
                {
                    AppendLog("HDBSCAN 当前仅支持 2D 输入，已自动切换到 UMAP 2D 聚类空间。");
                }

                List<float[]> clusteringSpace = dataPoints;
                if (useUmapSpace)
                {
                    if (dataPoints.Count < 3)
                    {
                        AppendLog($"样本数量过少（{dataPoints.Count}），跳过 UMAP，直接使用特征前两维。");
                        clusteringSpace = ProjectToTwoDimensions(dataPoints);
                    }
                    else
                    {
                        try
                        {
                            int nNeighbors = Math.Clamp(15, 2, Math.Max(2, dataPoints.Count - 1));
                            AppendLog($"开始 UMAP 降维（metric=cosine, seed=42, n_neighbors={nNeighbors}, n_components=2）: {SelectedFeatureType}");
                            var umapOptions = new Utils.UmapReducer.Options
                            {
                                Metric = "cosine",
                                RandomState = 42,
                                NNeighbors = nNeighbors,
                                NComponents = 2,
                                MinDist = 0.1f,
                                Spread = 1.0f
                            };

                            clusteringSpace = Utils.UmapReducer.ReduceToTwoDimensions(
                                dataPoints,
                                umapOptions,
                                token,
                                p => Application.Current.Dispatcher.Invoke(() => Progress = 50 + p * 25));
                        }
                        catch (Exception ex)
                        {

                            AppendLog($"UMAP 失败，回退 PCA。原因: {ex.Message}");
                            clusteringSpace = Utils.FeatureSpaceReducer.ReduceToTwoDimensionsPca(dataPoints);
                        }
                    }

                    if (clusteringSpace.Count != dataPoints.Count || clusteringSpace.Any(v => v == null || v.Length != 2))
                    {
                        throw new InvalidOperationException("降维输出异常：UMAP/PCA 未生成有效 2D 特征。");
                    }
                }
                else
                {
                    AppendLog($"使用高维向量直接聚类（维度={dataPoints[0].Length}）。");
                }

                int[] assignments = SelectedAlgorithm switch
                {
                    "KMeans" => Utils.SklearnKMeansClusterer.Cluster(clusteringSpace, NumClusters, randomState: 42),
                    "HDBSCAN" => Utils.HdbscanClusterer.Cluster(clusteringSpace, MinClusterSize),
                    _ => new int[dataPoints.Count]
                };

                Application.Current.Dispatcher.Invoke(() => Progress = 75);

                string featureLower = GetFeatureTypeSlug(SelectedFeatureType);
                string spaceLower = useUmapSpace ? "umap2d" : "raw";
                string title = $"{SelectedAlgorithm}_{featureLower}_{spaceLower}";
                string outputRoot = Path.Combine(OutputPath, title);
                PrepareOutputRoot(outputRoot);

                string? distributionImagePath = null;
                try
                {
                    var distributionSpace = BuildDistributionSpace(dataPoints, clusteringSpace, useUmapSpace, token);
                    distributionImagePath = Utils.ClusterDistributionPlotter.SaveScatterPlot(
                        distributionSpace,
                        assignments,
                        outputRoot,
                        ClusterDistributionFileName);
                    AppendLog($"已生成聚类分布图: {distributionImagePath}");
                }
                catch (Exception ex)
                {
                    AppendLog($"生成聚类分布图失败: {ex.Message}");
                }

                if (SelectedAlgorithm == "KMeans")
                {
                    for (int c = 0; c < NumClusters; c++)
                    {
                        Directory.CreateDirectory(Path.Combine(outputRoot, $"class_{c}"));
                    }
                }
                else if (SelectedAlgorithm == "HDBSCAN")
                {
                    int clusterCount = assignments.Where(a => a >= 0).Distinct().Count();
                    for (int c = 0; c < clusterCount; c++)
                    {
                        Directory.CreateDirectory(Path.Combine(outputRoot, $"class_{c}"));
                    }
                    Directory.CreateDirectory(Path.Combine(outputRoot, "noise"));
                }

                for (int i = 0; i < imageResults.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    int clusterId = assignments[i];
                    string sourceFilePath = imageResults[i].FilePath;
                    string targetDir = clusterId == -1
                        ? Path.Combine(outputRoot, "noise")
                        : Path.Combine(outputRoot, $"class_{clusterId}");

                    Directory.CreateDirectory(targetDir);
                    string targetFilePath = BuildUniqueTargetFilePath(targetDir, sourceFilePath, i);
                    CopyFileWithRetry(sourceFilePath, targetFilePath);
                    CopyCompanionTextFileWithRetry(sourceFilePath, targetFilePath);

                    Application.Current.Dispatcher.Invoke(() =>
                        Progress = 75 + (double)(i + 1) / imageResults.Count * 25);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AppendLog("图片聚类完成");
                    Progress = 100;
                    if (showDialogs)
                    {
                        ShowClusteringCompletionDialog(outputRoot, distributionImagePath, imageResults.Count, assignments);
                    }
                });
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
                int nNeighbors = Math.Clamp(15, 2, Math.Max(2, dataPoints.Count - 1));
                AppendLog($"开始生成分布图坐标（UMAP 2D, n_neighbors={nNeighbors}）");
                var umapOptions = new Utils.UmapReducer.Options
                {
                    Metric = "cosine",
                    RandomState = 42,
                    NNeighbors = nNeighbors,
                    NComponents = 2,
                    MinDist = 0.1f,
                    Spread = 1.0f
                };

                return Utils.UmapReducer.ReduceToTwoDimensions(dataPoints, umapOptions, token);
            }
            catch (Exception ex)
            {
                AppendLog($"分布图 UMAP 失败，回退 PCA。原因: {ex.Message}");
                return Utils.FeatureSpaceReducer.ReduceToTwoDimensionsPca(dataPoints);
            }
        }

        private static bool IsValidTwoDimensionalSpace(IReadOnlyList<float[]> points)
        {
            return points.Count > 0 && points.All(v => v != null && v.Length == 2);
        }

        private void ShowClusteringCompletionDialog(string outputRoot, string? distributionImagePath, int totalCount, IReadOnlyList<int> assignments)
        {
            int clusterCount = assignments.Where(a => a >= 0).Distinct().Count();
            int noiseCount = assignments.Count(a => a < 0);
            string summary = $"图片聚类完成：共 {totalCount} 张，聚类簇 {clusterCount} 个";
            if (noiseCount > 0)
            {
                summary += $"，噪声 {noiseCount} 张";
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

        private static float[] BuildFeatureVector(ImageStyleResult result, string featureType)
        {
            if (result == null)
            {
                throw new InvalidOperationException("特征提取结果为空。");
            }

            return GetFeatureTypeKey(featureType) switch
            {
                "Style" => NormalizeVectorCopy(result.StyleVector),
                "Content" => NormalizeVectorCopy(result.ContentVector),
                "Style+Content" => ConcatNormalizedVectors(result.StyleVector, result.ContentVector),
                _ => throw new InvalidOperationException($"未知特征类型: {featureType}")
            };
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
                "Content" => "content",
                "Style+Content" => "hybrid",
                _ => "unknown"
            };
        }

        private static string GetFeatureTypeKey(string? featureType)
        {
            if (string.IsNullOrWhiteSpace(featureType))
            {
                return "Style+Content";
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
                "Content" => FeatureTypeContentDisplay,
                _ => FeatureTypeHybridDisplay
            };
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

        private static string BuildUniqueTargetFilePath(string targetDir, string sourceFilePath, int fallbackIndex)
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

            string candidate = Path.Combine(targetDir, sourceName + extension);
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            for (int i = 1; ; i++)
            {
                candidate = Path.Combine(targetDir, $"{sourceName}_{i}{extension}");
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
                                Log += $"警告: 文件 {fileName} 已存在于目标文件夹，已跳过。\n";
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
                AppendLog($"检测到已有输出目录，先清理旧结果: {outputRoot}");

                if (!TryDeleteDirectoryWithRetry(outputRoot, maxRetries: 6, delayMilliseconds: 400, out Exception? deleteError))
                {
                    var backupPath = $"{outputRoot}_old_{DateTime.Now:yyyyMMdd_HHmmss}";
                    try
                    {
                        Directory.Move(outputRoot, backupPath);
                        AppendLog($"旧输出目录被占用，已重命名为: {backupPath}");
                    }
                    catch (Exception moveEx)
                    {
                        throw new IOException(
                            $"无法清理旧输出目录: {outputRoot}，请关闭占用该目录的程序后重试。",
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
