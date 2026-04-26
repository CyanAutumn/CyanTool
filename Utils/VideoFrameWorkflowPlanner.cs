using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyanTools.Utils
{
    public static class VideoFrameWorkflowPlanner
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",
            ".avi",
            ".mov",
            ".mkv"
        };

        public static IReadOnlyList<string> ResolveInputVideos(string? configuredVideoPath, string? inputDirectory)
        {
            if (!string.IsNullOrWhiteSpace(configuredVideoPath))
            {
                string fullConfiguredPath = Path.GetFullPath(configuredVideoPath);
                if (!File.Exists(fullConfiguredPath))
                {
                    throw new FileNotFoundException($"找不到视频文件: {fullConfiguredPath}");
                }

                return new[] { fullConfiguredPath };
            }

            if (string.IsNullOrWhiteSpace(inputDirectory) || !Directory.Exists(inputDirectory))
            {
                throw new DirectoryNotFoundException("工作流输入目录无效，且未指定视频文件。");
            }

            List<string> videos = Directory.GetFiles(inputDirectory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(Path.GetFullPath)
                .ToList();

            if (videos.Count == 0)
            {
                throw new InvalidOperationException("输入目录中未找到可导出的的视频文件。");
            }

            return videos;
        }
    }
}
