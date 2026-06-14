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
            Register(new PipelineNodeDescriptor("图片上下采", typeof(ImageScalingPipelineNode), () => new ImageScalingPipelineNode()));
            Register(new PipelineNodeDescriptor("图片分辨率归类", typeof(ImageBucketingPipelineNode), () => new ImageBucketingPipelineNode()));
            Register(new PipelineNodeDescriptor("图片三分法", typeof(ThreeStagePipelineNode), () => new ThreeStagePipelineNode()));
            Register(new PipelineNodeDescriptor("分辨率筛选", typeof(ResolutionFilterPipelineNode), () => new ResolutionFilterPipelineNode()));
            Register(new PipelineNodeDescriptor("保留指定文件", typeof(KeepFilesPipelineNode), () => new KeepFilesPipelineNode()));
            Register(new PipelineNodeDescriptor("文件夹处理", typeof(FolderProcessingPipelineNode), () => new FolderProcessingPipelineNode()));
            Register(new PipelineNodeDescriptor("导出视频帧", typeof(ExportVideoFramePipelineNode), () => new ExportVideoFramePipelineNode()));
            Register(new PipelineNodeDescriptor("高斯模糊", typeof(GaussianBlurPipelineNode), () => new GaussianBlurPipelineNode()));
            Register(new PipelineNodeDescriptor("格式转换", typeof(ConvertImageFormatPipelineNode), () => new ConvertImageFormatPipelineNode()));
            Register(new PipelineNodeDescriptor("转换透明通道", typeof(ConvertTransparentChannelPipelineNode), () => new ConvertTransparentChannelPipelineNode()));
            Register(new PipelineNodeDescriptor("同源图片归类", typeof(SameSourceImageGroupingViewModel), () => new SameSourceImageGroupingViewModel()));
            Register(new PipelineNodeDescriptor("图片打标", typeof(TaggingPipelineNode), () => new TaggingPipelineNode()));
            Register(new PipelineNodeDescriptor("训练集配平", typeof(TrainingSetBalancerPipelineNode), () => new TrainingSetBalancerPipelineNode()));
            Register(new PipelineNodeDescriptor("训练输出整理", typeof(TrainingOutputOrganizerPipelineNode), () => new TrainingOutputOrganizerPipelineNode()));
            Register(new PipelineNodeDescriptor("图片重命名", typeof(ImageRenamePipelineNode), () => new ImageRenamePipelineNode()));
            Register(new PipelineNodeDescriptor("数据集下载器", typeof(DatasetDownloaderPipelineNode), () => new DatasetDownloaderPipelineNode(), requiresUnlock: true));
        }
    }
}
