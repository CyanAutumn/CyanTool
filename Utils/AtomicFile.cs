using System;
using System.IO;
using System.Text;

namespace CyanTools.Utils;


public static class AtomicFile
{
    private static readonly object[] Gates = System.Linq.Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
    public static void WriteAllText(string path, string contents)
        => Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
            writer.Write(contents);
            writer.Flush();
        });

    public static void Write(string path, Action<Stream> write)
    {
        string destination = Path.GetFullPath(path);
        object gate = Gates[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(destination) % (uint)Gates.Length];
        lock (gate) WriteCore(destination, write);
    }

    private static void WriteCore(string destination, Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
