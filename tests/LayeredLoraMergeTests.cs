using CyanTools.Model;
using CyanTools.Utils;
using Xunit;

public sealed class LayeredLoraMergeTests
{
    [Fact]
    public void ParseReportWeights_AcceptsExactlyTwentyFiveLayerValues()
    {
        var weights = LayeredLoraWeightParser.ParseReportWeights(
            "1,0.9,0.8,0.7,0.6,0.5,0.4,0.3,0.2,0.1,0,0.11,0.22,0.33,0.44,0.55,0.66,0.77,0.88,0.99,1,0.12,0.23,0.34,0.45",
            baseAlpha: 0.25d);

        Assert.Equal(0.25d, weights.BaseAlpha, precision: 6);
        Assert.Equal(1d, weights.GetLayerWeight(LoraLayerSlot.In00), precision: 6);
        Assert.Equal(0.11d, weights.GetLayerWeight(LoraLayerSlot.In11), precision: 6);
        Assert.Equal(0.22d, weights.GetLayerWeight(LoraLayerSlot.Mid00), precision: 6);
        Assert.Equal(0.33d, weights.GetLayerWeight(LoraLayerSlot.Out00), precision: 6);
        Assert.Equal(0.45d, weights.GetLayerWeight(LoraLayerSlot.Out11), precision: 6);
    }

    [Fact]
    public void ParseReportWeights_RejectsWrongValueCount()
    {
        var values = string.Join(",", Enumerable.Repeat("1", 24));

        Assert.Throws<FormatException>(() => LayeredLoraWeightParser.ParseReportWeights(values, baseAlpha: 0d));
    }

    [Fact]
    public void CreateReportLayerItems_RoundTripsSliderValuesInReportOrder()
    {
        var sourceValues = Enumerable.Range(0, LayeredLoraWeights.ReportLayerCount)
            .Select(index => index / 100d)
            .ToArray();
        var items = LayeredLoraWeights.CreateReportLayerItems(sourceValues).ToArray();

        Assert.Equal(LayeredLoraWeights.ReportLayerCount, items.Length);
        Assert.Equal(LoraLayerSlot.In00, items[0].Slot);
        Assert.Equal("IN00", items[0].DisplayName);
        Assert.Equal(LoraLayerSlot.Mid00, items[12].Slot);
        Assert.Equal("M00", items[12].DisplayName);
        Assert.Equal(LoraLayerSlot.Out11, items[24].Slot);
        Assert.Equal("OUT11", items[24].DisplayName);

        items[1].Value = 0.42d;
        var weights = LayeredLoraWeights.CreateFromLayerItems(baseAlpha: 0.25d, items);

        Assert.Equal(0.25d, weights.BaseAlpha, precision: 6);
        Assert.Equal(0d, weights.GetLayerWeight(LoraLayerSlot.In00), precision: 6);
        Assert.Equal(0.42d, weights.GetLayerWeight(LoraLayerSlot.In01), precision: 6);
        Assert.Equal(0.12d, weights.GetLayerWeight(LoraLayerSlot.Mid00), precision: 6);
        Assert.Equal(0.24d, weights.GetLayerWeight(LoraLayerSlot.Out11), precision: 6);
        Assert.Equal("0.42", items[1].ValueText);
    }

    [Fact]
    public void CreateAnimaBlockItems_RoundTripsBlockWeightsByBlockIndex()
    {
        var items = LayeredLoraWeights.CreateAnimaBlockItems(new[] { 14, 0, 2, 2 }, defaultValue: 0.5d).ToArray();

        Assert.Equal(new[] { 0, 2, 14 }, items.Select(item => item.BlockIndex).ToArray());
        Assert.Equal("Block 00", items[0].DisplayName);
        Assert.Equal("Block 14", items[2].DisplayName);

        items[2].Value = 0.75d;
        var weights = LayeredLoraWeights.CreateFromAnimaBlockItems(baseAlpha: 0.2d, items);

        Assert.Equal(0.2d, weights.BaseAlpha, precision: 6);
        Assert.Equal(0.5d, weights.GetAnimaBlockWeight(0), precision: 6);
        Assert.Equal(0.75d, weights.GetAnimaBlockWeight(14), precision: 6);
        Assert.Equal(0.2d, weights.GetAnimaBlockWeight(99), precision: 6);
    }

