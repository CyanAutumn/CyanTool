using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyanTools.Utils
{
    public readonly record struct InputDirectoryCleanupResult(bool WasSkipped, string Message);

    public static class InputDirectoryCleanupHelper
    {
        public static InputDirectoryCleanupResult TryClearContents(string inputDirectory, IEnumerable<string?> protectedPaths)
        {
            try
            {
                return ClearContents(inputDirectory, protectedPaths);
            }
            catch (Exception ex)
            {
                return new InputDirectoryCleanupResult(true, $"清空输入路径文件夹失败: {ex.Message}");
            }
        }

        public static InputDirectoryCleanupResult ClearContents(string inputDirectory, IEnumerable<string?> protectedPaths)
        {
            if (string.IsNullOrWhiteSpace(inputDirectory) || !Directory.Exists(inputDirectory))
            {
                return new InputDirectoryCleanupResult(true, "输入路径不存在，已跳过清空输入路径文件夹。");
            }

            string normalizedInputDirectory = NormalizePath(inputDirectory);
            HashSet<string> normalizedProtectedPaths = protectedPaths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => NormalizePath(path!))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (normalizedProtectedPaths.Contains(normalizedInputDirectory))
            {
                return new InputDirectoryCleanupResult(true, "输出路径与输入路径相同，已跳过清空输入路径文件夹。");
            }

            DeleteContentsRecursive(normalizedInputDirectory, normalizedProtectedPaths);

            bool preservedNestedOutput = normalizedProtectedPaths.Any(path => IsChildPathOf(path, normalizedInputDirectory));
            string message = preservedNestedOutput
                ? "已清空输入路径文件夹，并保留输出目录。"
                : "已清空输入路径文件夹。";

            return new InputDirectoryCleanupResult(false, message);
        }

        private static void DeleteContentsRecursive(string currentDirectory, IReadOnlyCollection<string> protectedPaths)
        {
            foreach (string filePath in Directory.GetFiles(currentDirectory))
            {
                if (protectedPaths.Any(path => PathEquals(path, filePath)))
                {
                    continue;
                }

                File.Delete(filePath);
            }

            foreach (string directoryPath in Directory.GetDirectories(currentDirectory))
            {
                if (protectedPaths.Any(path => PathEquals(path, directoryPath)))
                {
                    continue;
                }

                if (protectedPaths.Any(path => IsChildPathOf(path, directoryPath)))
                {
                    DeleteContentsRecursive(directoryPath, protectedPaths);
                    if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                    {
                        Directory.Delete(directoryPath, recursive: false);
                    }

                    continue;
                }

                Directory.Delete(directoryPath, recursive: true);
            }
        }

        private static string NormalizePath(string path)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }

        private static bool PathEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChildPathOf(string candidatePath, string parentPath)
        {
            if (PathEquals(candidatePath, parentPath))
            {
                return false;
            }

            string normalizedParent = parentPath + Path.DirectorySeparatorChar;
            return candidatePath.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
        }
    }
}
