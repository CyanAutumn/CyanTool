using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public class TagData
    {
        public string Name { get; set; }
        public int Category { get; set; }
    }

    public class JsonTag
    {
        [JsonPropertyName("tag")]
        public string Tag { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }
    }

    public class TagCategoryOptions
    {
        public bool IncludeGeneral { get; set; } = true;
        public bool IncludeArtist { get; set; } = false;
        public bool IncludeCopyright { get; set; } = false;
        public bool IncludeCharacter { get; set; } = true;
        public bool IncludeMeta { get; set; } = false;
        public bool IncludeQuality { get; set; } = false;
        public bool IncludeRating { get; set; } = false;
        public bool IncludeModel { get; set; } = false;
    }

    public class TaggingProcessor
    {
        private string _modelPath;
        private string _tagsPath;

        private static readonly HttpClient httpClient = new HttpClient();
        private InferenceSession _session;
        private TagData[] _labels;

        public TaggingProcessor()
        {
        }

        public async Task InitializeAsync(string modelUrl, string tagsUrl, string modelDisplayName, IProgress<string> progress)
        {
            string appDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TaggerModels");
            Directory.CreateDirectory(appDataPath);

            _modelPath = Path.Combine(appDataPath, modelDisplayName + ".onnx");
            string tagsExtension = Path.GetExtension(tagsUrl);
            _tagsPath = Path.Combine(appDataPath, modelDisplayName + tagsExtension);

            await CheckAndDownloadFileAsync($"Model ({modelDisplayName})", modelUrl, _modelPath, progress);
            await CheckAndDownloadFileAsync($"Tags ({modelDisplayName})", tagsUrl, _tagsPath, progress);

            progress.Report("Loading model into memory...");
            await Task.Run(async () =>
            {
                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                _session = new InferenceSession(_modelPath, sessionOptions);


















                if (tagsExtension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    _labels = File.ReadAllLines(_tagsPath)
                                  .Skip(1)
                                  .Select(line =>
                                  {
                                      var parts = line.Split(',');

                                      if (parts.Length == 3) 
                                      {
                                           return new TagData
                                           { Name = parts[0].Replace("\"", "").Trim(), Category = int.Parse(parts[1]) };
                                      }
                                      else
                                      {
                                           return new TagData
                                           { Name = parts[1].Replace("\"", "").Trim(), Category = int.Parse(parts[2]) };
                                      }
                                  })
                                  .ToArray();
                }
                else if (tagsExtension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    await LoadTagsFromJsonAsync(_tagsPath);
                }
            });
            progress.Report("Initialization complete. Ready to tag.");
        }
        
        private async Task LoadTagsFromJsonAsync(string jsonPath)
        {
            var jsonString = await File.ReadAllTextAsync(jsonPath);
            var jsonTags = JsonSerializer.Deserialize<Dictionary<string, JsonTag>>(jsonString);

            _labels = jsonTags.OrderBy(kvp => int.Parse(kvp.Key))
                              .Select(kvp => kvp.Value)
                              .Select(jsonTag => new TagData
                              {
                                  Name = jsonTag.Tag,
                                  Category = ConvertCategory(jsonTag.Category)
                              }).ToArray();
        }

        private int ConvertCategory(string category) {
            return category switch {
                "General" => 0,
                "Artist" => 1,
                "Copyright" => 3,
                "Character" => 4,
                "Meta" => 5,
                "Quality" => 8,
                "Rating" => 9,
                "Model" => 10,
                _ => -1 
            };
        }
        private async Task CheckAndDownloadFileAsync(string fileDescription, string url, string destinationPath, IProgress<string> progress)
        {
            if (File.Exists(destinationPath))
            {
                progress.Report($"{fileDescription} already exists. Skipping download.");
                return;
            }

            progress.Report($"Downloading {fileDescription}...");

            try
            {
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                long totalBytesRead = 0;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    if (totalBytes.HasValue)
                    {
                        double percentage = (double)totalBytesRead / totalBytes.Value * 100;
                        progress.Report($"Downloading {fileDescription}: {totalBytesRead / 1024 / 1024:F2}MB / {totalBytes.Value / 1024 / 1024:F2}MB ({percentage:F2}%)");
                    }
                }
            }
            catch (Exception)
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                throw;
            }
        }

        public async Task ProcessDirectoryAsync(string inputPath, float generalThreshold, float characterThreshold, int inputSize, string inputFormat, TagCategoryOptions categoryOptions, IEnumerable<string> priorityTags, IEnumerable<string> customTags, IProgress<string> progress)
        {
            var imageFiles = Directory.GetFiles(inputPath, "*.*", SearchOption.TopDirectoryOnly)
                                      .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                  f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                  f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                  f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                                      .ToList();

            categoryOptions ??= new TagCategoryOptions();
            var priorityList = priorityTags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList() ?? new List<string>();
            var customList = customTags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).ToList() ?? new List<string>();

            int processedCount = 0;
            foreach (var imageFile in imageFiles)
            {
                try
                {
                    var tags = await Task.Run(() => ProcessImage(imageFile, generalThreshold, characterThreshold, inputSize, inputFormat, categoryOptions, priorityList));
                    var finalTags = MergeCustomTags(customList, tags);
                    string txtFile = Path.ChangeExtension(imageFile, ".txt");
                    File.WriteAllText(txtFile, string.Join(", ", finalTags));
                    processedCount++;
                    progress.Report($"Processed [{processedCount}/{imageFiles.Count}]: {Path.GetFileName(imageFile)}");
                }
                catch (Exception ex)
                {
                    progress.Report($"Error processing {Path.GetFileName(imageFile)}: {ex.Message}");
                }
            }
            progress.Report($"Tagging complete. Processed {processedCount} images.");
        }
        
        private List<TagInfo> ProcessImage(string imagePath, float generalThreshold, float characterThreshold, int inputSize, string inputFormat, TagCategoryOptions categoryOptions, IReadOnlyList<string> priorityTags) {
            categoryOptions ??= new TagCategoryOptions();

            using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(imagePath);

            int size = Math.Max(image.Width, image.Height);
            using var squareImage = new Image<Rgb24>(size, size);
            squareImage.Mutate(x => x.BackgroundColor(Color.White));
            int padX = (size - image.Width) / 2;
            int padY = (size - image.Height) / 2;
            squareImage.Mutate(x => x.DrawImage(image, new Point(padX, padY), 1f));

            squareImage.Mutate(x => x.Resize(inputSize, inputSize, KnownResamplers.Bicubic));

            string inputName = _session.InputMetadata.Keys.First();
            Tensor<float> inputTensor;

            if (inputFormat == "NCHW") {
                inputTensor = new DenseTensor<float>(new[] { 1, 3, inputSize, inputSize });
                squareImage.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < inputSize; y++) {
                        Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                        for (int x = 0; x < inputSize; x++) {
                            float r = pixelRow[x].R / 255.0f;
                            float g = pixelRow[x].G / 255.0f;
                            float b = pixelRow[x].B / 255.0f;

                            inputTensor[0, 0, y, x] = (b - 0.5f) / 0.5f;
                            inputTensor[0, 1, y, x] = (g - 0.5f) / 0.5f;
                            inputTensor[0, 2, y, x] = (r - 0.5f) / 0.5f;
                        }
                    }
                });
            }
            else
            {
                inputTensor = new DenseTensor<float>(new[] { 1, inputSize, inputSize, 3 });
                squareImage.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < inputSize; y++) {
                        Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                        for (int x = 0; x < inputSize; x++) {
                            inputTensor[0, y, x, 0] = pixelRow[x].B;
                            inputTensor[0, y, x, 1] = pixelRow[x].G;
                            inputTensor[0, y, x, 2] = pixelRow[x].R;
                        }
                    }
                });
            }

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };
            using var results = _session.Run(inputs);
            var outputSpan = results.First().AsTensor<float>().ToDenseTensor().Buffer.Span;

            var probs = new float[outputSpan.Length];
            if (LooksLikeProbabilities(outputSpan))
            {
                for (int i = 0; i < outputSpan.Length; i++)
                {
                    probs[i] = Math.Clamp(outputSpan[i], 0f, 1f);
                }
            }
            else
            {
                for (int i = 0; i < outputSpan.Length; i++)
                {
                    probs[i] = Sigmoid(outputSpan[i]);
                }
            }

            var ratingTags = new List<TagInfo>();
            var qualityTags = new List<TagInfo>();
            var characterTags = new List<TagInfo>();
            var copyrightTags = new List<TagInfo>();
            var artistTags = new List<TagInfo>();
            var generalTags = new List<TagInfo>();
            var metaTags = new List<TagInfo>();
            var modelTags = new List<TagInfo>();

            var ratingIndices = Enumerable.Range(0, _labels.Length).Where(i => _labels[i].Category == 9).ToArray();
            if (categoryOptions.IncludeRating && ratingIndices.Any()) {
                var maxIndex = ratingIndices.Aggregate((max, i) => probs[i] > probs[max] ? i : max);
                ratingTags.Add(new TagInfo { TagName = _labels[maxIndex].Name.Replace('_', ' '), Confidence = probs[maxIndex] });
            }

            var qualityIndices = Enumerable.Range(0, _labels.Length).Where(i => _labels[i].Category == 8).ToArray();
            if (categoryOptions.IncludeQuality && qualityIndices.Any()) {
                var maxIndex = qualityIndices.Aggregate((max, i) => probs[i] > probs[max] ? i : max);
                qualityTags.Add(new TagInfo { TagName = _labels[maxIndex].Name.Replace('_', ' '), Confidence = probs[maxIndex] });
            }

            for (int i = 0; i < _labels.Length; i++) {
                if (i >= probs.Length) continue;

                float confidence = probs[i];
                TagData label = _labels[i];
                var tagName = label.Name.Replace('_', ' ');

                switch (label.Category) {
                    case 0: if (categoryOptions.IncludeGeneral && confidence >= generalThreshold) generalTags.Add(new TagInfo { TagName = tagName, Confidence = confidence }); break;
                    case 1: if (categoryOptions.IncludeArtist && confidence >= characterThreshold) artistTags.Add(new TagInfo { TagName = tagName, Confidence = confidence }); break;
                    case 3: if (categoryOptions.IncludeCopyright && confidence >= characterThreshold) copyrightTags.Add(new TagInfo { TagName = tagName, Confidence = confidence }); break;
                    case 4: if (categoryOptions.IncludeCharacter && confidence >= characterThreshold) characterTags.Add(new TagInfo { TagName = tagName, Confidence = confidence }); break;
                    case 5: if (categoryOptions.IncludeMeta && confidence >= generalThreshold) metaTags.Add(new TagInfo { TagName = tagName, Confidence = confidence }); break;
                }
            }

            var modelCandidates = new List<TagInfo>();
            var modelIndices = Enumerable.Range(0, _labels.Length).Where(i => _labels[i].Category == 10).ToArray();
            if (categoryOptions.IncludeModel) {
                foreach (var i in modelIndices) {
                    if (probs[i] >= generalThreshold) {
                        modelCandidates.Add(new TagInfo { TagName = _labels[i].Name.Replace('_', ' '), Confidence = probs[i] });
                    }
                }
            }
            if (categoryOptions.IncludeModel && modelCandidates.Any()) {
                modelTags.Add(modelCandidates.OrderByDescending(t => t.Confidence).First());
            }


            var allTags = new List<TagInfo>();
            allTags.AddRange(ratingTags);
            allTags.AddRange(qualityTags);
            allTags.AddRange(characterTags);
            allTags.AddRange(copyrightTags);
            allTags.AddRange(artistTags);
            allTags.AddRange(generalTags);
            allTags.AddRange(metaTags);
            allTags.AddRange(modelTags);

            var orderedByConfidence = allTags.OrderByDescending(t => t.Confidence).ToList();
            return ApplyPriorityOrdering(orderedByConfidence, priorityTags);
        }

        private static bool LooksLikeProbabilities(ReadOnlySpan<float> output)
        {
            const float epsilon = 1e-3f;
            for (int i = 0; i < output.Length; i++)
            {
                float value = output[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }

                if (value < -epsilon || value > 1f + epsilon)
                {
                    return false;
                }
            }

            return true;
        }

        private static float Sigmoid(float value)
        {
            return 1f / (1f + MathF.Exp(-value));
        }

        private static List<TagInfo> ApplyPriorityOrdering(List<TagInfo> tags, IReadOnlyList<string> priorityTags)
        {
            if (tags == null || tags.Count == 0 || priorityTags == null || priorityTags.Count == 0)
            {
                return tags;
            }

            var priorityMap = new Dictionary<string, int>();
            for (int i = 0; i < priorityTags.Count; i++)
            {
                var key = NormalizeTag(priorityTags[i]);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!priorityMap.ContainsKey(key))
                {
                    priorityMap[key] = i;
                }
            }

            if (priorityMap.Count == 0)
            {
                return tags;
            }

            var front = new List<(int order, TagInfo tag)>();
            var remaining = new List<TagInfo>();

            foreach (var tag in tags)
            {
                var key = NormalizeTag(tag.TagName);
                if (priorityMap.TryGetValue(key, out var order))
                {
                    front.Add((order, tag));
                }
                else
                {
                    remaining.Add(tag);
                }
            }

            if (front.Count == 0)
            {
                return tags;
            }

            front.Sort((a, b) => a.order.CompareTo(b.order));
            var ordered = front.Select(item => item.tag).ToList();
            ordered.AddRange(remaining);
            return ordered;
        }

        private static List<string> MergeCustomTags(IReadOnlyList<string> customTags, IEnumerable<TagInfo> orderedTags)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (customTags != null)
            {
                foreach (var tag in customTags)
                {
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        continue;
                    }

                    var normalized = NormalizeTag(tag);
                    if (seen.Add(normalized))
                    {
                        result.Add(tag.Trim());
                    }
                }
            }

            foreach (var tagInfo in orderedTags)
            {
                var tag = tagInfo.TagName;
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                var normalized = NormalizeTag(tag);
                if (seen.Add(normalized))
                {
                    result.Add(tag);
                }
            }

            return result;
        }

        private static string NormalizeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            var normalized = tag.Replace('_', ' ').Trim().ToLowerInvariant();
            normalized = string.Concat(normalized.Where(c => !char.IsWhiteSpace(c)));
            return normalized;
        }
        public class TagInfo
        {
            public string TagName { get; set; }
            public float Confidence { get; set; }
        }
    }
}