    [Fact]
    public void ReadTensorKeys_ReturnsTensorNamesWithoutLoadingMetadata()
    {
        using var workspace = TemporaryWorkspace.Create();
        var path = workspace.GetPath("keys.safetensors");
        var keyA = "diffusion_model.blocks.14.attn.to_q.lora_A.weight";
        var keyB = "diffusion_model.llm_adapter.in_proj.lora_A.weight";
        SafetensorsFile.Save(
            path,
            new[]
            {
                SafetensorsTensor.FromSingleArray(keyA, new long[] { 1 }, new[] { 1f }),
                SafetensorsTensor.FromSingleArray(keyB, new long[] { 1 }, new[] { 2f })
            },
            new Dictionary<string, string> { ["author"] = "cyan" });

        var keys = SafetensorsFile.ReadTensorKeys(path).OrderBy(key => key, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { keyA, keyB }, keys);
    }

    [Theory]
    [InlineData("lora_unet_input_blocks_4_1_transformer_blocks_0_attn1_to_k.lora_down.weight", LoraLayerSlot.In04)]
    [InlineData("lora_unet_middle_block_1_transformer_blocks_0_attn1_to_q.lora_up.weight", LoraLayerSlot.Mid00)]
    [InlineData("lora_unet_output_blocks_7_1_transformer_blocks_0_attn2_to_v.lora_up.weight", LoraLayerSlot.Out07)]
    [InlineData("lora_te1_text_model_encoder_layers_0_mlp_fc1.lora_down.weight", LoraLayerSlot.Base)]
    public void MapKey_MapsSdxlKohyaKeysToReportSlots(string key, LoraLayerSlot expectedSlot)
    {
        var slot = LoraLayerMapper.MapKey(key, LayeredLoraArchitecture.Sdxl);

        Assert.Equal(expectedSlot, slot);
    }

    [Theory]
    [InlineData("diffusion_model.blocks.0.attn.to_q.lora_A.weight", LoraLayerSlot.Unknown)]
    [InlineData("diffusion_model.blocks.14.attn.to_q.lora_A.weight", LoraLayerSlot.Unknown)]
    [InlineData("diffusion_model.blocks.27.attn.to_q.lora_A.weight", LoraLayerSlot.Unknown)]
    [InlineData("diffusion_model.llm_adapter.in_proj.lora_A.weight", LoraLayerSlot.Base)]
    public void MapKey_DoesNotMapAnimaBlocksToSdxlReportSlots(string key, LoraLayerSlot expectedSlot)
    {
        var slot = LoraLayerMapper.MapKey(key, LayeredLoraArchitecture.Anima);

        Assert.Equal(expectedSlot, slot);
    }

    [Theory]
    [InlineData("diffusion_model.blocks.0.attn.to_q.lora_A.weight", 28, LoraLayerSlot.In00)]
    [InlineData("diffusion_model.blocks.14.attn.to_q.lora_A.weight", 28, LoraLayerSlot.Mid00)]
    [InlineData("diffusion_model.blocks.27.attn.to_q.lora_A.weight", 28, LoraLayerSlot.Out11)]
    [InlineData("diffusion_model.llm_adapter.in_proj.lora_A.weight", 28, LoraLayerSlot.Base)]
    public void MapKey_MapsAnimaBlocksToSdxlCompatibleSlots(string key, int blockCount, LoraLayerSlot expectedSlot)
    {
        var slot = LoraLayerMapper.MapKey(key, LayeredLoraArchitecture.AnimaSdxlCompatible, blockCount);

        Assert.Equal(expectedSlot, slot);
    }

