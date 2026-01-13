using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CyanTools.Utils
{
    public static class TransparentChannelConverter
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".webp", ".bmp", ".jpg", ".jpeg"
        };

        public static void Convert(string inputPath, string outputPath, IProgress<double> progress, Action<string> logAction)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
            {
                logAction?.Invoke("输入路径无效或不存在。");
                progress.Report(100);
                return;
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                logAction?.Invoke("输出路径无效。");
                progress.Report(100);
                return;
            }

            Directory.CreateDirectory(outputPath);

            var files = Directory.GetFiles(inputPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            if (files.Count == 0)
            {
                logAction?.Invoke("未找到支持的图片格式。");
                progress.Report(100);
                return;
            }

            bool sameDirectory = PathsEqual(inputPath, outputPath);
            int convertedCount = 0;

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var extension = Path.GetExtension(file);
                var outputFilePath = Path.Combine(outputPath, Path.GetFileName(file));

                try
                {
                    if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        CopyIfNeeded(file, outputFilePath, sameDirectory);
                        logAction?.Invoke($"跳过: {Path.GetFileName(file)} (JPEG 无透明通道)");
                    }
                    else
                    {
                        using var image = Image.Load<Rgba32>(file);
                        if (!HasTransparentPixels(image))
                        {
                            CopyIfNeeded(file, outputFilePath, sameDirectory);
                            logAction?.Invoke($"跳过: {Path.GetFileName(file)} (无透明像素)");
                        }
                        else
                        {
                            using var background = new Image<Rgba32>(image.Width, image.Height, Color.White);
                            background.Mutate(ctx => ctx.DrawImage(image, 1f));
                            using var flattened = background.CloneAs<Rgb24>();
                            flattened.Save(outputFilePath);
                            logAction?.Invoke($"已转换: {Path.GetFileName(file)}");
                            convertedCount++;
                        }
                    }

                    if (!sameDirectory)
                    {
                        CopyCompanionTxt(file, outputPath, logAction);
                    }
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"处理失败: {Path.GetFileName(file)}. 原因: {ex.Message}");
                }

                progress.Report((double)(i + 1) / files.Count * 100);
            }

            logAction?.Invoke($"--- 转换完成 --- 共转换 {convertedCount} 张图片。");
        }

        private static void CopyIfNeeded(string sourcePath, string destinationPath, bool sameDirectory)
        {
            if (sameDirectory)
            {
                return;
            }

            File.Copy(sourcePath, destinationPath, true);
        }

        private static void CopyCompanionTxt(string sourceImagePath, string outputPath, Action<string> logAction)
        {
            var txtFile = Path.ChangeExtension(sourceImagePath, ".txt");
            if (!string.IsNullOrEmpty(txtFile) && File.Exists(txtFile))
            {
                var destTxtFile = Path.Combine(outputPath, Path.GetFileName(txtFile));
                try
                {
                    File.Copy(txtFile, destTxtFile, true);
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"复制TXT失败: {Path.GetFileName(txtFile)} ({ex.Message})");
                }
            }
        }

        private static bool HasTransparentPixels(Image<Rgba32> image)
        {
            bool hasTransparency = false;
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        if (row[x].A < 255)
                        {
                            hasTransparency = true;
                            return;
                        }
                    }
                }
            });

            return hasTransparency;
        }

        private static bool PathsEqual(string pathA, string pathB)
        {
            var fullA = Path.GetFullPath(pathA).TrimEnd(Path.DirectorySeparatorChar);
            var fullB = Path.GetFullPath(pathB).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase);
        }
    }
}
