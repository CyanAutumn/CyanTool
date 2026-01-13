using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class SameSourceImageGroupingViewModel : PipelineNodeViewModelBase
    {
        public override string DisplayName => "同源图片归类";

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

        private bool _includeSubfolders = true;
        public bool IncludeSubfolders
        {
            get => _includeSubfolders;
            set => SetField(ref _includeSubfolders, value);
        }

        private bool _enablePatchHash = true;
        public bool EnablePatchHash
        {
            get => _enablePatchHash;
            set => SetField(ref _enablePatchHash, value);
        }

        private bool _enableHashMatch;
        public bool EnableHashMatch
        {
            get => _enableHashMatch;
            set => SetField(ref _enableHashMatch, value);
        }

        private bool _enableOrbMatch = true;
        public bool EnableOrbMatch
        {
            get => _enableOrbMatch;
            set => SetField(ref _enableOrbMatch, value);
        }

        private bool _skipMatchedNonRoot = true;
        public bool SkipMatchedNonRoot
        {
            get => _skipMatchedNonRoot;
            set => SetField(ref _skipMatchedNonRoot, value);
        }

        private int _phashThreshold = 12;
        public int PhashThreshold
        {
            get => _phashThreshold;
            set => SetField(ref _phashThreshold, value);
        }

        private int _patchPhashThreshold = 16;
        public int PatchPhashThreshold
        {
            get => _patchPhashThreshold;
            set => SetField(ref _patchPhashThreshold, value);
        }

        private int _lowResHashThreshold = 12;
        public int LowResHashThreshold
        {
            get => _lowResHashThreshold;
            set => SetField(ref _lowResHashThreshold, value);
        }

        private int _orbFeatures = 2000;
        public int OrbFeatures
        {
            get => _orbFeatures;
            set => SetField(ref _orbFeatures, value);
        }

        private int _orbMaxSize = 1024;
        public int OrbMaxSize
        {
            get => _orbMaxSize;
            set => SetField(ref _orbMaxSize, value);
        }

        private int _minGoodMatches = 25;
        public int MinGoodMatches
        {
            get => _minGoodMatches;
            set => SetField(ref _minGoodMatches, value);
        }

        private int _minInliers = 20;
        public int MinInliers
        {
            get => _minInliers;
            set => SetField(ref _minInliers, value);
        }

        private double _minInlierRatio = 0.30;
        public double MinInlierRatio
        {
            get => _minInlierRatio;
            set => SetField(ref _minInlierRatio, value);
        }

        private double _cropAreaRatioThreshold = 0.90;
        public double CropAreaRatioThreshold
        {
            get => _cropAreaRatioThreshold;
            set => SetField(ref _cropAreaRatioThreshold, value);
        }

        private double _lowResRatioThreshold = 0.85;
        public double LowResRatioThreshold
        {
            get => _lowResRatioThreshold;
            set => SetField(ref _lowResRatioThreshold, value);
        }

        private double _editedDiffRatioThreshold = 0.15;
        public double EditedDiffRatioThreshold
        {
            get => _editedDiffRatioThreshold;
            set => SetField(ref _editedDiffRatioThreshold, value);
        }

        private int _diffCheckSize = 512;
        public int DiffCheckSize
        {
            get => _diffCheckSize;
            set => SetField(ref _diffCheckSize, value);
        }

        private int _diffPixelThreshold = 20;
        public int DiffPixelThreshold
        {
            get => _diffPixelThreshold;
            set => SetField(ref _diffPixelThreshold, value);
        }

        private bool _enableTemplateMatch = true;
        public bool EnableTemplateMatch
        {
            get => _enableTemplateMatch;
            set => SetField(ref _enableTemplateMatch, value);
        }

        private double _templateMatchThreshold = 0.85;
        public double TemplateMatchThreshold
        {
            get => _templateMatchThreshold;
            set => SetField(ref _templateMatchThreshold, value);
        }

        private int _templateMaxSize = 1024;
        public int TemplateMaxSize
        {
            get => _templateMaxSize;
            set => SetField(ref _templateMaxSize, value);
        }

        private int _templateMinSize = 64;
        public int TemplateMinSize
        {
            get => _templateMinSize;
            set => SetField(ref _templateMinSize, value);
        }

        private int _maxCandidatesPerImage = 600;
        public int MaxCandidatesPerImage
        {
            get => _maxCandidatesPerImage;
            set => SetField(ref _maxCandidatesPerImage, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetField(ref _progress, value);
        }

        private string _log = string.Empty;
        public string Log
        {
            get => _log;
            set => SetField(ref _log, value);
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetField(ref _isProcessing, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand StartCommand { get; }

        public SameSourceImageGroupingViewModel()
        {
            SelectInputCommand = new RelayCommand(SelectInputFolder, () => !IsProcessing);
            SelectOutputCommand = new RelayCommand(SelectOutputFolder, () => !IsProcessing);
            StartCommand = new RelayCommand(async () => await RunGroupingAsync(showDialogs: true, CancellationToken.None),
                () => !string.IsNullOrEmpty(InputPath) && !string.IsNullOrEmpty(OutputPath) && !IsProcessing);
        }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            _ = context;
            return RunGroupingAsync(showDialogs: false, token);
        }

        public override PipelineNodeViewModelBase Clone()
        {
            return new SameSourceImageGroupingViewModel
            {
                InputPath = InputPath,
                OutputPath = OutputPath,
                IncludeSubfolders = IncludeSubfolders,
                EnablePatchHash = EnablePatchHash,
                EnableHashMatch = EnableHashMatch,
                EnableOrbMatch = EnableOrbMatch,
                SkipMatchedNonRoot = SkipMatchedNonRoot,
                PhashThreshold = PhashThreshold,
                PatchPhashThreshold = PatchPhashThreshold,
                LowResHashThreshold = LowResHashThreshold,
                OrbFeatures = OrbFeatures,
                OrbMaxSize = OrbMaxSize,
                MinGoodMatches = MinGoodMatches,
                MinInliers = MinInliers,
                MinInlierRatio = MinInlierRatio,
                CropAreaRatioThreshold = CropAreaRatioThreshold,
                LowResRatioThreshold = LowResRatioThreshold,
                EditedDiffRatioThreshold = EditedDiffRatioThreshold,
                DiffCheckSize = DiffCheckSize,
                DiffPixelThreshold = DiffPixelThreshold,
                EnableTemplateMatch = EnableTemplateMatch,
                TemplateMatchThreshold = TemplateMatchThreshold,
                TemplateMaxSize = TemplateMaxSize,
                TemplateMinSize = TemplateMinSize,
                MaxCandidatesPerImage = MaxCandidatesPerImage
            };
        }

        private void RefreshCommandStates()
        {
            (SelectInputCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SelectOutputCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

            LogToPipeline($"[同源图片归类] {message}");
        }

        private void SelectInputFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择输入文件夹"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                InputPath = dialog.FileName;
            }
        }

        private void SelectOutputFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择输出文件夹"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputPath = dialog.FileName;
            }
        }

        private async Task RunGroupingAsync(bool showDialogs, CancellationToken token)
        {
            if (IsProcessing)
            {
                return;
            }

            IsProcessing = true;
            Progress = 0;
            Log = string.Empty;

            try
            {
                var progress = CreateProgressReporter();
                await Task.Run(() => RunGroupingWork(showDialogs, token, progress), token);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void RunGroupingWork(bool showDialogs, CancellationToken token, IProgress<double> progress)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(InputPath) || !Directory.Exists(InputPath))
                {
                    AppendLog("输入文件夹无效。");
                    if (showDialogs)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show("输入文件夹无效。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    return;
                }

                if (string.IsNullOrWhiteSpace(OutputPath))
                {
                    AppendLog("输出文件夹无效。");
                    if (showDialogs)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show("输出文件夹无效。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    return;
                }

                var options = new SameSourceGroupingOptions
                {
                    IncludeSubfolders = IncludeSubfolders,
                    EnablePatchHash = EnablePatchHash,
                    EnableHashMatch = EnableHashMatch,
                    EnableOrbMatch = EnableOrbMatch,
                    SkipMatchedNonRoot = SkipMatchedNonRoot,
                    PhashThreshold = PhashThreshold,
                    PatchPhashThreshold = PatchPhashThreshold,
                    LowResHashThreshold = LowResHashThreshold,
                    OrbFeatures = OrbFeatures,
                    OrbMaxSize = OrbMaxSize,
                    MinGoodMatches = MinGoodMatches,
                    MinInliers = MinInliers,
                    MinInlierRatio = MinInlierRatio,
                    CropAreaRatioThreshold = CropAreaRatioThreshold,
                    LowResRatioThreshold = LowResRatioThreshold,
                    EditedDiffRatioThreshold = EditedDiffRatioThreshold,
                    DiffCheckSize = DiffCheckSize,
                    DiffPixelThreshold = DiffPixelThreshold,
                    EnableTemplateMatch = EnableTemplateMatch,
                    TemplateMatchThreshold = TemplateMatchThreshold,
                    TemplateMaxSize = TemplateMaxSize,
                    TemplateMinSize = TemplateMinSize,
                    MaxCandidatesPerImage = MaxCandidatesPerImage
                };

                SameSourceImageGroupingProcessor.Process(InputPath, OutputPath, options, progress, AppendLog, token);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Progress = 100;
                    if (showDialogs)
                    {
                        MessageBox.Show("同源图片归类完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                AppendLog("任务已取消。");
            }
            catch (Exception ex)
            {
                AppendLog($"发生错误: {ex.Message}");
                if (showDialogs)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        MessageBox.Show($"处理失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(RefreshCommandStates);
            }
        }

        private IProgress<double> CreateProgressReporter()
        {
            return new Progress<double>(value =>
            {
                if (Application.Current == null)
                {
                    Progress = value;
                    return;
                }

                if (Application.Current.Dispatcher.CheckAccess())
                {
                    Progress = value;
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => Progress = value));
                }
            });
        }
    }
}
