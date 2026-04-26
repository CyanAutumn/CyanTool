using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CyanTools.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class WorkflowNodeExecutionHelperTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "CyanToolsWorkflowNodeExecutionHelperTests", Path.GetRandomFileName());

    [Fact]
    public async Task ResolutionFilterPipelineProcessor_DeletesImagesBelowThresholdAndMatchingTxt()
    {
        string inputPath = CreateDirectory("resolution-filter");
        string smallPath = Path.Combine(inputPath, "small.png");
        string largePath = Path.Combine(inputPath, "large.png");
        string smallTxtPath = Path.ChangeExtension(smallPath, ".txt");

        CreateImage(smallPath, 32, 32);
        CreateImage(largePath, 256, 256);
        await File.WriteAllTextAsync(smallTxtPath, "caption");

        int deletedCount = await ResolutionFilterPipelineProcessor.ExecuteAsync(
            inputPath,
            new ResolutionFilterPipelineOptions
            {
                IsWidthFilterEnabled = true,
                MinWidth = 64
            },
            _ => { },
            CancellationToken.None);

        Assert.Equal(1, deletedCount);
        Assert.False(File.Exists(smallPath));
        Assert.False(File.Exists(smallTxtPath));
        Assert.True(File.Exists(largePath));
    }

    [Fact]
    public async Task KeepFilesPipelineProcessor_DeletesUnselectedFilesAndChineseTxt()
    {
        string inputPath = CreateDirectory("keep-files");
        string keepImagePath = Path.Combine(inputPath, "keep.png");
        string deleteImagePath = Path.Combine(inputPath, "delete.gif");
        string deleteChineseTxtPath = Path.Combine(inputPath, "caption.txt");

        CreateImage(keepImagePath, 32, 32);
        await File.WriteAllTextAsync(deleteImagePath, "gif");
        await File.WriteAllTextAsync(deleteChineseTxtPath, "测试");

        int deletedCount = await KeepFilesPipelineProcessor.ExecuteAsync(
            inputPath,
            new KeepFilesOptions
            {
                KeepPng = true,
                KeepTxt = true,
                KeepChineseTxt = false,
                KeepJpg = false,
                KeepWebp = false,
                KeepBmp = false,
                KeepJpeg = false,
                KeepNpz = false
            },
            _ => { },
            CancellationToken.None);

        Assert.Equal(2, deletedCount);
        Assert.True(File.Exists(keepImagePath));
        Assert.False(File.Exists(deleteImagePath));
        Assert.False(File.Exists(deleteChineseTxtPath));
    }

    [Fact]
    public async Task TrainingSetBalancerPipelineProcessor_BalancesAndRenamesFolders()
    {
        string inputPath = CreateDirectory("training-set-balancer");
        string fooPath = Directory.CreateDirectory(Path.Combine(inputPath, "foo")).FullName;
        string barPath = Directory.CreateDirectory(Path.Combine(inputPath, "5_bar")).FullName;

        CreateImage(Path.Combine(fooPath, "a.png"), 32, 32);
        CreateImage(Path.Combine(barPath, "a.png"), 32, 32);
        CreateImage(Path.Combine(barPath, "b.png"), 32, 32);

        int renamedCount = await TrainingSetBalancerPipelineProcessor.ExecuteAsync(inputPath, _ => { }, CancellationToken.None);

        Assert.Equal(2, renamedCount);
        Assert.True(Directory.Exists(Path.Combine(inputPath, "2_foo")));
        Assert.True(Directory.Exists(Path.Combine(inputPath, "1_bar")));
    }

    [Fact]
    public async Task ImageRenamePipelineProcessor_RenamesTopLevelImagesSequentially()
    {
        string inputPath = CreateDirectory("image-rename");
        CreateImage(Path.Combine(inputPath, "b.png"), 32, 32);
        CreateImage(Path.Combine(inputPath, "a.jpg"), 32, 32);
        string nestedPath = Directory.CreateDirectory(Path.Combine(inputPath, "nested")).FullName;
        CreateImage(Path.Combine(nestedPath, "nested.png"), 32, 32);

        int renamedCount = await ImageRenamePipelineProcessor.ExecuteAsync(
            inputPath,
            new ImageRenamePipelineOptions
            {
                NamePrefix = "image_",
                StartIndex = 1,
                NumberPadding = 4
            },
            _ => { },
            CancellationToken.None);

        Assert.Equal(2, renamedCount);
        Assert.True(File.Exists(Path.Combine(inputPath, "image_0001.jpg")));
        Assert.True(File.Exists(Path.Combine(inputPath, "image_0002.png")));
        Assert.True(File.Exists(Path.Combine(nestedPath, "nested.png")));
    }

    [Fact]
    public async Task FolderProcessingPipelineProcessor_FlattensNestedFilesAndDeletesEmptyDirectories()
    {
        string inputPath = CreateDirectory("folder-processing-flatten");
        string nestedPath = Directory.CreateDirectory(Path.Combine(inputPath, "group", "leaf")).FullName;
        await File.WriteAllTextAsync(Path.Combine(nestedPath, "inner.txt"), "content");

        await FolderProcessingPipelineProcessor.ExecuteAsync(
            inputPath,
            new FolderProcessingOptions
            {
                Operation = FolderProcessingOperation.MoveFilesToTopLevel
            },
            _ => { },
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(inputPath, "inner.txt")));
        Assert.False(Directory.Exists(Path.Combine(inputPath, "group")));
    }

    [Fact]
    public async Task FolderProcessingPipelineProcessor_DeletesEmptyDirectoriesRecursively()
    {
        string inputPath = CreateDirectory("folder-processing-empty");
        Directory.CreateDirectory(Path.Combine(inputPath, "empty", "leaf"));
        string keepPath = Directory.CreateDirectory(Path.Combine(inputPath, "keep", "child")).FullName;
        await File.WriteAllTextAsync(Path.Combine(keepPath, "file.txt"), "content");

        await FolderProcessingPipelineProcessor.ExecuteAsync(
            inputPath,
            new FolderProcessingOptions
            {
                Operation = FolderProcessingOperation.DeleteEmptyDirectories
            },
            _ => { },
            CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(inputPath, "empty")));
        Assert.True(Directory.Exists(Path.Combine(inputPath, "keep")));
    }

    [Fact]
    public async Task FolderProcessingPipelineProcessor_RetainsKeywordDirectoriesAndAncestors()
    {
        string inputPath = CreateDirectory("folder-processing-keyword");
        Directory.CreateDirectory(Path.Combine(inputPath, "drop-me"));
        Directory.CreateDirectory(Path.Combine(inputPath, "parent", "keep-cat"));
        await File.WriteAllTextAsync(Path.Combine(inputPath, "root-note.txt"), "stay");

        await FolderProcessingPipelineProcessor.ExecuteAsync(
            inputPath,
            new FolderProcessingOptions
            {
                Operation = FolderProcessingOperation.RetainDirectoriesByKeyword,
                ParameterText = "cat"
            },
            _ => { },
            CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(inputPath, "parent")));
        Assert.True(Directory.Exists(Path.Combine(inputPath, "parent", "keep-cat")));
        Assert.False(Directory.Exists(Path.Combine(inputPath, "drop-me")));
        Assert.True(File.Exists(Path.Combine(inputPath, "root-note.txt")));
    }

    [Fact]
    public async Task FolderProcessingPipelineProcessor_ExtractsArchivesIntoSameNameFolderAndKeepsArchive()
    {
        string inputPath = CreateDirectory("folder-processing-extract");
        string archivePath = Path.Combine(inputPath, "sample.zip");
        CreateZipArchive(archivePath, ("nested/file.txt", "hello"));

        await FolderProcessingPipelineProcessor.ExecuteAsync(
            inputPath,
            new FolderProcessingOptions
            {
                Operation = FolderProcessingOperation.ExtractArchives
            },
            _ => { },
            CancellationToken.None);

        Assert.True(File.Exists(archivePath));
        Assert.True(File.Exists(Path.Combine(inputPath, "sample", "nested", "file.txt")));
    }

    [Fact]
    public async Task TaggingPipelineExecutor_UsesProvidedOptionsAndReportsProgress()
    {
        var processor = new FakeTaggingPipelineProcessor();
        var progressValues = new List<double>();

        await TaggingPipelineExecutor.ExecuteAsync(
            processor,
            new TaggingPipelineExecutionOptions
            {
                InputDirectory = "input",
                ModelUrl = "model-url",
                TagsUrl = "tags-url",
                DisplayName = "model-name",
                PreferGpu = true,
                GeneralThreshold = 0.35f,
                CharacterThreshold = 0.85f,
                InputSize = 448,
                InputFormat = "NHWC",
                CategoryOptions = new TagCategoryOptions
                {
                    IncludeGeneral = true,
                    IncludeCharacter = true
                },
                PriorityTags = new[] { "best quality", "solo" },
                CustomTags = new[] { "artist foo" },
                ExistingTxtHandlingMode = ExistingTxtHandlingMode.MergeUnique
            },
            _ => { },
            progressValues.Add,
            CancellationToken.None);

        Assert.True(processor.InitializeCalled);
        Assert.True(processor.ProcessCalled);
        Assert.Equal("input", processor.SeenInputDirectory);
        Assert.Equal(new[] { "best quality", "solo" }, processor.SeenPriorityTags);
        Assert.Equal(new[] { "artist foo" }, processor.SeenCustomTags);
        Assert.Contains(50d, progressValues);
        Assert.Contains(100d, progressValues);
    }

    [Fact]
    public void VideoFrameWorkflowPlanner_UsesConfiguredVideoPathBeforeInputDirectory()
    {
        string inputPath = CreateDirectory("video-frame-input");
        string configuredVideoPath = Path.Combine(_rootPath, "configured.mp4");
        string discoveredVideoPath = Path.Combine(inputPath, "discovered.mkv");
        File.WriteAllText(configuredVideoPath, string.Empty);
        File.WriteAllText(discoveredVideoPath, string.Empty);

        IReadOnlyList<string> resolved = VideoFrameWorkflowPlanner.ResolveInputVideos(configuredVideoPath, inputPath);

        Assert.Equal(new[] { Path.GetFullPath(configuredVideoPath) }, resolved);
    }

    [Fact]
    public void VideoFrameWorkflowPlanner_FallsBackToVideosInInputDirectory()
    {
        string inputPath = CreateDirectory("video-frame-fallback");
        string firstVideoPath = Path.Combine(inputPath, "a.mp4");
        string secondVideoPath = Path.Combine(inputPath, "b.avi");
        File.WriteAllText(firstVideoPath, string.Empty);
        File.WriteAllText(secondVideoPath, string.Empty);
        File.WriteAllText(Path.Combine(inputPath, "note.txt"), string.Empty);

        IReadOnlyList<string> resolved = VideoFrameWorkflowPlanner.ResolveInputVideos(null, inputPath);

        Assert.Equal(
            new[]
            {
                Path.GetFullPath(firstVideoPath),
                Path.GetFullPath(secondVideoPath)
            },
            resolved);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private string CreateDirectory(string name)
    {
        string path = Path.Combine(_rootPath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateImage(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.Save(path);
    }

    private static void CreateZipArchive(string archivePath, params (string EntryPath, string Content)[] entries)
    {
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach ((string entryPath, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryPath);
            using StreamWriter writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    private sealed class FakeTaggingPipelineProcessor : ITaggingPipelineProcessor
    {
        public bool InitializeCalled { get; private set; }

        public bool ProcessCalled { get; private set; }

        public string SeenInputDirectory { get; private set; } = string.Empty;

        public IReadOnlyList<string> SeenPriorityTags { get; private set; } = [];

        public IReadOnlyList<string> SeenCustomTags { get; private set; } = [];

        public Task InitializeAsync(string modelUrl, string tagsUrl, string displayName, bool preferGpu, IProgress<string> progress, CancellationToken token)
        {
            InitializeCalled = true;
            progress.Report("Processed [1/2]");
            return Task.CompletedTask;
        }

        public Task ProcessDirectoryAsync(
            string inputDirectory,
            float generalThreshold,
            float characterThreshold,
            int inputSize,
            string inputFormat,
            TagCategoryOptions categoryOptions,
            IReadOnlyList<string> priorityTags,
            IReadOnlyList<string> customTags,
            ExistingTxtHandlingMode existingTxtHandlingMode,
            IProgress<string> progress,
            CancellationToken token)
        {
            ProcessCalled = true;
            SeenInputDirectory = inputDirectory;
            SeenPriorityTags = priorityTags;
            SeenCustomTags = customTags;
            progress.Report("Processed [2/2]");
            return Task.CompletedTask;
        }
    }
}
