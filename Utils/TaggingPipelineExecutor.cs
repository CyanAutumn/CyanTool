using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public interface ITaggingPipelineProcessor
    {
        Task InitializeAsync(string modelUrl, string tagsUrl, string displayName, bool preferGpu, IProgress<string> progress, CancellationToken token);

        Task ProcessDirectoryAsync(
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
            CancellationToken token);
    }

    public sealed class TaggingPipelineExecutionOptions
    {
        public string InputDirectory { get; init; } = string.Empty;
        public string ModelUrl { get; init; } = string.Empty;
        public string TagsUrl { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool PreferGpu { get; init; } = true;
        public float GeneralThreshold { get; init; } = 0.35f;
        public float CharacterThreshold { get; init; } = 0.85f;
        public int InputSize { get; init; } = 448;
        public string InputFormat { get; init; } = "NHWC";
        public TagCategoryOptions CategoryOptions { get; init; } = new();
        public IReadOnlyList<string> PriorityTags { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> CustomTags { get; init; } = Array.Empty<string>();
        public ExistingTxtHandlingMode ExistingTxtHandlingMode { get; init; } = ExistingTxtHandlingMode.Overwrite;
    }

    public static class TaggingPipelineExecutor
    {
        public static async Task ExecuteAsync(
            ITaggingPipelineProcessor processor,
            TaggingPipelineExecutionOptions options,
            Action<string>? log,
            Action<double>? progressSink,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(processor);
            ArgumentNullException.ThrowIfNull(options);

            if (string.IsNullOrWhiteSpace(options.InputDirectory))
            {
                throw new InvalidOperationException("输入目录不能为空。");
            }

            IProgress<string> progress = new ForwardingProgress(message =>
            {
                log?.Invoke(message);
                if (TryParseProgress(message, out double value))
                {
                    progressSink?.Invoke(value);
                }
            });

            token.ThrowIfCancellationRequested();
            await processor.InitializeAsync(
                options.ModelUrl,
                options.TagsUrl,
                options.DisplayName,
                options.PreferGpu,
                progress,
                token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();
            await processor.ProcessDirectoryAsync(
                options.InputDirectory,
                options.GeneralThreshold,
                options.CharacterThreshold,
                options.InputSize,
                options.InputFormat,
                options.CategoryOptions,
                options.PriorityTags,
                options.CustomTags,
                options.ExistingTxtHandlingMode,
                progress,
                token).ConfigureAwait(false);

            log?.Invoke("Tagging complete.");
            progressSink?.Invoke(100);
        }

        private static bool TryParseProgress(string? message, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(message) || !message.StartsWith("Processed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] parts = message.Split('[', '/', ']');
            if (parts.Length <= 2 ||
                !double.TryParse(parts[1], out double current) ||
                !double.TryParse(parts[2], out double total) ||
                total <= 0)
            {
                return false;
            }

            value = current / total * 100;
            return true;
        }

        private sealed class ForwardingProgress : IProgress<string>
        {
            private readonly Action<string> _callback;

            public ForwardingProgress(Action<string> callback)
            {
                _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            }

            public void Report(string value)
            {
                _callback(value);
            }
        }
    }
}
