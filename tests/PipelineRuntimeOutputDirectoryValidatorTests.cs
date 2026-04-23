using CyanTools.Pipeline;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class PipelineRuntimeOutputDirectoryValidatorTests
{
    [Fact]
    public void RunOutputRootSnapshot_ResolvesOnlyOnce_PerRun()
    {
        var snapshot = new PipelineRuntimeOutputDirectoryValidator.RunOutputRootSnapshot();
        int resolveCallCount = 0;

        string first = snapshot.ResolveOnce(() =>
        {
            resolveCallCount++;
            return Path.Combine("root", $"run-{resolveCallCount}");
        });

        string second = snapshot.ResolveOnce(() =>
        {
            resolveCallCount++;
            return Path.Combine("root", $"run-{resolveCallCount}");
        });

        Assert.Equal(Path.GetFullPath(Path.Combine("root", "run-1")), first);
        Assert.Equal(first, second);
        Assert.Equal(1, resolveCallCount);
    }

    [Fact]
    public void EnsureDirectoryHasFiles_Throws_WhenDirectoryDoesNotExist()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        DirectoryNotFoundException ex = Assert.Throws<DirectoryNotFoundException>(() =>
            PipelineRuntimeOutputDirectoryValidator.EnsureDirectoryHasFiles(missingPath, "数据集下载器"));

        Assert.Equal($"数据集下载器输出目录不存在: {Path.GetFullPath(missingPath)}", ex.Message);
    }

    [Fact]
    public void EnsureDirectoryHasFiles_Throws_WhenDirectoryIsEmpty()
    {
        string folder = Directory.CreateTempSubdirectory().FullName;

        try
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                PipelineRuntimeOutputDirectoryValidator.EnsureDirectoryHasFiles(folder, "数据集下载器"));

            Assert.Equal("数据集下载器下载完成但未产生任何文件。", ex.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void EnsureDirectoryHasFiles_DoesNotThrow_WhenDirectoryContainsFiles()
    {
        string folder = Directory.CreateTempSubdirectory().FullName;
        string nested = Directory.CreateDirectory(Path.Combine(folder, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, "sample.txt"), "ok");

        try
        {
            PipelineRuntimeOutputDirectoryValidator.EnsureDirectoryHasFiles(folder, "数据集下载器");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ValidateAndGetPublishPath_ReturnsFullPathOnlyWhenDirectoryHasFiles()
    {
        string folder = Directory.CreateTempSubdirectory().FullName;
        string nested = Directory.CreateDirectory(Path.Combine(folder, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, "sample.txt"), "ok");

        try
        {
            string publishedPath = PipelineRuntimeOutputDirectoryValidator.ValidateAndGetPublishPath(folder, "数据集下载器");

            Assert.Equal(Path.GetFullPath(folder), publishedPath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
