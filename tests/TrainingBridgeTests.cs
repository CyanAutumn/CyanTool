using CyanTools.Model;
using CyanTools.Utils;
using Xunit;

public sealed class TrainingBridgeTests
{
    [Fact]
    public void Import_DiffusionPipeBundle_MapsAliasesAndRuntimePaths()
    {
        var files = new[]
        {
            new TrainingBridgeConfigFile("anima_train.toml", """
                output_dir = "/mnt/d/outputs/anime"
                epochs = 2
                micro_batch_size_per_gpu = 1

                [model]
                transformer_path = "/mnt/d/models/anima.safetensors"
                vae_path = "/mnt/d/models/vae.safetensors"
                llm_path = "/mnt/d/models/qwen3"
                max_sequence_length = 128

                [adapter]
                rank = 8

                [optimizer]
                type = "Prodigy"
                lr = 0.0002
                """),
            new TrainingBridgeConfigFile("anima_dataset.toml", """
                size_buckets = [
                    [768, 768, 1],
                ]

                [[directory]]
                path = "/mnt/d/datasets/anime"
                num_repeats = 3
                """)
        };

        var values = TrainingBridgeConfigService.Import(
            TrainingBridgeConfigService.DiffusionPipeBackendId,
            files);

        Assert.Equal("D:/outputs/anime", values["output_dir"]);
        Assert.Equal("D:/datasets/anime", values["dataset_path"]);
        Assert.Equal("D:/models/anima.safetensors", values["transformer_path"]);
        Assert.Equal(values["transformer_path"], values["pretrained_model_path"]);
        Assert.Equal("D:/models/qwen3", values["llm_path"]);
        Assert.Equal(values["llm_path"], values["qwen3_path"]);
        Assert.Equal("768", values["resolution"]);
        Assert.Equal(values["resolution"], values["dataset_resolution"]);
        Assert.Equal("3", values["num_repeats"]);
        Assert.Equal(values["num_repeats"], values["dataset_repeats"]);
        Assert.Equal("2", values["epochs"]);
        Assert.Equal(values["epochs"], values["max_train_epochs"]);
        Assert.Equal("8", values["rank"]);
        Assert.Equal(values["rank"], values["network_dim"]);
        Assert.Equal(0.0002d, double.Parse(values["learning_rate"], System.Globalization.CultureInfo.InvariantCulture), precision: 8);
    }

    [Fact]
    public void Export_DiffusionPipeBundle_WritesTwoTomlFilesWithWslPaths()
    {
        var values = new Dictionary<string, string>
        {
            ["dataset_path"] = "D:/datasets/anime",
            ["output_dir"] = "D:/outputs/anime",
            ["pretrained_model_path"] = "D:/models/anima.safetensors",
            ["qwen3_path"] = "D:/models/qwen3",
            ["vae_path"] = "D:/models/vae.safetensors",
            ["dataset_resolution"] = "768",
            ["dataset_repeats"] = "2",
            ["max_train_epochs"] = "1",
            ["train_batch_size"] = "1",
            ["network_dim"] = "8",
            ["learning_rate"] = "0.0002"
        };

        var files = TrainingBridgeConfigService.Export(
            TrainingBridgeConfigService.DiffusionPipeBackendId,
            values).ToDictionary(file => file.FileName, StringComparer.Ordinal);

        Assert.Equal(new[] { "anima_dataset.toml", "anima_train.toml" }, files.Keys.OrderBy(key => key).ToArray());
        Assert.Contains("output_dir = \"/mnt/d/outputs/anime\"", files["anima_train.toml"].Content);
        Assert.Contains("transformer_path = \"/mnt/d/models/anima.safetensors\"", files["anima_train.toml"].Content);
        Assert.Contains("llm_path = \"/mnt/d/models/qwen3\"", files["anima_train.toml"].Content);
        Assert.Contains("rank = 8", files["anima_train.toml"].Content);
        Assert.Contains("[768, 768, 1]", files["anima_dataset.toml"].Content);
        Assert.Contains("path = \"/mnt/d/datasets/anime\"", files["anima_dataset.toml"].Content);
        Assert.Contains("num_repeats = 2", files["anima_dataset.toml"].Content);
    }

    [Fact]
    public void ExportAndImport_WslSdScriptsSdxl_DoesNotAddAnimaArguments()
    {
        var values = new Dictionary<string, string>
        {
            ["model_family"] = "sdxl",
            ["dataset_path"] = "D:/datasets/anime",
            ["output_dir"] = "D:/outputs/sdxl",
            ["pretrained_model_path"] = "D:/models/sdxl.safetensors",
            ["network_module"] = "networks.lora"
        };

        var exported = TrainingBridgeConfigService.Export(
            TrainingBridgeConfigService.WslSdScriptsBackendId,
            values);
        string arguments = exported.Single(file => file.FileName == "wsl-sd-scripts.args.txt").Content;

        Assert.Contains("model_family=sdxl", arguments);
        Assert.Contains("--network_module\nnetworks.lora", arguments);
        Assert.Contains("--pretrained_model_name_or_path\n/mnt/d/models/sdxl.safetensors", arguments);
        Assert.DoesNotContain("--qwen3", arguments);
        Assert.DoesNotContain("--vae", arguments);

        var imported = TrainingBridgeConfigService.Import(
            TrainingBridgeConfigService.WslSdScriptsBackendId,
            new[] { new TrainingBridgeConfigFile("wsl-sd-scripts.args.txt", arguments) });

        Assert.Equal("sdxl", imported["model_family"]);
        Assert.Equal("networks.lora", imported["network_module"]);
        Assert.Equal("D:/models/sdxl.safetensors", imported["pretrained_model_path"]);
    }

    [Fact]
    public void ProjectStore_RoundTripsAndDeletesDraft()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyantools-training-bridge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TrainingBridgeProjectStore(root);
            var draft = new TrainingBridgeProjectDraft
            {
                Name = "bridge-test",
                SourceBackendId = TrainingBridgeConfigService.SdScriptsBackendId,
                TargetBackendId = TrainingBridgeConfigService.DiffusionPipeBackendId,
                Values = new Dictionary<string, string> { ["learning_rate"] = "0.0002" }
            };

            store.Save(draft);
            var loaded = Assert.Single(store.ListAll());

            Assert.Equal(draft.Id, loaded.Id);
            Assert.Equal("bridge-test", loaded.Name);
            Assert.Equal("0.0002", loaded.Values["learning_rate"]);

            store.Delete(draft.Id);
            Assert.Empty(store.ListAll());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void View_UsesAvailableImportSymbol()
    {
        string xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "TrainingBridgeView.xaml"));

        Assert.Contains("Symbol=\"ArrowUpload24\"", xaml);
        Assert.DoesNotContain("Symbol=\"DocumentArrowUp24\"", xaml);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CyanTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
