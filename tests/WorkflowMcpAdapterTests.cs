using CyanTools.McpServer;
using CyanTools.Pipeline;
using CyanTools.ViewModel;
using Xunit;

namespace CyanTools.Mcp.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class McpWorkflowEnvironmentCollection
{
    public const string Name = "MCP workflow environment";
}

[Collection(McpWorkflowEnvironmentCollection.Name)]
public sealed class WorkflowMcpAdapterTests
{
    [Fact]
    public void ListAndDescribe_ReturnSavedWorkflowAndFirstNodeParametersOnly()
    {
        string root = CreateTemporaryRoot();
        string workflowFolder = Path.Combine(root, "workflows");
        try
        {
            var downloader = new DatasetDownloaderPipelineNode();
            downloader.Inner.SelectedMode = DatasetDownloadMode.ArtistId;
            downloader.Inner.InputText = "saved-artist-id";
            downloader.Inner.OutputDirectory = Path.Combine(root, "downloads");
            var scaling = new ImageScalingPipelineNode();

            WorkflowStorage.Save(
                "基础数据集制备",
                new PipelineNodeViewModelBase[] { downloader, scaling },
                workflowFolder);

            var executor = new WorkflowOperationExecutor(workflowFolder);
            WorkflowSummary summary = Assert.Single(executor.ListWorkflows());
            WorkflowDescription description = executor.DescribeWorkflow("基础数据集制备");

            Assert.Equal("ready", summary.Status);
            Assert.Equal(2, summary.NodeCount);
            Assert.Equal("数据集下载器", summary.FirstNodeDisplayName);
            Assert.Equal("external_service", summary.Risk);
            Assert.Equal(2, description.Nodes.Count);
            Assert.Equal("数据集下载器", description.FirstNode.DisplayName);

            PipelineParameterDescription artistId = Assert.Single(
                description.FirstNode.Parameters,
                parameter => parameter.Name == nameof(DatasetDownloaderViewModel.InputText));
            Assert.Equal("saved-artist-id", artistId.DefaultValue);
            Assert.DoesNotContain(
                description.FirstNode.Parameters,
                parameter => parameter.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("first_node_only", description.OverrideScope, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_OverridesFirstNodeAndKeepsLaterNodeSavedDefaults()
    {
        string root = CreateTemporaryRoot();
        string workflowFolder = Path.Combine(root, "workflows");
        string savedInput = Path.Combine(root, "saved-input");
        string overriddenInput = Path.Combine(root, "overridden-input");
        Directory.CreateDirectory(savedInput);
        Directory.CreateDirectory(overriddenInput);
        File.WriteAllText(Path.Combine(savedInput, "untouched.jpg"), "saved input");
        File.WriteAllText(Path.Combine(overriddenInput, "keep.png"), "png");
        File.WriteAllText(Path.Combine(overriddenInput, "remove.jpg"), "jpg");

        string? previousAllowedRoots = Environment.GetEnvironmentVariable("CYANTOOLS_MCP_ALLOWED_ROOTS");
        try
        {
            var picker = new FolderPickerPipelineNode
            {
                FolderPath = savedInput,
                WorkflowOutputPortKey = "root"
            };
            var keepFiles = new KeepFilesPipelineNode();
            keepFiles.Inner.KeepPng = true;
            keepFiles.Inner.KeepJpg = false;
            keepFiles.Inner.KeepJpeg = false;
            keepFiles.Inner.KeepWebp = false;
            keepFiles.Inner.KeepBmp = false;
            keepFiles.Inner.KeepNpz = false;
            keepFiles.Inner.KeepTxt = false;
            keepFiles.Inner.KeepChineseTxt = false;

            WorkflowStorage.Save(
                "覆盖首节点",
                new PipelineNodeViewModelBase[] { picker, keepFiles },
                workflowFolder);

            Environment.SetEnvironmentVariable("CYANTOOLS_MCP_ALLOWED_ROOTS", root);
            var executor = new WorkflowOperationExecutor(workflowFolder);
            string parametersJson =
                $"{{\"FolderPath\":{System.Text.Json.JsonSerializer.Serialize(overriddenInput)}}}";

            WorkflowExecutionResult result = await executor.ExecuteAsync(
                "覆盖首节点",
                parametersJson,
                confirmDestructive: true,
                CancellationToken.None);

            Assert.Equal("completed", result.Status);
            Assert.Equal("destructive", result.Risk);
            Assert.Equal(Path.GetFullPath(overriddenInput), result.FirstNodeParameters["FolderPath"]);
            Assert.True(File.Exists(Path.Combine(overriddenInput, "keep.png")));
            Assert.False(File.Exists(Path.Combine(overriddenInput, "remove.jpg")));
            Assert.True(File.Exists(Path.Combine(savedInput, "untouched.jpg")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CYANTOOLS_MCP_ALLOWED_ROOTS", previousAllowedRoots);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyantools-workflow-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
