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

        private bool _isClustering;

        public bool IsClustering {
            get { return _isClustering; }
            set {
                _isClustering = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsClustering)));
            }
        }

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand StartCommand { get; }

        public ImageClusteringViewModel() {
            SelectInputCommand = new Utils.RelayCommand(SelectInputFolder);
            SelectOutputCommand = new Utils.RelayCommand(SelectOutputFolder);
            StartCommand = new Utils.RelayCommand(() => StartClustering(),
                () => !string.IsNullOrEmpty(InputPath) && !string.IsNullOrEmpty(OutputPath) && !IsClustering);
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
                    Application.Current.Dispatcher.Invoke(() => Progress = 0);
                    string modelPath = "Models/csd/csd_clip_model.onnx";
                    if (!File.Exists(modelPath)) {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show($"模型文件不存在: {modelPath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error));
                        return;
                    }

                    var processor = new Utils.ImageClusteringProcessor(modelPath);
                    var imageResults = processor.ProcessFolder(InputPath,
                        p => Application.Current.Dispatcher.Invoke(() =>
                            Progress = p * 0.5),
                        error => Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show(error, "处理失败", MessageBoxButton.OK, MessageBoxImage.Warning)));

                    if (imageResults.Count == 0) {
                        Application.Current.Dispatcher.Invoke(() => MessageBox.Show("未成功处理任何图片，请检查图片格式或模型是否正确。", "信息",
                            MessageBoxButton.OK, MessageBoxImage.Information));
                        return;
                    }

                    List<float[]> dataPoints;
                    if (SelectedFeatureType == "Style") {
                        dataPoints = imageResults.Select(r => r.StyleVector).ToList();
                    }
                    else
                    {
                        dataPoints = imageResults.Select(r => r.ContentVector).ToList();
                    }

                    int[] assignments;
                    if (SelectedAlgorithm == "KMeans") {
                        assignments = Utils.MlNetClusterer.Cluster(dataPoints, NumClusters);
                    }
                    else if (SelectedAlgorithm == "HDBSCAN") {
                        assignments = Utils.HdbscanClusterer.Cluster(dataPoints, MinClusterSize);
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