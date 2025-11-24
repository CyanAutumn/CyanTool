using System.Windows.Input;
using System.Threading.Tasks;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.IO;
using System.Linq;
using System.Windows;
using System.Collections.Generic;
using System;

namespace CyanTools.ViewModel {
    public class ImageClusteringViewModel : System.ComponentModel.INotifyPropertyChanged {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private string _inputPath = string.Empty;

        public string InputPath {
            get { return _inputPath; }
            set {
                _inputPath = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(InputPath)));
            }
        }

        private string _outputPath = string.Empty;

        public string OutputPath {
            get { return _outputPath; }
            set {
                _outputPath = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(OutputPath)));
            }
        }

        private int _numClusters = 5;

        public int NumClusters {
            get { return _numClusters; }
            set {
                _numClusters = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(NumClusters)));
            }
        }

        private int _minClusterSize = 10;

        public int MinClusterSize {
            get { return _minClusterSize; }
            set {
                _minClusterSize = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(MinClusterSize)));
            }
        }

        private double _progress;

        public double Progress {
            get { return _progress; }
            set {
                _progress = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Progress)));
            }
        }

        public List<string> FeatureTypes { get; } = new List<string> { "Style", "Content" };
        private string _selectedFeatureType = "Style";

        public string SelectedFeatureType {
            get { return _selectedFeatureType; }
            set {
                _selectedFeatureType = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectedFeatureType)));
            }
        }

        public List<string> Algorithms { get; } = new List<string> { "KMeans", "HDBSCAN" };
        private string _selectedAlgorithm = "KMeans";

        public string SelectedAlgorithm {
            get { return _selectedAlgorithm; }
            set {
                _selectedAlgorithm = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectedAlgorithm)));
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(IsKMeansSelected)));
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(IsHdbscanSelected)));
            }
        }

        public bool IsKMeansSelected => SelectedAlgorithm == "KMeans";
        public bool IsHdbscanSelected => SelectedAlgorithm == "HDBSCAN";

        public List<string> ComputeDevices { get; } = new List<string> { "CPU", "GPU" };
        private string _selectedComputeDevice = "GPU";

        public string SelectedComputeDevice {
            get { return _selectedComputeDevice; }
            set {
                _selectedComputeDevice = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectedComputeDevice)));
            }
        }

        private string _log = "";
        public string Log {
            get { return _log; }
            set {
                _log = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Log)));
            }
        }

        private void AppendLog(string message) {
            void update() => Log += $"{DateTime.Now:HH:mm:ss} {message}\n";
            if (Application.Current.Dispatcher.CheckAccess()) {
                update();
            }
            else {
                Application.Current.Dispatcher.Invoke(update);
            }
        }

        private bool _isClustering;

        public bool IsClustering {
            get { return _isClustering; }
            set {
                _isClustering = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsClustering)));
                Application.Current.Dispatcher.Invoke(() => {
                    ((Utils.RelayCommand)StartCommand).RaiseCanExecuteChanged();
                    ((Utils.RelayCommand)MoveFilesToTopLevelCommand).RaiseCanExecuteChanged();
                });
            }
        }

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand MoveFilesToTopLevelCommand { get; }

        public ImageClusteringViewModel() {
            SelectInputCommand = new Utils.RelayCommand(SelectInputFolder);
            SelectOutputCommand = new Utils.RelayCommand(SelectOutputFolder);
            StartCommand = new Utils.RelayCommand(() => StartClustering(),
                () => !string.IsNullOrEmpty(InputPath) && !string.IsNullOrEmpty(OutputPath) && !IsClustering);
            MoveFilesToTopLevelCommand = new Utils.RelayCommand(async () => await MoveFilesToTopLevelAsync(), () => !string.IsNullOrEmpty(InputPath) && !IsClustering);
        }

        private async Task MoveFilesToTopLevelAsync() {
            IsClustering = true;
            Log = "开始移动文件...\n";
            Progress = 0;

            await Task.Run(() => {
                try {
                    var subDirectories = Directory.GetDirectories(InputPath, "*", SearchOption.AllDirectories);
                    var filesToMove = new List<string>();
                    foreach (var dir in subDirectories) {
                        filesToMove.AddRange(Directory.GetFiles(dir));
                    }

                    if (filesToMove.Count == 0) {
                        Application.Current.Dispatcher.Invoke(() => {
                            Log += "没有在子文件夹中找到需要移动的文件。\n";
                            MessageBox.Show("没有在子文件夹中找到需要移动的文件。", "操作完成", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                        return;
                    }

                    int movedCount = 0;
                    int skippedCount = 0;
                    for (int i = 0; i < filesToMove.Count; i++) {
                        string sourcePath = filesToMove[i];
                        string fileName = Path.GetFileName(sourcePath);
                        string destPath = Path.Combine(InputPath, fileName);

                        if (File.Exists(destPath)) {
                            skippedCount++;
                            Application.Current.Dispatcher.Invoke(() => {
                                Log += $"警告: 文件 {fileName} 已存在于目标文件夹，已跳过。\n";
                            });
                        } else {
                            File.Move(sourcePath, destPath);
                            movedCount++;
                        }
                        
                        Application.Current.Dispatcher.Invoke(() => {
                            Progress = (double)(i + 1) / filesToMove.Count * 100;
                        });
                    }

                    Application.Current.Dispatcher.Invoke(() => {
                        Log += "操作完成。成功移动 " + movedCount + " 个文件，跳过 " + skippedCount + " 个重复文件。\n";
                        MessageBox.Show($"操作完成。\n成功移动 {movedCount} 个文件。\n跳过 {skippedCount} 个重复文件。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        Progress = 100;
                    });

                } catch (Exception ex) {
                    Application.Current.Dispatcher.Invoke(() => {
                        Log += "发生错误: " + ex.Message + "\n";
                        MessageBox.Show($"发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                } finally {

                    try {
                        var subDirectories = Directory.GetDirectories(InputPath);
                        foreach (var dir in subDirectories) {
                            if (!Directory.EnumerateFileSystemEntries(dir).Any()) {
                                Directory.Delete(dir);
                                Application.Current.Dispatcher.Invoke(() => {
                                    Log += $"已删除空目录: {Path.GetFileName(dir)}\n";
                                });
                            }
                        }
                    } catch (Exception ex) {
                        Application.Current.Dispatcher.Invoke(() => {
                            Log += "清理空目录时发生错误: " + ex.Message + "\n";
                        });
                    }
                }
            });
            IsClustering = false;
        }


        private void SelectInputFolder() {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok) {
                InputPath = dialog.FileName;
            }
        }

        private void SelectOutputFolder() {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok) {
                OutputPath = dialog.FileName;
            }
        }

        private void StartClustering() {
            IsClustering = true;
            ((Utils.RelayCommand)StartCommand).RaiseCanExecuteChanged();

            Task.Run(() => {
                try {
                    Application.Current.Dispatcher.Invoke(() => {
                        Progress = 0;
                        Log = string.Empty;
                    });
                    AppendLog("开始聚类");
                    string modelPath = "Models/csd/csd_clip_model.onnx";
                    if (!File.Exists(modelPath)) {
                        AppendLog($"模型文件不存在: {modelPath}");
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show($"模型文件不存在: {modelPath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error));
                        return;
                    }

                    bool useGpu = SelectedComputeDevice.Equals("GPU", StringComparison.OrdinalIgnoreCase);
                    AppendLog($"选择计算设备: {SelectedComputeDevice}");
                    var processor = new Utils.ImageClusteringProcessor(modelPath, useGpu, msg => AppendLog(msg));
                    var imageResults = processor.ProcessFolder(InputPath,
                        p => Application.Current.Dispatcher.Invoke(() =>
                            Progress = p * 0.5),
                        error => {
                            AppendLog(error);
                            Application.Current.Dispatcher.Invoke(() =>
                                MessageBox.Show(error, "处理失败", MessageBoxButton.OK, MessageBoxImage.Warning));
                        });

                    if (imageResults.Count == 0) {
                        AppendLog("未成功处理任何图片");
                        Application.Current.Dispatcher.Invoke(() => MessageBox.Show("未成功处理任何图片，请检查图片格式或模型是否正确。", "信息",
                            MessageBoxButton.OK, MessageBoxImage.Information));
                        return;
                    }

                    List<float[]> dataPoints = SelectedFeatureType == "Style"
                        ? imageResults.Select(r => r.StyleVector).ToList()
                        : imageResults.Select(r => r.ContentVector).ToList();

                    var featureSpace = Utils.FeatureSpaceReducer.ReduceToTwoDimensions(dataPoints);
                    if (featureSpace.Count != dataPoints.Count) {
                        featureSpace = dataPoints;
                    }

                    int[] assignments;
                    if (SelectedAlgorithm == "KMeans") {
                        assignments = Utils.MlNetClusterer.Cluster(featureSpace, NumClusters);
                    }
                    else if (SelectedAlgorithm == "HDBSCAN") {
                        assignments = Utils.HdbscanClusterer.Cluster(featureSpace, MinClusterSize);
                    }
                    else {
                        assignments = new int[dataPoints.Count];
                    }

                    Application.Current.Dispatcher.Invoke(() => Progress = 50 + 25);

                    for (int i = 0; i < imageResults.Count; i++) {
                        int clusterId = assignments[i];
                        string sourceFilePath = imageResults[i].FilePath;
                        string clusterFolderName = clusterId == -1 ? "noise" : $"group_{clusterId}";
                        string targetSubFolder = Path.Combine(OutputPath, clusterFolderName);


                        Directory.CreateDirectory(targetSubFolder);

                        string fileName = Path.GetFileName(sourceFilePath);
                        string targetFilePath = Path.Combine(targetSubFolder, fileName);


                        CopyFileWithRetry(sourceFilePath, targetFilePath);


                        string txtFilePath = Path.ChangeExtension(sourceFilePath, ".txt");
                        if (File.Exists(txtFilePath)) {
                            string targetTxtFilePath = Path.Combine(targetSubFolder, Path.GetFileName(txtFilePath));
                            CopyFileWithRetry(txtFilePath, targetTxtFilePath);
                        }

                        Application.Current.Dispatcher.Invoke(() =>
                            Progress = 75 + (double)(i + 1) / imageResults.Count * 25);
                    }

                    Application.Current.Dispatcher.Invoke(() => {
                        AppendLog("图片聚类完成");
                        MessageBox.Show("图片聚类完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        Progress = 100;
                    });
                }
                finally {
                    Application.Current.Dispatcher.Invoke(() => {
                        IsClustering = false;
                        ((Utils.RelayCommand)StartCommand).RaiseCanExecuteChanged();
                    });
                }
            });
        }

        private void CopyFileWithRetry(string source, string destination, int maxRetries = 3) {
            for (int attempt = 0; attempt < maxRetries; attempt++) {
                try {

                    if (File.Exists(destination)) {
                        File.Delete(destination);
                        System.Threading.Thread.Sleep(100);
                    }


                    using (var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var destStream =
                           new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None)) {
                        sourceStream.CopyTo(destStream);
                    }

                    return;
                }
                catch (IOException ex) when (attempt < maxRetries - 1) {

                    System.Threading.Thread.Sleep(500 * (attempt + 1));
                }
                catch (Exception ex) {
                    throw new IOException($"无法复制文件 {Path.GetFileName(source)}: {ex.Message}", ex);
                }
            }
        }
    }
}
