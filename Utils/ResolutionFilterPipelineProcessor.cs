using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public sealed class ResolutionFilterPipelineOptions
    {
        public bool IsHeightFilterEnabled { get; init; }
        public int MinHeight { get; init; } = 1024;
        public bool IsWidthFilterEnabled { get; init; }
        public int MinWidth { get; init; } = 1024;
        public bool IsResolutionFilterEnabled { get; init; }
        public int MinResolutionWidth { get; init; } = 1024;
        public int MinResolutionHeight { get; init; } = 1024;
    }

    public static class ResolutionFilterPipelineProcessor
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif"
        };

        public static Task<int> ExecuteAsync(
            string inputPath,
            ResolutionFilterPipelineOptions options,
            Action<string>? log,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
            {
                throw new DirectoryNotFoundException("输入路径无效。");
            }

            ArgumentNullException.ThrowIfNull(options);

            return Task.Run(() => ExecuteCore(Path.GetFullPath(inputPath), options, log, token), token);
        }

        private static int ExecuteCore(
            string inputPath,
            ResolutionFilterPipelineOptions options,
            Action<string>? log,
            CancellationToken token)
        {
            log?.Invoke("开始进行分辨率筛选...");

            List<string> files = Directory.GetFiles(inputPath, "*.*", SearchOption.AllDirectories)
                .Where(file => SupportedExtensions.Contains(Path.GetExtension(file)))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int deletedCount = 0;
            long minResolution = (long)options.MinResolutionWidth * options.MinResolutionHeight;

            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();

                bool shouldDelete = false;
                string reason = string.Empty;

                try
                {
                    var imageInfo = Image.Identify(file);
                    if (imageInfo is null)
                    {
                        log?.Invoke($"错误: 无法识别图像文件 {file}");
                        continue;
                    }

                    int width = imageInfo.Width;
                    int height = imageInfo.Height;

                    if (options.IsHeightFilterEnabled && height < options.MinHeight)
                    {
                        shouldDelete = true;
                        reason = $"高度 {height}px < {options.MinHeight}px";
                    }
                    else if (options.IsWidthFilterEnabled && width < options.MinWidth)
                    {
                        shouldDelete = true;
                        reason = $"宽度 {width}px < {options.MinWidth}px";
                    }
                    else if (options.IsResolutionFilterEnabled && (long)width * height < minResolution)
                    {
                        shouldDelete = true;
                        reason = $"分辨率 {width}x{height} < {minResolution} pixels";
                    }

                    if (!shouldDelete)
                    {
                        continue;
                    }

                    File.Delete(file);
                    string txtFile = Path.ChangeExtension(file, ".txt");
                    if (File.Exists(txtFile))
                    {
                        File.Delete(txtFile);
                        log?.Invoke($"已删除: {txtFile} (与图像同名)");
                    }

                    log?.Invoke($"已删除: {file} (原因: {reason})");
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"错误: 处理文件 {file} 时发生错误: {ex.Message}");
                }
            }

            log?.Invoke($"筛选完成。共删除了 {deletedCount} 个文件。");
            return deletedCount;
        }
    }
}
