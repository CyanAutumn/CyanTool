using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CyanTools.Model;
using OpenCvSharp;

namespace CyanTools.Utils {
    public class ImageProcessor {
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
            IProgress<string> progress) {
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

                var analysisResult = await AnalyzeImageAsync(imagePath, prompt, progress);

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

        private async Task<AnalysisResult> AnalyzeImageAsync(string imagePath, string prompt,
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
                        Content = new List<ContentPart>
                        {
                            new ContentPart { Type = "text", Text = prompt },
                            new ContentPart { Type = "image_url", ImageUrl = new ImageUrlData { Url = $"data:image/jpeg;base64,{base64Image}" } }
                        }
                    }
                }
            };

            try {
                var response = await HttpClient.PostAsJsonAsync($"{LmStudioApiUrl}/chat/completions", request);
                response.EnsureSuccessStatusCode();

                var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse>();
                var modelResponse = apiResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

                if (string.IsNullOrEmpty(modelResponse)) {
                    progress.Report($"  - Failed to parse response for {Path.GetFileName(imagePath)}");
                    return null;
                }


                var cleanJson = modelResponse.Replace("```json", "").Replace("```", "").Trim();
                var analysisResult = JsonSerializer.Deserialize<AnalysisResult>(cleanJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return analysisResult;
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