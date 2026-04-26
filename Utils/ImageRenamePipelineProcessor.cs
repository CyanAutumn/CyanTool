using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public sealed class ImageRenamePipelineOptions
    {
        public string NamePrefix { get; init; } = "image_";
        public int StartIndex { get; init; } = 1;
        public int NumberPadding { get; init; } = 4;
    }

    public static class ImageRenamePipelineProcessor
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"
        };

        public static Task<int> ExecuteAsync(
            string inputPath,
            ImageRenamePipelineOptions options,
            Action<string>? log,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
            {
                throw new DirectoryNotFoundException("请先选择有效的输入路径并生成预览。");
            }

            ArgumentNullException.ThrowIfNull(options);

            return Task.Run(() => ExecuteCore(Path.GetFullPath(inputPath), options, log, token), token);
        }

        private static int ExecuteCore(
            string inputPath,
            ImageRenamePipelineOptions options,
            Action<string>? log,
            CancellationToken token)
        {
            List<string> files = Directory.EnumerateFiles(inputPath, "*", SearchOption.TopDirectoryOnly)
                .Where(file => SupportedExtensions.Contains(Path.GetExtension(file) ?? string.Empty))
                .OrderBy(file => file, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                log?.Invoke("没有需要重命名的文件。");
                return 0;
            }

            List<RenameOperation> operations = BuildRenameOperations(inputPath, files, options);
            if (operations.Count == 0)
            {
                log?.Invoke("没有需要重命名的文件。");
                return 0;
            }

            List<(string TempPath, string TargetPath)> tempMappings = new(operations.Count);
            foreach (RenameOperation operation in operations)
            {
                token.ThrowIfCancellationRequested();
                string tempPath = CreateTemporaryPath(inputPath);
                File.Move(operation.Source, tempPath);
                tempMappings.Add((tempPath, operation.Target));
            }

            foreach ((string tempPath, string targetPath) in tempMappings)
            {
                token.ThrowIfCancellationRequested();
                File.Move(tempPath, targetPath);
            }

            log?.Invoke($"重命名完成，共处理 {operations.Count} 个文件。");
            return operations.Count;
        }

        private static List<RenameOperation> BuildRenameOperations(
            string inputPath,
            IReadOnlyList<string> files,
            ImageRenamePipelineOptions options)
        {
            int normalizedStartIndex = options.StartIndex < 0 ? 0 : options.StartIndex;
            int normalizedPadding = options.NumberPadding < 1 ? 1 : options.NumberPadding;
            string prefix = options.NamePrefix ?? string.Empty;

            int index = normalizedStartIndex;
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var operations = new List<RenameOperation>(files.Count);

            foreach (string file in files)
            {
                string extension = Path.GetExtension(file) ?? string.Empty;
                string newName = BuildNewFileName(prefix, index, normalizedPadding, extension);
                string newFullPath = Path.Combine(inputPath, newName);
                bool isSamePath = string.Equals(file, newFullPath, StringComparison.OrdinalIgnoreCase);

                if (!usedNames.Add(newName))
                {
                    throw new InvalidOperationException($"存在命名冲突: {newName}");
                }

                if (!isSamePath && File.Exists(newFullPath))
                {
                    throw new InvalidOperationException($"目标文件已存在: {newName}");
                }

                if (!isSamePath)
                {
                    operations.Add(new RenameOperation(file, newFullPath));
                }

                index++;
            }

            return operations;
        }

        private static string BuildNewFileName(string prefix, int index, int numberPadding, string extension)
        {
            string padded = index.ToString().PadLeft(numberPadding, '0');
            return string.Concat(prefix, padded, extension ?? string.Empty);
        }

        private static string CreateTemporaryPath(string directory)
        {
            string tempPath;
            do
            {
                tempPath = Path.Combine(directory, Path.GetRandomFileName());
            } while (File.Exists(tempPath));

            return tempPath;
        }

        private readonly struct RenameOperation
        {
            public RenameOperation(string source, string target)
            {
                Source = source;
                Target = target;
            }

            public string Source { get; }

            public string Target { get; }
        }
    }
}
