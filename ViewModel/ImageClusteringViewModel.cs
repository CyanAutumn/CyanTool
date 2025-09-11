using System.Windows.Input;
using System.Threading.Tasks;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.IO;
using System.Linq;
using System.Windows;

namespace CyanTools.ViewModel
{
    public class ImageClusteringViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private string _inputPath = string.Empty;
        public string InputPath
        {
            get { return _inputPath; }
            set
            {
                _inputPath = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(InputPath)));
            }
        }

        private string _outputPath = string.Empty;
        public string OutputPath
        {
            get { return _outputPath; }
            set
            {
                _outputPath = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(OutputPath)));
            }
        }

        private int _numClusters = 5;
        public int NumClusters
        {
            get { return _numClusters; }
            set
            {
                _numClusters = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(NumClusters)));
            }
        }

        private double _progress;
        public double Progress
        {
            get { return _progress; }
            set
            {
                _progress = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Progress)));
            }
        }

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand StartCommand { get; }

        public ImageClusteringViewModel()
        { 
            SelectInputCommand = new Utils.RelayCommand(SelectInputFolder);
            SelectOutputCommand = new Utils.RelayCommand(SelectOutputFolder);
            StartCommand = new Utils.RelayCommand(async () => await StartClustering(), () => !string.IsNullOrEmpty(InputPath) && !string.IsNullOrEmpty(OutputPath));
        }

        private void SelectInputFolder()
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                InputPath = dialog.FileName;
            }
        }

        private void SelectOutputFolder()
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputPath = dialog.FileName;
            }
        }

        private async Task StartClustering()
        {
            Progress = 0;
            string modelPath = "Models/csd/csd_clip_model.onnx";
            if (!File.Exists(modelPath))
            {
                MessageBox.Show($"模型文件不存在: {modelPath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await Task.Run(() =>
            {
                var processor = new Utils.ImageClusteringProcessor(modelPath);
                var imageResults = processor.ProcessFolder(InputPath, 
                    p => Application.Current.Dispatcher.Invoke(() => Progress = p),
                    error => Application.Current.Dispatcher.Invoke(() => MessageBox.Show(error, "处理失败", MessageBoxButton.OK, MessageBoxImage.Warning)));

                if (imageResults.Count == 0)
                {
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("未成功处理任何图片，请检查图片格式或模型是否正确。", "信息", MessageBoxButton.OK, MessageBoxImage.Information));
                    return;
                }

                var dataPoints = imageResults.Select(r => r.StyleVector).ToList();
                int[] assignments = Utils.KMeansClusterer.Cluster(dataPoints, NumClusters);

                for (int i = 0; i < imageResults.Count; i++)
                {
                    int clusterId = assignments[i];
                    string sourceFilePath = imageResults[i].FilePath;
                    string clusterFolderName = $"group_{clusterId}";
                    string targetSubFolder = Path.Combine(OutputPath, clusterFolderName);
                    Directory.CreateDirectory(targetSubFolder);
                    string targetFilePath = Path.Combine(targetSubFolder, Path.GetFileName(sourceFilePath));
                    File.Copy(sourceFilePath, targetFilePath, true);
                    Application.Current.Dispatcher.Invoke(() => Progress = 50 + (double)(i + 1) / imageResults.Count * 50);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("图片聚类完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    Progress = 100;
                });
            });
        }
    }
}