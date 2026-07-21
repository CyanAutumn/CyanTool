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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
