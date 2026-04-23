using CyanTools.Pipeline;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class PipelineWorkflowValidationTests
{
    [Fact]
    public void TryValidateSingletonFirstNode_ReturnsFalseAndLogsOnce_WhenDownloaderIsInvalid()
    {
        var nodes = new PipelineNodeViewModelBase[]
        {
            new TestNode("图片聚类"),
            new TestNode("数据集下载器")
        };
        var logs = new List<string>();

        bool canContinue = PipelineWorkflowValidation.TryValidateSingletonFirstNode(
            nodes,
            node => node.DisplayName == "数据集下载器",
            "数据集下载器",
            logs.Add);

        Assert.False(canContinue);
        Assert.Equal(new[] { "数据集下载器只能位于工作流的第一个节点。" }, logs);
    }

    [Fact]
    public void TryValidateSingletonFirstNode_ReturnsTrueAndDoesNotLog_WhenDownloaderIsValid()
    {
        var nodes = new PipelineNodeViewModelBase[]
        {
            new TestNode("数据集下载器"),
            new TestNode("图片聚类")
        };
        var logs = new List<string>();

        bool canContinue = PipelineWorkflowValidation.TryValidateSingletonFirstNode(
            nodes,
            node => node.DisplayName == "数据集下载器",
            "数据集下载器",
            logs.Add);

        Assert.True(canContinue);
        Assert.Empty(logs);
    }

    [Fact]
    public void ValidateSingletonFirstNode_ReturnsError_WhenDownloaderIsNotFirst()
    {
        var nodes = new PipelineNodeViewModelBase[]
        {
            new TestNode("图片聚类"),
            new TestNode("数据集下载器")
        };

        string? error = PipelineWorkflowValidation.ValidateSingletonFirstNode(
            nodes,
            node => node.DisplayName == "数据集下载器",
            "数据集下载器");

        Assert.Equal("数据集下载器只能位于工作流的第一个节点。", error);
    }

    [Fact]
    public void ValidateSingletonFirstNode_ReturnsError_WhenDownloaderAppearsMultipleTimes()
    {
        var nodes = new PipelineNodeViewModelBase[]
        {
            new TestNode("数据集下载器"),
            new TestNode("图片聚类"),
            new TestNode("数据集下载器")
        };

        string? error = PipelineWorkflowValidation.ValidateSingletonFirstNode(
            nodes,
            node => node.DisplayName == "数据集下载器",
            "数据集下载器");

        Assert.Equal("数据集下载器在同一个工作流中只能出现一次。", error);
    }

    [Fact]
    public void ValidateSingletonFirstNode_ReturnsNull_WhenDownloaderIsFirstAndUnique()
    {
        var nodes = new PipelineNodeViewModelBase[]
        {
            new TestNode("数据集下载器"),
            new TestNode("图片聚类")
        };

        string? error = PipelineWorkflowValidation.ValidateSingletonFirstNode(
            nodes,
            node => node.DisplayName == "数据集下载器",
            "数据集下载器");

        Assert.Null(error);
    }

    private sealed class TestNode : PipelineNodeViewModelBase
    {
        public TestNode(string displayName)
        {
            DisplayName = displayName;
        }

        public override string DisplayName { get; }

        public override Task RunAsync(PipelineContext context, CancellationToken token) => Task.CompletedTask;

        public override PipelineNodeViewModelBase Clone() => throw new NotSupportedException();
    }
}
