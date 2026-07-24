using CyanTools.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class ImageScalingProcessorTests
{
    [Fact]
    public async Task ProcessFolderAsync_WhenCombinedScalingSkipsSmallImage_CopiesImageAndCompanionText()
    {
        string root = Path.Combine(Path.GetTempPath(), "cyantools-image-scaling-tests", Guid.NewGuid().ToString("N"));
        string input = Path.Combine(root, "input");
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(input);

        try
        {
            string inputImage = Path.Combine(input, "small.png");
            string inputText = Path.Combine(input, "small.txt");
            using (var image = new Image<Rgba32>(64, 64))
            {
                image[0, 0] = new Rgba32(12, 34, 56, 255);
                image.Save(inputImage);
            }

            File.WriteAllText(inputText, "small image prompt");
            byte[] originalBytes = File.ReadAllBytes(inputImage);
            var logs = new List<string>();

            await ImageScalingProcessor.ProcessFolderAsync(
                new ImageScalingOptions
                {
                    InputFolder = input,
                    OutputFolder = output,
                    EnableUpsample = true,
                    UpsampleTargetWidth = 4096,
                    UpsampleTargetHeight = 4096,
                    EnableDownsample = true,
                    DownsampleTargetWidth = 1536,
                    DownsampleTargetHeight = 1536
                },
                progress: null,
                logs.Add);

            string outputImage = Path.Combine(output, "small.png");
            string outputText = Path.Combine(output, "small.txt");
            Assert.True(File.Exists(outputImage));
            Assert.Equal(originalBytes, File.ReadAllBytes(outputImage));
            Assert.Equal("small image prompt", File.ReadAllText(outputText));
            Assert.Contains(logs, message => message.Contains("下采样: 跳过", StringComparison.Ordinal));
            Assert.Contains(logs, message => message.Contains("已原样复制: small.png", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
