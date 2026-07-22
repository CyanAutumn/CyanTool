using CyanTools.Model;
using CyanTools.Utils;
using CyanTools.ViewModel;
using Xunit;

public sealed class TrainingBridgeTests
{
    [Fact]
    public void SystemMonitorWidget_FormatsElapsedAndTotalTimeWithDays()
    {
        Assert.Equal(
            "1d 02:03:04/3d 10:20:30",
            SystemMonitorWidgetViewModel.FormatTrainingDurationPair(
                new TimeSpan(days: 1, hours: 2, minutes: 3, seconds: 4),
                new TimeSpan(days: 3, hours: 10, minutes: 20, seconds: 30)));
        Assert.Equal("--d --:--:--/--d --:--:--", SystemMonitorWidgetViewModel.FormatTrainingDurationPair(null, null));
    }

    [Fact]
    public void Import_DiffusionPipeBundle_MapsAliasesAndRuntimePaths()
    {
        var files = new[]
        {
            new TrainingBridgeConfigFile("anima_train.toml", """
                output_dir = "/mnt/d/outputs/anime"
                epochs = 2
                micro_batch_size_per_gpu = 1

                [preview]
                enabled = true
                every_n_epochs = 2
                positive_prompt = "1girl, rainy street"
                negative_prompt = "blurry"
                sampler = "flow_euler"
                scheduler = "normal"
                width = 768
                height = 1152
                steps = 12
                cfg = 4.5
                seed = 123
                offload_pipeline_for_vae = false

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
        Assert.Equal("true", values["preview_enabled"]);
        Assert.Equal("2", values["preview_every_n_epochs"]);
        Assert.Equal("1girl, rainy street", values["preview_positive_prompt"]);
        Assert.Equal("blurry", values["preview_negative_prompt"]);
        Assert.Equal("flow_euler", values["preview_sampler"]);
        Assert.Equal("normal", values["preview_scheduler"]);
        Assert.Equal("768", values["preview_width"]);
        Assert.Equal("1152", values["preview_height"]);
        Assert.Equal("12", values["preview_steps"]);
        Assert.Equal("4.5", values["preview_cfg"]);
        Assert.Equal("123", values["preview_seed"]);
        Assert.Equal("false", values["preview_offload_pipeline_for_vae"]);
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
            ["learning_rate"] = "0.0002",
            ["preview_enabled"] = "true",
            ["preview_every_n_epochs"] = "2",
            ["preview_positive_prompt"] = "1girl, rainy street",
            ["preview_negative_prompt"] = "blurry",
            ["preview_sampler"] = "flow_euler",
            ["preview_scheduler"] = "normal",
            ["preview_width"] = "768",
            ["preview_height"] = "1152",
            ["preview_steps"] = "12",
            ["preview_cfg"] = "4.5",
            ["preview_seed"] = "123",
            ["preview_offload_pipeline_for_vae"] = "false"
        };

        var files = TrainingBridgeConfigService.Export(
            TrainingBridgeConfigService.DiffusionPipeBackendId,
            values).ToDictionary(file => file.FileName, StringComparer.Ordinal);

        Assert.Equal(new[] { "anima_dataset.toml", "anima_train.toml" }, files.Keys.OrderBy(key => key).ToArray());
        Assert.Contains("output_dir = \"/mnt/d/outputs/anime\"", files["anima_train.toml"].Content);
        Assert.Contains("transformer_path = \"/mnt/d/models/anima.safetensors\"", files["anima_train.toml"].Content);
        Assert.Contains("llm_path = \"/mnt/d/models/qwen3\"", files["anima_train.toml"].Content);
        Assert.Contains("rank = 8", files["anima_train.toml"].Content);
        Assert.Contains("[preview]", files["anima_train.toml"].Content);
        Assert.Contains("enabled = true", files["anima_train.toml"].Content);
        Assert.Contains("every_n_epochs = 2", files["anima_train.toml"].Content);
        Assert.Contains("positive_prompt = \"1girl, rainy street\"", files["anima_train.toml"].Content);
        Assert.Contains("negative_prompt = \"blurry\"", files["anima_train.toml"].Content);
        Assert.Contains("sampler = \"flow_euler\"", files["anima_train.toml"].Content);
        Assert.Contains("scheduler = \"normal\"", files["anima_train.toml"].Content);
        Assert.Contains("width = 768", files["anima_train.toml"].Content);
        Assert.Contains("height = 1152", files["anima_train.toml"].Content);
        Assert.Contains("steps = 12", files["anima_train.toml"].Content);
        Assert.Contains("cfg = 4.5", files["anima_train.toml"].Content);
        Assert.Contains("seed = 123", files["anima_train.toml"].Content);
        Assert.Contains("offload_pipeline_for_vae = false", files["anima_train.toml"].Content);
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

    [Fact]
    public void View_UsesParameterCardsAndSystemMonitor()
    {
        string xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "TrainingBridgeView.xaml"));

        Assert.Contains("<WrapPanel Orientation=\"Horizontal\"/>", xaml);
        Assert.Contains("Binding DisplayLabel", xaml);
        Assert.DoesNotContain("<DataGrid", xaml);
        Assert.Contains("Header=\"系统监控\"", xaml);
        Assert.Contains("Value=\"{Binding GpuLoadPercent}\"", xaml);
        Assert.Contains("Value=\"{Binding VramLoadPercent}\"", xaml);
        Assert.Contains("Text=\"{Binding CpuText}\"", xaml);
        Assert.Contains("Text=\"{Binding RamText}\"", xaml);
        Assert.Contains("Value=\"{Binding DiskLoadPercent}\"", xaml);
        Assert.Contains("Header=\"Epoch 预览图\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding CoreParameters}\"", xaml);
        Assert.Contains("Text=\"{Binding PreviewPositivePrompt", xaml);
        Assert.Contains("SelectedItem=\"{Binding PreviewSampler", xaml);
        Assert.Contains("Text=\"{Binding PreviewWidth", xaml);
        Assert.Contains("Text=\"{Binding PreviewHeight", xaml);
    }

    [Fact]
    public void SystemMetricsParser_ReadsPrimaryNvidiaGpu()
    {
        TrainingBridgeGpuMetrics? metrics = TrainingBridgeSystemMetricsService.ParsePrimaryGpuCsv(
            "NVIDIA GeForce RTX 5090 D, 32, 5325, 32568, 52");

        Assert.NotNull(metrics);
        Assert.Equal("NVIDIA GeForce RTX 5090 D", metrics.Name);
        Assert.Equal(32d, metrics.LoadPercent);
        Assert.Equal(5.2d, metrics.MemoryUsedGb);
        Assert.Equal(31.8d, metrics.MemoryTotalGb);
        Assert.Equal(16.4d, metrics.MemoryLoadPercent);
        Assert.Equal(52d, metrics.TemperatureC);
    }

    [Fact]
    public void DesktopWidgetSettingsStore_RoundTripsEnabledStateAndPosition()
    {
        string root = Path.Combine(Path.GetTempPath(), $"cyan-widget-{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(root, "desktop-widgets.json");
        try
        {
            var store = new DesktopWidgetSettingsStore(settingsPath);
            store.Save(new DesktopWidgetSettings
            {
                ShowSystemMonitorWidget = true,
                SystemMonitorLeft = 128.5,
                SystemMonitorTop = 64.25
            });

            DesktopWidgetSettings loaded = store.Load();

            Assert.True(loaded.ShowSystemMonitorWidget);
            Assert.Equal(128.5, loaded.SystemMonitorLeft);
            Assert.Equal(64.25, loaded.SystemMonitorTop);
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
    public void DesktopSystemMonitor_IsAlwaysOnTopDraggableAndTrainingBridgeControlled()
    {
        string root = FindRepositoryRoot();
        string widgetXaml = File.ReadAllText(Path.Combine(root, "SystemMonitorWidgetWindow.xaml"));
        string widgetCode = File.ReadAllText(Path.Combine(root, "SystemMonitorWidgetWindow.xaml.cs"));
        string trainingBridgeXaml = File.ReadAllText(Path.Combine(root, "TrainingBridgeView.xaml"));
        string optionsXaml = File.ReadAllText(Path.Combine(root, "OptionsView.xaml"));
        string appXaml = File.ReadAllText(Path.Combine(root, "App.xaml"));
        string appCode = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));

        Assert.Contains("Topmost=\"True\"", widgetXaml);
        Assert.Contains("ShowInTaskbar=\"False\"", widgetXaml);
        Assert.Contains("Width=\"340\"", widgetXaml);
        Assert.Contains("Height=\"64\"", widgetXaml);
        Assert.Contains("Text=\"{Binding CpuText}\"", widgetXaml);
        Assert.Contains("Text=\"{Binding GpuLoadText}\"", widgetXaml);
        Assert.Contains("Text=\"{Binding TrainingEpochText}\"", widgetXaml);
        Assert.Contains("Text=\"{Binding TrainingDurationText}\"", widgetXaml);
        Assert.Contains("Text=\"{Binding TrainingLossText}\"", widgetXaml);
        Assert.DoesNotContain("最近 10 个 Epoch", widgetXaml);
        Assert.Contains("Points=\"{Binding LossCurvePoints}\"", widgetXaml);
        Assert.DoesNotContain("ProgressBar", widgetXaml);
        Assert.Contains("DragMove();", widgetCode);
        Assert.Contains("IsChecked=\"{Binding ShowSystemMonitorWidget", trainingBridgeXaml);
        Assert.DoesNotContain("ShowSystemMonitorWidget", optionsXaml);
        Assert.Contains("ShutdownMode=\"OnMainWindowClose\"", appXaml);
        Assert.Contains("PrepareForApplicationShutdown", appCode);
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
