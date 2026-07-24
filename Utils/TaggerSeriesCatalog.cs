using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils;







public enum TaggerBackendKind
{
    OnnxMultiLabel,
    OpenAiCompatibleVisionCaption
}

public sealed class TaggerSeriesDefinition
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TaggerBackendKind BackendKind { get; init; }
    public bool SupportsLocalOnnxModels { get; init; }
}

public sealed class ToriiGateCaptionFormatDefinition
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Instruction { get; init; } = string.Empty;
}

public static class TaggerSeriesCatalog
{
    public const string OnnxMultiLabelSeriesId = "onnx-multilabel";
    public const string ToriiGateSeriesId = "toriigate";

    public const string ToriiGateDefaultEndpoint = "http://127.0.0.1:9001/v1/chat/completions";
    public const string ToriiGateDefaultModelName = "toriigate-0.5";
    public const string ToriiGateSystemPrompt = "You are image captioning expert. Describe user's picture according to requested format and instructions.";

    public static IReadOnlyList<TaggerSeriesDefinition> BuiltInSeries { get; } =
    [
        new TaggerSeriesDefinition
        {
            Id = OnnxMultiLabelSeriesId,
            DisplayName = "ONNX 多标签分类",
            Description = "wd14、deepghs、cl_tagger 等本地 ONNX 概率标签模型。",
            BackendKind = TaggerBackendKind.OnnxMultiLabel,
            SupportsLocalOnnxModels = true
        },
        new TaggerSeriesDefinition
        {
            Id = ToriiGateSeriesId,
            DisplayName = "ToriiGate 图像描述",
            Description = "通过本机 OpenAI 兼容视觉接口生成图像 caption。",
            BackendKind = TaggerBackendKind.OpenAiCompatibleVisionCaption,
            SupportsLocalOnnxModels = false
        }
    ];




    public static IReadOnlyList<ToriiGateCaptionFormatDefinition> ToriiGateCaptionFormats { get; } =
    [
        new ToriiGateCaptionFormatDefinition
        {
            Id = "short",
            DisplayName = "简短描述",
            Instruction = "The caption for image should be quite short without long purple prose and slop. Cover main objects and details."
        },
        new ToriiGateCaptionFormatDefinition
        {
            Id = "long",
            DisplayName = "长篇自然语言描述",
            Instruction = "Make a caption for given image with natural text. Use 2 to 5 paragraphs. Make your description long and vivid, mentioning all the details."
        }
    ];

    public static TaggerSeriesDefinition GetSeries(string? id) =>
        BuiltInSeries.FirstOrDefault(series => string.Equals(series.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? BuiltInSeries[0];

    public static ToriiGateCaptionFormatDefinition GetToriiGateCaptionFormat(string? id) =>
        ToriiGateCaptionFormats.FirstOrDefault(format => string.Equals(format.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? ToriiGateCaptionFormats[0];
}
