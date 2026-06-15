using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace CyanTools.Utils
{
    public static class FolderProcessingPipelineProcessor
    {
        private const string AdapterFileName = "adapter_model.safetensors";
        private const int SequenceWidth = 6;
        private static readonly Regex NumberRegex = new Regex(@"\d+", RegexOptions.Compiled);

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
                throw new DirectoryNotFoundException("\u8BF7\u5148\u9009\u62E9\u6709\u6548\u7684\u8F93\u5165\u6587\u4EF6\u5939\u3002");
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
            log?.Invoke($"\u5F00\u59CB\u5904\u7406: {operationName} / {rootPath}");

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
                case FolderProcessingOperation.ConvertDiffusionPipeOutputToSdScript:
                    ExecuteConvertDiffusionPipeOutputToSdScript(rootPath, options.OutputPath, options.OverwriteExisting, log, token);
                    break;
                default:
                    throw new InvalidOperationException($"\u4E0D\u652F\u6301\u7684\u6587\u4EF6\u5939\u5904\u7406\u529F\u80FD: {options.Operation}");
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
                        log?.Invoke($"\u8DF3\u8FC7\u540C\u540D\u6587\u4EF6: {filePath}");
                        continue;
                    }

                    File.Move(filePath, targetPath);
                    movedCount++;
                    log?.Invoke($"\u5DF2\u79FB\u52A8: {filePath} -> {targetPath}");
                }
            }

            int deletedEmptyDirectoryCount = DeleteEmptyDirectoriesCore(rootPath, log, token);
            log?.Invoke($"\u5904\u7406\u5B8C\u6210\u3002\u5DF2\u79FB\u52A8 {movedCount} \u4E2A\u6587\u4EF6\uFF0C\u8DF3\u8FC7 {skippedCount} \u4E2A\u540C\u540D\u6587\u4EF6\uFF0C\u5220\u9664 {deletedEmptyDirectoryCount} \u4E2A\u7A7A\u6587\u4EF6\u5939\u3002");
        }

        private static void ExecuteDeleteEmptyDirectories(string rootPath, Action<string>? log, CancellationToken token)
        {
            int deletedCount = DeleteEmptyDirectoriesCore(rootPath, log, token);
            log?.Invoke($"\u5904\u7406\u5B8C\u6210\u3002\u5171\u5220\u9664 {deletedCount} \u4E2A\u7A7A\u6587\u4EF6\u5939\u3002");
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
                    log?.Invoke($"\u5DF2\u5220\u9664\u7A7A\u6587\u4EF6\u5939: {directory}");
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
                throw new InvalidOperationException("\u5173\u952E\u5B57\u4E0D\u80FD\u4E3A\u7A7A\u3002");
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
                log?.Invoke($"\u5DF2\u5220\u9664\u76EE\u5F55: {directory}");
            }

            log?.Invoke($"\u5904\u7406\u5B8C\u6210\u3002\u5171\u5220\u9664 {deletedDirectoryCount} \u4E2A\u975E\u4FDD\u7559\u76EE\u5F55\u3002");
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
                log?.Invoke($"\u5DF2\u89E3\u538B: {archivePath} -> {outputDirectory}");
            }

            log?.Invoke($"\u5904\u7406\u5B8C\u6210\u3002\u5171\u89E3\u538B {extractedArchiveCount} \u4E2A\u538B\u7F29\u5305\u3002");
        }

        private static void ExecuteConvertDiffusionPipeOutputToSdScript(
            string rootPath,
            string? outputPath,
            bool overwriteExisting,
            Action<string>? log,
            CancellationToken token)
        {
            string outputRoot = string.IsNullOrWhiteSpace(outputPath)
                ? rootPath
                : Path.GetFullPath(outputPath.Trim());

            Directory.CreateDirectory(outputRoot);

            var candidates = Directory.EnumerateDirectories(rootPath)
                .Select(path => new EpochFolder(path, Path.Combine(path, AdapterFileName)))
                .Where(item => File.Exists(item.SourceFile))
                .Select(item => TryGetEpochNumber(Path.GetFileName(item.FolderPath), out int epoch)
                    ? item with { Epoch = epoch }
                    : item)
                .ToList();

            if (candidates.Count == 0)
            {
                log?.Invoke($"\u672A\u5728\u4E00\u7EA7\u5B50\u6587\u4EF6\u5939\u4E2D\u627E\u5230 {AdapterFileName}\u3002");
                return;
            }

            int moved = 0;
            int skipped = 0;

            foreach (var item in candidates.OrderBy(x => x.Epoch ?? int.MaxValue).ThenBy(x => x.FolderPath, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();

                if (item.Epoch == null)
                {
                    skipped++;
                    log?.Invoke($"\u8DF3\u8FC7: {Path.GetFileName(item.FolderPath)} \u672A\u8BC6\u522B\u5230 epoch \u5E8F\u53F7\u3002");
                    continue;
                }

                string targetFileName = $"adapter_model-{item.Epoch.Value.ToString($"D{SequenceWidth}")}.safetensors";
                string targetPath = Path.Combine(outputRoot, targetFileName);

                if (File.Exists(targetPath) && !overwriteExisting)
                {
                    skipped++;
                    log?.Invoke($"\u8DF3\u8FC7: {targetFileName} \u5DF2\u5B58\u5728\u3002");
                    continue;
                }

                MoveFile(item.SourceFile, targetPath, overwriteExisting);
                moved++;
                log?.Invoke($"{Path.GetFileName(item.FolderPath)} -> {targetFileName}");
            }

            log?.Invoke($"\u5B8C\u6210: \u79FB\u52A8 {moved} \u4E2A\uFF0C\u8DF3\u8FC7 {skipped} \u4E2A\u3002");
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
                throw new InvalidDataException($"\u538B\u7F29\u5305\u5305\u542B\u975E\u6CD5\u8DEF\u5F84: {entryKey}");
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

        private static bool TryGetEpochNumber(string folderName, out int epoch)
        {
            epoch = 0;
            MatchCollection matches = NumberRegex.Matches(folderName ?? string.Empty);
            if (matches.Count == 0)
            {
                return false;
            }

            return int.TryParse(matches[^1].Value, out epoch) && epoch >= 0;
        }

        private static void MoveFile(string sourcePath, string targetPath, bool overwrite)
        {
            if (overwrite && File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            string sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath)) ?? string.Empty;
            string targetRoot = Path.GetPathRoot(Path.GetFullPath(targetPath)) ?? string.Empty;

            if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(sourcePath, targetPath);
                return;
            }

            File.Copy(sourcePath, targetPath);
            File.Delete(sourcePath);
        }

        private static bool IsSupportedArchivePath(string path)
        {
            return SupportedArchiveSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetOperationDisplayName(FolderProcessingOperation operation)
        {
            return operation switch
            {
                FolderProcessingOperation.MoveFilesToTopLevel => "\u79FB\u52A8\u5B50\u76EE\u5F55\u4E2D\u7684\u6587\u4EF6\u5230\u9876\u5C42",
                FolderProcessingOperation.DeleteEmptyDirectories => "\u5220\u9664\u7A7A\u6587\u4EF6\u5939",
                FolderProcessingOperation.RetainDirectoriesByKeyword => "\u4FDD\u7559\u542B\u6709\u6307\u5B9A\u5173\u952E\u5B57\u7684\u6587\u4EF6\u5939",
                FolderProcessingOperation.ExtractArchives => "\u89E3\u538B\u6240\u6709\u538B\u7F29\u5305",
                FolderProcessingOperation.ConvertDiffusionPipeOutputToSdScript => "\u8F6C\u6362diffusion-pipe\u8F93\u51FA\u81F3sd-script",
                _ => operation.ToString()
            };
        }

        private sealed record EpochFolder(string FolderPath, string SourceFile)
        {
            public int? Epoch { get; init; }
        }
    }
}
