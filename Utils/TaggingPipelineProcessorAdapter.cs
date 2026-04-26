using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public sealed class TaggingPipelineProcessorAdapter : ITaggingPipelineProcessor
    {
        private readonly TaggingProcessor _inner;

        public TaggingPipelineProcessorAdapter(TaggingProcessor inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public async Task InitializeAsync(string modelUrl, string tagsUrl, string displayName, bool preferGpu, IProgress<string> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await _inner.InitializeAsync(modelUrl, tagsUrl, displayName, preferGpu, progress).ConfigureAwait(false);
        }

        public async Task ProcessDirectoryAsync(
            string inputDirectory,
            float generalThreshold,
            float characterThreshold,
            int inputSize,
            string inputFormat,
            TagCategoryOptions categoryOptions,
            IReadOnlyList<string> priorityTags,
            IReadOnlyList<string> customTags,
            ExistingTxtHandlingMode existingTxtHandlingMode,
            IProgress<string> progress,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await _inner.ProcessDirectoryAsync(
                inputDirectory,
                generalThreshold,
                characterThreshold,
                inputSize,
                inputFormat,
                categoryOptions,
                priorityTags,
                customTags,
                existingTxtHandlingMode,
                progress).ConfigureAwait(false);
        }
    }
}
