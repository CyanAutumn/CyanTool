using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
namespace CyanTools.ViewModel
{
    public sealed class FolderPickerPipelineNode : PipelineNodeViewModelBase, IPipelineRuntimeOutputNode, IPipelineSnapshotProvider
    {
        private string _folderPath = string.Empty;
        private string _runtimeOutputPath = string.Empty;

        public FolderPickerPipelineNode()
        {
            SelectFolderCommand = new RelayCommand(SelectFolder);
        }

        public override string DisplayName => "\u9009\u62E9\u8F93\u5165\u6587\u4EF6\u5939";

        public string FolderPath
        {
            get => _folderPath;
            set
            {
                if (SetField(ref _folderPath, value ?? string.Empty))
                {
                    _runtimeOutputPath = string.Empty;
                    OnPropertyChanged(nameof(HasFolderPath));
                }
            }
        }

        public bool HasFolderPath => !string.IsNullOrWhiteSpace(FolderPath);

        public ICommand SelectFolderCommand { get; }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            string path = NormalizeExistingFolder(FolderPath);
            _runtimeOutputPath = path;
            context.AppendLog($"[{DisplayName}] \u5DF2\u9009\u62E9\u672C\u5730\u6587\u4EF6\u5939: {path}");

            return Task.CompletedTask;
        }

        public override PipelineNodeViewModelBase Clone()
        {
            return new FolderPickerPipelineNode
            {
                FolderPath = FolderPath
            };
        }

        public void ResetRuntimeOutputPath()
        {
            _runtimeOutputPath = string.Empty;
        }

        public bool TryGetRuntimeOutputPath(out string path)
        {
            if (!string.IsNullOrWhiteSpace(_runtimeOutputPath))
            {
                path = _runtimeOutputPath;
                return true;
            }

            return TryNormalizeExistingFolder(FolderPath, out path);
        }

        public Dictionary<string, object?> CaptureSnapshot()
        {
            return new Dictionary<string, object?>
            {
                ["FolderPath"] = FolderPath
            };
        }

        public void ApplySnapshot(Dictionary<string, object?> snapshot)
        {
            if (snapshot.TryGetValue("FolderPath", out object? value))
            {
                FolderPath = value?.ToString() ?? string.Empty;
            }
        }

        private void SelectFolder()
        {
            using var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "\u9009\u62E9\u7BA1\u9053\u8F93\u5165\u6587\u4EF6\u5939"
            };

            if (TryNormalizeExistingFolder(FolderPath, out string initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                FolderPath = dialog.FileName;
            }
        }

        private static string NormalizeExistingFolder(string? folderPath)
        {
            if (!TryNormalizeExistingFolder(folderPath, out string path))
            {
                throw new InvalidOperationException("\u8BF7\u5148\u9009\u62E9\u6709\u6548\u7684\u672C\u5730\u6587\u4EF6\u5939\u3002");
            }

            return path;
        }

