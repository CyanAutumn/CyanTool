using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CyanTools.Model;
using OpenCvSharp;

namespace CyanTools.Utils {
    public class ImageProcessor {
        private const string JsonOnlySystemPrompt =
            "Respond with a single JSON object only, matching the schema {\"level\":1..5,\"reason\":\"...\"}. " +
            "Do not output analysis, chain-of-thought, markdown, or extra text.";

        public static void ApplyGaussianBlur(string inputPath, string outputPath, int blurRadius)
        {
            using (Mat src = Cv2.ImRead(inputPath, ImreadModes.Color))
            {
                if (src.Empty())
                {
                    throw new FileNotFoundException($"无法加载图片: {inputPath}");
                }

                using (Mat dst = new Mat())
                {

                    int kSize = blurRadius;
                    if (kSize % 2 == 0)
                    {
                        kSize += 1;
                    }
                    Cv2.GaussianBlur(src, dst, new OpenCvSharp.Size(kSize, kSize), 0);
                    Cv2.ImWrite(outputPath, dst);
                }
            }
        }

        private static readonly HttpClient HttpClient = new HttpClient();
        private const string LmStudioApiUrl = "http://127.0.0.1:1234/v1";

        public async Task<List<ModelData>> GetLoadedModelsAsync() {
            try {
                var response = await HttpClient.GetAsync($"{LmStudioApiUrl}/models");
                response.EnsureSuccessStatusCode();
                var loadedModels = await response.Content.ReadFromJsonAsync<LoadedModelsModel>();
                return loadedModels?.Data ?? new List<ModelData>();
            }
            catch (Exception ex) {
                Console.WriteLine($"Error fetching models: {ex.Message}");
                return new List<ModelData>();
            }
        }

        public async Task ProcessImagesAsync(string prompt, string workingDirectory, IEnumerable<string> destPaths,
            bool useReasoning, IProgress<string> progress) {
            if (string.IsNullOrWhiteSpace(prompt)) {
                progress.Report("错误: 主输入框的提示词不能为空。");
                return;
            }

            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory)) {
                progress.Report("Error: Working directory is not valid.");
                return;
            }


            var destDirs = destPaths.ToList();
            var dirOther = Path.Combine(workingDirectory, "other");
            try {
                foreach (var dir in destDirs) {
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }

                Directory.CreateDirectory(dirOther);
            }
            catch (Exception ex) {
                progress.Report($"Error creating directories: {ex.Message}");
                return;
            }

            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var imageFiles = Directory.GetFiles(workingDirectory)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            if (!imageFiles.Any()) {
                progress.Report("No images found in the source directory.");
                return;
            }

            progress.Report($"Found {imageFiles.Count} images to process.");

            foreach (var imagePath in imageFiles) {
                var fileName = Path.GetFileName(imagePath);
                progress.Report($"Processing: {fileName}");

                var analysisResult = await AnalyzeImageAsync(imagePath, prompt, useReasoning, progress);

                if (analysisResult == null) {
                    progress.Report($"  -> Skipped (error): {fileName}");
                    continue;
                }

                var targetDir = analysisResult.Level > 0 && analysisResult.Level <= destDirs.Count
                    ? destDirs[analysisResult.Level - 1]
                    : dirOther;

                MoveFileWithCompanion(imagePath, targetDir, progress);
                progress.Report(
                    $"  -> Moved to level {analysisResult.Level}: {fileName}. Reason: {analysisResult.Reason}");
            }

