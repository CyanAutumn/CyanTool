using System.Text.Json;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class TaggerSeriesTests
{
    [Fact]
    public void BuiltInSeries_RegistersOnnxAndToriiGateWithDifferentRuntimeContracts()
    {
        TaggerSeriesDefinition onnx = TaggerSeriesCatalog.GetSeries(TaggerSeriesCatalog.OnnxMultiLabelSeriesId);
        TaggerSeriesDefinition toriiGate = TaggerSeriesCatalog.GetSeries(TaggerSeriesCatalog.ToriiGateSeriesId);

        Assert.Equal(TaggerBackendKind.OnnxMultiLabel, onnx.BackendKind);
        Assert.True(onnx.SupportsLocalOnnxModels);
        Assert.Equal(TaggerBackendKind.OpenAiCompatibleVisionCaption, toriiGate.BackendKind);
        Assert.False(toriiGate.SupportsLocalOnnxModels);
    }

    [Fact]
    public void ToriiGateRequest_UsesTheCatalogPromptAndOpenAiVisionMessageShape()
    {
        var options = new ToriiGateTaggingOptions
        {
            ModelName = "local-toriigate",
            CaptionFormat = TaggerSeriesCatalog.GetToriiGateCaptionFormat("short"),
            MaxTokens = 768,
            Temperature = 0.25
        };

        string json = ToriiGateOpenAiRequestFactory.BuildJson(options, "data:image/jpeg;base64,abc");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("local-toriigate", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(768, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("image_url", root.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal(
            TaggerSeriesCatalog.GetToriiGateCaptionFormat("short").Instruction,
            root.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("text").GetString());
    }

    [Fact]
    public void ToriiGateCaptionOutput_PrependsUniqueCustomTagsAndKeepsCaptionsWhole()
    {
        string caption = ToriiGateCaptionOutput.PrependCustomTags(
            "A girl standing beside a window, looking outside.",
            new[] { "character focus", "Character Focus", "anime" });

        Assert.Equal("character focus, anime, A girl standing beside a window, looking outside.", caption);
        Assert.Equal(
            "First caption." + Environment.NewLine + "Second caption.",
            ToriiGateCaptionOutput.MergeUniqueCaption("First caption.", "Second caption."));
    }

    [Fact]
    public void PipelineSnapshot_DoesNotStoreApiTokens()
    {
        var snapshot = PipelineSnapshotHelper.CaptureSimpleProperties(new SnapshotTarget
        {
            Endpoint = "http://127.0.0.1:9001/v1/chat/completions",
            ApiToken = "must-not-be-saved"
        });

        Assert.Equal("http://127.0.0.1:9001/v1/chat/completions", snapshot["Endpoint"]);
        Assert.DoesNotContain("ApiToken", snapshot.Keys);
    }

    private sealed class SnapshotTarget
    {
        public string Endpoint { get; set; } = string.Empty;
        public string ApiToken { get; set; } = string.Empty;
    }
}
