using CyanTools.Utils;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class InputDirectoryCleanupHelperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyanTools_InputCleanupTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ClearContents_RemovesAllInputEntries_WhenNoProtectedOutputIsNested()
    {
        string inputDirectory = CreateDirectory("input");
        CreateFile(Path.Combine(inputDirectory, "source.png"));
        CreateFile(Path.Combine(inputDirectory, "nested", "meta.txt"));

        InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.ClearContents(
            inputDirectory,
            new[] { Path.Combine(_root, "output") });

        Assert.False(result.WasSkipped);
        Assert.Empty(Directory.GetFileSystemEntries(inputDirectory));
    }

    [Fact]
    public void ClearContents_PreservesProtectedNestedOutputDirectory()
    {
        string inputDirectory = CreateDirectory("input");
        string nestedOutputDirectory = CreateDirectory(Path.Combine("input", "processed", "out"));

        CreateFile(Path.Combine(inputDirectory, "source.png"));
        CreateFile(Path.Combine(inputDirectory, "processed", "scratch", "temp.txt"));
        string outputFile = CreateFile(Path.Combine(nestedOutputDirectory, "result.png"));

        InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.ClearContents(
            inputDirectory,
            new[] { nestedOutputDirectory });

        Assert.False(result.WasSkipped);
        Assert.True(File.Exists(outputFile));
        Assert.False(File.Exists(Path.Combine(inputDirectory, "source.png")));
        Assert.False(Directory.Exists(Path.Combine(inputDirectory, "processed", "scratch")));
    }

    [Fact]
    public void ClearContents_Skips_WhenProtectedOutputMatchesInputDirectory()
    {
        string inputDirectory = CreateDirectory("input");
        string sourceFile = CreateFile(Path.Combine(inputDirectory, "source.png"));

        InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.ClearContents(
            inputDirectory,
            new[] { inputDirectory });

        Assert.True(result.WasSkipped);
        Assert.True(File.Exists(sourceFile));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateDirectory(string relativePath)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private string CreateFile(string fullPath)
    {
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, "test");
        return fullPath;
    }
}
