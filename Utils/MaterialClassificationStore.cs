using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace CyanTools.Utils;

public enum MaterialClassificationTool { SameSource, ResolutionBuckets, ResolutionFilter, Clustering, Aesthetic, FarScene }

public static class MaterialClassificationStore
{
    public static string FolderName(MaterialClassificationTool tool) => tool switch
    {
        MaterialClassificationTool.SameSource => "同源归类",
        MaterialClassificationTool.ResolutionBuckets => "分辨率归类",
        MaterialClassificationTool.ResolutionFilter => "分辨率筛选",
        MaterialClassificationTool.Clustering => "聚类",
        MaterialClassificationTool.Aesthetic => "美学评分",
        MaterialClassificationTool.FarScene => "远景",
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string IndexPath(string root, MaterialClassificationTool tool) => Path.Combine(root, FolderName(tool), "index.json");
    private static string JournalPath(string root) => Path.Combine(root, "config", "material-view.json");
    public static bool HasActiveView(string root) => File.Exists(JournalPath(root));

    public static MaterialLease Acquire(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("素材目录不存在");
        EnsurePlainPath(root, root);
        var config = SafePath(root, "config");
        Directory.CreateDirectory(config);
        try
        {
            var stream = new FileStream(SafePath(root, Path.Combine("config", "material-operations.lock")), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new MaterialLease(root, stream);
        }
        catch (IOException ex) { throw new IOException("该素材正在执行其他操作，请等待完成后重试。", ex); }
    }

    internal static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("索引路径无效。");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("索引路径超出素材目录。");
        EnsurePlainPath(root, full);
        return full;
    }

    private static void EnsurePlainPath(string root, string path)
    {
        for (var cursor = path; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
        {

            if (Directory.Exists(cursor) ? new DirectoryInfo(cursor).LinkTarget != null : File.Exists(cursor) && new FileInfo(cursor).LinkTarget != null)
                throw new IOException("素材索引模式不支持符号链接或联接目录。");
            if (string.Equals(cursor, root, StringComparison.OrdinalIgnoreCase)) return;
        }
    }

    internal static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, value, JsonOptions);
                file.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    public sealed class IndexDocument
    {
        public int Version { get; set; } = 1;
        public MaterialClassificationTool Tool { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<IndexEntry> Entries { get; set; } = new();
    }
    public sealed class IndexEntry
    {
        public string Source { get; set; } = "";
        public string Destination { get; set; } = "";
        public long Length { get; set; }
        public string Sha256 { get; set; } = "";
    }

    public sealed class MaterialLease : IDisposable
    {
        private readonly FileStream _lock;
        public string Root { get; }
        internal MaterialLease(string root, FileStream file) { Root = root; _lock = file; }
        public void Dispose() => _lock.Dispose();

        private List<(IndexEntry Entry, string Source, string Destination)> Validate(IndexDocument document)
        {
            if (document.Version != 1 || document.Entries == null) throw new InvalidDataException("不支持的素材索引版本。");
            var input = Path.Combine(Root, "原图");
            var output = Path.Combine(Root, FolderName(document.Tool));
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<(IndexEntry, string, string)>();
            foreach (var entry in document.Entries)
            {
                var source = SafePath(input, entry.Source);
                var destination = SafePath(output, entry.Destination);
                if (!sources.Add(source) || !destinations.Add(destination) || entry.Destination.Equals("index.json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("素材索引包含重复路径或保留文件名。");
                result.Add((entry, source, destination));
            }
            return result;
        }

        private static IndexDocument Read(string path) => JsonSerializer.Deserialize<IndexDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("素材索引为空。");

        public void Restore()
        {
            var journal = SafePath(Root, Path.Combine("config", "material-view.json"));
            if (!File.Exists(journal)) return;
            var entries = Validate(Read(journal));

            foreach (var (_, source, destination) in entries)
            {
                bool atSource = File.Exists(source), atDestination = File.Exists(destination);
                if (atSource && atDestination) throw new IOException($"还原遇到同名冲突，请保留并处理两个文件后重试：{source}");
                if (!atSource && !atDestination) throw new IOException($"还原文件缺失，请找回文件后重试：{destination}");
                if (Directory.Exists(source) || Directory.Exists(destination)) throw new IOException("还原路径被文件夹占用。");
            }
            foreach (var (_, source, destination) in entries)
            {
                if (File.Exists(source)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                File.Move(destination, source);
            }
            File.Delete(journal);
        }

        public void Show(MaterialClassificationTool tool, CancellationToken token = default)
        {
            Restore();
            var index = Read(SafePath(Root, Path.Combine(FolderName(tool), "index.json")));
            if (index.Tool != tool) throw new InvalidDataException("索引与功能不匹配。");
            var entries = Validate(index);
            foreach (var (entry, source, destination) in entries)
            {
                token.ThrowIfCancellationRequested();
                if (!File.Exists(source) || new FileInfo(source).Length != entry.Length || Hash(source) != entry.Sha256)
                    throw new IOException($"原图已变化或缺失，请重新生成索引：{source}");
                if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException($"结果目录存在同名文件，请先处理冲突：{destination}");
            }
            token.ThrowIfCancellationRequested();
            WriteAtomic(JournalPath(Root), index);
            try
            {
                foreach (var (_, source, destination) in entries)
                {
                    token.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(source, destination);
                }
            }
            catch
            {

                try { Restore(); } catch { }
                throw;
            }
        }

        public MaterialIndexBuilder CreateBuilder(MaterialClassificationTool tool)
        {
            Restore();
            var input = SafePath(Root, "原图");
            if (!Directory.Exists(input)) throw new DirectoryNotFoundException("素材的原图目录不存在。");
            SafePath(Root, FolderName(tool));
            var pending = new Stack<string>();
            pending.Push(input);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var child in Directory.EnumerateFileSystemEntries(directory))
                {
                    SafePath(input, Path.GetRelativePath(input, child));
                    if (Directory.Exists(child)) pending.Push(child);
                }
            }
            return new MaterialIndexBuilder(Root, tool);
        }
    }
}

public sealed class MaterialIndexBuilder
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MaterialClassificationStore.IndexEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _destinations = new(StringComparer.OrdinalIgnoreCase) { "index.json" };
    private readonly string _root;
    private readonly MaterialClassificationTool _tool;
    public string InputPath => Path.Combine(_root, "原图");
    public string OutputPath => Path.Combine(_root, MaterialClassificationStore.FolderName(_tool));
    internal MaterialIndexBuilder(string root, MaterialClassificationTool tool) { _root = root; _tool = tool; }

    public string RecordImage(string source, string destination)
    {
        lock (_gate)
        {
            MaterialClassificationSession.Current?.Token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(InputPath, Path.GetFullPath(source));
            MaterialClassificationStore.SafePath(InputPath, relative);
            if (_entries.TryGetValue(relative, out var previous)) return Path.Combine(OutputPath, previous.Destination);
            var target = Path.GetRelativePath(OutputPath, Path.GetFullPath(destination));
            MaterialClassificationStore.SafePath(OutputPath, target);
            if (target.Equals("index.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("index.json 是保留文件名。");
            var sidecars = new[] { ".txt", ".caption", ".json" }
                .Select(ext => Path.ChangeExtension(source, ext)).Where(File.Exists).ToArray();
            var proposed = target;
            for (int suffix = 2; _destinations.Contains(target)
                || sidecars.Any(file => _destinations.Contains(Path.ChangeExtension(target, Path.GetExtension(file)))); suffix++)
                target = Path.Combine(Path.GetDirectoryName(proposed) ?? "", Path.GetFileNameWithoutExtension(proposed) + "_" + suffix + Path.GetExtension(proposed));
            Add(source, target);
            foreach (var file in sidecars)
            {

                var sidecarRelative = Path.GetRelativePath(InputPath, file);
                if (!_entries.ContainsKey(sidecarRelative)) Add(file, Path.ChangeExtension(target, Path.GetExtension(file)));
            }
            return Path.Combine(OutputPath, target);
        }
    }

    private void Add(string source, string destination)
    {
        var relative = Path.GetRelativePath(InputPath, Path.GetFullPath(source));
        MaterialClassificationStore.SafePath(InputPath, relative);
        MaterialClassificationStore.SafePath(OutputPath, destination);
        var info = new FileInfo(source);
        _entries.Add(relative, new MaterialClassificationStore.IndexEntry
        {
            Source = relative, Destination = destination, Length = info.Length, Sha256 = MaterialClassificationStore.Hash(source)
        });
        _destinations.Add(destination);
    }

    public void Publish()
    {
        lock (_gate)
            MaterialClassificationStore.WriteAtomic(MaterialClassificationStore.IndexPath(_root, _tool), new MaterialClassificationStore.IndexDocument
            {
                Tool = _tool, Entries = _entries.Values.OrderBy(x => x.Source, StringComparer.OrdinalIgnoreCase).ToList()
            });
    }
}
