using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CyanTools.Utils;

public sealed class ToriiGateTaggingOptions
{
    public string InputDirectory { get; init; } = string.Empty;
    public string Endpoint { get; init; } = TaggerSeriesCatalog.ToriiGateDefaultEndpoint;
    public string ModelName { get; init; } = TaggerSeriesCatalog.ToriiGateDefaultModelName;
    public string ApiToken { get; init; } = string.Empty;
    public ToriiGateCaptionFormatDefinition? CaptionFormat { get; init; }
    public double MaxMegapixels { get; init; } = 1.0;
    public int MaxTokens { get; init; } = 1024;
    public double Temperature { get; init; } = 0.5;
    public int MaxParallelRequests { get; init; } = 1;
    public IReadOnlyList<string> CustomTags { get; init; } = Array.Empty<string>();
    public ExistingTxtHandlingMode ExistingTxtHandlingMode { get; init; } = ExistingTxtHandlingMode.Overwrite;
}






public static class ToriiGateTaggingProcessor
{
    private static readonly HttpClient HttpClient = new();
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp"
    };

    public static async Task ProcessDirectoryAsync(
        ToriiGateTaggingOptions options,
        IProgress<string> progress,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);
        ValidateOptions(options);

        var imageFiles = Directory.EnumerateFiles(options.InputDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (imageFiles.Count == 0)
        {
            progress.Report("未在输入路径顶层找到可处理的图片。");
            return;
        }

        progress.Report($"ToriiGate：将通过 {options.Endpoint} 处理 {imageFiles.Count} 张图片。");

        int completed = 0;
        int processed = 0;
        int skipped = 0;
        int failed = 0;
        int parallelism = Math.Clamp(options.MaxParallelRequests, 1, 16);
        using var gate = new SemaphoreSlim(parallelism, parallelism);

        var tasks = imageFiles.Select(async imagePath =>
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string captionPath = Path.ChangeExtension(imagePath, ".txt");
                bool hasExistingCaption = File.Exists(captionPath);
                if (TagTextMergeHelper.ShouldSkipExistingTxt(hasExistingCaption, options.ExistingTxtHandlingMode))
                {
                    Interlocked.Increment(ref skipped);
                    int current = Interlocked.Increment(ref completed);
                    progress.Report($"Skipped [{current}/{imageFiles.Count}]: {Path.GetFileName(imagePath)} (同名txt已存在)");
                    return;
                }

                string imageDataUrl = await EncodeImageAsDataUrlAsync(imagePath, options.MaxMegapixels, token).ConfigureAwait(false);
                string caption = await RequestCaptionAsync(options, imageDataUrl, token).ConfigureAwait(false);
                string outputText = ToriiGateCaptionOutput.PrependCustomTags(caption, options.CustomTags);

                await WriteCaptionAsync(captionPath, outputText, hasExistingCaption, options.ExistingTxtHandlingMode, token).ConfigureAwait(false);
                Interlocked.Increment(ref processed);
                int successCurrent = Interlocked.Increment(ref completed);
                progress.Report($"Processed [{successCurrent}/{imageFiles.Count}]: {Path.GetFileName(imagePath)}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                int current = Interlocked.Increment(ref completed);
                progress.Report($"Error [{current}/{imageFiles.Count}] {Path.GetFileName(imagePath)}: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        progress.Report($"ToriiGate 打标完成。已处理 {processed} 张，跳过 {skipped} 张，失败 {failed} 张。");
    }

    private static void ValidateOptions(ToriiGateTaggingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.InputDirectory) || !Directory.Exists(options.InputDirectory))
        {
            throw new InvalidOperationException("输入路径不存在或无效。");
        }

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("ToriiGate 接口地址必须是有效的 http 或 https URL。");
        }

        if (string.IsNullOrWhiteSpace(options.ModelName))
        {
            throw new InvalidOperationException("ToriiGate 模型名称不能为空。");
        }

        if (options.MaxMegapixels <= 0 || options.MaxMegapixels > 64)
        {
            throw new InvalidOperationException("最大输入分辨率必须在 0 到 64 MP 之间。");
        }

        if (options.MaxTokens is < 1 or > 32768)
        {
            throw new InvalidOperationException("最大输出 token 必须在 1 到 32768 之间。");
        }

        if (options.Temperature is < 0 or > 2)
        {
            throw new InvalidOperationException("Temperature 必须在 0 到 2 之间。");
        }
    }

    private static async Task<string> EncodeImageAsDataUrlAsync(string imagePath, double maxMegapixels, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using Image<Rgb24> image = Image.Load<Rgb24>(imagePath);
            double maxPixels = maxMegapixels * 1_000_000d;
            if (image.Width * (double)image.Height > maxPixels)
            {
                double scale = Math.Sqrt(maxPixels / (image.Width * (double)image.Height));
                int width = Math.Max(1, (int)Math.Round(image.Width * scale));
                int height = Math.Max(1, (int)Math.Round(image.Height * scale));
                image.Mutate(context => context.Resize(width, height, KnownResamplers.Lanczos3));
            }

            using var stream = new MemoryStream();
            image.Save(stream, new JpegEncoder { Quality = 95 });
            return "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
        }, token).ConfigureAwait(false);
    }

    private static async Task<string> RequestCaptionAsync(ToriiGateTaggingOptions options, string imageDataUrl, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = new StringContent(
                ToriiGateOpenAiRequestFactory.BuildJson(options, imageDataUrl),
                Encoding.UTF8,
                "application/json")
        };

        if (!string.IsNullOrWhiteSpace(options.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken.Trim());
        }

        using HttpResponseMessage response = await HttpClient.SendAsync(request, token).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string detail = responseBody.Length > 1000 ? responseBody[..1000] : responseBody;
            throw new HttpRequestException($"ToriiGate 接口返回 {(int)response.StatusCode} {response.ReasonPhrase}：{detail}");
        }

        return ToriiGateOpenAiRequestFactory.ExtractCaption(responseBody);
    }

    private static async Task WriteCaptionAsync(
        string captionPath,
        string caption,
        bool hasExistingCaption,
        ExistingTxtHandlingMode handlingMode,
        CancellationToken token)
    {
        string output = caption;
        if (hasExistingCaption && handlingMode == ExistingTxtHandlingMode.MergeUnique)
        {
            string existing = await File.ReadAllTextAsync(captionPath, token).ConfigureAwait(false);
            output = ToriiGateCaptionOutput.MergeUniqueCaption(existing, caption);
        }

        await File.WriteAllTextAsync(captionPath, output, token).ConfigureAwait(false);
    }
}

