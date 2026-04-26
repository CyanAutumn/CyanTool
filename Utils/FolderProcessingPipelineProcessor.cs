using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace CyanTools.Utils
{
    public static class FolderProcessingPipelineProcessor
    {
        private static readonly string[] SupportedArchiveSuffixes =
        {
            ".zip",
            ".7z",
            ".rar",
            ".tar",
            ".tar.gz",
            ".tgz",
            ".tar.bz2",
            ".tbz2",
            ".tar.xz",
            ".txz"
        };

        public static Task ExecuteAsync(
            string folderPath,
            FolderProcessingOptions options,
            Action<string>? log,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException("请先选择有效的输入文件夹。");
            }

            ArgumentNullException.ThrowIfNull(options);

            return Task.Run(() => ExecuteCore(Path.GetFullPath(folderPath), options, log, token), token);
        }

        private static void ExecuteCore(
            string rootPath,
            FolderProcessingOptions options,
            Action<string>? log,
            CancellationToken token)
        {
            string operationName = GetOperationDisplayName(options.Operation);
            log?.Invoke($"开始处理: {operationName} / {rootPath}");

            switch (options.Operation)
            {
                case FolderProcessingOperation.MoveFilesToTopLevel:
                    ExecuteMoveFilesToTopLevel(rootPath, log, token);
                    break;
                case FolderProcessingOperation.DeleteEmptyDirectories:
                    ExecuteDeleteEmptyDirectories(rootPath, log, token);
                    break;
                case FolderProcessingOperation.RetainDirectoriesByKeyword:
                    ExecuteRetainDirectoriesByKeyword(rootPath, options.ParameterText, log, token);
                    break;
                case FolderProcessingOperation.ExtractArchives:
                    ExecuteExtractArchives(rootPath, options.ParameterText, log, token);
                    break;
                default:
                    throw new InvalidOperationException($"不支持的文件夹处理功能: {options.Operation}");
            }
        }

        private static void ExecuteMoveFilesToTopLevel(string rootPath, Action<string>? log, CancellationToken token)
        {
            string[] directories = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories);
            int movedCount = 0;
            int skippedCount = 0;

            foreach (string directory in directories)
            {
                token.ThrowIfCancellationRequested();

                foreach (string filePath in Directory.GetFiles(directory))
                {
                    token.ThrowIfCancellationRequested();

                    string targetPath = Path.Combine(rootPath, Path.GetFileName(filePath));
                    if (File.Exists(targetPath))
                    {
                        skippedCount++;
                        log?.Invoke($"跳过同名文件: {filePath}");
                        continue;
                    }

                    File.Move(filePath, targetPath);
                    movedCount++;
                    log?.Invoke($"已移动: {filePath} -> {targetPath}");
                }
            }

            int deletedEmptyDirectoryCount = DeleteEmptyDirectoriesCore(rootPath, log, token);
            log?.Invoke($"处理完成。已移动 {movedCount} 个文件，跳过 {skippedCount} 个同名文件，删除 {deletedEmptyDirectoryCount} 个空文件夹。");
        }

        private static void ExecuteDeleteEmptyDirectories(string rootPath, Action<string>? log, CancellationToken token)
        {
            int deletedCount = DeleteEmptyDirectoriesCore(rootPath, log, token);
            log?.Invoke($"处理完成。共删除 {deletedCount} 个空文件夹。");
        }

        private static int DeleteEmptyDirectoriesCore(string rootPath, Action<string>? log, CancellationToken token)
        {
            int deletedCount = 0;
            bool deletedAny;

            do
            {
                deletedAny = false;
                foreach (string directory in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();

                    if (!Directory.Exists(directory))
                    {
                        continue;
                    }

                    if (Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        continue;
                    }

                    Directory.Delete(directory, recursive: false);
                    deletedAny = true;
                    deletedCount++;
                    log?.Invoke($"已删除空文件夹: {directory}");
                }
            } while (deletedAny);

            return deletedCount;
        }

        private static void ExecuteRetainDirectoriesByKeyword(
            string rootPath,
            string? keyword,
            Action<string>? log,
            CancellationToken token)
        {
            string normalizedKeyword = keyword?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                throw new InvalidOperationException("关键字不能为空。");
            }

            HashSet<string> keepDirectories = BuildKeepDirectorySet(rootPath, normalizedKeyword, token);
            int deletedDirectoryCount = 0;

            foreach (string directory in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();

                if (!Directory.Exists(directory))
                {
                    continue;
                }

                if (keepDirectories.Contains(directory))
                {
                    continue;
                }

                Directory.Delete(directory, recursive: true);
                deletedDirectoryCount++;
                log?.Invoke($"已删除目录: {directory}");
            }

            log?.Invoke($"处理完成。共删除 {deletedDirectoryCount} 个非保留目录。");
        }

        private static HashSet<string> BuildKeepDirectorySet(string rootPath, string keyword, CancellationToken token)
        {
            HashSet<string> keepDirectories = new(StringComparer.OrdinalIgnoreCase)
            {
                rootPath
            };

            foreach (string directory in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();

                if (!Path.GetFileName(directory).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? current = directory;
                while (!string.IsNullOrWhiteSpace(current) &&
                       IsSameOrChildPath(rootPath, current))
                {
                    keepDirectories.Add(current);
                    if (string.Equals(current, rootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    current = Path.GetDirectoryName(current);
                }
            }

            return keepDirectories;
        }

        private static bool IsSameOrChildPath(string rootPath, string candidatePath)
        {
            string normalizedRoot = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCandidate = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string prefixedRoot = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedCandidate.StartsWith(prefixedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static void ExecuteExtractArchives(
            string rootPath,
            string? password,
            Action<string>? log,
            CancellationToken token)
        {
            string[] archivePaths = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories)
                .Where(IsSupportedArchivePath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            int extractedArchiveCount = 0;
            foreach (string archivePath in archivePaths)
            {
                token.ThrowIfCancellationRequested();

                string outputDirectory = Path.Combine(
                    Path.GetDirectoryName(archivePath) ?? rootPath,
                    GetArchiveBaseName(archivePath));

                Directory.CreateDirectory(outputDirectory);
                ExtractArchive(archivePath, outputDirectory, password, token);
                extractedArchiveCount++;
                log?.Invoke($"已解压: {archivePath} -> {outputDirectory}");
            }

            log?.Invoke($"处理完成。共解压 {extractedArchiveCount} 个压缩包。");
        }

        private static void ExtractArchive(
            string archivePath,
            string outputDirectory,
            string? password,
            CancellationToken token)
        {
            ReaderOptions options = new ReaderOptions();
            if (!string.IsNullOrWhiteSpace(password))
            {
                options.Password = password;
            }

            using IArchive archive = ArchiveFactory.Open(archivePath, options);
            string normalizedOutputDirectory = Path.GetFullPath(outputDirectory);

            foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                token.ThrowIfCancellationRequested();

                string destinationPath = GetSafeExtractionPath(normalizedOutputDirectory, entry.Key);
                string? destinationFolder = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                using Stream entryStream = entry.OpenEntryStream();
                using FileStream destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                entryStream.CopyTo(destinationStream);
            }
        }

        private static string GetSafeExtractionPath(string outputDirectory, string entryKey)
        {
            string relativePath = entryKey
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            string destinationPath = Path.GetFullPath(Path.Combine(outputDirectory, relativePath));
            string normalizedOutputDirectory = Path.GetFullPath(outputDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string outputPrefix = normalizedOutputDirectory + Path.DirectorySeparatorChar;

            if (!string.Equals(destinationPath, normalizedOutputDirectory, StringComparison.OrdinalIgnoreCase) &&
                !destinationPath.StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"压缩包包含非法路径: {entryKey}");
            }

            return destinationPath;
        }

        private static string GetArchiveBaseName(string archivePath)
        {
            string fileName = Path.GetFileName(archivePath);
            string suffix = SupportedArchiveSuffixes
                .OrderByDescending(value => value.Length)
                .FirstOrDefault(value => fileName.EndsWith(value, StringComparison.OrdinalIgnoreCase))
                ?? Path.GetExtension(archivePath);

            if (!string.IsNullOrWhiteSpace(suffix) &&
                fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - suffix.Length);
            }

            return Path.GetFileNameWithoutExtension(archivePath);
        }

        private static bool IsSupportedArchivePath(string path)
        {
            return SupportedArchiveSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetOperationDisplayName(FolderProcessingOperation operation)
        {
            return operation switch
            {
                FolderProcessingOperation.MoveFilesToTopLevel => "移动子目录中的文件到顶层",
                FolderProcessingOperation.DeleteEmptyDirectories => "删除空文件夹",
                FolderProcessingOperation.RetainDirectoriesByKeyword => "保留含有指定关键字的文件夹",
                FolderProcessingOperation.ExtractArchives => "解压所有压缩包",
                _ => operation.ToString()
            };
        }
    }
}
