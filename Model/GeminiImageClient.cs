using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Model
{
    public sealed class GeminiImageClient
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        private readonly JsonSerializerOptions _serializerOptions;

        public GeminiImageClient(JsonSerializerOptions serializerOptions)
        {
            _serializerOptions = serializerOptions ?? throw new ArgumentNullException(nameof(serializerOptions));
        }

        public async Task<GeminiImageResult> GenerateAsync(GeminiImageRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var payload = BuildRequestPayload(request);
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{request.Model}:generateContent?key={request.ApiKey}";
            string jsonBody = JsonSerializer.Serialize(payload, _serializerOptions);

            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };

            try
            {
                using var response = await HttpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return GeminiImageResult.Failed(response.StatusCode, response.ReasonPhrase, Truncate(body));
                }

                return ParseResponse(body);
            }
            catch (TaskCanceledException ex)
            {
                return GeminiImageResult.Failed(null, "请求已取消或超时", ex.Message);
            }
            catch (Exception ex)
            {
                return GeminiImageResult.Failed(null, "请求异常", ex.Message);
            }
        }

        private GeminiImageResult ParseResponse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var result = new GeminiImageResult();
            var safety = new List<string>();

            if (root.TryGetProperty("promptFeedback", out var promptFeedback))
            {
                if (promptFeedback.TryGetProperty("blockReason", out var blockReason))
                {
                    result = result with { PromptFeedback = blockReason.GetString() };
                }
                else if (promptFeedback.TryGetProperty("block_reason", out var blockReasonSnake))
                {
                    result = result with { PromptFeedback = blockReasonSnake.GetString() };
                }
            }

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return result with
                {
                    Error = "未返回候选结果。",
                    SafetyRatings = safety
                };
            }

            var first = candidates[0];

            if (first.TryGetProperty("finishReason", out var finishReasonProp))
            {
                result = result with { FinishReason = finishReasonProp.GetString() };
            }
            else if (first.TryGetProperty("finish_reason", out var finishReasonSnake))
            {
                result = result with { FinishReason = finishReasonSnake.GetString() };
            }

            if (first.TryGetProperty("safetyRatings", out var safetyRatings))
            {
                safety.AddRange(safetyRatings.EnumerateArray()
                    .Select(s => s.TryGetProperty("category", out var category) ? category.GetString() : null)
                    .Where(s => !string.IsNullOrWhiteSpace(s))!
                    .Select(s => s!));
            }
            else if (first.TryGetProperty("safety_ratings", out var safetyRatingsSnake))
            {
                safety.AddRange(safetyRatingsSnake.EnumerateArray()
                    .Select(s => s.TryGetProperty("category", out var category) ? category.GetString() : null)
                    .Where(s => !string.IsNullOrWhiteSpace(s))!
                    .Select(s => s!));
            }

            byte[]? bytes = ExtractInlineBytes(first);
            if (bytes == null)
            {
                return result with
                {
                    Error = "未能在响应中找到图像数据。",
                    SafetyRatings = safety
                };
            }

            return result with
            {
                ImageBytes = bytes,
                SafetyRatings = safety
            };
        }

        private GeminiRequest BuildRequestPayload(GeminiImageRequest request)
        {
            var content = new GeminiContent
            {
                Role = "user",
                Parts = new List<GeminiPart>
                {
                    new() { Text = request.Prompt }
                }
            };

            foreach (var image in request.Images)
            {
                content.Parts.Add(new GeminiPart
                {
                    InlineData = new GeminiInlineData
                    {
                        MimeType = image.MimeType,
                        Data = Convert.ToBase64String(image.Data)
                    }
                });
            }

            var generationConfig = new GeminiGenerationConfig
            {
                Temperature = request.Temperature,
                ResponseModalities = new List<string> { "TEXT", "IMAGE" }
            };

            if (request.ImageSize.HasValue || !string.IsNullOrWhiteSpace(request.AspectRatio))
            {
                generationConfig.ImageConfig = new GeminiImageConfig
                {
                    AspectRatio = string.IsNullOrWhiteSpace(request.AspectRatio) ? null : request.AspectRatio,
                    ImageSize = request.ImageSize?.ToRequestValue()
                };
            }

            return new GeminiRequest
            {
                Contents = new List<GeminiContent> { content },
                GenerationConfig = generationConfig
            };
        }

        private byte[]? ExtractInlineBytes(JsonElement candidate)
        {
            if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
            {
                return null;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (!part.TryGetProperty("inlineData", out var inlineData) &&
                    !part.TryGetProperty("inline_data", out inlineData))
                {
                    continue;
                }

                if (!inlineData.TryGetProperty("data", out var dataProp))
                {
                    continue;
                }

                string? base64 = dataProp.GetString();
                if (string.IsNullOrWhiteSpace(base64))
                {
                    continue;
                }

                try
                {
                    return Convert.FromBase64String(base64);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private string Truncate(string text, int maxLength = 2000)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "...(truncated)";
        }

        private sealed class GeminiRequest
        {
            [JsonPropertyName("contents")]
            public List<GeminiContent> Contents { get; set; } = new();

            [JsonPropertyName("generationConfig")]
            public GeminiGenerationConfig? GenerationConfig { get; set; }
        }

        private sealed class GeminiContent
        {
            [JsonPropertyName("parts")]
            public List<GeminiPart> Parts { get; set; } = new();

            [JsonPropertyName("role")]
            public string Role { get; set; } = "user";
        }

        private sealed class GeminiPart
        {
            [JsonPropertyName("text")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Text { get; set; }

            [JsonPropertyName("inlineData")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public GeminiInlineData? InlineData { get; set; }
        }

        private sealed class GeminiInlineData
        {
            [JsonPropertyName("mimeType")]
            public string MimeType { get; set; } = string.Empty;

            [JsonPropertyName("data")]
            public string Data { get; set; } = string.Empty;
        }

        private sealed class GeminiGenerationConfig
        {
            [JsonPropertyName("temperature")]
            public double Temperature { get; set; }

            [JsonPropertyName("responseModalities")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<string>? ResponseModalities { get; set; }

            [JsonPropertyName("imageConfig")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public GeminiImageConfig? ImageConfig { get; set; }
        }

        private sealed class GeminiImageConfig
        {
            [JsonPropertyName("aspectRatio")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? AspectRatio { get; set; }

            [JsonPropertyName("imageSize")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? ImageSize { get; set; }
        }
    }

    public sealed record GeminiImageRequest
    {
        public required string ApiKey { get; init; }

        public required string Model { get; init; }

        public required string Prompt { get; init; }

        public double Temperature { get; init; }

        public GeminiImageSize? ImageSize { get; init; }

        public string? AspectRatio { get; init; }

        public IReadOnlyList<GeminiInlineImage> Images { get; init; } = Array.Empty<GeminiInlineImage>();
    }

    public sealed record GeminiInlineImage
    {
        public required string MimeType { get; init; }
        public required byte[] Data { get; init; }
        public string? DisplayName { get; init; }
    }

    public sealed record GeminiImageResult
    {
        public byte[]? ImageBytes { get; init; }
        public string? FinishReason { get; init; }
        public string? PromptFeedback { get; init; }
        public IReadOnlyList<string> SafetyRatings { get; init; } = Array.Empty<string>();
        public string? Error { get; init; }
        public HttpStatusCode? StatusCode { get; init; }
        public string? ErrorBody { get; init; }

        public bool Success => ImageBytes != null && string.IsNullOrWhiteSpace(Error);

        public static GeminiImageResult Failed(HttpStatusCode? statusCode, string? reason, string? body)
        {
            return new GeminiImageResult
            {
                StatusCode = statusCode,
                Error = string.IsNullOrWhiteSpace(reason) ? "请求失败" : reason,
                ErrorBody = body
            };
        }
    }

    public readonly struct GeminiImageSize : IEquatable<GeminiImageSize>
    {
        private static readonly string[] SupportedValues = { "1K", "2K", "4K" };

        public GeminiImageSize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("图片大小不能为空。", nameof(value));
            }

            Value = value.Trim().ToUpperInvariant();
        }

        public string Value { get; }

        public string ToRequestValue() => Value;

        public bool Equals(GeminiImageSize other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is GeminiImageSize other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public static bool TryParse(string? value, out GeminiImageSize size, out string? error)
        {
            size = default;
            error = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                error = "图片大小不能为空。";
                return false;
            }

            string normalized = value.Trim().ToUpperInvariant();
            if (!normalized.EndsWith("K", StringComparison.Ordinal))
            {
                error = "仅支持带大写 K 的规格，例如 1K、2K、4K。";
                return false;
            }

            if (!SupportedValues.Contains(normalized, StringComparer.Ordinal))
            {
                error = "仅支持 1K、2K、4K 分辨率。";
                return false;
            }

            size = new GeminiImageSize(normalized);
            return true;
        }
    }
}
