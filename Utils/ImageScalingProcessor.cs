using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
        private const int MaxParallelUpperBound = 8;
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
                int maxParallelism = Math.Clamp(Environment.ProcessorCount, 1, Math.Min(MaxParallelUpperBound, imageFiles.Count));
                ConfigureMagickAcceleration(maxParallelism, logAction);
                logAction?.Invoke($"启用并行处理: {maxParallelism} 路。");

                int completed = 0;
                Parallel.ForEach(
                    imageFiles,
                    new ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
                    inputFile =>
                    {
                        ProcessSingleFile(inputFile, options, logAction);
                        int done = Interlocked.Increment(ref completed);
                        progress?.Report(done * 100.0 / imageFiles.Count);
                    });
            });
        }

        private static void ProcessSingleFile(string inputFile, ImageScalingOptions options, Action<string>? logAction)
        {
            var fileName = Path.GetFileName(inputFile);
            try
            {
                var outputFile = Path.Combine(options.OutputFolder, fileName);
                var sourceInfo = new MagickImageInfo(inputFile);
                var sourceArea = (long)sourceInfo.Width * sourceInfo.Height;

                if (options.EnableUpsample)
                {
                    var upsampleTargetArea = (long)options.UpsampleTargetWidth * options.UpsampleTargetHeight;
                    if (sourceArea > upsampleTargetArea)
                    {
                        logAction?.Invoke(
                            $"上采样: 跳过，源图 {sourceInfo.Width}x{sourceInfo.Height}（面积 {sourceArea}） > 目标 {options.UpsampleTargetWidth}x{options.UpsampleTargetHeight}（面积 {upsampleTargetArea}）: {fileName}");
                        CopyOriginalFile(inputFile, outputFile);
                        CopyCompanionText(inputFile, options.OutputFolder);
                        logAction?.Invoke($"已原样复制: {fileName}");
                        return;
                    }
                }

                if (options.EnableDownsample)
                {
                    var downsampleTargetArea = (long)options.DownsampleTargetWidth * options.DownsampleTargetHeight;
                    if (sourceArea < downsampleTargetArea)
                    {
                        logAction?.Invoke(
                            $"下采样: 跳过，源图 {sourceInfo.Width}x{sourceInfo.Height}（面积 {sourceArea}） < 目标 {options.DownsampleTargetWidth}x{options.DownsampleTargetHeight}（面积 {downsampleTargetArea}）: {fileName}");
                        CopyOriginalFile(inputFile, outputFile);
                        CopyCompanionText(inputFile, options.OutputFolder);
                        logAction?.Invoke($"已原样复制: {fileName}");
                        return;
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
                        logAction);
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
                        logAction);
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
        }

        private static bool ApplyStage(
            MagickImage image,
            int baseWidth,
            int baseHeight,
            string stageName,
            int lobes,
            double blur,
            Action<string>? logAction)
        {
            var sourceWidth = image.Width;
            var sourceHeight = image.Height;
            var targetArea = (long)baseWidth * baseHeight;

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

        private static void ConfigureMagickAcceleration(int maxParallelism, Action<string>? logAction)
        {
            TrySetMagickResourceThreadLimit(1, logAction);
            TryEnableOpenCl(logAction);
        }

        private static void TryEnableOpenCl(Action<string>? logAction)
        {
            try
            {
                var assembly = typeof(MagickImage).Assembly;
                var magickNetType = assembly.GetType("ImageMagick.MagickNET");
                var openCl = magickNetType?.GetProperty("OpenCL", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (openCl == null)
                {
                    logAction?.Invoke("Magick.NET OpenCL: 当前版本未暴露可用入口，继续使用 CPU。");
                    return;
                }

                var isEnabledProperty = openCl.GetType().GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance);
                if (isEnabledProperty == null || !isEnabledProperty.CanWrite)
                {
                    logAction?.Invoke("Magick.NET OpenCL: 当前版本不支持启用开关，继续使用 CPU。");
                    return;
                }

                isEnabledProperty.SetValue(openCl, true);
                bool enabled = isEnabledProperty.GetValue(openCl) as bool? ?? false;
                logAction?.Invoke(enabled
                    ? "Magick.NET OpenCL: 已尝试启用。"
                    : "Magick.NET OpenCL: 启用失败，继续使用 CPU。");
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Magick.NET OpenCL: 不可用，继续使用 CPU。原因: {ex.Message}");
            }
        }

        private static void TrySetMagickResourceThreadLimit(int threadLimit, Action<string>? logAction)
        {
            try
            {
                var assembly = typeof(MagickImage).Assembly;
                var magickNetType = assembly.GetType("ImageMagick.MagickNET");
                var resourceLimits = magickNetType?.GetProperty("ResourceLimits", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (resourceLimits == null)
                {
                    return;
                }

                var threadProperty = resourceLimits.GetType().GetProperty("Thread", BindingFlags.Public | BindingFlags.Instance);
                if (threadProperty == null || !threadProperty.CanWrite)
                {
                    return;
                }

                object convertedValue = Convert.ChangeType(threadLimit, threadProperty.PropertyType, CultureInfo.InvariantCulture);
                threadProperty.SetValue(resourceLimits, convertedValue);
                logAction?.Invoke($"Magick.NET 内部线程限制: {threadLimit}。");
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Magick.NET 内部线程限制设置失败，继续使用默认值。原因: {ex.Message}");
            }
        }
    }
}
