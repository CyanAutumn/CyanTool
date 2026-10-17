using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace CyanTools.Utils
{
    public static class ImageFormatConverter
    {
        private const int MaxParallelUpperBound = 8;
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".psd"
        };

        public static async Task ConvertAsync(string inputPath, string outputPath, string targetFormat, IProgress<double> progress,
            bool clearConvertedSources = false, CancellationToken token = default)
        {
            inputPath = Path.GetFullPath(inputPath);
            outputPath = Path.GetFullPath(outputPath);
            _ = GetEncoder(targetFormat);
            var files = Directory.GetFiles(inputPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                progress.Report(100);
                return;
            }



            var duplicateTarget = files.GroupBy(file => GetOutputFilePath(file, outputPath, targetFormat), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Skip(1).Any());
            if (duplicateTarget != null)
                throw new IOException($"多个源图片映射到同一输出文件，请先重命名：{duplicateTarget.Key}");

            Directory.CreateDirectory(outputPath);

            if (files.Any(IsPsdFile))
            {
                ConfigureMagickAcceleration();
            }

            int maxParallelism = Math.Clamp(Environment.ProcessorCount, 1, Math.Min(MaxParallelUpperBound, files.Length));
            int completed = 0;
            var convertedSources = new ConcurrentBag<string>();
            var errors = new ConcurrentBag<Exception>();

            await Parallel.ForEachAsync(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = token },
                async (file, cancellationToken) =>
                {
                    string outputFilePath = GetOutputFilePath(file, outputPath, targetFormat);
                    try
                    {
                        await ConvertSingleFileAsync(file, inputPath, outputPath, targetFormat, outputFilePath, cancellationToken);
                        convertedSources.Add(file);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        errors.Add(new IOException($"Failed to convert {file}: {ex.Message}", ex));
                    }
                    finally
                    {
                        int done = Interlocked.Increment(ref completed);
                        progress.Report(done * 100.0 / files.Length);
                    }
                });
            if (!errors.IsEmpty) throw new AggregateException(errors);
            token.ThrowIfCancellationRequested();
            if (clearConvertedSources)
                foreach (var source in convertedSources)
                {

                    if (!string.Equals(Path.GetExtension(source), "." + targetFormat, StringComparison.OrdinalIgnoreCase))
                        File.Delete(source);
                }
        }

        private static async Task ConvertSingleFileAsync(
            string file,
            string inputPath,
            string outputPath,
            string targetFormat,
            string outputFilePath,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(outputFilePath), StringComparison.OrdinalIgnoreCase))
            {
                var temporary = outputFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    if (IsPsdFile(file))
                    {
                        using var image = new MagickImage(file);
                        image.Format = GetMagickFormat(targetFormat);
                        image.Write(temporary);
                    }
                    else
                    {
                        using var image = await Image.LoadAsync(file, cancellationToken);
                        await image.SaveAsync(temporary, GetEncoder(targetFormat), cancellationToken);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporary, outputFilePath, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }

            if (!string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                CopyCompanionText(file, outputPath);
            }
        }

        private static IImageEncoder GetEncoder(string format)
        {
            switch (format.ToUpperInvariant())
            {
                case "PNG": return new PngEncoder();
                case "JPG":
                case "JPEG": return new JpegEncoder();
                case "BMP": return new BmpEncoder();
                case "WEBP": return new WebpEncoder();
                default: throw new NotSupportedException("Unsupported image format");
            }
        }

        private static MagickFormat GetMagickFormat(string format)
        {
            switch (format.ToUpperInvariant())
            {
                case "PNG": return MagickFormat.Png;
                case "JPG":
                case "JPEG": return MagickFormat.Jpeg;
                case "BMP": return MagickFormat.Bmp;
                case "WEBP": return MagickFormat.WebP;
                default: throw new NotSupportedException("Unsupported image format");
            }
        }

        private static string GetOutputFilePath(string sourceFilePath, string outputPath, string targetFormat)
        {
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFilePath);
            var newFileName = $"{fileNameWithoutExt}.{targetFormat.ToLowerInvariant()}";
            return Path.Combine(outputPath, newFileName);
        }

        private static bool IsPsdFile(string filePath)
        {
            return string.Equals(Path.GetExtension(filePath), ".psd", StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyCompanionText(string sourceImagePath, string outputFolder)
        {
            var sourceTxtPath = Path.ChangeExtension(sourceImagePath, ".txt");
            if (!File.Exists(sourceTxtPath))
            {
                return;
            }

            var destinationTxtPath = Path.Combine(outputFolder, Path.GetFileName(sourceTxtPath));
            File.Copy(sourceTxtPath, destinationTxtPath, true);
        }

        private static void ConfigureMagickAcceleration()
        {

            TrySetMagickResourceThreadLimit(1);
            TryEnableOpenCl();
        }

        private static void TryEnableOpenCl()
        {
            try
            {
                var assembly = typeof(MagickImage).Assembly;
                var magickNetType = assembly.GetType("ImageMagick.MagickNET");
                var openCl = magickNetType?.GetProperty("OpenCL", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (openCl == null)
                {
                    return;
                }

                var isEnabledProperty = openCl.GetType().GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance);
                if (isEnabledProperty == null || !isEnabledProperty.CanWrite)
                {
                    return;
                }

                isEnabledProperty.SetValue(openCl, true);
            }
            catch
            {

            }
        }

        private static void TrySetMagickResourceThreadLimit(int threadLimit)
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
            }
            catch
            {

            }
        }
    }
}
