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
            Assert.Equal("~/diffusion-pipe/configs", managed.ConfigurationDirectory);
            Assert.DoesNotContain("/root/", managed.ConfigurationDirectory, StringComparison.OrdinalIgnoreCase);

            TrainingBackendBinding comfyWsl = store.ListAll().Single(binding =>
                binding.Id == "managed-comfyui-wsl");
            Assert.True(comfyWsl.IsManaged);
            Assert.Equal(RuntimePathBindingBackendIds.ComfyUiWsl, comfyWsl.BackendId);
            Assert.Equal("~/ComfyUI", comfyWsl.TrainerPath);
            Assert.Equal("~/ComfyUI/user/default/workflows", comfyWsl.ConfigurationDirectory);

            TrainingBackendBinding forgeWsl = store.ListAll().Single(binding =>
                binding.Id == "managed-forge-neo-wsl");
            Assert.Equal(RuntimePathBindingBackendIds.ForgeNeoWsl, forgeWsl.BackendId);
            Assert.Equal("~/sd-webui-forge-neo", forgeWsl.TrainerPath);
            Assert.Equal(8, store.ListAll().Count(binding => binding.IsManaged));
            Assert.Equal(
                4,
                store.ListAll().Count(binding => binding.IsManaged && binding.SupportsTraining));
            Assert.DoesNotContain(
                store.ListAll().Where(binding => binding.SupportsTraining),
                binding => RuntimePathBindingBackendIds.IsInferenceFrontend(binding.BackendId));

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
    [InlineData(RuntimePathBindingBackendIds.ComfyUiWindows)]
    [InlineData(RuntimePathBindingBackendIds.ComfyUiWsl)]
    [InlineData(RuntimePathBindingBackendIds.ForgeNeoWindows)]
    [InlineData(RuntimePathBindingBackendIds.ForgeNeoWsl)]
    public void BindingStore_PersistsInferenceFrontendPathBindings(string backendId)
    {
        string root = Path.Combine(Path.GetTempPath(), "cyantools-path-binding-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TrainingBackendBindingStore(
                bindingsPath: Path.Combine(root, "state", "trainer-bindings.json"),
                managedRoot: root);
            bool isWsl = RuntimePathBindingBackendIds.IsWsl(backendId);
            var binding = new TrainingBackendBinding
            {
                Name = $"Custom {backendId}",
                BackendId = backendId,
                TrainerPath = isWsl ? "~/runtime" : @"D:\runtimes\frontend",
                ConfigurationDirectory = isWsl ? "~/runtime/configs" : @"D:\runtimes\frontend\configs",
                WslDistribution = isWsl ? "Ubuntu-24.04" : string.Empty
            };

            store.SaveCustom(binding);

            TrainingBackendBinding saved = store.ListAll().Single(item => item.Id == binding.Id);
            Assert.Equal(backendId, saved.BackendId);
            Assert.Equal(binding.TrainerPath, saved.TrainerPath);
            Assert.Equal(binding.ConfigurationDirectory, saved.ConfigurationDirectory);
            Assert.Equal(isWsl, saved.IsWsl);
            Assert.False(saved.SupportsTraining);
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
    public void WslPathResolver_UsesSelectedDistributionsCurrentUserHomeForTildePaths()
    {
        var resolver = new WslPathResolver(distribution =>
        {
            Assert.Equal("Ubuntu-24.04", distribution);
            return "/home/anima";
        });
        var binding = new TrainingBackendBinding
        {
            BackendId = RuntimePathBindingBackendIds.ComfyUiWsl,
            WslDistribution = "Ubuntu-24.04"
        };

        string windowsPath = resolver.ResolveWindowsPath(binding, "~/ComfyUI/user/default/workflows");

        Assert.Equal(
            @"\\wsl.localhost\Ubuntu-24.04\home\anima\ComfyUI\user\default\workflows",
            windowsPath);
        Assert.DoesNotContain(@"\root\", windowsPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "~/ComfyUI/user/default/workflows",
            resolver.ToPortableWslPath("Ubuntu-24.04", windowsPath));
    }

    [Fact]
    public void WslPathResolver_PreservesAbsoluteLinuxAndExistingUncPaths()
    {
        var resolver = new WslPathResolver(_ => "/home/user");

        Assert.Equal(
            @"\\wsl.localhost\TrainerUbuntu\srv\forge\models",
            resolver.ResolveWindowsPath("TrainerUbuntu", "/srv/forge/models"));
        Assert.Equal(
            @"\\wsl.localhost\TrainerUbuntu\srv\forge",
            resolver.ResolveWindowsPath("TrainerUbuntu", @"\\wsl.localhost\TrainerUbuntu\srv\forge"));
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
                transformer_path = '/home/anima/models/anima.safetensors'
                llm_path = '/home/anima/models/qwen3'
                """,
                @"\\wsl.localhost\Ubuntu\home\anima\diffusion-pipe\configs\anima_anime_1024.toml")
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

    [Fact]
    public void Prepare_CustomWindowsBinding_UsesBoundTrainerRootAndVirtualEnvironment()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyantools-training-binding-run-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string trainerRoot = Path.Combine(root, "custom sd-scripts");
            var runner = new TrainingBackendRunner(
                managedRoot: Path.Combine(root, "managed"),
                runsRoot: Path.Combine(root, "runs"));
            var binding = new TrainingBackendBinding
            {
                Name = "Custom sd-scripts",
                BackendId = TrainingBridgeConfigService.SdScriptsBackendId,
                TrainerPath = trainerRoot,
                ConfigurationDirectory = Path.Combine(trainerRoot, "configs")
            };

            TrainingBackendLaunchPlan plan = runner.Prepare(
                "custom-windows",
                binding,
                RequiredSdScriptsValues());

            Assert.Equal(trainerRoot, plan.WorkingDirectory);
            Assert.Equal(
                Path.Combine(trainerRoot, ".venv", "Scripts", "python.exe"),
                plan.FileName);
            Assert.Contains(Path.Combine(trainerRoot, "anima_train_network.py"), plan.RequiredWindowsFiles);
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
    public void Prepare_CustomWslBinding_UsesBoundDistributionEnvironmentAndTrainerRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyantools-training-binding-run-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var runner = new TrainingBackendRunner(
                managedRoot: Path.Combine(root, "managed"),
                runsRoot: Path.Combine(root, "runs"));
            var binding = new TrainingBackendBinding
            {
                Name = "Custom WSL sd-scripts",
                BackendId = TrainingBridgeConfigService.WslSdScriptsBackendId,
                TrainerPath = @"\\wsl.localhost\TrainerUbuntu\root\trainers\custom sd-scripts",
                ConfigurationDirectory = @"\\wsl.localhost\TrainerUbuntu\root\trainers\custom sd-scripts\configs",
                WslDistribution = "TrainerUbuntu",
                CondaEnvironment = "custom-sd-env"
            };

            TrainingBackendLaunchPlan plan = runner.Prepare(
                "custom-wsl",
                binding,
                RequiredSdScriptsValues());

            Assert.Equal("wsl.exe", plan.FileName);
            Assert.Equal("TrainerUbuntu", plan.Arguments[1]);
            string command = plan.Arguments.Last();
            Assert.Contains("conda activate 'custom-sd-env'", command);
            Assert.Contains("cd '/root/trainers/custom sd-scripts'", command);
            Assert.DoesNotContain("cd ~/sd-scripts", command);
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
    public void Prepare_CustomAnimaFastLoraBinding_UsesBoundTrainerRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyantools-training-binding-run-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string trainerRoot = Path.Combine(root, "custom anima");
            var runner = new TrainingBackendRunner(
                managedRoot: Path.Combine(root, "managed"),
                runsRoot: Path.Combine(root, "runs"));
            var binding = new TrainingBackendBinding
            {
                Name = "Custom Anima Fast LoRA",
                BackendId = TrainingBridgeConfigService.AnimaFastLoraBackendId,
                TrainerPath = trainerRoot,
                ConfigurationDirectory = Path.Combine(trainerRoot, "configs")
            };

            TrainingBackendLaunchPlan plan = runner.Prepare(
                "custom-anima",
                binding,
                RequiredSdScriptsValues());

            Assert.Equal(trainerRoot, plan.WorkingDirectory);
            Assert.Equal(
                Path.Combine(trainerRoot, ".venv", "Scripts", "python.exe"),
                plan.FileName);
            Assert.Contains(Path.Combine(trainerRoot, "train.py"), plan.RequiredWindowsFiles);
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
    public void Prepare_CustomDiffusionPipeBinding_UsesBoundWslRuntime()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyantools-training-binding-run-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var runner = new TrainingBackendRunner(
                managedRoot: Path.Combine(root, "managed"),
                runsRoot: Path.Combine(root, "runs"));
            var binding = new TrainingBackendBinding
            {
                Name = "Custom diffusion-pipe",
                BackendId = TrainingBridgeConfigService.DiffusionPipeBackendId,
                TrainerPath = "/srv/trainers/diffusion pipe",
                ConfigurationDirectory = @"\\wsl.localhost\PipeUbuntu\srv\trainers\diffusion pipe\configs",
                WslDistribution = "PipeUbuntu",
                CondaEnvironment = "custom-pipe-env"
            };

            TrainingBackendLaunchPlan plan = runner.Prepare(
                "custom-pipe",
                binding,
                RequiredDiffusionPipeValues());

            Assert.Equal("wsl.exe", plan.FileName);
            Assert.Equal("PipeUbuntu", plan.Arguments[1]);
            string command = plan.Arguments.Last();
            Assert.Contains("conda activate 'custom-pipe-env'", command);
            Assert.Contains("cd '/srv/trainers/diffusion pipe'", command);
            Assert.DoesNotContain("cd ~/diffusion-pipe", command);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static Dictionary<string, string> RequiredSdScriptsValues()
        => new()
        {
            ["model_family"] = "anima",
            ["dataset_path"] = "D:/datasets/anime",
            ["output_dir"] = "D:/outputs/anime",
            ["pretrained_model_path"] = "D:/models/anima.safetensors",
            ["qwen3_path"] = "D:/models/qwen3",
            ["vae_path"] = "D:/models/vae.safetensors"
        };

    private static Dictionary<string, string> RequiredDiffusionPipeValues()
        => new()
        {
            ["dataset_path"] = "D:/datasets/anime",
            ["output_dir"] = "D:/outputs/anime",
            ["transformer_path"] = "D:/models/anima.safetensors",
            ["llm_path"] = "D:/models/qwen3",
            ["vae_path"] = "D:/models/vae.safetensors"
        };
}
