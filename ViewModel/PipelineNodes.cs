using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CyanTools.Pipeline;
namespace CyanTools.ViewModel
{
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
    }

    public sealed class KeepFilesPipelineNode : CyanTools.Pipeline.SimplePipelineNode<KeepFilesViewModel>
    {
        public KeepFilesPipelineNode() : base("保留指定文件") { }
    }

    public sealed class ExportVideoFramePipelineNode : CyanTools.Pipeline.SimplePipelineNode<ExportVideoFrameViewModel>
    {
        public ExportVideoFramePipelineNode() : base("导出视频帧") { }
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
    }

    public sealed class TrainingSetBalancerPipelineNode : CyanTools.Pipeline.SimplePipelineNode<TrainingSet.TrainingSetBalancerViewModel>
    {
        public TrainingSetBalancerPipelineNode() : base("训练集配平") { }
    }

    public sealed class ImageRenamePipelineNode : CyanTools.Pipeline.SimplePipelineNode<ImageRenameViewModel>
    {
        public ImageRenamePipelineNode() : base("图片重命名") { }
    }

    public sealed class DatasetDownloaderPipelineNode : CyanTools.Pipeline.SimplePipelineNode<DatasetDownloaderViewModel>
    {
        public DatasetDownloaderPipelineNode() : base("数据集下载器") { }
    }
}
