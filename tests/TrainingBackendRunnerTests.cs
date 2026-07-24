using CyanTools.Model;
using CyanTools.Utils;
using Xunit;

namespace CyanTools.Tests;

public sealed class TrainingBackendRunnerTests
{
    [Fact]
    public void BindingStore_KeepsManagedBindingsAndPersistsCustomBinding()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyantools-training-binding-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TrainingBackendBindingStore(
                bindingsPath: Path.Combine(root, "state", "trainer-bindings.json"),
                managedRoot: root);

            TrainingBackendBinding managed = store.ListAll().Single(binding =>
                binding.Id == "managed-diffusion-pipe");
            Assert.True(managed.IsManaged);
            Assert.Equal(TrainingBridgeConfigService.DiffusionPipeBackendId, managed.BackendId);
            Assert.Contains("diffusion-pipe", managed.ConfigurationDirectory, StringComparison.OrdinalIgnoreCase);

            var custom = new TrainingBackendBinding
            {
                Name = "Existing Anima",
                BackendId = TrainingBridgeConfigService.AnimaFastLoraBackendId,
                TrainerPath = "D:/trainers/anima-fast-lora",
                ConfigurationDirectory = "D:/trainers/anima-fast-lora/configs"
            };
            store.SaveCustom(custom);

            TrainingBackendBinding saved = store.ListAll().Single(binding => binding.Id == custom.Id);
            Assert.False(saved.IsManaged);
            Assert.Equal(custom.ConfigurationDirectory, saved.ConfigurationDirectory);

            store.DeleteCustom(custom.Id);
            Assert.DoesNotContain(store.ListAll(), binding => binding.Id == custom.Id);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("sd-scripts.args.txt", TrainingBridgeConfigService.SdScriptsBackendId)]
    [InlineData("wsl-sd-scripts.args.txt", TrainingBridgeConfigService.WslSdScriptsBackendId)]
    [InlineData("anima-fast-lora.args.txt", TrainingBridgeConfigService.AnimaFastLoraBackendId)]
    public void DetectSourceBackend_RecognizesCyanToolsExportedFileNames(string fileName, string expectedBackendId)
    {
        string? detected = TrainingBridgeConfigService.DetectSourceBackend(
        [
            new TrainingBridgeConfigFile(fileName, "--output_dir\nD:/outputs/anime")
        ]);

        Assert.Equal(expectedBackendId, detected);
    }

    [Fact]
    public void DetectSourceBackend_RecognizesDiffusionPipeConfigFromWslPathAndToml()
    {
        string? detected = TrainingBridgeConfigService.DetectSourceBackend(
        [
            new TrainingBridgeConfigFile(
                "anima_anime_1024.toml",
                """
                dataset = 'configs/anima_anime_dataset_1024.toml'

                [model]
                transformer_path = '/root/models/anima.safetensors'
                llm_path = '/root/models/qwen3'
                """,
                @"\\wsl.localhost\Ubuntu\root\diffusion-pipe\configs\anima_anime_1024.toml")
        ]);

        Assert.Equal(TrainingBridgeConfigService.DiffusionPipeBackendId, detected);
    }

    [Fact]
    public void DetectSourceBackend_LeavesAmbiguousGenericConfigForManualSelection()
    {
        string? detected = TrainingBridgeConfigService.DetectSourceBackend(
        [
            new TrainingBridgeConfigFile("train.args", "--output_dir\nD:/outputs/anime")
        ]);

        Assert.Null(detected);
    }

    [Fact]
    public void Prepare_DiffusionPipe_WritesBothAnimaFilesAndLaunchesTheirAbsoluteConfig()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyantools-training-run-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var runner = new TrainingBackendRunner(
                managedRoot: Path.Combine(root, "backends"),
                runsRoot: Path.Combine(root, "runs"));
            var values = new Dictionary<string, string>
            {
                ["dataset_path"] = "D:/datasets/anime",
                ["output_dir"] = "D:/outputs/anime",
                ["transformer_path"] = "D:/models/anima.safetensors",
                ["llm_path"] = "D:/models/qwen3",
                ["vae_path"] = "D:/models/vae.safetensors"
            };

            TrainingBackendLaunchPlan plan = runner.Prepare(
                "anima-test",
                TrainingBridgeConfigService.DiffusionPipeBackendId,
                values);

            Assert.Equal("wsl.exe", plan.FileName);
            Assert.Equal(2, plan.GeneratedFiles.Count);
            string trainConfig = File.ReadAllText(Path.Combine(plan.RunDirectory, "anima_train.toml"));
            string expectedDatasetPath = TrainingBridgeConfigService.WindowsPathToWsl(
                Path.Combine(plan.RunDirectory, "anima_dataset.toml"));
            Assert.Contains($"dataset = \"{expectedDatasetPath}\"", trainConfig);
            Assert.Contains("deepspeed --num_gpus=1 train.py --deepspeed --config", plan.Arguments.Last());
            Assert.True(File.Exists(Path.Combine(plan.RunDirectory, "anima_dataset.toml")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
