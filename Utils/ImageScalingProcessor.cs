using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageMagick;

namespace CyanTools.Utils
{
    public sealed class ImageScalingOptions
    {
        public string InputFolder { get; set; } = string.Empty;
        public string OutputFolder { get; set; } = string.Empty;

        public bool EnableUpsample { get; set; }
        public int UpsampleTargetWidth { get; set; }
        public int UpsampleTargetHeight { get; set; }

        public bool EnableDownsample { get; set; }
        public int DownsampleTargetWidth { get; set; }
        public int DownsampleTargetHeight { get; set; }
    }

    public static class ImageScalingProcessor
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff"
        };

        public static Task ProcessFolderAsync(ImageScalingOptions options, IProgress<double>? progress, Action<string>? logAction)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return Task.Run(() =>
            {
                var imageFiles = Directory.GetFiles(options.InputFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                    .ToList();

                if (imageFiles.Count == 0)
                {
                    logAction?.Invoke("输入目录中未找到可处理的图片。");
                    progress?.Report(100);
                    return;
                }

                Directory.CreateDirectory(options.OutputFolder);
                logAction?.Invoke($"找到 {imageFiles.Count} 张图片。");

                for (int i = 0; i < imageFiles.Count; i++)
                {
                    var inputFile = imageFiles[i];
                    var fileName = Path.GetFileName(inputFile);

                    try
                    {
                        var outputFile = Path.Combine(options.OutputFolder, fileName);

                        if (options.EnableDownsample)
                        {
                            var sourceInfo = new MagickImageInfo(inputFile);
                            var sourceArea = (long)sourceInfo.Width * sourceInfo.Height;
                            var downsampleTargetArea = (long)options.DownsampleTargetWidth * options.DownsampleTargetHeight;
                            if (sourceArea < downsampleTargetArea)
                            {
                                CopyOriginalFile(inputFile, outputFile);
                                CopyCompanionText(inputFile, options.OutputFolder);
                                logAction?.Invoke($"下采样: 跳过，源图面积 {sourceArea} < 目标面积 {downsampleTargetArea}，已原样复制: {fileName}");
                                progress?.Report((i + 1) * 100.0 / imageFiles.Count);
                                continue;
                            }
                        }

                        using var image = new MagickImage(inputFile);
                        var hasResized = false;

                        if (options.EnableUpsample)
                        {
                            hasResized |= ApplyStage(
                                image,
                                options.UpsampleTargetWidth,
                                options.UpsampleTargetHeight,
                                stageName: "上采样",
                                lobes: 4,
                                blur: 0.98,
                                logAction,
                                skipWhenSourceAreaBelowTarget: false);
                        }

                        if (options.EnableDownsample)
                        {
                            hasResized |= ApplyStage(
                                image,
                                options.DownsampleTargetWidth,
                                options.DownsampleTargetHeight,
                                stageName: "下采样",
                                lobes: 4,
                                blur: 0.98,
                                logAction,
                                skipWhenSourceAreaBelowTarget: true);
                        }

                        if (hasResized)
                        {
                            image.Write(outputFile);
                            logAction?.Invoke($"已输出: {fileName}");
                        }
                        else
                        {
                            CopyOriginalFile(inputFile, outputFile);
                            logAction?.Invoke($"已原样复制: {fileName}");
                        }

                        CopyCompanionText(inputFile, options.OutputFolder);
                    }
                    catch (Exception ex)
                    {
                        logAction?.Invoke($"处理失败: {fileName}，原因: {ex.Message}");
                    }

                    progress?.Report((i + 1) * 100.0 / imageFiles.Count);
                }
            });
        }

        private static bool ApplyStage(
            MagickImage image,
            int baseWidth,
            int baseHeight,
            string stageName,
            int lobes,
            double blur,
            Action<string>? logAction,
            bool skipWhenSourceAreaBelowTarget)
        {
            var sourceWidth = image.Width;
            var sourceHeight = image.Height;
            var sourceArea = (long)sourceWidth * sourceHeight;
            var targetArea = (long)baseWidth * baseHeight;

            if (skipWhenSourceAreaBelowTarget && sourceArea < targetArea)
            {
                logAction?.Invoke($"  {stageName}: 跳过，源图面积 {sourceArea} < 目标面积 {targetArea}");
                return false;
            }

            var (targetWidth, targetHeight) = CalculateTargetSizeByArea(sourceWidth, sourceHeight, targetArea);
            ResizeInLinearSpace(image, targetWidth, targetHeight, lobes, blur);
            logAction?.Invoke($"  {stageName}: {sourceWidth}x{sourceHeight} -> {targetWidth}x{targetHeight}");
            return true;
        }

        private static (int Width, int Height) CalculateTargetSizeByArea(uint sourceWidth, uint sourceHeight, long targetArea)
        {
            var aspect = (double)sourceWidth / sourceHeight;
            var idealWidth = Math.Sqrt(targetArea * aspect);
            var idealHeight = Math.Sqrt(targetArea / aspect);

            var width = NormalizeToNearest64(idealWidth);
            var height = NormalizeToNearest64(idealHeight);
            return (width, height);
        }

        private static void ResizeInLinearSpace(MagickImage image, int width, int height, int lobes, double blur)
        {
            image.ColorSpace = ColorSpace.RGB;
            image.FilterType = FilterType.Lanczos;
            image.Settings.SetDefine(MagickFormat.Unknown, "filter:lobes", lobes.ToString(CultureInfo.InvariantCulture));
            image.Settings.SetDefine(MagickFormat.Unknown, "filter:blur", blur.ToString(CultureInfo.InvariantCulture));

            var geometry = new MagickGeometry((uint)width, (uint)height)
            {
                FillArea = true
            };

            image.Resize(geometry);
            image.Extent((uint)width, (uint)height, Gravity.Center);
            image.ColorSpace = ColorSpace.sRGB;
        }

        private static int NormalizeToNearest64(double value)
        {
            var rounded = (int)Math.Round(value / 64.0, MidpointRounding.AwayFromZero) * 64;
            return Math.Max(64, rounded);
        }

        private static void CopyCompanionText(string sourceImagePath, string outputFolder)
        {
            var sourceTxt = Path.ChangeExtension(sourceImagePath, ".txt");
            if (!File.Exists(sourceTxt))
            {
                return;
            }

            var destTxt = Path.Combine(outputFolder, Path.GetFileName(sourceTxt));
            File.Copy(sourceTxt, destTxt, true);
        }

        private static void CopyOriginalFile(string sourceImagePath, string outputImagePath)
        {
            File.Copy(sourceImagePath, outputImagePath, true);
        }
    }
}
