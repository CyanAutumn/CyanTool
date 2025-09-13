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
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public class TagData
    {
        public string Name { get; set; }
        public int Category { get; set; }
    }

    public class TaggingProcessor
    {
        private string _modelPath;
        private string _csvPath;

        private static readonly HttpClient httpClient = new HttpClient();
        private InferenceSession _session;
        private TagData[] _labels;

        public TaggingProcessor()
        {
        }

        public async Task InitializeAsync(string modelUrl, string csvUrl, string modelDisplayName, IProgress<string> progress)
        {
            string appDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TaggerModels");
            Directory.CreateDirectory(appDataPath);

            _modelPath = Path.Combine(appDataPath, modelDisplayName + ".onnx");
            _csvPath = Path.Combine(appDataPath, modelDisplayName + ".csv");

            await CheckAndDownloadFileAsync($"Model ({modelDisplayName})", modelUrl, _modelPath, progress);
            await CheckAndDownloadFileAsync($"Tags ({modelDisplayName})", csvUrl, _csvPath, progress);

            progress.Report("Loading model into memory...");
            await Task.Run(() =>
            {
                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                _session = new InferenceSession(_modelPath, sessionOptions);
                _labels = File.ReadAllLines(_csvPath)
                              .Skip(1)
                              .Select(line =>
                              {
                                  var parts = line.Split(',');
                                  return new TagData
                                  {
                                      Name = parts[1].Replace("\"", "").Trim(),
                                      Category = int.Parse(parts[2])
                                  };
                              })
                              .ToArray();
            });
            progress.Report("Initialization complete. Ready to tag.");
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

        public async Task ProcessDirectoryAsync(string inputPath, float generalThreshold, float characterThreshold, int inputSize, IProgress<string> progress)
        {
            var imageFiles = Directory.GetFiles(inputPath, "*.*", SearchOption.TopDirectoryOnly)
                                      .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                  f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                  f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                  f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                                      .ToList();

            int processedCount = 0;
            foreach (var imageFile in imageFiles)
            {
                try
                {
                    var tags = await Task.Run(() => ProcessImage(imageFile, generalThreshold, characterThreshold, inputSize));
                    string txtFile = Path.ChangeExtension(imageFile, ".txt");
                    File.WriteAllText(txtFile, string.Join(", ", tags.Select(t => t.TagName)));
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

        private List<TagInfo> ProcessImage(string imagePath, float generalThreshold, float characterThreshold, int inputSize)
        {
            using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(imagePath);


            int size = Math.Max(image.Width, image.Height);
            using var squareImage = new Image<Rgb24>(size, size);
            squareImage.Mutate(x => x.BackgroundColor(Color.White));
            int padX = (size - image.Width) / 2;
            int padY = (size - image.Height) / 2;
            squareImage.Mutate(x => x.DrawImage(image, new Point(padX, padY), 1f));


            squareImage.Mutate(x => x.Resize(inputSize, inputSize));


            string inputName = _session.InputMetadata.Keys.First();
            var inputTensor = new DenseTensor<float>(new[] { 1, inputSize, inputSize, 3 });
            squareImage.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < inputSize; y++)
                {
                    Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < inputSize; x++)
                    {

                        inputTensor[0, y, x, 0] = pixelRow[x].B;
                        inputTensor[0, y, x, 1] = pixelRow[x].G;
                        inputTensor[0, y, x, 2] = pixelRow[x].R;
                    }
                }
            });


            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };
            using var results = _session.Run(inputs);
            var outputTensor = results.First().AsTensor<float>();
            var outputSpan = outputTensor.ToDenseTensor().Buffer.Span;


            var allTags = new List<TagInfo>();
            var characterTags = new List<TagInfo>();
            var generalTags = new List<TagInfo>();

            for (int i = 0; i < _labels.Length && i < outputSpan.Length; i++)
            {
                TagData label = _labels[i];
                float confidence = outputSpan[i];

                var tagName = label.Name.Replace('_', ' ');

                if (label.Category == 4)
                {
                    if (confidence >= characterThreshold)
                    {
                        characterTags.Add(new TagInfo { TagName = tagName, Confidence = confidence });
                    }
                }
                else if (label.Category == 0)
                {
                    if (confidence >= generalThreshold)
                    {
                        generalTags.Add(new TagInfo { TagName = tagName, Confidence = confidence });
                    }
                }

            }


            allTags.AddRange(characterTags.OrderByDescending(t => t.Confidence));
            allTags.AddRange(generalTags.OrderByDescending(t => t.Confidence));

            return allTags;
        }

        public class TagInfo
        {
            public string TagName { get; set; }
            public float Confidence { get; set; }
        }
    }
}