        private static bool TryNormalizeExistingFolder(string? folderPath, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            try
            {
                string normalizedPath = Path.GetFullPath(folderPath.Trim());
                if (!Directory.Exists(normalizedPath))
                {
                    return false;
                }

                path = normalizedPath;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public sealed class OutputFolderPickerPipelineNode : PipelineNodeViewModelBase, IPipelineOutputPathSelectorNode, IPipelineSnapshotProvider
    {
        private string _outputFolderPath = string.Empty;

        public OutputFolderPickerPipelineNode()
        {
            SelectOutputFolderCommand = new RelayCommand(SelectOutputFolder);
        }

        public override string DisplayName => "\u9009\u62E9\u8F93\u51FA\u6587\u4EF6\u5939";

        public string OutputFolderPath
        {
            get => _outputFolderPath;
            set => SetField(ref _outputFolderPath, value ?? string.Empty);
        }

        public ICommand SelectOutputFolderCommand { get; }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!TryGetOutputFolderPath(out string outputFolderPath))
            {
                throw new InvalidOperationException("\u8BF7\u5148\u9009\u62E9\u6709\u6548\u7684\u8F93\u51FA\u6587\u4EF6\u5939\u3002");
            }

            context.AppendLog($"[{DisplayName}] \u4E0A\u4E00\u4E2A\u8282\u70B9\u7684\u8F93\u51FA\u5DF2\u8BBE\u4E3A: {outputFolderPath}");
            return Task.CompletedTask;
        }

        public override PipelineNodeViewModelBase Clone()
        {
            return new OutputFolderPickerPipelineNode
            {
                OutputFolderPath = OutputFolderPath
            };
        }

        public bool TryGetOutputFolderPath(out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(OutputFolderPath))
            {
                return false;
            }

            try
            {
                path = Path.GetFullPath(OutputFolderPath.Trim());
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public Dictionary<string, object?> CaptureSnapshot()
        {
            return new Dictionary<string, object?>
            {
                ["OutputFolderPath"] = OutputFolderPath
            };
        }

        public void ApplySnapshot(Dictionary<string, object?> snapshot)
        {
            if (snapshot.TryGetValue("OutputFolderPath", out object? value))
            {
                OutputFolderPath = value?.ToString() ?? string.Empty;
            }
        }

        private void SelectOutputFolder()
        {
            using var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "\u9009\u62E9\u7BA1\u9053\u8F93\u51FA\u6587\u4EF6\u5939"
            };

            if (TryGetOutputFolderPath(out string initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputFolderPath = dialog.FileName;
            }
        }
    }

    public sealed class TrainingPipelineNode : PipelineNodeViewModelBase,
        IPipelineDirectoryNode,
        IPipelineInputPortProvider,
        IPipelineOutputPortProvider,
        IPipelineSnapshotProvider
    {
        private string _configurationFilePath = string.Empty;

        public TrainingPipelineNode()
        {
            SelectConfigurationFileCommand = new RelayCommand(SelectConfigurationFile);
        }

        public override string DisplayName => "训练";

        public string ConfigurationFilePath
        {
            get => _configurationFilePath;
            set => SetField(ref _configurationFilePath, value ?? string.Empty);
        }

        public ICommand SelectConfigurationFileCommand { get; }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;

        public string PipelineInputPath { get; set; } = string.Empty;

        public string PipelineOutputPath { get; set; } = string.Empty;

        public bool IsWorkflowMode { get; set; }

        public bool HasInheritedInputPath { get; set; }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            string configurationFilePath = GetExistingConfigurationFilePath();
            string inputPath = GetExistingDirectoryPath(PipelineInputPath, "训练集输入路径");
            string outputPath = GetOutputDirectoryPath(PipelineOutputPath);
            Directory.CreateDirectory(outputPath);

            context.AppendLog($"[{DisplayName}] 已选择配置文件: {configurationFilePath}");
            context.AppendLog($"[{DisplayName}] 训练集输入路径: {inputPath}");
            context.AppendLog($"[{DisplayName}] 训练集输出路径: {outputPath}");
            return Task.CompletedTask;
        }

        public override PipelineNodeViewModelBase Clone()
        {
            return new TrainingPipelineNode
            {
                ConfigurationFilePath = ConfigurationFilePath
            };
        }

        public IReadOnlyList<PipelineOutputPort> GetPipelineOutputPorts()
        {
            return [new PipelineOutputPort("root", "训练集输出路径")];
        }

        public string GetPipelineInputPortTitle() => "训练集输入路径";

        public Dictionary<string, object?> CaptureSnapshot()
        {
            return new Dictionary<string, object?>
            {
                [nameof(ConfigurationFilePath)] = ConfigurationFilePath
            };
        }

        public void ApplySnapshot(Dictionary<string, object?> snapshot)
        {
            if (snapshot != null && snapshot.TryGetValue(nameof(ConfigurationFilePath), out object? value))
            {
                ConfigurationFilePath = value?.ToString() ?? string.Empty;
            }
        }

        private void SelectConfigurationFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.Translate("选择训练配置文件"),
                Filter = LocalizationManager.Translate("训练配置 (*.json;*.toml;*.txt;*.args;*.cfg)|*.json;*.toml;*.txt;*.args;*.cfg|所有文件 (*.*)|*.*"),
                CheckFileExists = true,
                FileName = ConfigurationFilePath
            };

