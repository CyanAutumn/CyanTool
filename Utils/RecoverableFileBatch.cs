using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CyanTools.Utils;


public static class RecoverableFileBatch
{
    private const string Journal = "save-journal.json";

    public static void Save(string root, IReadOnlyDictionary<string, string> files)
    {
        using var lease = Acquire(root);
        RecoverCore(root);
        var relativeFiles = files.ToDictionary(pair => Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(pair.Key)), pair => pair.Value);
        Validate(root, relativeFiles);
        AtomicFile.WriteAllText(JournalPath(root), JsonSerializer.Serialize(relativeFiles));
        RecoverCore(root);
    }

    public static void Recover(string root)
    {
        if (!File.Exists(JournalPath(root))) return;
        using var lease = Acquire(root);
        RecoverCore(root);
    }

    public static T Read<T>(string root, Func<T> read)
    {
        using var lease = Acquire(root);
        RecoverCore(root);
        return read();
    }

    private static FileStream Acquire(string root)
    {
        string folder = Path.Combine(root, ".git", "cyantools");
        for (string? cursor = Path.GetFullPath(folder); cursor != null; cursor = Path.GetDirectoryName(cursor))
        {
            FileSystemInfo info = Directory.Exists(cursor) ? new DirectoryInfo(cursor) : new FileInfo(cursor);
            if (info.LinkTarget != null) throw new InvalidDataException("项目保存日志不能通过符号链接或联接。");
        }
        Directory.CreateDirectory(folder);
        return new FileStream(Path.Combine(folder, "save.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static string JournalPath(string root) => Path.Combine(root, ".git", "cyantools", Journal);

    private static void RecoverCore(string root)
    {
        string path = JournalPath(root);
        if (!File.Exists(path)) return;
        var files = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("项目保存日志无效。");
        Validate(root, files);
        foreach (var pair in files) AtomicFile.WriteAllText(Path.Combine(root, pair.Key), pair.Value);
        File.Delete(path);
    }

    private static void Validate(string root, IReadOnlyDictionary<string, string> files)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in files)
        {
            string path = Path.GetFullPath(Path.Combine(fullRoot, pair.Key));
            if (Path.IsPathRooted(pair.Key) || !path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !destinations.Add(path) || pair.Value == null ||
                string.Equals(path, Path.GetFullPath(JournalPath(root)), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, Path.Combine(fullRoot, ".git", "cyantools", "save.lock"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("项目保存日志包含无效路径。");
            for (string? cursor = path; cursor != null; cursor = Path.GetDirectoryName(cursor))
            {
                FileSystemInfo info = Directory.Exists(cursor) ? new DirectoryInfo(cursor) : new FileInfo(cursor);
                if (info.LinkTarget != null) throw new InvalidDataException("项目保存路径不能通过符号链接或联接。");
                if (string.Equals(cursor, fullRoot, StringComparison.OrdinalIgnoreCase)) break;
            }
        }
    }
}
