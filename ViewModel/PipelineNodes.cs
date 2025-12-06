namespace CyanTools.ViewModel
{
    public sealed class RealEsrganPipelineNode : CyanTools.Pipeline.SimplePipelineNode<RealEsrganViewModel>
    {
        public RealEsrganPipelineNode() : base("图片超分") { }
    }

    public sealed class ImageBucketingPipelineNode : CyanTools.Pipeline.SimplePipelineNode<ImageBucketingViewModel>
    {
        public ImageBucketingPipelineNode() : base("图片分辨率归类") { }
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
        public ConvertImageFormatPipelineNode() : base("转换图片格式") { }
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

    public sealed class DanbooruCrawlerPipelineNode : CyanTools.Pipeline.SimplePipelineNode<DanbooruCrawlerViewModel>
    {
        public DanbooruCrawlerPipelineNode() : base("Danbooru下载器") { }
    }

    public sealed class KemonoDownloaderPipelineNode : CyanTools.Pipeline.SimplePipelineNode<KemonoDownloaderViewModel>
    {
        public KemonoDownloaderPipelineNode() : base("Kemono下载器") { }
    }
}
