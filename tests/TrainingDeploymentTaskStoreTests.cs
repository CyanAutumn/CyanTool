using CyanTools.Utils;
using Xunit;

namespace CyanTools.Tests;

public sealed class TrainingDeploymentTaskStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cyantools-deployment-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void OptionsView_UsesIndependentBackendRowsAndClickableSystemStatuses()
    {
        string root = FindRepositoryRoot();
        string view = File.ReadAllText(Path.Combine(root, "OptionsView.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(root, "OptionsView.xaml.cs"));

        Assert.Contains("Header=\"系统检查\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Windows 原生训练后端\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"WSL2 训练后端\"", view, StringComparison.Ordinal);
        (string BackendId, string TargetName, string Header)[] rows =
        [
            ("sd-scripts", "SdScriptsDeploymentSection", "Windows sd-scripts 部署"),
            ("anima-fast-lora", "AnimaFastLoraDeploymentSection", "Windows Anima Fast LoRA 部署"),
            ("wsl-sd-scripts", "WslSdScriptsDeploymentSection", "WSL sd-scripts 部署"),
            ("diffusion-pipe", "DiffusionPipeDeploymentSection", "WSL diffusion-pipe 部署"),
            ("comfyui-windows", "ComfyUiWindowsDeploymentSection", "ComfyUI（Windows）部署"),
            ("comfyui-wsl", "ComfyUiWslDeploymentSection", "ComfyUI（WSL）部署"),
            ("forge-neo-windows", "ForgeNeoWindowsDeploymentSection", "Webui Forge Neo（Windows）部署"),
            ("forge-neo-wsl", "ForgeNeoWslDeploymentSection", "Webui Forge Neo（WSL）部署")
        ];
        foreach ((string backendId, string targetName, string header) in rows)
        {
            Assert.Contains($"Tag=\"{backendId}\" Click=\"BackendStatusButton_Click\"", view, StringComparison.Ordinal);
            Assert.Contains($"x:Name=\"{targetName}\"", view, StringComparison.Ordinal);
            Assert.Contains($"Header=\"{header}\"", view, StringComparison.Ordinal);
            Assert.Contains($"\"{backendId}\" => {targetName}", codeBehind, StringComparison.Ordinal);
        }
        Assert.Contains("target.BringIntoView();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("target.Focus();", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistsTaskStateAndSanitizedLogs()
    {
        var store = new TrainingDeploymentTaskStore(_root);
        TrainingDeploymentTaskRecord task = store.Create("sd-scripts", "Install");

        store.UpdateState(task.Id, TrainingDeploymentTaskState.Running, "running");
        store.AppendLog(task.Id, "\u001b[31mhello\u001b[0m\0");
        store.UpdateState(task.Id, TrainingDeploymentTaskState.Succeeded, "done");

        TrainingDeploymentTaskRecord restored = Assert.Single(new TrainingDeploymentTaskStore(_root).ListAll());
        Assert.Equal(TrainingDeploymentTaskState.Succeeded, restored.State);
        Assert.Equal("done", restored.Summary);
        Assert.Equal("hello", Assert.Single(restored.Logs));
    }

    [Fact]
    public void MarksInterruptedRunningTaskFailedOnRestart()
    {
        var store = new TrainingDeploymentTaskStore(_root);
        TrainingDeploymentTaskRecord task = store.Create("diffusion-pipe", "Install");
        store.UpdateState(task.Id, TrainingDeploymentTaskState.Running, "running");

        var restarted = new TrainingDeploymentTaskStore(_root);
        restarted.RecoverInterruptedTasks();

        TrainingDeploymentTaskRecord restored = Assert.Single(restarted.ListAll());
        Assert.Equal(TrainingDeploymentTaskState.Failed, restored.State);
        Assert.Contains("上次运行被中断", restored.Summary);
        Assert.Contains(restored.Summary, restored.Logs);
    }

    [Fact]
    public void DiffusionPipePreviewInjectionIsPartOfEveryLifecycleAndKeepsTheUpstreamDiffMinimal()
    {
        string root = FindRepositoryRoot();
        string installer = File.ReadAllText(Path.Combine(root, "scripts", "Install-DiffusionPipeWsl.ps1"));
        string hook = File.ReadAllText(Path.Combine(root, "scripts", "local_anima_epoch_preview.py"));
        string dispatcher = File.ReadAllText(Path.Combine(root, "scripts", "Install-TrainingBackends.ps1"));
        string view = File.ReadAllText(Path.Combine(root, "OptionsView.xaml"));
        string viewModel = File.ReadAllText(Path.Combine(root, "ViewModel", "OptionsViewModel.cs"));
        string deployer = File.ReadAllText(Path.Combine(root, "Utils", "TrainingBackendDeployer.cs"));

        Assert.DoesNotContain("InjectDiffusionPipePreviewCommand", view);
        Assert.DoesNotContain("TrainingBackendAction.InjectPreview", viewModel);
        Assert.DoesNotContain("InjectPreview", deployer);
        Assert.DoesNotContain("InjectPreview", dispatcher);
        Assert.Contains("function Install-DiffusionPipePreviewHook", installer);
        Assert.Contains("function Suspend-DiffusionPipePreviewHook", installer);
        Assert.Contains("Suspend-DiffusionPipePreviewHook", installer);
        Assert.Contains("finally {", installer);
        Assert.Contains("Install-DiffusionPipePreviewHook\n    Apply-SmokePatch\n    Run-SmokeTraining", installer.Replace("\r\n", "\n"));
        Assert.Contains("import_line='import local_anima_epoch_preview'", installer);
        Assert.Contains("/local_anima_epoch_preview.py", installer);
        Assert.Contains("git -C \"$repo\" rev-parse --git-path info/exclude", installer);
        Assert.Contains("local_anima_epoch_preview.py", installer);
        Assert.Contains("HOOK_VERSION = 2", hook);
        Assert.Contains("'width': 1024", hook);
        Assert.Contains("'height': 1024", hook);
        Assert.Contains("'every_n_epochs': 1", hook);
        Assert.Contains("'offload_pipeline_for_vae': True", hook);
        Assert.Contains("kwargs['keep_models_loaded'] = True", hook);
        Assert.Contains("saver.pipeline_model.to('cpu')", hook);
        Assert.Contains("saver.pipeline_model.to('cuda')", hook);
        Assert.Contains("failed without stopping training", hook);
        Assert.Contains("DIFFPIPEFORGE_PREVIEW_CONFIG_B64", hook);
        Assert.Contains("output_dir = Path(saver.save_root) / 'sample'", hook);
        Assert.Contains("Anima image preview expected one frame", hook);
        Assert.Contains("Anima image preview expected HWC output", hook);
        Assert.Contains("Anima epoch 预览图功能已完整注入，无需修改。", installer);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CyanTools.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the CyanTools repository root.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
