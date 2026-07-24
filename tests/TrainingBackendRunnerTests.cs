using CyanTools.Utils;
using Xunit;

namespace CyanTools.Tests;

public sealed class TrainingBackendRunnerTests
{
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
