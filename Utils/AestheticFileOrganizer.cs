using System.IO;

namespace CyanTools.Utils;

public static class AestheticFileOrganizer
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff" };
    private static readonly string[] Sidecars = { ".txt", ".json", ".caption" };

    public static IReadOnlyList<string> FindImages(string input, bool recursive, string? excludedOutput, CancellationToken token)
    {
        string path = Path.GetFullPath(input);
        if (File.Exists(path))
        {
            if (!Extensions.Contains(Path.GetExtension(path))) throw new InvalidDataException("Unsupported image format.");
            return new[] { path };
        }
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            if (excludedOutput != null && IsWithin(directory, excludedOutput)) continue;
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                token.ThrowIfCancellationRequested();
                if (Extensions.Contains(Path.GetExtension(file))) result.Add(file);
            }
            if (recursive)
                foreach (string child in Directory.EnumerateDirectories(directory))
                    if (!File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)) pending.Push(child);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    public static void ValidateOutput(string input, string output)
    {
        string inputDirectory = File.Exists(input) ? Path.GetDirectoryName(Path.GetFullPath(input))! : Path.GetFullPath(input);
        if (IsWithin(inputDirectory, output))
            throw new InvalidOperationException(LocalizationManager.Translate("输出路径不能是输入路径或其上级目录。"));
    }

    public static bool IsWithin(string path, string root)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    public static string CopyClassified(string source, string inputRoot, string output, string label, CancellationToken token)
    {
        if (!AestheticScoringProcessor.Labels.Contains(label)) throw new ArgumentException("Unknown aesthetic label.", nameof(label));
        string relative = Path.GetRelativePath(Path.GetFullPath(inputRoot), Path.GetDirectoryName(Path.GetFullPath(source))!);
        string directory = Path.GetFullPath(Path.Combine(output, label, relative));
        if (!IsWithin(directory, Path.Combine(output, label))) throw new InvalidOperationException("Image is outside the input root.");
        token.ThrowIfCancellationRequested();
        if (MaterialClassificationSession.Current?.Builder is { } index)
            return index.RecordImage(source, Path.Combine(directory, Path.GetFileName(source)));
        Directory.CreateDirectory(directory);
        string stem = Path.GetFileNameWithoutExtension(source);
        string extension = Path.GetExtension(source);
        var sourceFiles = new[] { source }.Concat(Sidecars.Select(x => Path.ChangeExtension(source, x)).Where(File.Exists)).ToArray();

        string targetStem = stem;
        for (int suffix = 1; new[] { extension }.Concat(Sidecars).Any(x => File.Exists(Path.Combine(directory, targetStem + x))); suffix++)
            targetStem = stem + "_" + suffix;

        var created = new List<string>();
        var staged = new List<(string Temporary, string Destination)>();
        try
        {
            foreach (string file in sourceFiles)
            {
                token.ThrowIfCancellationRequested();
                string destination = Path.Combine(directory, targetStem + Path.GetExtension(file));
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
                using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    staged.Add((temporary, destination));
                    using var original = File.OpenRead(file);
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = original.Read(buffer)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        target.Write(buffer, 0, read);
                    }
                }
            }
            foreach (var (temporary, destination) in staged)
            {
                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination);
                created.Add(destination);
            }
            token.ThrowIfCancellationRequested();
            return created[0];
        }
        catch
        {
            foreach (string file in created) File.Delete(file);
            throw;
        }
        finally
        {
            foreach (var (temporary, _) in staged)
                if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
