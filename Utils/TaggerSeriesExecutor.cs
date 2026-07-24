using System;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils;






public sealed class TaggerSeriesExecutionOptions
{
    public TaggerBackendKind BackendKind { get; init; }
    public TaggingPipelineExecutionOptions? OnnxOptions { get; init; }
    public ToriiGateTaggingOptions? ToriiGateOptions { get; init; }
}

public static class TaggerSeriesExecutor
{
    public static Task ExecuteAsync(
        TaggerSeriesExecutionOptions options,
        Action<string>? log,
        Action<double>? progressSink,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.BackendKind switch
        {
            TaggerBackendKind.OnnxMultiLabel => ExecuteOnnxAsync(options, log, progressSink, token),
            TaggerBackendKind.OpenAiCompatibleVisionCaption => ExecuteToriiGateAsync(options, log, progressSink, token),
            _ => throw new NotSupportedException($"不支持的打标器后端：{options.BackendKind}。")
        };
    }

    private static Task ExecuteOnnxAsync(
        TaggerSeriesExecutionOptions options,
        Action<string>? log,
        Action<double>? progressSink,
        CancellationToken token)
    {
        if (options.OnnxOptions == null)
        {
            throw new InvalidOperationException("ONNX 打标器缺少执行参数。");
        }

        return TaggingPipelineExecutor.ExecuteAsync(
            new TaggingPipelineProcessorAdapter(new TaggingProcessor()),
            options.OnnxOptions,
            log,
            progressSink,
            token);
    }

    private static Task ExecuteToriiGateAsync(
        TaggerSeriesExecutionOptions options,
        Action<string>? log,
        Action<double>? progressSink,
        CancellationToken token)
    {
        if (options.ToriiGateOptions == null)
        {
            throw new InvalidOperationException("ToriiGate 打标器缺少执行参数。");
        }

        var progress = new ForwardingProgress(log, progressSink);
        return ToriiGateTaggingProcessor.ProcessDirectoryAsync(options.ToriiGateOptions, progress, token);
    }

    private sealed class ForwardingProgress : IProgress<string>
    {
        private readonly Action<string>? _log;
        private readonly Action<double>? _progressSink;

        public ForwardingProgress(Action<string>? log, Action<double>? progressSink)
        {
            _log = log;
            _progressSink = progressSink;
        }

        public void Report(string value)
        {
            _log?.Invoke(value);
            if (TryParseProgress(value, out double progress))
            {
                _progressSink?.Invoke(progress);
            }
        }

        private static bool TryParseProgress(string? message, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(message) ||
                !message.StartsWith("Processed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] parts = message.Split('[', '/', ']');
            return parts.Length > 2 &&
                   double.TryParse(parts[1], out double current) &&
                   double.TryParse(parts[2], out double total) &&
                   total > 0 &&
                   (value = current / total * 100) >= 0;
        }
    }
}
