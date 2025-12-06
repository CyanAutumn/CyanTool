using System;
using System.Collections.Generic;
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

        private int _numClusters = 5;
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

        public ImageClusteringViewModel()
        {
            SelectInputCommand = new RelayCommand(_ => SelectInputFolder());
            SelectOutputCommand = new RelayCommand(_ => SelectOutputFolder());
            StartCommand = new RelayCommand(async () => await RunClusteringAsync(showDialogs: true, CancellationToken.None),
                () => !string.IsNullOrEmpty(InputPath) && !string.IsNullOrEmpty(OutputPath) && !IsClustering);
            MoveFilesToTopLevelCommand = new RelayCommand(async () => await MoveFilesToTopLevelAsync(), () => !string.IsNullOrEmpty(InputPath) && !IsClustering);
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
                    });

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

                var featureSpace = Utils.FeatureSpaceReducer.ReduceToTwoDimensions(dataPoints);
                if (featureSpace.Count != dataPoints.Count)
                {
                    featureSpace = dataPoints;
                }

                int[] assignments = SelectedAlgorithm switch
                {
                    "KMeans" => Utils.MlNetClusterer.Cluster(featureSpace, NumClusters),
                    "HDBSCAN" => Utils.HdbscanClusterer.Cluster(featureSpace, MinClusterSize),
                    _ => new int[dataPoints.Count]
                };

                Application.Current.Dispatcher.Invoke(() => Progress = 75);

                for (int i = 0; i < imageResults.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    int clusterId = assignments[i];
                    string sourceFilePath = imageResults[i].FilePath;
                    string clusterFolderName = clusterId == -1 ? "noise" : $"group_{clusterId}";
                    string targetSubFolder = Path.Combine(OutputPath, clusterFolderName);

                    Directory.CreateDirectory(targetSubFolder);

                    string fileName = Path.GetFileName(sourceFilePath);
                    string targetFilePath = Path.Combine(targetSubFolder, fileName);
                    CopyFileWithRetry(sourceFilePath, targetFilePath);

                    string txtFilePath = Path.ChangeExtension(sourceFilePath, ".txt");
                    if (File.Exists(txtFilePath))
                    {
                        string targetTxtFilePath = Path.Combine(targetSubFolder, Path.GetFileName(txtFilePath));
                        CopyFileWithRetry(txtFilePath, targetTxtFilePath);
                    }

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
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                        Thread.Sleep(100);
                    }

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
                    throw new IOException($"无法复制文件 {Path.GetFileName(source)}: {ex.Message}", ex);
                }
            }
        }
    }
}
