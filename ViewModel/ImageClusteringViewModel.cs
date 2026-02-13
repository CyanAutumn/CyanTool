using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        public List<string> FeatureTypes { get; } = new() { "Style", "Content" };

        private string _selectedFeatureType = "Style";
        public string SelectedFeatureType
        {
            get => _selectedFeatureType;
            set => SetField(ref _selectedFeatureType, value);
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

                string modelPath = "Models/csd/csd_clip_model.onnx";
                if (!File.Exists(modelPath))
                {
                    AppendLog($"模型文件不存在: {modelPath}");
                    if (showDialogs)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show($"模型文件不存在: {modelPath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error));
                    }
                    return;
                }

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
                    p => Application.Current.Dispatcher.Invoke(() => Progress = p * 0.5),
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

                List<float[]> dataPoints = SelectedFeatureType == "Style"
                    ? imageResults.Select(r => r.StyleVector).ToList()
                    : imageResults.Select(r => r.ContentVector).ToList();

                List<float[]> featureSpace;
                try
                {
                    AppendLog($"开始 UMAP 降维（metric=cosine, seed=42, n_neighbors=15, n_components=2）: {SelectedFeatureType}");
                    var umapOptions = new Utils.UmapReducer.Options
                    {
                        Metric = "cosine",
                        RandomState = 42,
                        NNeighbors = 15,
                        NComponents = 2,
                        MinDist = 0.1f,
                        Spread = 1.0f
                    };

                    featureSpace = Utils.UmapReducer.ReduceToTwoDimensions(
                        dataPoints,
                        umapOptions,
                        token,
                        p => Application.Current.Dispatcher.Invoke(() => Progress = 30 + p * 40));
                }
                catch (Exception ex)
                {

                    AppendLog($"UMAP 失败，回退 PCA。原因: {ex.Message}");
                    featureSpace = Utils.FeatureSpaceReducer.ReduceToTwoDimensionsPca(dataPoints);
                }

                if (featureSpace.Count != dataPoints.Count || featureSpace.Any(v => v == null || v.Length != 2))
                {
                    throw new InvalidOperationException("降维输出异常：UMAP/PCA 未生成有效 2D 特征。");
                }

                int[] assignments = SelectedAlgorithm switch
                {
                    "KMeans" => Utils.SklearnKMeansClusterer.Cluster(featureSpace, NumClusters, randomState: 42),
                    "HDBSCAN" => Utils.HdbscanClusterer.Cluster(featureSpace, MinClusterSize),
                    _ => new int[dataPoints.Count]
                };

                Application.Current.Dispatcher.Invoke(() => Progress = 75);

                string featureLower = SelectedFeatureType.Equals("Style", StringComparison.OrdinalIgnoreCase) ? "style" : "content";
                string title = $"{SelectedAlgorithm}_{featureLower}";
                string outputRoot = Path.Combine(OutputPath, title);
                PrepareOutputRoot(outputRoot);

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
                    string targetFilePath = Path.Combine(targetDir, $"image_{i}.jpg");
                    CopyFileWithRetry(sourceFilePath, targetFilePath);

                    Application.Current.Dispatcher.Invoke(() =>
                        Progress = 75 + (double)(i + 1) / imageResults.Count * 25);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AppendLog("图片聚类完成");
                    Progress = 100;
                    if (showDialogs)
                    {
                        MessageBox.Show("图片聚类完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                });
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() => RefreshCommandStates());
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
