using CyanTools.Agent;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class AgentToolCatalogTests
{
    [Fact]
    public void EveryFunctionalPageHasOneStableAgentOperation()
    {
        IReadOnlyList<AgentPageDefinition> pages = AgentToolCatalog.GetPages();

        Assert.Equal(32, pages.Count);
        Assert.Equal(17, pages.SelectMany(page => page.Operations).Count(operation => operation.ExecutionKind == AgentExecutionKind.PipelineNode));
        Assert.Equal(pages.Count, pages.Select(page => page.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(pages, page => Assert.NotEmpty(page.Operations));
        Assert.Equal(
            pages.SelectMany(page => page.Operations).Count(),
            pages.SelectMany(page => page.Operations).Select(operation => operation.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("images_convert_format")]
    [InlineData("images_cluster")]
    [InlineData("dataset_download")]
    public void PipelineOperationsDeclareTheirBackingNode(string operationId)
    {
        AgentOperationDefinition operation = AgentToolCatalog.GetOperation(operationId);

        Assert.True(operation.IsHeadlessExecutable);
        Assert.False(string.IsNullOrWhiteSpace(operation.PipelineNodeType));
        Assert.Contains(operation.Parameters!, parameter => parameter.Name == "input_dir" && parameter.Required);
    }

    [Fact]
    public void DestructiveOperationsRemainExplicitlyClassified()
    {
        AgentOperationDefinition resolutionFilter = AgentToolCatalog.GetOperation("images_filter_resolution");
        AgentOperationDefinition rename = AgentToolCatalog.GetOperation("images_rename");

        Assert.Equal(AgentOperationRisk.Destructive, resolutionFilter.Risk);
        Assert.Equal(AgentOperationRisk.Destructive, rename.Risk);
    }

    [Fact]
    public void SavedWorkflowOperationUsesTheHeadlessWorkflowAdapter()
    {
        AgentOperationDefinition operation = AgentToolCatalog.GetOperation("workflow_run_saved");

        Assert.Equal(AgentExecutionKind.Workflow, operation.ExecutionKind);
        Assert.True(operation.IsHeadlessExecutable);
        Assert.Contains(operation.Parameters!, parameter => parameter.Name == "workflow_name" && parameter.Required);
        Assert.Contains(operation.Parameters!, parameter => parameter.Name == "first_node_parameters");
    }
}