    [Fact]
    public void Merge_BlendsSdxlTensorUsingMappedLayerWeight()
    {
        using var workspace = TemporaryWorkspace.Create();
        var key = "lora_unet_input_blocks_4_1_transformer_blocks_0_attn1_to_k.lora_down.weight";
        var modelA = workspace.GetPath("sdxl-a.safetensors");
        var modelB = workspace.GetPath("sdxl-b.safetensors");
        var output = workspace.GetPath("sdxl-out.safetensors");
        SafetensorsFile.Save(
            modelA,
            new[] { SafetensorsTensor.FromSingleArray(key, new long[] { 2 }, new[] { 10f, 20f }) },
            new Dictionary<string, string> { ["ss_network_module"] = "networks.lora" });
        SafetensorsFile.Save(
            modelB,
            new[] { SafetensorsTensor.FromSingleArray(key, new long[] { 2 }, new[] { 30f, 60f }) },
            new Dictionary<string, string> { ["ss_base_model_version"] = "sdxl_base_v1-0" });
        var layerWeights = CreateWeights(LoraLayerSlot.In04, 0.25d, baseAlpha: 0d);

        var report = LayeredLoraMerger.Merge(new LayeredLoraMergeOptions(
            modelA,
            modelB,
            output,
            LayeredLoraArchitecture.Sdxl,
            layerWeights));

        Assert.Equal(1, report.MergedTensorCount);
        var merged = SafetensorsFile.Load(output);
        Assert.Equal(new[] { 15f, 30f }, merged.GetTensor(key).ToSingleArray());
        Assert.Equal("networks.lora", merged.Metadata["ss_network_module"]);
        Assert.Equal("sdxl_base_v1-0", merged.Metadata["ss_base_model_version"]);
    }

    [Fact]
    public void Merge_BlendsAnimaBlockTensorUsingBlockWeight()
    {
        using var workspace = TemporaryWorkspace.Create();
        var key = "diffusion_model.blocks.14.attn.to_q.lora_A.weight";
        var modelA = workspace.GetPath("anima-a.safetensors");
        var modelB = workspace.GetPath("anima-b.safetensors");
        var output = workspace.GetPath("anima-out.safetensors");
        SafetensorsFile.Save(
            modelA,
            new[] { SafetensorsTensor.FromSingleArray(key, new long[] { 2 }, new[] { 2f, 4f }) },
            new Dictionary<string, string>());
        SafetensorsFile.Save(
            modelB,
            new[] { SafetensorsTensor.FromSingleArray(key, new long[] { 2 }, new[] { 10f, 20f }) },
            new Dictionary<string, string>());
        var layerWeights = LayeredLoraWeights.CreateFromAnimaBlockItems(
            baseAlpha: 0d,
            LayeredLoraWeights.CreateAnimaBlockItems(new[] { 14 }, defaultValue: 0.5d));

        LayeredLoraMerger.Merge(new LayeredLoraMergeOptions(
            modelA,
            modelB,
            output,
            LayeredLoraArchitecture.Anima,
            layerWeights));

        var merged = SafetensorsFile.Load(output);
        Assert.Equal(new[] { 6f, 12f }, merged.GetTensor(key).ToSingleArray());
    }

