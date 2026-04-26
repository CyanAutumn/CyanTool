using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public static class TrainingSetBalancerPipelineProcessor
    {
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

        public static Task<int> ExecuteAsync(
            string rootPath,
            Action<string>? log,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                throw new DirectoryNotFoundException("请先选择一个包含子目录的有效输入路径。");
            }

            return Task.Run(() => ExecuteCore(Path.GetFullPath(rootPath), log, token), token);
        }

        private static int ExecuteCore(
            string rootPath,
            Action<string>? log,
            CancellationToken token)
        {
            List<FolderBalanceItem> items = Directory.GetDirectories(rootPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => BuildItem(path))
                .ToList();

            if (items.Count == 0)
            {
                throw new InvalidOperationException("请先选择一个包含子目录的有效输入路径。");
            }

            int maxImageCount = items.Max(item => item.ImageCount);
            if (maxImageCount <= 0)
            {
                throw new InvalidOperationException("所选目录中没有可用于配平的图片。");
            }

            foreach (FolderBalanceItem item in items)
            {
                token.ThrowIfCancellationRequested();
                item.Repetitions = item.ImageCount > 0
                    ? Math.Max(1, (int)Math.Round((double)maxImageCount / item.ImageCount))
                    : 1;
            }

            int renamedCount = 0;
            foreach (FolderBalanceItem item in items)
            {
                token.ThrowIfCancellationRequested();

                string originalFolderName = Path.GetFileName(item.FolderPath);
                string baseName = StripRepeatPrefix(originalFolderName);
                string newFolderName = $"{item.Repetitions}_{baseName}";
                if (string.Equals(originalFolderName, newFolderName, StringComparison.Ordinal))
                {
                    continue;
                }

                string newFullPath = Path.Combine(rootPath, newFolderName);
                if (Directory.Exists(newFullPath))
                {
                    throw new InvalidOperationException($"无法重命名 '{originalFolderName}' 为 '{newFolderName}'，因为目标路径已存在。");
                }

                Directory.Move(item.FolderPath, newFullPath);
                log?.Invoke($"已重命名: {originalFolderName} -> {newFolderName}");
                renamedCount++;
            }

            log?.Invoke($"训练集配平完成。共重命名 {renamedCount} 个文件夹。");
            return renamedCount;
        }

        private static FolderBalanceItem BuildItem(string folderPath)
        {
            int imageCount = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                .Count(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

            return new FolderBalanceItem(folderPath, imageCount);
        }

        private static string StripRepeatPrefix(string folderName)
        {
            string[] parts = folderName.Split('_');
            if (parts.Length > 1 && int.TryParse(parts[0], out _))
            {
                return string.Join("_", parts.Skip(1));
            }

            return folderName;
        }

        private sealed class FolderBalanceItem
        {
            public FolderBalanceItem(string folderPath, int imageCount)
            {
                FolderPath = folderPath;
                ImageCount = imageCount;
            }

            public string FolderPath { get; }

            public int ImageCount { get; }

            public int Repetitions { get; set; }
        }
    }
}
