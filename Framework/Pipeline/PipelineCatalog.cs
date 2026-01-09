using System.Collections.Generic;
using System.Linq;
using CyanTools.ViewModel;

namespace CyanTools.Pipeline
{
    public static class PipelineCatalog
    {
        private static readonly List<PipelineNodeDescriptor> _descriptors = new();
        private static bool _initialized;

        public static IReadOnlyList<PipelineNodeDescriptor> GetAll()
        {
            EnsureInitialized();
            return _descriptors.ToList();
        }

        public static void Register(PipelineNodeDescriptor descriptor)
        {
            var existingIndex = _descriptors.FindIndex(d => d.NodeType == descriptor.NodeType);
            if (existingIndex >= 0)
            {
                _descriptors[existingIndex] = descriptor;
            }
            else
            {
                _descriptors.Add(descriptor);
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            Register(new PipelineNodeDescriptor("图片聚类", typeof(ImageClusteringViewModel), () => new ImageClusteringViewModel()));
            Register(new PipelineNodeDescriptor("图片超分", typeof(RealEsrganPipelineNode), () => new RealEsrganPipelineNode()));
            Register(new PipelineNodeDescriptor("图片分辨率归类", typeof(ImageBucketingPipelineNode), () => new ImageBucketingPipelineNode()));
            Register(new PipelineNodeDescriptor("图片三分法", typeof(ThreeStagePipelineNode), () => new ThreeStagePipelineNode()));
            Register(new PipelineNodeDescriptor("分辨率筛选", typeof(ResolutionFilterPipelineNode), () => new ResolutionFilterPipelineNode()));
            Register(new PipelineNodeDescriptor("保留指定文件", typeof(KeepFilesPipelineNode), () => new KeepFilesPipelineNode()));
            Register(new PipelineNodeDescriptor("导出视频帧", typeof(ExportVideoFramePipelineNode), () => new ExportVideoFramePipelineNode()));
            Register(new PipelineNodeDescriptor("高斯模糊", typeof(GaussianBlurPipelineNode), () => new GaussianBlurPipelineNode()));
            Register(new PipelineNodeDescriptor("转换图片格式", typeof(ConvertImageFormatPipelineNode), () => new ConvertImageFormatPipelineNode()));
            Register(new PipelineNodeDescriptor("图片打标", typeof(TaggingPipelineNode), () => new TaggingPipelineNode()));
            Register(new PipelineNodeDescriptor("训练集配平", typeof(TrainingSetBalancerPipelineNode), () => new TrainingSetBalancerPipelineNode()));
            Register(new PipelineNodeDescriptor("图片重命名", typeof(ImageRenamePipelineNode), () => new ImageRenamePipelineNode()));
            Register(new PipelineNodeDescriptor("Booru下载器", typeof(DanbooruCrawlerPipelineNode), () => new DanbooruCrawlerPipelineNode(), requiresUnlock: true));
            Register(new PipelineNodeDescriptor("Kemono下载器", typeof(KemonoDownloaderPipelineNode), () => new KemonoDownloaderPipelineNode(), requiresUnlock: true));
            Register(new PipelineNodeDescriptor("Pixiv下载器", typeof(PixivDownloaderPipelineNode), () => new PixivDownloaderPipelineNode(), requiresUnlock: true));
        }
    }
}
