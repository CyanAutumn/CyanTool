using CyanTools.Pipeline;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class PipelineWorkflowPathPlannerTests
{
    [Fact]
    public void BuildGeneratedOutputPath_UsesParentAndOrderedNodeName()
    {
        var inputPath = Path.Combine("aaa", "bbb");

        var result = PipelineWorkflowPathPlanner.BuildGeneratedOutputPath(inputPath, "图片聚类", 1);

        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "01-图片聚类")), result);
    }

    [Fact]
    public void ApplyWorkflowPaths_ChainsGeneratedOutputToNextNodeInput()
    {
        var first = new TestDirectoryNode("图片聚类", PipelineDirectoryBehavior.GeneratedOutput)
        {
            PipelineInputPath = Path.Combine("aaa", "bbb")
        };
        var second = new TestDirectoryNode("图片超分", PipelineDirectoryBehavior.GeneratedOutput)
        {
            PipelineInputPath = Path.Combine("manual", "input"),
            PipelineOutputPath = Path.Combine("manual", "output")
        };

        PipelineWorkflowPathPlanner.ApplyWorkflowPaths(new PipelineNodeViewModelBase[] { first, second });

        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "bbb")), first.PipelineInputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "01-图片聚类")), first.PipelineOutputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "01-图片聚类")), second.PipelineInputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "02-图片超分")), second.PipelineOutputPath);
    }

    [Fact]
    public void ApplyWorkflowPaths_UsesInputAsOutput_ForInPlaceNode()
    {
        var first = new TestDirectoryNode("图片打标", PipelineDirectoryBehavior.InPlace)
        {
            PipelineInputPath = Path.Combine("aaa", "bbb")
        };
        var second = new TestDirectoryNode("图片聚类", PipelineDirectoryBehavior.GeneratedOutput);

        PipelineWorkflowPathPlanner.ApplyWorkflowPaths(new PipelineNodeViewModelBase[] { first, second });

        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "bbb")), first.PipelineInputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "bbb")), first.PipelineOutputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "bbb")), second.PipelineInputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "02-图片聚类")), second.PipelineOutputPath);
    }

    [Fact]
    public void ApplyWorkflowPaths_LocksOnlyNodesWithInheritedInput()
    {
        var first = new TestDirectoryNode("图片聚类", PipelineDirectoryBehavior.GeneratedOutput)
        {
            PipelineInputPath = Path.Combine("aaa", "bbb")
        };
        var second = new TestDirectoryNode("图片超分", PipelineDirectoryBehavior.GeneratedOutput);

        PipelineWorkflowPathPlanner.ApplyWorkflowPaths(new PipelineNodeViewModelBase[] { first, second });

        Assert.False(first.HasInheritedInputPath);
        Assert.True(second.HasInheritedInputPath);
    }

    [Fact]
    public void ApplyWorkflowPaths_FallsBackToPreviousInputForInPlaceNode()
    {
        var first = new TestDirectoryNode("图片打标", PipelineDirectoryBehavior.InPlace)
        {
            PipelineInputPath = Path.Combine("aaa", "bbb")
        };
        var second = new TestDirectoryNode("图片超分", PipelineDirectoryBehavior.GeneratedOutput);

        PipelineWorkflowPathPlanner.ApplyWorkflowPaths(new PipelineNodeViewModelBase[] { first, second });

        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "bbb")), first.PipelineOutputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "bbb")), second.PipelineInputPath);
        Assert.True(second.HasInheritedInputPath);
    }

    [Fact]
    public void ApplyWorkflowPaths_UsesKnownRuntimeOutputPathForNextNodeInput()
    {
        var first = new TestRuntimeOutputNode("Pick", Path.Combine("aaa", "bbb"));
        var second = new TestDirectoryNode("Process", PipelineDirectoryBehavior.GeneratedOutput)
        {
            PipelineInputPath = Path.Combine("manual", "input")
        };

        PipelineWorkflowPathPlanner.ApplyWorkflowPaths(new PipelineNodeViewModelBase[] { first, second });

        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "bbb")), second.PipelineInputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "02-Process")), second.PipelineOutputPath);
        Assert.True(second.HasInheritedInputPath);
    }

    [Fact]
    public void ApplyWorkflowPaths_RedirectsGeneratedOutputToFollowingOutputFolderSelector()
    {
        var processor = new TestDirectoryNode("图片聚类", PipelineDirectoryBehavior.GeneratedOutput)
        {
            PipelineInputPath = Path.Combine("aaa", "bbb")
        };
        var outputSelector = new TestOutputFolderSelectorNode(Path.Combine("target", "final-output"));

        PipelineWorkflowPathPlanner.ApplyWorkflowPaths(new PipelineNodeViewModelBase[] { processor, outputSelector });

        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "bbb")), processor.PipelineInputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("target", "final-output")), processor.PipelineOutputPath);
    }

    private sealed class TestDirectoryNode : PipelineNodeViewModelBase, IPipelineDirectoryNode
    {
        public TestDirectoryNode(string displayName, PipelineDirectoryBehavior directoryBehavior)
        {
            DisplayName = displayName;
            DirectoryBehavior = directoryBehavior;
        }

        public override string DisplayName { get; }

        public PipelineDirectoryBehavior DirectoryBehavior { get; }

        public string PipelineInputPath { get; set; } = string.Empty;

        public string PipelineOutputPath { get; set; } = string.Empty;

        public bool IsWorkflowMode { get; set; }

        public bool HasInheritedInputPath { get; set; }

        public override Task RunAsync(PipelineContext context, CancellationToken token) => Task.CompletedTask;

        public override PipelineNodeViewModelBase Clone() => throw new NotSupportedException();
    }

    private sealed class TestRuntimeOutputNode : PipelineNodeViewModelBase, IPipelineRuntimeOutputNode
    {
        private readonly string _runtimeOutputPath;

        public TestRuntimeOutputNode(string displayName, string runtimeOutputPath)
        {
            DisplayName = displayName;
            _runtimeOutputPath = runtimeOutputPath;
        }

        public override string DisplayName { get; }

        public override Task RunAsync(PipelineContext context, CancellationToken token) => Task.CompletedTask;

        public override PipelineNodeViewModelBase Clone() => throw new NotSupportedException();

        public void ResetRuntimeOutputPath()
        {
        }

        public bool TryGetRuntimeOutputPath(out string path)
        {
            path = _runtimeOutputPath;
            return !string.IsNullOrWhiteSpace(path);
        }
    }

    private sealed class TestOutputFolderSelectorNode : PipelineNodeViewModelBase, IPipelineOutputPathSelectorNode
    {
        private readonly string _outputFolderPath;

        public TestOutputFolderSelectorNode(string outputFolderPath)
        {
            _outputFolderPath = outputFolderPath;
        }

        public override string DisplayName => "选择输出文件夹";

        public bool TryGetOutputFolderPath(out string path)
        {
            path = Path.GetFullPath(_outputFolderPath);
            return true;
        }

        public override Task RunAsync(PipelineContext context, CancellationToken token) => Task.CompletedTask;

        public override PipelineNodeViewModelBase Clone() => throw new NotSupportedException();
    }
}
