using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace CyanTools.Utils;


public sealed class AestheticModelStore
{
    public const string ModelName = "swinv2pv3_v0_448_ls0.2_x";
    public const string Revision = "a83ab545e1d2a869f1180b99b2a7dee22ec3b97e";
    public const long ModelSize = 418260629;
    public const long SamplesSize = 20515784;
    public const string ModelSha256 = "174a988ff458e84bd28c2a71cd22a5e7e160ec2518e501efbdfc3de1d09d82a2";
    public const string MetadataSha256 = "c8b90241aa5f99ad98b17a224711688ae1f7fa29ea9aeb4f7f010435c26d0eef";
    public const string SamplesSha256 = "f37d22a7fbfb2748c007cefddf7b4c95602cb5037f847a81ab47fc5d4c1df013";
    private const string BaseUrl = "https://huggingface.co/deepghs/anime_aesthetic/resolve/" + Revision + "/" + ModelName;
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Models", "anime_aesthetic", ModelName);

    public static bool IsPresent(string directory)
    {
        try
        {
            return new FileInfo(Path.Combine(directory, "model.onnx")).Length == ModelSize
                && File.Exists(Path.Combine(directory, "meta.json"))
                && new FileInfo(Path.Combine(directory, "samples.npz")).Length == SamplesSize;
        }
        catch { return false; }
    }

    public static async Task VerifyAsync(string directory, CancellationToken token)
    {
        await VerifyFileAsync(Path.Combine(directory, "meta.json"), MetadataSha256, token);
        await VerifyFileAsync(Path.Combine(directory, "samples.npz"), SamplesSha256, token);
        await VerifyFileAsync(Path.Combine(directory, "model.onnx"), ModelSha256, token);
    }

    public async Task DownloadAsync(string directory, IProgress<double>? progress, CancellationToken token,
        HttpClient? client = null)
    {
        await DownloadLock.WaitAsync(token);
        bool ownsClient = client == null;
        client ??= new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        try
        {
            Directory.CreateDirectory(directory);
            await DownloadFileAsync(client, directory, "meta.json", MetadataSha256, null, token);
            await DownloadFileAsync(client, directory, "samples.npz", SamplesSha256, null, token);
            await DownloadFileAsync(client, directory, "model.onnx", ModelSha256, progress, token);
            progress?.Report(100);
        }
        finally
        {
            if (ownsClient) client.Dispose();
            DownloadLock.Release();
        }
    }

    private static async Task DownloadFileAsync(HttpClient client, string directory, string name, string hash,
        IProgress<double>? progress, CancellationToken token)
    {
        string destination = Path.Combine(directory, name);
        if (File.Exists(destination))
        {
            try { await VerifyFileAsync(destination, hash, token); return; }
            catch (InvalidDataException) {                                        }
        }

        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await client.GetAsync(BaseUrl + "/" + name, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int lastPercent = -1;
                int count;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count), token);
                    total += count;
                    int percent = (int)Math.Min(99, total * 100 / ModelSize);
                    if (percent != lastPercent) { progress?.Report(percent); lastPercent = percent; }
                }
            }
            await VerifyFileAsync(temporary, hash, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task VerifyFileAsync(string path, string expectedHash, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 verification failed: {Path.GetFileName(path)}");
    }
}
