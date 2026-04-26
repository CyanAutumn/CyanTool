using CyanTools.Pipeline;
using CyanTools.Utils;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class FolderProcessingPipelineIntegrationTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "CyanToolsFolderProcessingPipelineIntegrationTests", Path.GetRandomFileName());

    [Fact]
    public async Task RunAsync_ExecutesFolderProcessingNodeInPlace()
    {
        string inputPath = Directory.CreateDirectory(Path.Combine(_rootPath, "input")).FullName;
        string nestedPath = Directory.CreateDirectory(Path.Combine(inputPath, "nested")).FullName;
        await File.WriteAllTextAsync(Path.Combine(nestedPath, "inner.txt"), "content");

        var node = new FolderProcessingSimpleNode();
        node.Inner.InputPath = inputPath;
        node.Inner.SelectedOperation = FolderProcessingOperation.MoveFilesToTopLevel;

        var runner = new PipelineRunner();
        await runner.RunAsync(new PipelineNodeViewModelBase[] { node }, new PipelineContext(_ => { }), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(inputPath, "inner.txt")));
        Assert.Equal(Path.GetFullPath(inputPath), node.Inner.PipelineOutputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private sealed class FolderProcessingSimpleNode : SimplePipelineNode<TestFolderProcessingInnerViewModel>
    {
        public FolderProcessingSimpleNode() : base("文件夹处理")
        {
        }

        public override PipelineNodeViewModelBase Clone() => throw new NotSupportedException();
    }

    private sealed class TestFolderProcessingInnerViewModel : IPipelineRunnableViewModel, IPipelineDirectoryNode
    {
        public string InputPath { get; set; } = string.Empty;

        public FolderProcessingOperation SelectedOperation { get; set; }

        public string ParameterText { get; set; } = string.Empty;

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.InPlace;

        public string PipelineInputPath
        {
            get => InputPath;
            set => InputPath = value ?? string.Empty;
        }

        public string PipelineOutputPath
        {
            get => InputPath;
            set => InputPath = value ?? string.Empty;
        }

        public bool IsWorkflowMode { get; set; }

        public bool HasInheritedInputPath { get; set; }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            return FolderProcessingPipelineProcessor.ExecuteAsync(
                InputPath,
                new FolderProcessingOptions
                {
                    Operation = SelectedOperation,
                    ParameterText = ParameterText
                },
                context.AppendLog,
                token);
        }
    }
}
