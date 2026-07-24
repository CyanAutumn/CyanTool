using CyanTools.Pipeline;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class PipelineWorkflowRunnerTests
{
    [Fact]
    public async Task RunAsync_AppliesEffectivePathsBeforeExecutingNode()
    {
        var first = new RecordingDirectoryNode("图片聚类", PipelineDirectoryBehavior.GeneratedOutput)
        {
            PipelineInputPath = Path.Combine("aaa", "bbb")
        };
        var second = new RecordingDirectoryNode("图片超分", PipelineDirectoryBehavior.GeneratedOutput);

        var logs = new List<string>();
        var runner = new PipelineRunner();
        var context = new PipelineContext(logs.Add);

        await runner.RunAsync(new PipelineNodeViewModelBase[] { first, second }, context, CancellationToken.None);

        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "bbb")), first.SeenInputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "01-图片聚类")), first.SeenOutputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "01-图片聚类")), second.SeenInputPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "02-图片超分")), second.SeenOutputPath);
    }

    [Fact]
    public async Task RunAsync_UsesRuntimeOutputPathFromCompletedNodeForNextInput()
    {
        var first = new RecordingRuntimeOutputNode("数据集下载器", Path.Combine("aaa", "dataset_root"));
        var second = new RecordingDirectoryNode("图片聚类", PipelineDirectoryBehavior.GeneratedOutput);

        var runner = new PipelineRunner();
        var context = new PipelineContext(_ => { });

        await runner.RunAsync(new PipelineNodeViewModelBase[] { first, second }, context, CancellationToken.None);

        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "dataset_root")), second.SeenInputPath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine("aaa", "02-图片聚类")),
            second.SeenOutputPath);
    }

    [Fact]
    public async Task RunAsync_AppliesOutputFolderSelectorBeforeRunningThePrecedingNode()
    {
        var processor = new RecordingDirectoryNode("图片聚类", PipelineDirectoryBehavior.GeneratedOutput)
        {
            PipelineInputPath = Path.Combine("aaa", "bbb")
        };
        var outputSelector = new TestOutputFolderSelectorNode(Path.Combine("target", "final-output"));
        var runner = new PipelineRunner();
        var context = new PipelineContext(_ => { });

        await runner.RunAsync(new PipelineNodeViewModelBase[] { processor, outputSelector }, context, CancellationToken.None);

        Assert.Equal(Path.GetFullPath(Path.Combine("target", "final-output")), processor.SeenOutputPath);
    }

    [Fact]
    public async Task RunAsync_UsesRuntimeOutputPathFromWrappedInnerViewModelForNextInput()
    {
        var first = new RuntimeOutputSimpleNode("数据集下载器");
        first.Inner.Publish(Path.Combine("aaa", "wrapped_dataset_root"));
        var second = new RecordingDirectoryNode("图片聚类", PipelineDirectoryBehavior.GeneratedOutput);

        var runner = new PipelineRunner();
        var context = new PipelineContext(_ => { });

        await runner.RunAsync(new PipelineNodeViewModelBase[] { first, second }, context, CancellationToken.None);

        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "wrapped_dataset_root")), second.SeenInputPath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine("aaa", "02-图片聚类")),
            second.SeenOutputPath);
    }

    [Fact]
    public async Task RunAsync_ResetsRuntimeOutputPathBeforeEachExecution()
    {
        var first = new ResettableRuntimeOutputNode("数据集下载器");
        var second = new RecordingDirectoryNode("图片聚类", PipelineDirectoryBehavior.GeneratedOutput);
        var runner = new PipelineRunner();
        var context = new PipelineContext(_ => { });

        first.Publish(Path.Combine("aaa", "dataset_root"));
        await runner.RunAsync(new PipelineNodeViewModelBase[] { first, second }, context, CancellationToken.None);

        Assert.Equal(Path.GetFullPath(Path.Combine("aaa", "dataset_root")), second.SeenInputPath);

        var secondRunObserver = new RecordingDirectoryNode("图片聚类", PipelineDirectoryBehavior.GeneratedOutput);
        await runner.RunAsync(new PipelineNodeViewModelBase[] { first, secondRunObserver }, context, CancellationToken.None);

        Assert.True(string.IsNullOrEmpty(secondRunObserver.SeenInputPath));
        Assert.True(string.IsNullOrEmpty(secondRunObserver.SeenOutputPath));
    }

    private sealed class RecordingDirectoryNode : PipelineNodeViewModelBase, IPipelineDirectoryNode
    {
        public RecordingDirectoryNode(string displayName, PipelineDirectoryBehavior directoryBehavior)
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

        public string? SeenInputPath { get; private set; }

        public string? SeenOutputPath { get; private set; }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            SeenInputPath = PipelineInputPath;
            SeenOutputPath = PipelineOutputPath;
            return Task.CompletedTask;
        }

        public override PipelineNodeViewModelBase Clone() => throw new NotSupportedException();
    }

    private sealed class RecordingRuntimeOutputNode : PipelineNodeViewModelBase, IPipelineRuntimeOutputNode
    {
        private readonly string _runtimeOutputPath;

        public RecordingRuntimeOutputNode(string displayName, string runtimeOutputPath)
        {
            DisplayName = displayName;
            _runtimeOutputPath = Path.GetFullPath(runtimeOutputPath);
        }

        public override string DisplayName { get; }

        public void ResetRuntimeOutputPath() { }

        public bool TryGetRuntimeOutputPath(out string path)
        {
            path = _runtimeOutputPath;
            return true;
        }

        public override Task RunAsync(PipelineContext context, CancellationToken token) => Task.CompletedTask;

        public override PipelineNodeViewModelBase Clone() => throw new NotSupportedException();
    }

    private sealed class RuntimeOutputSimpleNode : SimplePipelineNode<RuntimeOutputInnerViewModel>
    {
        public RuntimeOutputSimpleNode(string displayName) : base(displayName)
        {
        }

        public override PipelineNodeViewModelBase Clone() => throw new NotSupportedException();
    }

    private sealed class RuntimeOutputInnerViewModel : IPipelineRunnableViewModel, IPipelineRuntimeOutputNode
    {
        private string? _nextPublishedPath;
        private string? _runtimeOutputPath;

        public void Publish(string runtimeOutputPath)
        {
            _nextPublishedPath = Path.GetFullPath(runtimeOutputPath);
        }

        public void ResetRuntimeOutputPath()
        {
            _runtimeOutputPath = null;
        }

        public bool TryGetRuntimeOutputPath(out string path)
        {
            path = _runtimeOutputPath ?? string.Empty;
            return !string.IsNullOrWhiteSpace(_runtimeOutputPath);
        }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            _runtimeOutputPath = _nextPublishedPath;
            _nextPublishedPath = null;
            return Task.CompletedTask;
        }
    }

    private sealed class ResettableRuntimeOutputNode : PipelineNodeViewModelBase, IPipelineRuntimeOutputNode
    {
        private string? _nextPublishedPath;
        private string? _runtimeOutputPath;

        public ResettableRuntimeOutputNode(string displayName)
        {
            DisplayName = displayName;
        }

        public override string DisplayName { get; }

        public void Publish(string runtimeOutputPath)
        {
            _nextPublishedPath = Path.GetFullPath(runtimeOutputPath);
        }

        public void ResetRuntimeOutputPath()
        {
            _runtimeOutputPath = null;
        }

        public bool TryGetRuntimeOutputPath(out string path)
        {
            path = _runtimeOutputPath ?? string.Empty;
            return !string.IsNullOrWhiteSpace(_runtimeOutputPath);
        }

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            _runtimeOutputPath = _nextPublishedPath;
            _nextPublishedPath = null;
            return Task.CompletedTask;
        }

        public override PipelineNodeViewModelBase Clone() => throw new NotSupportedException();
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
