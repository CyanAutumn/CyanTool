using CyanTools.Model;
using CyanTools.Utils;
using Xunit;

public sealed class LayeredLoraPresetStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsLayeredLoraPresetConfig()
    {
        using var workspace = TemporaryWorkspace.Create();
        var store = new LayeredLoraPresetStore(workspace.Path);
        var config = new LayeredLoraPresetConfig
        {
            Name = "A style to B layout",
            Architecture = LayeredLoraArchitecture.Anima,
            BaseAlpha = 0.25d,
            LayerWeights = Enumerable.Range(0, LayeredLoraWeights.ReportLayerCount)
                .Select(index => index / 100d)
                .ToList(),
            AnimaBlockWeights = new List<LayeredLoraBlockPresetWeight>
            {
                new LayeredLoraBlockPresetWeight { BlockIndex = 0, Value = 1d },
                new LayeredLoraBlockPresetWeight { BlockIndex = 14, Value = 0.2d }
            }
        };

        store.Save(config);

        Assert.Equal(new[] { "A style to B layout" }, store.LoadNames());
        var loaded = store.Load("A style to B layout");
        Assert.NotNull(loaded);
        Assert.Equal(LayeredLoraArchitecture.Anima, loaded.Architecture);
        Assert.Equal(0.25d, loaded.BaseAlpha, precision: 6);
        Assert.Equal(0.14d, loaded.LayerWeights[14], precision: 6);
        Assert.Equal(14, loaded.AnimaBlockWeights[1].BlockIndex);
        Assert.Equal(0.2d, loaded.AnimaBlockWeights[1].Value, precision: 6);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryWorkspace Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CyanTools.LayeredLoraPresetStoreTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryWorkspace(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
