using System.Security.Cryptography;
using System.Text;
using CyanTools.Utils;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class SameSourceImageGroupingProcessorTests : IDisposable
{
    private readonly string _root;

    public SameSourceImageGroupingProcessorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CyanToolsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ResolveComputeBackend_AutoFallsBackToCpu_WhenCudaUnavailable()
    {
        string backend = SameSourceImageGroupingProcessor.ResolveComputeBackend("auto");

        Assert.Equal("cpu", backend);
    }

    [Fact]
    public void Process_ExportsReferenceStyleFoldersAndManifest()
    {
        string inputDir = Path.Combine(_root, "input");
        string outputDir = Path.Combine(_root, "output");
        Directory.CreateDirectory(inputDir);

        byte[] parentPixels = BuildStructuredPixels(320, 240);
        SaveRgb(Path.Combine(inputDir, "parent.png"), 320, 240, parentPixels);
        File.WriteAllText(Path.Combine(inputDir, "parent.txt"), "parent tags", Encoding.UTF8);

        byte[] thumbnailPixels = ResizeNearest(parentPixels, 320, 240, 160, 120);
        SaveRgb(Path.Combine(inputDir, "parent_thumbnail.png"), 160, 120, thumbnailPixels);

        SaveRgb(Path.Combine(inputDir, "parent_duplicate.png"), 320, 240, parentPixels);

        byte[] containedPixels = ExtractRegion(parentPixels, 320, 240, 60, 50, 140, 100);
        SaveRgb(Path.Combine(inputDir, "parent_contained.png"), 140, 100, containedPixels);
        File.WriteAllText(Path.Combine(inputDir, "parent_contained.txt"), "contained note", Encoding.UTF8);

        byte[] differencePixels = (byte[])parentPixels.Clone();
        InvertPatch(differencePixels, 320, 240, 40, 40, 32, 24);
        SaveRgb(Path.Combine(inputDir, "parent_difference.png"), 320, 240, differencePixels);

        byte[] otherPixels = BuildStructuredPixels(180, 180, seed: 91);
        SaveRgb(Path.Combine(inputDir, "other.png"), 180, 180, otherPixels);

        SameSourceImageGroupingProcessor.Process(
            inputDir,
            outputDir,
            new SameSourceGroupingOptions { ComputeBackend = "cpu" },
            progress: null,
            logAction: null,
            CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(outputDir, "原图")));
        Assert.True(Directory.Exists(Path.Combine(outputDir, "缩略图")));
        Assert.True(Directory.Exists(Path.Combine(outputDir, "副本")));
        Assert.True(Directory.Exists(Path.Combine(outputDir, "被包含")));
        Assert.True(Directory.Exists(Path.Combine(outputDir, "差分")));
        Assert.True(Directory.Exists(Path.Combine(outputDir, "其他")));

        Assert.True(File.Exists(Path.Combine(outputDir, "原图", "parent.png")));
        Assert.True(File.Exists(Path.Combine(outputDir, "原图", "other.png")));
        Assert.True(File.Exists(Path.Combine(outputDir, "缩略图", "parent_缩略图1.png")));
        Assert.True(File.Exists(Path.Combine(outputDir, "副本", "parent_副本1.png")));
        Assert.True(File.Exists(Path.Combine(outputDir, "被包含", "parent_被包含1.png")));
        Assert.True(File.Exists(Path.Combine(outputDir, "差分", "parent_差分1.png")));
        Assert.False(Directory.EnumerateFiles(Path.Combine(outputDir, "其他")).Any());

        string manifestPath = Path.Combine(outputDir, "manifest.json");
        Assert.True(File.Exists(manifestPath));

        JArray manifest = JArray.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
        Assert.Contains(manifest, row => row["src_path"]?.ToString()?.EndsWith("parent.png", StringComparison.OrdinalIgnoreCase) == true
                                         && row["class"]?.ToString() == "original");
        Assert.Contains(manifest, row => row["src_path"]?.ToString()?.EndsWith("parent_thumbnail.png", StringComparison.OrdinalIgnoreCase) == true
                                         && row["class"]?.ToString() == "thumbnail");
        Assert.Contains(manifest, row => row["src_path"]?.ToString()?.EndsWith("parent_duplicate.png", StringComparison.OrdinalIgnoreCase) == true
                                         && row["class"]?.ToString() == "duplicate");
        Assert.Contains(manifest, row => row["src_path"]?.ToString()?.EndsWith("parent_contained.png", StringComparison.OrdinalIgnoreCase) == true
                                         && row["class"]?.ToString() == "contained");
        Assert.Contains(manifest, row => row["src_path"]?.ToString()?.EndsWith("parent_difference.png", StringComparison.OrdinalIgnoreCase) == true
                                         && row["class"]?.ToString() == "difference");
    }

    [Fact]
    public void Process_CopiesSidecarTxtFilesWithExportedNames()
    {
        string inputDir = Path.Combine(_root, "input-sidecar");
        string outputDir = Path.Combine(_root, "output-sidecar");
        Directory.CreateDirectory(inputDir);

        byte[] parentPixels = BuildStructuredPixels(220, 180);
        SaveRgb(Path.Combine(inputDir, "parent.png"), 220, 180, parentPixels);
        File.WriteAllText(Path.Combine(inputDir, "parent.txt"), "parent note", Encoding.UTF8);

        byte[] childPixels = ExtractRegion(parentPixels, 220, 180, 30, 20, 110, 90);
        SaveRgb(Path.Combine(inputDir, "child.png"), 110, 90, childPixels);
        File.WriteAllText(Path.Combine(inputDir, "child.txt"), "child note", Encoding.UTF8);

        SameSourceImageGroupingProcessor.Process(
            inputDir,
            outputDir,
            new SameSourceGroupingOptions { ComputeBackend = "cpu" },
            progress: null,
            logAction: null,
            CancellationToken.None);

        Assert.Equal("parent note", File.ReadAllText(Path.Combine(outputDir, "原图", "parent.txt"), Encoding.UTF8));
        Assert.Equal("child note", File.ReadAllText(Path.Combine(outputDir, "被包含", "parent_被包含1.txt"), Encoding.UTF8));
    }

    [Fact]
    public void Process_WritesRecordCacheFile()
    {
        string inputDir = Path.Combine(_root, "input-cache");
        string outputDir = Path.Combine(_root, "output-cache");
        Directory.CreateDirectory(inputDir);

        byte[] pixels = BuildStructuredPixels(96, 96);
        SaveRgb(Path.Combine(inputDir, "parent.png"), 96, 96, pixels);

        SameSourceImageGroupingProcessor.Process(
            inputDir,
            outputDir,
            new SameSourceGroupingOptions { ComputeBackend = "cpu" },
            progress: null,
            logAction: null,
            CancellationToken.None);

        string cachePath = Path.Combine(inputDir, ".image_relation_pipeline_cache", "records.json");
        Assert.True(File.Exists(cachePath));

        string expectedSha = ComputePixelSha256(pixels);
        JObject payload = JObject.Parse(File.ReadAllText(cachePath, Encoding.UTF8));
        JObject entry = (JObject)payload["parent.png"]!;

        Assert.Equal(96, entry["width"]!.Value<int>());
        Assert.Equal(96, entry["height"]!.Value<int>());
        Assert.Equal(expectedSha, entry["pixel_sha256"]!.Value<string>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static byte[] BuildStructuredPixels(int width, int height, int seed = 17)
    {
        byte[] pixels = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 3;
                int baseValue = (x * 7 + y * 11 + ((x / 13 + y / 9 + seed) % 2) * 63 + seed * 3) % 256;
                pixels[index] = (byte)baseValue;
                pixels[index + 1] = (byte)((baseValue + x * 3 + seed) % 256);
                pixels[index + 2] = (byte)((baseValue + y * 5 + seed * 2) % 256);
            }
        }

        return pixels;
    }

    private static byte[] ResizeNearest(byte[] sourcePixels, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        byte[] resized = new byte[targetWidth * targetHeight * 3];
        for (int y = 0; y < targetHeight; y++)
        {
            int sourceY = y * sourceHeight / targetHeight;
            for (int x = 0; x < targetWidth; x++)
            {
                int sourceX = x * sourceWidth / targetWidth;
                int sourceIndex = (sourceY * sourceWidth + sourceX) * 3;
                int targetIndex = (y * targetWidth + x) * 3;
                resized[targetIndex] = sourcePixels[sourceIndex];
                resized[targetIndex + 1] = sourcePixels[sourceIndex + 1];
                resized[targetIndex + 2] = sourcePixels[sourceIndex + 2];
            }
        }

        return resized;
    }

    private static byte[] ExtractRegion(byte[] sourcePixels, int sourceWidth, int sourceHeight, int left, int top, int width, int height)
    {
        byte[] region = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourceIndex = ((top + y) * sourceWidth + (left + x)) * 3;
                int targetIndex = (y * width + x) * 3;
                region[targetIndex] = sourcePixels[sourceIndex];
                region[targetIndex + 1] = sourcePixels[sourceIndex + 1];
                region[targetIndex + 2] = sourcePixels[sourceIndex + 2];
            }
        }

        return region;
    }

    private static void InvertPatch(byte[] pixels, int width, int height, int left, int top, int patchWidth, int patchHeight)
    {
        int right = Math.Min(width, left + patchWidth);
        int bottom = Math.Min(height, top + patchHeight);
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int index = (y * width + x) * 3;
                pixels[index] = (byte)(255 - pixels[index]);
                pixels[index + 1] = (byte)(255 - pixels[index + 1]);
                pixels[index + 2] = (byte)(255 - pixels[index + 2]);
            }
        }
    }

    private static void SaveRgb(string path, int width, int height, byte[] pixels)
    {
        using Image<Rgb24> image = Image.LoadPixelData<Rgb24>(pixels, width, height);
        image.Save(path);
    }

    private static string ComputePixelSha256(byte[] pixels)
    {
        byte[] hash = SHA256.HashData(pixels);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