            if (dialog.ShowDialog() == true)
            {
                ConfigurationFilePath = dialog.FileName;
            }
        }

        private string GetExistingConfigurationFilePath()
        {
            if (string.IsNullOrWhiteSpace(ConfigurationFilePath))
            {
                throw new InvalidOperationException("请先选择配置文件。");
            }

            string path;
            try
            {
                path = Path.GetFullPath(ConfigurationFilePath.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                throw new InvalidOperationException("配置文件路径无效。", exception);
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("未找到配置文件。", path);
            }

            return path;
        }

        private static string GetExistingDirectoryPath(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException($"请先设置有效的{label}。");
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                throw new InvalidOperationException($"{label}无效。", exception);
            }

            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"未找到{label}：{fullPath}");
            }

            return fullPath;
        }

        private static string GetOutputDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("未设置训练集输出路径。");
            }

            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                throw new InvalidOperationException("训练集输出路径无效。", exception);
            }
        }
    }

    public sealed class RealEsrganPipelineNode : CyanTools.Pipeline.SimplePipelineNode<RealEsrganViewModel>
    {
        public RealEsrganPipelineNode() : base("图片超分") { }

        public override System.Collections.Generic.Dictionary<string, object?> CaptureSnapshot()
        {
            var snapshot = base.CaptureSnapshot();
            if (Inner.SelectedGpu != null)
            {
                snapshot["SelectedGpuId"] = Inner.SelectedGpu.Id;
            }
            return snapshot;
        }

        public override void ApplySnapshot(System.Collections.Generic.Dictionary<string, object?> snapshot)
        {
            base.ApplySnapshot(snapshot);
            if (snapshot != null && snapshot.TryGetValue("SelectedGpuId", out var idObj) && idObj != null)
            {
                if (int.TryParse(idObj.ToString(), out var gpuId))
                {
                    var target = Inner.Gpus.FirstOrDefault(g => g.Id == gpuId);
                    if (target != null)
                    {
                        Inner.SelectedGpu = target;
                    }
                }
            }
        }
    }

    public sealed class ImageBucketingPipelineNode : CyanTools.Pipeline.SimplePipelineNode<ImageBucketingViewModel>
    {
        public ImageBucketingPipelineNode() : base("图片分辨率归类") { }
    }

    public sealed class ImageScalingPipelineNode : CyanTools.Pipeline.SimplePipelineNode<ImageScalingViewModel>
    {
        public ImageScalingPipelineNode() : base("图片上下采") { }
    }

    public sealed class ThreeStagePipelineNode : CyanTools.Pipeline.SimplePipelineNode<ThreeStageViewModel>
    {
        public ThreeStagePipelineNode() : base("图片三分法") { }
    }

    public sealed class ResolutionFilterPipelineNode : CyanTools.Pipeline.SimplePipelineNode<ResolutionFilterViewModel>
    {
        public ResolutionFilterPipelineNode() : base("分辨率筛选") { }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            return ResolutionFilterPipelineProcessor.ExecuteAsync(
                Inner.InputPath ?? string.Empty,
                new ResolutionFilterPipelineOptions
                {
                    IsHeightFilterEnabled = Inner.IsHeightFilterEnabled,
                    MinHeight = Inner.MinHeight,
                    IsWidthFilterEnabled = Inner.IsWidthFilterEnabled,
                    MinWidth = Inner.MinWidth,
                    IsResolutionFilterEnabled = Inner.IsResolutionFilterEnabled,
                    MinResolutionWidth = Inner.MinResolutionWidth,
                    MinResolutionHeight = Inner.MinResolutionHeight
                },
                message => context.AppendLog($"[{DisplayName}] {message}"),
                token);
        }
    }

    public sealed class KeepFilesPipelineNode : CyanTools.Pipeline.SimplePipelineNode<KeepFilesViewModel>
    {
        public KeepFilesPipelineNode() : base("保留指定文件") { }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            return KeepFilesPipelineProcessor.ExecuteAsync(
                Inner.FolderPath ?? string.Empty,
                new KeepFilesOptions
                {
                    KeepJpg = Inner.KeepJpg,
                    KeepPng = Inner.KeepPng,
                    KeepWebp = Inner.KeepWebp,
                    KeepBmp = Inner.KeepBmp,
                    KeepJpeg = Inner.KeepJpeg,
                    KeepNpz = Inner.KeepNpz,
                    KeepTxt = Inner.KeepTxt,
                    KeepChineseTxt = Inner.KeepChineseTxt
                },
                message => context.AppendLog($"[{DisplayName}] {message}"),
                token);
        }
    }

    public sealed class FolderProcessingPipelineNode : CyanTools.Pipeline.SimplePipelineNode<FolderProcessingViewModel>
    {
        public FolderProcessingPipelineNode() : base("文件夹处理") { }
    }

    public sealed class ExportVideoFramePipelineNode : CyanTools.Pipeline.SimplePipelineNode<ExportVideoFrameViewModel>, IPipelineDirectoryNode
    {
        public ExportVideoFramePipelineNode() : base("导出视频帧") { }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;

        public string PipelineInputPath { get; set; } = string.Empty;

        public string PipelineOutputPath { get; set; } = string.Empty;

        public bool IsWorkflowMode { get; set; }

        public bool HasInheritedInputPath { get; set; }

        public override IPipelineDirectoryNode? GetDirectoryAutomationNode() => this;

        public override void SetWorkflowEditorMode(bool isWorkflowMode)
        {
            IsWorkflowMode = isWorkflowMode;
        }

        public override async Task RunAsync(PipelineContext context, CancellationToken token)
        {
            var videoPaths = VideoFrameWorkflowPlanner.ResolveInputVideos(Inner.VideoPath, PipelineInputPath);
            string outputFolder = !string.IsNullOrWhiteSpace(PipelineOutputPath)
                ? PipelineOutputPath
                : Inner.OutputFolder ?? string.Empty;

            Inner.OutputFolder = outputFolder;

            await VideoFrameWorkflowProcessor.ExportAllFramesAsync(
                videoPaths,
                outputFolder,
                ParseFrameIntervalMilliseconds(Inner.FrameIntervalText),
                message => context.AppendLog($"[{DisplayName}] {message}"),
                token).ConfigureAwait(false);
        }

        private static int ParseFrameIntervalMilliseconds(string? text)
        {
            return int.TryParse(text, out int intervalMs) && intervalMs > 0
                ? intervalMs
                : 1;
        }
    }

    public sealed class GaussianBlurPipelineNode : CyanTools.Pipeline.SimplePipelineNode<GaussianBlurViewModel>
    {
        public GaussianBlurPipelineNode() : base("高斯模糊") { }
    }

    public sealed class ConvertImageFormatPipelineNode : CyanTools.Pipeline.SimplePipelineNode<ConvertImageFormatViewModel>
    {
        public ConvertImageFormatPipelineNode() : base("格式转换") { }
    }

    public sealed class ConvertTransparentChannelPipelineNode : CyanTools.Pipeline.SimplePipelineNode<ConvertTransparentChannelViewModel>
    {
        public ConvertTransparentChannelPipelineNode() : base("转换透明通道") { }
    }

    public sealed class TaggingPipelineNode : CyanTools.Pipeline.SimplePipelineNode<TaggingViewModel>
    {
        public TaggingPipelineNode() : base("图片打标") { }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            return TaggerSeriesExecutor.ExecuteAsync(
                Inner.BuildSeriesExecutionOptions(),
                message => context.AppendLog($"[{DisplayName}] {message}"),
                _ => { },
                token);
        }
    }

    public sealed class TrainingSetBalancerPipelineNode : CyanTools.Pipeline.SimplePipelineNode<TrainingSet.TrainingSetBalancerViewModel>
    {
        public TrainingSetBalancerPipelineNode() : base("训练集配平") { }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            return TrainingSetBalancerPipelineProcessor.ExecuteAsync(
                Inner.SelectedFolderPath ?? string.Empty,
                message => context.AppendLog($"[{DisplayName}] {message}"),
                token);
        }
    }

    public sealed class TrainingOutputOrganizerPipelineNode : CyanTools.Pipeline.SimplePipelineNode<TrainingOutputOrganizerViewModel>
    {
        public TrainingOutputOrganizerPipelineNode() : base("训练输出整理") { }
    }

    public sealed class ImageRenamePipelineNode : CyanTools.Pipeline.SimplePipelineNode<ImageRenameViewModel>
    {
        public ImageRenamePipelineNode() : base("图片重命名") { }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            return ImageRenamePipelineProcessor.ExecuteAsync(
                Inner.InputPath ?? string.Empty,
                new ImageRenamePipelineOptions
                {
                    NamePrefix = Inner.NamePrefix,
                    StartIndex = Inner.StartIndex,
                    NumberPadding = Inner.NumberPadding
                },
                message => context.AppendLog($"[{DisplayName}] {message}"),
                token);
        }
    }

    public sealed class DatasetDownloaderPipelineNode : CyanTools.Pipeline.SimplePipelineNode<DatasetDownloaderViewModel>
    {
        public DatasetDownloaderPipelineNode() : base("数据集下载器") { }
    }
}