    [Fact]
    public void Merge_BlendsAnimaTensorUsingSdxlCompatibleLayerWeight()
    {
        using var workspace = TemporaryWorkspace.Create();
        var key = "diffusion_model.blocks.14.attn.to_q.lora_A.weight";
        var modelA = workspace.GetPath("anima-compatible-a.safetensors");
        var modelB = workspace.GetPath("anima-compatible-b.safetensors");
        var output = workspace.GetPath("anima-compatible-out.safetensors");
        SafetensorsFile.Save(
            modelA,
            new[]
            {
                SafetensorsTensor.FromSingleArray("diffusion_model.blocks.0.attn.to_q.lora_A.weight", new long[] { 1 }, new[] { 1f }),
                SafetensorsTensor.FromSingleArray(key, new long[] { 2 }, new[] { 2f, 4f }),
                SafetensorsTensor.FromSingleArray("diffusion_model.blocks.27.attn.to_q.lora_A.weight", new long[] { 1 }, new[] { 1f })
            },
            new Dictionary<string, string>());
        SafetensorsFile.Save(
            modelB,
            new[]
            {
                SafetensorsTensor.FromSingleArray("diffusion_model.blocks.0.attn.to_q.lora_A.weight", new long[] { 1 }, new[] { 1f }),
                SafetensorsTensor.FromSingleArray(key, new long[] { 2 }, new[] { 10f, 20f }),
                SafetensorsTensor.FromSingleArray("diffusion_model.blocks.27.attn.to_q.lora_A.weight", new long[] { 1 }, new[] { 1f })
            },
            new Dictionary<string, string>());
        var layerWeights = CreateWeights(LoraLayerSlot.Mid00, 0.25d, baseAlpha: 0d);

        var report = LayeredLoraMerger.Merge(new LayeredLoraMergeOptions(
            modelA,
            modelB,
            output,
            LayeredLoraArchitecture.AnimaSdxlCompatible,
            layerWeights));

        var merged = SafetensorsFile.Load(output);
        Assert.Equal(new[] { 4f, 8f }, merged.GetTensor(key).ToSingleArray());
        Assert.Equal(1, report.LayerTensorCounts[LoraLayerSlot.Mid00]);
        Assert.Equal("0.25", merged.Metadata["cyan_merge_layers"].Split(',')[12]);
    }

    [Fact]
    public void Merge_BlendsAnimaLlmAdapterTensorUsingBaseAlpha()
    {
        using var workspace = TemporaryWorkspace.Create();
        var key = "diffusion_model.llm_adapter.in_proj.lora_A.weight";
        var modelA = workspace.GetPath("adapter-a.safetensors");
        var modelB = workspace.GetPath("adapter-b.safetensors");
        var output = workspace.GetPath("adapter-out.safetensors");
        SafetensorsFile.Save(
            modelA,
            new[] { SafetensorsTensor.FromSingleArray(key, new long[] { 2 }, new[] { 1f, 3f }) },
            new Dictionary<string, string>());
        SafetensorsFile.Save(
            modelB,
            new[] { SafetensorsTensor.FromSingleArray(key, new long[] { 2 }, new[] { 11f, 13f }) },
            new Dictionary<string, string>());
        var layerWeights = LayeredLoraWeights.CreateUniform(baseAlpha: 0.6d, layerWeight: 0d);

        LayeredLoraMerger.Merge(new LayeredLoraMergeOptions(
            modelA,
            modelB,
            output,
            LayeredLoraArchitecture.Anima,
            layerWeights));

        var merged = SafetensorsFile.Load(output);
        Assert.Equal(new[] { 7f, 9f }, merged.GetTensor(key).ToSingleArray());
    }

    private static LayeredLoraWeights CreateWeights(LoraLayerSlot targetSlot, double targetValue, double baseAlpha)
    {
        var values = Enumerable.Repeat(0d, LayeredLoraWeights.ReportLayerCount).ToArray();
        var index = LayeredLoraWeights.GetReportLayerIndex(targetSlot);
        Assert.True(index >= 0, $"Expected {targetSlot} to be a report layer slot.");
        values[index] = targetValue;
        return new LayeredLoraWeights(baseAlpha, values);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string _path;

        private TemporaryWorkspace(string path)
        {
            _path = path;
        }

        public static TemporaryWorkspace Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "CyanTools.LayeredLoraMergeTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryWorkspace(path);
        }

        public string GetPath(string fileName)
        {
            return Path.Combine(_path, fileName);
        }

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
    }
}
