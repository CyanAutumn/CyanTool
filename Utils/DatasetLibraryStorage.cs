using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;

namespace CyanTools.Utils;

public static class DatasetLibraryStorage
{
    public static bool HasSupportedImageExtension(string path) =>
        string.Equals(Path.GetExtension(path), ".psd", StringComparison.OrdinalIgnoreCase) || DownloadedImageValidator.HasSupportedImageExtension(path);

    public static bool CanDecodeImage(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".psd", StringComparison.OrdinalIgnoreCase)) return DownloadedImageValidator.CanDecodeImage(path);
        try { using var image = new ImageMagick.MagickImage(path); return image.Width > 0 && image.Height > 0; }
        catch { return false; }
    }
    public static Regex FolderMatcher(string template)
    {
        if (string.IsNullOrWhiteSpace(template) || template.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || template is "." or ".." || !template.Contains("{id}", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("素材文件夹名必须是包含 {id} 的单级文件夹模板。");
        var pattern = Regex.Escape(template);
        foreach (var token in new[] { "id", "name", "artist", "time" })
            pattern = pattern.Replace(Regex.Escape("{" + token + "}"),
                token == "id" ? "(?<id>.+?)" : "(?<" + token + ">.+)", StringComparison.OrdinalIgnoreCase);
        return new Regex("^" + pattern + "$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }

    public static string? FindExisting(string path)
    {
        if (File.Exists(path) && (!HasSupportedImageExtension(path) || CanDecodeImage(path)))
            return path;
        if (!HasSupportedImageExtension(path) || !Directory.Exists(Path.GetDirectoryName(path))) return null;
        return Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".*")
            .FirstOrDefault(candidate => string.Equals(Path.GetFileNameWithoutExtension(candidate), Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)
                && HasSupportedImageExtension(candidate) && CanDecodeImage(candidate));
    }

    public static void ConvertDownloadedImage(string path, string format)
    {
        if (format == "原格式" || !HasSupportedImageExtension(path)) return;
        var extension = format.ToLowerInvariant();
        if (!new[] { "png", "jpg", "jpeg", "bmp", "webp" }.Contains(extension)) throw new ArgumentException("不支持的输出格式");
        var target = Path.ChangeExtension(path, extension);
        if (string.Equals(path, target, StringComparison.OrdinalIgnoreCase)) return;
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp." + extension;
        try
        {
            if (string.Equals(Path.GetExtension(path), ".psd", StringComparison.OrdinalIgnoreCase))
            {
                using var image = new ImageMagick.MagickImage(path);
                image.Write(temporary);
            }
            else { using var image = Image.Load(path); image.Save(temporary); }
            File.Move(temporary, target, true);
            File.Delete(path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
