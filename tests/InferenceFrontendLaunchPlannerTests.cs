using CyanTools.Model;
using CyanTools.Utils;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace CyanTools.Tests;

public sealed class InferenceFrontendLaunchPlannerTests
{
    [Theory]
    [InlineData(RuntimePathBindingBackendIds.ComfyUiWindows, "main.py")]
    [InlineData(RuntimePathBindingBackendIds.ComfyUiWsl, "main.py")]
    [InlineData(RuntimePathBindingBackendIds.ForgeNeoWindows, "launch.py")]
    [InlineData(RuntimePathBindingBackendIds.ForgeNeoWsl, "launch.py")]
    public void InstallationDetector_ReportsMissingBrokenAndAvailableStates(
        string backendId,
        string entryName)
    {
        const string root = "X:/frontend";
        var binding = new TrainingBackendBinding
        {
            BackendId = backendId,
            TrainerPath = root
        };

        InferenceBackendInstallationStatus missing = InferenceBackendInstallationDetector.Inspect(
            binding,
            (_, _) => root,
            _ => false,
            _ => false);
        InferenceBackendInstallationStatus broken = InferenceBackendInstallationDetector.Inspect(
            binding,
            (_, _) => root,
            _ => true,
            _ => false);
        InferenceBackendInstallationStatus available = InferenceBackendInstallationDetector.Inspect(
            binding,
            (_, _) => root,
            _ => true,
            path => string.Equals(path, Path.Combine(root, entryName), StringComparison.Ordinal));

        Assert.Equal("未安装", missing.InstallState);
        Assert.Equal("安装异常", broken.InstallState);
        Assert.Contains(entryName, broken.Summary, StringComparison.Ordinal);
        Assert.Equal("已安装", available.InstallState);
    }

    [Fact]
    public void InferenceProjectsPage_IsRegisteredAsAnIndependentToolPage()
    {
        string root = FindRepositoryRoot();
        string codeBehind = File.ReadAllText(Path.Combine(root, "InferenceProjectsView.xaml.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "Framework", "Navigation", "ToolPageRegistry.cs"));
        string shell = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"));

        Assert.Contains("[ToolPage(\"跑图管理\", 890)]", codeBehind, StringComparison.Ordinal);
        Assert.Contains("typeof(global::CyanTools.InferenceProjectsView)", registry, StringComparison.Ordinal);
        Assert.Contains("[typeof(InferenceProjectsView)]", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void InferenceProjectsView_ExposesLaunchLogsAndModelFolders()
    {
        string xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "InferenceProjectsView.xaml"));

        Assert.Contains("Command=\"{Binding StartCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding StopCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding LogText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"Checkpoint\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"Lora\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"Vae\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"Nodes\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"节点文件夹\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsComfyUiSelected}\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RuntimePathBindingBackendIds.ComfyUiWindows)]
    [InlineData(RuntimePathBindingBackendIds.ComfyUiWsl)]
    public void GetRelativeFolder_ComfyUiNodes_UsesCustomNodes(string backendId)
    {
        string actual = InferenceFrontendLaunchPlanner.GetRelativeFolder(
            backendId,
            InferenceFrontendFolder.Nodes);

        Assert.Equal("custom_nodes", actual);
    }

    [Theory]
    [InlineData(RuntimePathBindingBackendIds.ForgeNeoWindows)]
    [InlineData(RuntimePathBindingBackendIds.ForgeNeoWsl)]
    public void GetRelativeFolder_ForgeNodes_IsRejected(string backendId)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            InferenceFrontendLaunchPlanner.GetRelativeFolder(backendId, InferenceFrontendFolder.Nodes));