public static class ToriiGateOpenAiRequestFactory
{
    public static string BuildJson(ToriiGateTaggingOptions options, string imageDataUrl)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(imageDataUrl))
        {
            throw new ArgumentException("图片数据不能为空。", nameof(imageDataUrl));
        }

        ToriiGateCaptionFormatDefinition format = options.CaptionFormat
            ?? TaggerSeriesCatalog.GetToriiGateCaptionFormat(null);

        var payload = new
        {
            model = options.ModelName,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "text", text = TaggerSeriesCatalog.ToriiGateSystemPrompt }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = imageDataUrl } },
                        new { type = "text", text = format.Instruction }
                    }
                }
            },
            max_tokens = options.MaxTokens,
            temperature = options.Temperature,
            stream = false
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string ExtractCaption(string responseBody)
    {
        using JsonDocument response = JsonDocument.Parse(responseBody);
        if (!response.RootElement.TryGetProperty("choices", out JsonElement choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out JsonElement message) ||
            !message.TryGetProperty("content", out JsonElement content))
        {
            throw new InvalidOperationException("ToriiGate 接口响应缺少 choices[0].message.content。");
        }

        string caption = content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                Environment.NewLine,
                content.EnumerateArray()
                    .Where(part => part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out _))
                    .Select(part => part.GetProperty("text").GetString())
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => string.Empty
        };

        caption = caption.Trim();
        return !string.IsNullOrWhiteSpace(caption)
            ? caption
            : throw new InvalidOperationException("ToriiGate 接口没有返回可用 caption。");
    }
}

public static class ToriiGateCaptionOutput
{
    public static string PrependCustomTags(string caption, IReadOnlyList<string>? customTags)
    {
        string normalizedCaption = (caption ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedCaption))
        {
            throw new InvalidOperationException("ToriiGate 没有生成可写入的 caption。");
        }

        var tags = (customTags ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return tags.Count == 0 ? normalizedCaption : string.Join(", ", tags) + ", " + normalizedCaption;
    }

    public static string MergeUniqueCaption(string existingCaption, string newCaption)
    {
        string existing = (existingCaption ?? string.Empty).Trim();
        string incoming = (newCaption ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(existing))
        {
            return incoming;
        }

        if (string.IsNullOrWhiteSpace(incoming) ||
            string.Equals(existing, incoming, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return existing + Environment.NewLine + incoming;
    }
}
