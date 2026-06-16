using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
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

        public override string DisplayName => "\u9009\u62E9\u6587\u4EF6\u5939";

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
                Title = "\u9009\u62E9\u7BA1\u9053\u8D77\u59CB\u6587\u4EF6\u5939"
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
            if (Inner.SelectedTaggerModel == null)
            {
                throw new InvalidOperationException("未选择打标模型。");
            }

            return TaggingPipelineExecutor.ExecuteAsync(
                new TaggingPipelineProcessorAdapter(new TaggingProcessor()),
                new TaggingPipelineExecutionOptions
                {
                    InputDirectory = Inner.InputDirectory ?? string.Empty,
                    ModelUrl = Inner.SelectedTaggerModel.ModelUrl,
                    TagsUrl = Inner.SelectedTaggerModel.TagsUrl,
                    DisplayName = Inner.SelectedTaggerModel.DisplayName,
                    PreferGpu = Inner.PreferGpu,
                    GeneralThreshold = Inner.GeneralThreshold,
                    CharacterThreshold = Inner.CharacterThreshold,
                    InputSize = Inner.SelectedTaggerModel.InputSize,
                    InputFormat = Inner.SelectedTaggerModel.InputFormat,
                    CategoryOptions = new TagCategoryOptions
                    {
                        IncludeGeneral = Inner.IncludeGeneral,
                        IncludeArtist = Inner.IncludeArtist,
                        IncludeCopyright = Inner.IncludeCopyright,
                        IncludeCharacter = Inner.IncludeCharacter,
                        IncludeMeta = Inner.IncludeMeta,
                        IncludeQuality = Inner.IncludeQuality,
                        IncludeRating = Inner.IncludeRating,
                        IncludeModel = Inner.IncludeModel
                    },
                    PriorityTags = TaggingTextParser.ParseTagList(Inner.PriorityTags),
                    CustomTags = TaggingTextParser.ParseTagList(Inner.CustomTags),
                    ExistingTxtHandlingMode = Inner.SelectedExistingTxtConflictOption?.Mode ?? ExistingTxtHandlingMode.Overwrite
                },
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