        Assert.Contains("ComfyUI", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RuntimePathBindingBackendIds.ComfyUiWindows, InferenceFrontendFolder.Checkpoint, "models/checkpoints")]
    [InlineData(RuntimePathBindingBackendIds.ComfyUiWindows, InferenceFrontendFolder.Lora, "models/loras")]
    [InlineData(RuntimePathBindingBackendIds.ComfyUiWindows, InferenceFrontendFolder.Vae, "models/vae")]
    [InlineData(RuntimePathBindingBackendIds.ForgeNeoWindows, InferenceFrontendFolder.Checkpoint, "models/Stable-diffusion")]
    [InlineData(RuntimePathBindingBackendIds.ForgeNeoWindows, InferenceFrontendFolder.Lora, "models/Lora")]
    [InlineData(RuntimePathBindingBackendIds.ForgeNeoWindows, InferenceFrontendFolder.Vae, "models/VAE")]
    public void GetRelativeFolder_UsesFrontendSpecificModelLayout(
        string backendId,
        InferenceFrontendFolder folder,
        string expected)
    {
        string actual = InferenceFrontendLaunchPlanner.GetRelativeFolder(backendId, folder)
            .Replace('\\', '/');

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(RuntimePathBindingBackendIds.ComfyUiWsl, "comfyui-cu130")]
    [InlineData(RuntimePathBindingBackendIds.ForgeNeoWsl, "forge-neo-cu128")]
    public void Create_WslBindingWithEmptyEnvironment_UsesCompatibleDefault(
        string backendId,
        string expectedEnvironment)
    {
        var binding = new TrainingBackendBinding
        {
            BackendId = backendId,
            TrainerPath = backendId == RuntimePathBindingBackendIds.ComfyUiWsl
                ? "~/ComfyUI"
                : "~/sd-webui-forge-neo",
            WslDistribution = "Ubuntu-24.04",
            CondaEnvironment = string.Empty
        };

        InferenceFrontendLaunchPlan plan = InferenceFrontendLaunchPlanner.Create(binding);
        string command = plan.Arguments.Last();

        Assert.Equal("wsl.exe", plan.FileName);
        Assert.Equal("Ubuntu-24.04", plan.Arguments[1]);
        Assert.Contains($"conda activate '{expectedEnvironment}'", command, StringComparison.Ordinal);
        Assert.Contains("cd \"$HOME\"/", command, StringComparison.Ordinal);
        Assert.Contains(
            backendId == RuntimePathBindingBackendIds.ComfyUiWsl ? "main.py" : "launch.py",
            command,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WindowsBinding_PrefersManagedAnimaTrainerEnvironment()
    {
        string root = Path.Combine(Path.GetTempPath(), "ComfyUI");
        string localAppData = Path.Combine(Path.GetTempPath(), "LocalAppData");
        string expectedPython = Path.Combine(
            localAppData,
            "AnimaTrainer",
            "venvs",
            RuntimePathBindingBackendIds.ComfyUiWindows,
            "Scripts",
            "python.exe");
        var binding = new TrainingBackendBinding
        {
            BackendId = RuntimePathBindingBackendIds.ComfyUiWindows,
            TrainerPath = root
        };

        InferenceFrontendLaunchPlan plan = InferenceFrontendLaunchPlanner.Create(
            binding,
            path => string.Equals(path, expectedPython, StringComparison.OrdinalIgnoreCase),
            localAppData);

        Assert.Equal(expectedPython, plan.FileName);
        Assert.Equal(root, plan.WorkingDirectory);
        Assert.Equal(Path.Combine(root, "main.py"), plan.Arguments[0]);
        Assert.Contains("8188", plan.Arguments);
    }

    [Fact]
    public void ManagedWslInferenceBindings_DeclareCompatibleCondaEnvironments()
    {
        string managedRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new TrainingBackendBindingStore(managedRoot);
        TrainingBackendBinding[] bindings = store.ListAll()
            .Where(binding => binding.BackendId is RuntimePathBindingBackendIds.ComfyUiWsl
                or RuntimePathBindingBackendIds.ForgeNeoWsl)
            .ToArray();

        Assert.Collection(
            bindings.OrderBy(binding => binding.BackendId),
            binding =>
            {
                Assert.Equal(RuntimePathBindingBackendIds.ComfyUiWsl, binding.BackendId);
                Assert.Equal("comfyui-cu130", binding.CondaEnvironment);
            },
            binding =>
            {
                Assert.Equal(RuntimePathBindingBackendIds.ForgeNeoWsl, binding.BackendId);
                Assert.Equal("forge-neo-cu128", binding.CondaEnvironment);
            });
    }

    private static string FindRepositoryRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "CyanTools.csproj")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("CyanTools repository root was not found.");
    }
}
