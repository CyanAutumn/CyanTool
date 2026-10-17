using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CyanTools.Utils;

internal static class DatasetLibraryCredentials
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyanTools", "dataset-library-credentials");

    public static string? Store(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var bytes = Encoding.UTF8.GetBytes(token);
        var id = Convert.ToHexString(SHA256.HashData(bytes));
        var path = Path.Combine(Root, id + ".bin");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Root);
            File.WriteAllBytes(path, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }
        return id;
    }

    public static string? Read(string? id)
    {
        if (string.IsNullOrEmpty(id)) return string.Empty;
        if (id.Length != 64 || !System.Linq.Enumerable.All(id, Uri.IsHexDigit)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(Root, id + ".bin")), null, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }
}
