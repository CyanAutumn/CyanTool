using System.Collections.Generic;
using System.Linq;
using CyanTools.ViewModel;

namespace CyanTools.Pipeline
{
    public static class PipelineCatalog
    {
        private static readonly List<PipelineNodeDescriptor> _descriptors = new();
        private static readonly object Gate = new();
        private static bool _initialized;

        public static IReadOnlyList<PipelineNodeDescriptor> GetAll()
        {
            lock (Gate)
            {
                EnsureInitialized();
                return _descriptors.ToList();
            }
        }

        public static void Register(PipelineNodeDescriptor descriptor)
        {
            lock (Gate)
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
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            Register(new PipelineNodeDescriptor("\u9009\u62E9\u8F93\u5165\u6587\u4EF6\u5939", typeof(FolderPickerPipelineNode), () => new FolderPickerPipelineNode()));
            Register(new PipelineNodeDescriptor("\u9009\u62E9\u8F93\u51FA\u6587\u4EF6\u5939", typeof(OutputFolderPickerPipelineNode), () => new OutputFolderPickerPipelineNode()));
            Register(new PipelineNodeDescriptor("\u56FE\u7247\u805A\u7C7B", typeof(ImageClusteringViewModel), () => new ImageClusteringViewModel()));
            Register(new PipelineNodeDescriptor("\u56FE\u7247\u8D85\u5206", typeof(RealEsrganPipelineNode), () => new RealEsrganPipelineNode()));
            Register(new PipelineNodeDescriptor("\u56FE\u7247\u4E0A\u4E0B\u91C7", typeof(ImageScalingPipelineNode), () => new ImageScalingPipelineNode()));
            Register(new PipelineNodeDescriptor("\u56FE\u7247\u5206\u8FA8\u7387\u5F52\u7C7B", typeof(ImageBucketingPipelineNode), () => new ImageBucketingPipelineNode()));
            Register(new PipelineNodeDescriptor("\u56FE\u7247\u4E09\u5206\u6CD5", typeof(ThreeStagePipelineNode), () => new ThreeStagePipelineNode()));
            Register(new PipelineNodeDescriptor("\u5206\u8FA8\u7387\u7B5B\u9009", typeof(ResolutionFilterPipelineNode), () => new ResolutionFilterPipelineNode()));
            Register(new PipelineNodeDescriptor("\u4FDD\u7559\u6307\u5B9A\u6587\u4EF6", typeof(KeepFilesPipelineNode), () => new KeepFilesPipelineNode()));
            Register(new PipelineNodeDescriptor("\u6587\u4EF6\u5939\u5904\u7406", typeof(FolderProcessingPipelineNode), () => new FolderProcessingPipelineNode()));
            Register(new PipelineNodeDescriptor("\u5BFC\u51FA\u89C6\u9891\u5E27", typeof(ExportVideoFramePipelineNode), () => new ExportVideoFramePipelineNode()));
            Register(new PipelineNodeDescriptor("\u9AD8\u65AF\u6A21\u7CCA", typeof(GaussianBlurPipelineNode), () => new GaussianBlurPipelineNode()));
            Register(new PipelineNodeDescriptor("\u683C\u5F0F\u8F6C\u6362", typeof(ConvertImageFormatPipelineNode), () => new ConvertImageFormatPipelineNode()));
            Register(new PipelineNodeDescriptor("\u8F6C\u6362\u900F\u660E\u901A\u9053", typeof(ConvertTransparentChannelPipelineNode), () => new ConvertTransparentChannelPipelineNode()));
            Register(new PipelineNodeDescriptor("\u540C\u6E90\u56FE\u7247\u5F52\u7C7B", typeof(SameSourceImageGroupingViewModel), () => new SameSourceImageGroupingViewModel()));
            Register(new PipelineNodeDescriptor("\u56FE\u7247\u6253\u6807", typeof(TaggingPipelineNode), () => new TaggingPipelineNode()));
            Register(new PipelineNodeDescriptor("\u8BAD\u7EC3\u96C6\u914D\u5E73", typeof(TrainingSetBalancerPipelineNode), () => new TrainingSetBalancerPipelineNode()));
            Register(new PipelineNodeDescriptor("\u8BAD\u7EC3", typeof(TrainingPipelineNode), () => new TrainingPipelineNode()));
            Register(new PipelineNodeDescriptor("\u56FE\u7247\u91CD\u547D\u540D", typeof(ImageRenamePipelineNode), () => new ImageRenamePipelineNode()));
            Register(new PipelineNodeDescriptor("\u6570\u636E\u96C6\u4E0B\u8F7D\u5668", typeof(DatasetDownloaderPipelineNode), () => new DatasetDownloaderPipelineNode()));
        }
    }
}
