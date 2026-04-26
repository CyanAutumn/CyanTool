using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public static class KeepFilesPipelineProcessor
    {
        public static Task<int> ExecuteAsync(
            string folderPath,
            KeepFilesOptions options,
            Action<string>? log,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException("请先输入有效的输入路径。");
            }

            ArgumentNullException.ThrowIfNull(options);

            return Task.Run(() => ExecuteCore(Path.GetFullPath(folderPath), options, log, token), token);
        }

        private static int ExecuteCore(
            string folderPath,
            KeepFilesOptions options,
            Action<string>? log,
            CancellationToken token)
        {
            if (KeepFilesRetentionPolicy.BuildExtensionsToKeep(options).Count == 0)
            {
                throw new InvalidOperationException("请至少保留一种文件类型。");
            }

            string[] files = Directory.GetFiles(folderPath);
            int deletedCount = 0;

            log?.Invoke($"开始处理输入路径: {folderPath}");

            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();

                KeepFilesDeleteReason deleteReason = KeepFilesRetentionPolicy.GetDeleteReason(file, options);
                if (deleteReason == KeepFilesDeleteReason.None)
                {
                    continue;
                }

                File.Delete(file);
                log?.Invoke(BuildDeleteLog(file, deleteReason));
                deletedCount++;
            }

            log?.Invoke($"操作完成。共删除了 {deletedCount} 个文件。");
            return deletedCount;
        }

        private static string BuildDeleteLog(string filePath, KeepFilesDeleteReason deleteReason)
        {
            return deleteReason == KeepFilesDeleteReason.ChineseContentNotAllowed
                ? $"已删除(含中文内容 txt): {filePath}"
                : $"已删除: {filePath}";
        }
    }
}
