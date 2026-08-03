using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public sealed class ModelOption
    {
        public ModelOption(string displayName, string fileName, string url)
        {
            DisplayName = displayName;
            FileName = fileName;
            Url = url;
        }

        public string DisplayName { get; }
        public string FileName { get; }
        public string Url { get; }
    }

    public class HandIntegrityFilterViewModel : INotifyPropertyChanged
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private const string ToolDisplayName = "手指完整性筛选";

        private string _inputDirectory = string.Empty;
        private string _outputDirectory = string.Empty;
        private string _goodOutputDirectory = string.Empty;
        private bool _includeSubfolders = true;
        private bool _copyCompanionText = true;
        private bool _shouldClearInputDirectoryAfterCompletion;
        private bool _isIdle = true;
        private double _progressValue;
        private float _detectorConfidence = 0.25f;
        private float _detectorIouThreshold = 0.7f;
        private int _detectorInputSize = 640;
        private int _landmarkInputSize = 224;
        private int _handClassId;
        private float _cropScale = 1.2f;
        private float _minFingerLengthRatio = 0.25f;
        private float _minTipToMcpRatio = 1.05f;
        private float _minPresenceScore = 0.5f;
        private bool _failOnLandmarkMissing = true;
        private string _selectedOutputMode = "移动";
        private string _selectedLandmarkInputFormat = "NHWC";
        private readonly string _detectorModelsDirectory;
        private readonly string _landmarkModelsDirectory;
        private ModelOption _selectedDetectorModel = null!;
        private ModelOption _selectedLandmarkModel = null!;

        private const int MaxLogLines = 1000;

        public ObservableCollection<string> OutputModes { get; } = new ObservableCollection<string> { "移动", "复制" };
        public ObservableCollection<string> LandmarkInputFormats { get; } = new ObservableCollection<string> { "NHWC", "NCHW" };
        public ObservableCollection<string> LogLines { get; } = new ObservableCollection<string>();
        public ObservableCollection<ModelOption> DetectorModels { get; } = new ObservableCollection<ModelOption>();
        public ObservableCollection<ModelOption> LandmarkModels { get; } = new ObservableCollection<ModelOption>();

        public string InputDirectory
        {
            get => _inputDirectory;
            set
            {
                var previousInput = _inputDirectory;
                if (SetField(ref _inputDirectory, value))
                {
                    OutputDirectory = ToolOutputDirectoryDefaults.RefreshForInputChange(OutputDirectory, previousInput, value, ToolDisplayName);
                }
            }
        }

        public string OutputDirectory { get => _outputDirectory; set => SetField(ref _outputDirectory, value); }
        public string GoodOutputDirectory { get => _goodOutputDirectory; set => SetField(ref _goodOutputDirectory, value); }
        public bool IncludeSubfolders { get => _includeSubfolders; set => SetField(ref _includeSubfolders, value); }
        public bool CopyCompanionText { get => _copyCompanionText; set => SetField(ref _copyCompanionText, value); }
        public bool ShouldClearInputDirectoryAfterCompletion { get => _shouldClearInputDirectoryAfterCompletion; set => SetField(ref _shouldClearInputDirectoryAfterCompletion, value); }
        public bool IsIdle { get => _isIdle; set => SetField(ref _isIdle, value); }
        public double ProgressValue { get => _progressValue; set => SetField(ref _progressValue, value); }
        public float DetectorConfidence { get => _detectorConfidence; set => SetField(ref _detectorConfidence, value); }
        public float DetectorIouThreshold { get => _detectorIouThreshold; set => SetField(ref _detectorIouThreshold, value); }
        public int DetectorInputSize { get => _detectorInputSize; set => SetField(ref _detectorInputSize, value); }
        public int LandmarkInputSize { get => _landmarkInputSize; set => SetField(ref _landmarkInputSize, value); }
        public int HandClassId { get => _handClassId; set => SetField(ref _handClassId, value); }
        public float CropScale { get => _cropScale; set => SetField(ref _cropScale, value); }
        public float MinFingerLengthRatio { get => _minFingerLengthRatio; set => SetField(ref _minFingerLengthRatio, value); }
        public float MinTipToMcpRatio { get => _minTipToMcpRatio; set => SetField(ref _minTipToMcpRatio, value); }
        public float MinPresenceScore { get => _minPresenceScore; set => SetField(ref _minPresenceScore, value); }
        public bool FailOnLandmarkMissing { get => _failOnLandmarkMissing; set => SetField(ref _failOnLandmarkMissing, value); }
        public string SelectedOutputMode { get => _selectedOutputMode; set => SetField(ref _selectedOutputMode, value); }
        public string SelectedLandmarkInputFormat { get => _selectedLandmarkInputFormat; set => SetField(ref _selectedLandmarkInputFormat, value); }
        public ModelOption SelectedDetectorModel { get => _selectedDetectorModel; set => SetField(ref _selectedDetectorModel, value); }
        public ModelOption SelectedLandmarkModel { get => _selectedLandmarkModel; set => SetField(ref _selectedLandmarkModel, value); }

        public ICommand BrowseInputCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand BrowseGoodOutputCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand ClearLogCommand { get; }

        public HandIntegrityFilterViewModel()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _detectorModelsDirectory = Path.Combine(baseDir, "Models", "hand_detection");
            _landmarkModelsDirectory = Path.Combine(baseDir, "Models", "hand_landmark");

            DetectorModels.Add(new ModelOption("anime_hand_detection v1.0 s (精度优先)", "hand_detect_v1.0_s.onnx", "https://huggingface.co/deepghs/anime_hand_detection/resolve/main/hand_detect_v1.0_s/model.onnx"));
            DetectorModels.Add(new ModelOption("anime_hand_detection v1.0 n (速度优先)", "hand_detect_v1.0_n.onnx", "https://huggingface.co/deepghs/anime_hand_detection/resolve/main/hand_detect_v1.0_n/model.onnx"));
            SelectedDetectorModel = DetectorModels[0];

            LandmarkModels.Add(new ModelOption("MediaPipe Hands 2023Feb (21点)", "handpose_estimation_mediapipe_2023feb.onnx", "https://huggingface.co/opencv/handpose_estimation_mediapipe/resolve/main/handpose_estimation_mediapipe_2023feb.onnx"));
            SelectedLandmarkModel = LandmarkModels[0];

            BrowseInputCommand = new RelayCommand(_ => BrowseForFolder(path => InputDirectory = path, "选择输入路径"));
            BrowseOutputCommand = new RelayCommand(_ => BrowseForFolder(path => OutputDirectory = path, "选择输出路径"));
            BrowseGoodOutputCommand = new RelayCommand(_ => BrowseForFolder(path => GoodOutputDirectory = path, "选择好图输出路径"));
            StartCommand = new RelayCommand(async _ => await StartProcessing(), _ => IsIdle);
            ClearLogCommand = new RelayCommand(_ => LogLines.Clear(), _ => IsIdle);
        }

        private void AddLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add(message);
                if (LogLines.Count > MaxLogLines)
                {
                    LogLines.RemoveAt(0);
                }
            });
        }

        private void BrowseForFolder(Action<string> setPathAction, string title)
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = title
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                setPathAction(dialog.FileName);
            }
        }

        private async Task StartProcessing()
        {
            IsIdle = false;
            (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            ProgressValue = 0;
            LogLines.Clear();
            OutputDirectory = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputDirectory, InputDirectory, ToolDisplayName);

            var detectorPath = await EnsureModelAsync(SelectedDetectorModel, _detectorModelsDirectory, "手部检测模型");
            if (detectorPath == null)
            {
                IsIdle = true;
                (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
                return;
            }

            var landmarkPath = await EnsureModelAsync(SelectedLandmarkModel, _landmarkModelsDirectory, "手指关键点模型");
            if (landmarkPath == null)
            {
                IsIdle = true;
                (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
                return;
            }

            var options = new HandIntegrityFilterOptions
            {
                InputDirectory = InputDirectory,
                OutputDirectory = OutputDirectory,
                GoodOutputDirectory = GoodOutputDirectory,
                IncludeSubfolders = IncludeSubfolders,
                MoveBadImages = SelectedOutputMode == "移动",
                MoveGoodImages = SelectedOutputMode == "移动",
                CopyCompanionText = CopyCompanionText,
                HandDetectorModelPath = detectorPath,
                HandLandmarkModelPath = landmarkPath,
                HandClassId = HandClassId,
                DetectorConfidence = DetectorConfidence,
                DetectorIouThreshold = DetectorIouThreshold,
                DetectorInputSize = DetectorInputSize,
                LandmarkInputSize = LandmarkInputSize,
                LandmarkInputFormat = SelectedLandmarkInputFormat,
                CropScale = CropScale,
                MinFingerLengthRatio = MinFingerLengthRatio,
                MinTipToMcpRatio = MinTipToMcpRatio,
                MinPresenceScore = MinPresenceScore,
                FailOnLandmarkMissing = FailOnLandmarkMissing
            };

            var processor = new HandIntegrityFilter();
            var progress = new Progress<double>(value => ProgressValue = value);
            bool success = false;

            try
            {
                await Task.Run(() => processor.ProcessAsync(options, progress, AddLog));
                success = true;
            }
            catch (Exception ex)
            {
                AddLog($"发生严重错误: {ex.Message}");
            }
            finally
            {
                if (success)
                {
                    TryClearInputDirectoryAfterCompletion();
                }

                ProgressValue = 100;
                IsIdle = true;
                (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void TryClearInputDirectoryAfterCompletion()
        {
            if (!ShouldClearInputDirectoryAfterCompletion)
            {
                return;
            }

            InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.TryClearContents(
                InputDirectory,
                new[] { OutputDirectory, GoodOutputDirectory });
            AddLog(result.Message);
        }

        private async Task<string?> EnsureModelAsync(ModelOption option, string modelsDirectory, string description)
        {
            if (option == null)
            {
                AddLog($"错误: 未选择{description}。");
                return null;
            }

            var fullPath = Path.GetFullPath(Path.Combine(modelsDirectory, option.FileName));
            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            if (string.IsNullOrWhiteSpace(option.Url))
            {
                AddLog($"错误: 无法自动下载{description}。");
                return null;
            }

            var targetDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            AddLog($"开始下载{description}...");

            try
            {
                await DownloadFileAsync(option.Url, fullPath, description);
                AddLog($"{description}下载完成。");
                return fullPath;
            }
            catch (Exception ex)
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                AddLog($"{description}下载失败: {ex.Message}");
                return null;
            }
        }

        private async Task DownloadFileAsync(string url, string destinationPath, string description)
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            long totalBytesRead = 0;
            long reportStep = totalBytes.HasValue && totalBytes.Value > 0 ? Math.Max(1, totalBytes.Value / 20) : 5 * 1024 * 1024;
            long nextReport = reportStep;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalBytesRead += bytesRead;

                if (totalBytes.HasValue && totalBytesRead >= nextReport)
                {
                    double percentage = (double)totalBytesRead / totalBytes.Value * 100;
                    AddLog($"{description}下载进度: {totalBytesRead / 1024.0 / 1024.0:F2}MB / {totalBytes.Value / 1024.0 / 1024.0:F2}MB ({percentage:F1}%)");
                    nextReport += reportStep;
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