            progress.Report("--- Finished Image Filtering Process ---");
        }

        private async Task<AnalysisResult> AnalyzeImageAsync(string imagePath, string prompt, bool useReasoning,
            IProgress<string> progress) {
            var base64Image = EncodeImageToBase64(imagePath);
            if (base64Image == null) {
                progress.Report($"  - Error encoding image: {Path.GetFileName(imagePath)}");
                return null;
            }

            var request = new ApiRequest
            {
                Messages = new List<CyanTools.Model.Message>
                {
                    new CyanTools.Model.Message
                    {
                        Role = "system",
                        Content = new List<ContentPart>
                        {
                            new ContentPart { Type = "text", Text = JsonOnlySystemPrompt }
                        }
                    },
                    new CyanTools.Model.Message
                    {
                        Content = new List<ContentPart>
                        {
                            new ContentPart { Type = "text", Text = prompt },
                            new ContentPart { Type = "image_url", ImageUrl = new ImageUrlData { Url = $"data:image/jpeg;base64,{base64Image}" } }
                        }
                    }
                },
                Stream = false,
                ResponseFormat = useReasoning ? BuildJsonSchemaResponseFormat() : null
            };

            try {
                var (isSuccess, statusCode, body) = await SendChatRequestAsync(request);
                if (!isSuccess) {
                    progress.Report($"  - API request failed ({(int)statusCode} {statusCode}): {Truncate(body)}");
                    if (useReasoning && statusCode == HttpStatusCode.BadRequest && request.ResponseFormat != null) {
                        progress.Report("  - Retrying without response_format...");
                        request.ResponseFormat = null;
                        var retry = await SendChatRequestAsync(request);
                        if (!retry.IsSuccess) {
                            progress.Report($"  - API request failed ({(int)retry.StatusCode} {retry.StatusCode}): {Truncate(retry.Body)}");
                            return null;
                        }

                        body = retry.Body;
                    } else {
                        return null;
                    }
                }

                var apiResponse = JsonSerializer.Deserialize<ApiResponse>(body);
                var modelResponse = apiResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

                if (string.IsNullOrEmpty(modelResponse)) {
                    progress.Report($"  - Failed to parse response for {Path.GetFileName(imagePath)}");
                    return null;
                }

                progress.Report($"  - LLM response: {BuildLogResponse(modelResponse)}");

                if (TryExtractJsonObject(modelResponse, out var jsonPayload)
                    && TryParseAnalysisResultFromJson(jsonPayload, out var jsonResult)) {
                    return jsonResult;
                }

                if (!TryGetParseSource(modelResponse, out var parseSource)) {
                    if (HasThinkTag(modelResponse)) {
                        progress.Report("  - Model returned reasoning only. Use a non-reasoning model or enforce JSON-only output.");
                    }
                    progress.Report($"  - No final result found in response for {Path.GetFileName(imagePath)}");
                    return null;
                }

                if (!TryParseLevel(parseSource, out var level)) {
                    progress.Report($"  - No level keyword found in response for {Path.GetFileName(imagePath)}");
                    return null;
                }

                return new AnalysisResult
                {
                    Level = level,
                    Reason = parseSource.Trim()
                };
            }
            catch (HttpRequestException ex) {
                progress.Report($"  - API request failed for {Path.GetFileName(imagePath)}: {ex.Message}");
                return null;
            }
            catch (JsonException ex) {
                progress.Report(
                    $"  - Failed to deserialize JSON response for {Path.GetFileName(imagePath)}: {ex.Message}");
                return null;
            }
            catch (Exception ex) {
                progress.Report($"  - An unexpected error occurred for {Path.GetFileName(imagePath)}: {ex.Message}");
                return null;
            }
        }

        private string EncodeImageToBase64(string filePath) {
            try {
                byte[] imageBytes = File.ReadAllBytes(filePath);
                return Convert.ToBase64String(imageBytes);
            }
            catch (Exception ex) {

                Console.WriteLine($"Error encoding image {filePath}: {ex.Message}");
                return null;
            }
        }

        private static ResponseFormat BuildJsonSchemaResponseFormat() {
            return new ResponseFormat
            {
                Type = "json_schema",
                JsonSchema = new ResponseJsonSchema
                {
                    Name = "image_classification",
                    Strict = true,
                    Schema = new JsonSchemaDefinition
                    {
                        Type = "object",
                        Properties = new Dictionary<string, JsonSchemaProperty>
                        {
                            { "level", new JsonSchemaProperty { Type = "integer", Minimum = 1, Maximum = 5 } },
                            { "reason", new JsonSchemaProperty { Type = "string", MaxLength = 500 } }
                        },
                        Required = new List<string> { "level", "reason" },
                        AdditionalProperties = false
                    }
                }
            };
        }

        private static async Task<(bool IsSuccess, HttpStatusCode StatusCode, string Body)> SendChatRequestAsync(ApiRequest request) {
            using var response = await HttpClient.PostAsJsonAsync($"{LmStudioApiUrl}/chat/completions", request);
            var body = await response.Content.ReadAsStringAsync();
            return (response.IsSuccessStatusCode, response.StatusCode, body);
        }

        private static bool TryParseAnalysisResultFromJson(string json, out AnalysisResult result) {
            result = null;
            if (string.IsNullOrWhiteSpace(json)) {
                return false;
            }

            try {
                var parsed = JsonSerializer.Deserialize<AnalysisResult>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed == null || parsed.Level < 1 || parsed.Level > 5) {
                    return false;
                }

                parsed.Reason = parsed.Reason?.Trim() ?? string.Empty;
                result = parsed;
                return true;
            }
            catch (JsonException) {
                return false;
            }
        }

        private static bool TryParseLevel(string text, out int level) {
            level = 0;
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }

            var matches = Regex.Matches(text, @"\blevel\s*[:=]?\s*""?([1-5])\b", RegexOptions.IgnoreCase);
            if (matches.Count == 0) {
                return false;
            }

            var match = matches[matches.Count - 1];
            return int.TryParse(match.Groups[1].Value, out level);
        }

        private static string BuildLogResponse(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return text ?? string.Empty;
            }

            if (TryExtractJsonObject(text, out var json)) {
                return json;
            }

            if (TryExtractFinalSection(text, out var finalText)) {
                return finalText.Trim();
            }

            if (!HasThinkTag(text)) {
                return text;
            }

            return "[THINK] (omitted)";
        }

        private static bool TryGetParseSource(string text, out string parseSource) {
            parseSource = text;
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }

            if (TryExtractJsonObject(text, out var json)) {
                parseSource = json;
                return true;
            }

            if (TryExtractFinalSection(text, out var finalText)) {
                parseSource = finalText;
                return true;
            }

            if (!HasThinkTag(text)) {
                return true;
            }

            return false;
        }

        private static bool HasThinkTag(string text) {
            var markers = new[]
            {
                "[THINK]",
                "<think>",
                "</think>",
                "[analysis]",
                "<analysis>",
                "</analysis>"
            };

            foreach (var marker in markers) {
                if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractFinalSection(string text, out string finalText) {
            finalText = null;
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }

            var markers = new[]
            {
                "[final]",
                "final answer",
                "final:",
                "final",
                "最终",
                "结果",
                "结论",
                "答案",
                "输出"
            };

            int bestIndex = -1;
            string bestMarker = null;
            foreach (var marker in markers) {
                int index = text.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index > bestIndex) {
                    bestIndex = index;
                    bestMarker = marker;
                }
            }

            if (bestIndex < 0 || string.IsNullOrEmpty(bestMarker)) {
                return false;
            }

            int start = bestIndex + bestMarker.Length;
            while (start < text.Length && (text[start] == ':' || text[start] == '：' || char.IsWhiteSpace(text[start]))) {
                start++;
            }

            if (start >= text.Length) {
                return false;
            }

            finalText = text.Substring(start);
            return !string.IsNullOrWhiteSpace(finalText);
        }

        private static bool TryExtractJsonObject(string text, out string json) {
            json = null;
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }

            foreach (var fencedBlock in EnumerateCodeFenceBlocks(text)) {
                if (TryExtractJsonObjectCore(fencedBlock, out json)) {
                    return true;
                }
            }

            return TryExtractJsonObjectCore(text, out json);
        }

        private static IEnumerable<string> EnumerateCodeFenceBlocks(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                yield break;
            }

            var matches = Regex.Matches(text, @"(```|~~~)(?:\s*\w+)?\s*(?<body>.*?)(\1)",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match match in matches) {
                var body = match.Groups["body"].Value;
                if (!string.IsNullOrWhiteSpace(body)) {
                    yield return body;
                }
            }
        }

        private static bool TryExtractJsonObjectCore(string text, out string json) {
            json = null;
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }

            int depth = 0;
            int start = -1;
            int lastStart = -1;
            int lastEnd = -1;

            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (c == '{') {
                    if (depth == 0) {
                        start = i;
                    }
                    depth++;
                } else if (c == '}') {
                    if (depth == 0) {
                        continue;
                    }
                    depth--;
                    if (depth == 0 && start >= 0) {
                        lastStart = start;
                        lastEnd = i;
                    }
                }
            }

            if (lastStart < 0 || lastEnd < 0) {
                return false;
            }

            json = text.Substring(lastStart, lastEnd - lastStart + 1);
            return true;
        }

        private static string Truncate(string value, int maxLength = 500) {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength) + "...";
        }

        private void MoveFileWithCompanion(string sourcePath, string destinationDir, IProgress<string> progress) {
            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(destinationDir, fileName);

            try {
                File.Move(sourcePath, destPath);


                var companionPath = Path.ChangeExtension(sourcePath, ".txt");
                if (File.Exists(companionPath)) {
                    var destCompanionPath = Path.Combine(destinationDir, Path.GetFileName(companionPath));
                    File.Move(companionPath, destCompanionPath);
                }
            }
            catch (Exception ex) {
                progress.Report($"  - Error moving file {fileName}: {ex.Message}");
            }
        }
    }
}
