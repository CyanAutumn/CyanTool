using CyanTools.Utils;
using Xunit;

namespace CyanTools.Tests;

public sealed class TrainingDeploymentTaskStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cyantools-deployment-tests", Guid.NewGuid().ToString("N"));

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
    public void DiffusionPipePreviewInjectionIsWiredAndKeepsTheUpstreamDiffMinimal()
    {
        string root = FindRepositoryRoot();
        string installer = File.ReadAllText(Path.Combine(root, "scripts", "Install-DiffusionPipeWsl.ps1"));
        string dispatcher = File.ReadAllText(Path.Combine(root, "scripts", "Install-TrainingBackends.ps1"));
        string view = File.ReadAllText(Path.Combine(root, "OptionsView.xaml"));
        string viewModel = File.ReadAllText(Path.Combine(root, "ViewModel", "OptionsViewModel.cs"));
        string deployer = File.ReadAllText(Path.Combine(root, "Utils", "TrainingBackendDeployer.cs"));

        Assert.Contains("InjectDiffusionPipePreviewCommand", view);
        Assert.Contains("TrainingBackendAction.InjectPreview", viewModel);
        Assert.Contains("InjectPreview", deployer);
        Assert.Contains("\"InjectPreview\" { Install-DiffusionPipePreviewHook }", dispatcher);
        Assert.Contains("function Install-DiffusionPipePreviewHook", installer);
        Assert.Contains("import_line='import local_anima_epoch_preview'", installer);
        Assert.Contains("/local_anima_epoch_preview.py", installer);
        Assert.Contains("git -C \"$repo\" rev-parse --git-path info/exclude", installer);
        Assert.Contains("WIDTH = 1024", installer);
        Assert.Contains("HEIGHT = 1024", installer);
        Assert.Contains("preview_epoch{epoch}.png", installer);
        Assert.Contains("kwargs['keep_models_loaded'] = True", installer);
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
