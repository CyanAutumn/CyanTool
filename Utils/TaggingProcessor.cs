using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
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
        private static readonly string[] Cuda12RequiredDlls = {
            "cudart64_12.dll",
            "cublas64_12.dll",
            "cublasLt64_12.dll",
            "cufft64_11.dll",
            "cudnn64_8.dll"
        };

        public TaggingProcessor()
        {
        }

        public sealed class CudaCheckResult
        {
            public bool NvidiaDriverDetected { get; init; }
            public bool CudaAppendApiSupported { get; init; }
            public bool CudaExecutionProviderAvailable { get; init; }
            public string Details { get; init; } = string.Empty;
        }

        public static CudaCheckResult CheckCudaEnvironment()
        {
            TryConfigureCuda12ForCurrentProcess(log: null, out var cuda12Bin, out var cuda12Version, out var missingCuda12Dlls);
            TryGetCudaBinDirectory(13, out var cuda13Bin, out var cuda13Version);

            bool driverDetected = NativeLibrary.TryLoad("nvcuda.dll", out var driverHandle);
            if (driverDetected && driverHandle != IntPtr.Zero)
            {
                try
                {
                    NativeLibrary.Free(driverHandle);
                }
                catch
                {

                }
            }

            bool cudaEpAvailable = false;
            bool cudaAppendApiSupported = false;
            string cudaEpDetails;
            try
            {
                using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                options.AppendExecutionProvider_CUDA();
                cudaAppendApiSupported = true;
                cudaEpAvailable = true;
                cudaEpDetails = "ONNXRuntime CUDA 执行提供器可用。";
            }
            catch (EntryPointNotFoundException ex)
            {
                cudaAppendApiSupported = false;
                cudaEpDetails =
                    $"当前 onnxruntime.dll 不支持 CUDA 执行提供器（缺少入口点：{ex.Message}）。\n" +
                    "这通常是因为应用加载了 CPU 版 onnxruntime.dll（或版本不匹配）。请确保使用 `Microsoft.ML.OnnxRuntime.Gpu` 并重新发布/更新程序。";
            }
            catch (DllNotFoundException ex)
            {
                cudaAppendApiSupported = false;
                cudaEpDetails =
                    $"无法加载 CUDA 相关 DLL（或其依赖）。{ex.Message}\n" +
                    "通常需要安装 CUDA 12.x 运行库（cudart64_12.dll / cublas64_12.dll / cublasLt64_12.dll / cufft64_11.dll）以及 cuDNN 8（cudnn64_8.dll）。\n" +
                    "如果你安装的是 CUDA 13.x，也需要额外安装 CUDA 12.x（因为 DLL 名称是 cudart64_12.dll，而不是 cudart64_13.dll）。";
            }
            catch (OnnxRuntimeException ex)
            {
                cudaAppendApiSupported = true;
                if (ex.Message.Contains("LoadLibrary failed with error 126", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("onnxruntime_providers_cuda.dll", StringComparison.OrdinalIgnoreCase))
                {
                    cudaEpDetails =
                        $"CUDA 执行提供器不可用。{ex.Message}\n" +
                        "通常是缺少 CUDA 12.x / cuDNN 8 的运行库 DLL，请先安装对应版本并确保它们在 PATH 中（或放到程序同目录）。";
                }
                else
                {
                    cudaEpDetails = $"CUDA 执行提供器不可用。{ex.Message}";
                }
            }
            catch (Exception ex)
            {
                cudaAppendApiSupported = true;
                cudaEpDetails = $"检测 CUDA 时发生错误。{ex.Message}";
            }

            var cuda12Summary = cuda12Bin == null
                ? (cuda13Bin == null
                    ? "未检测到 CUDA 12.x 安装目录。"
                    : $"未检测到 CUDA 12.x 安装目录，但检测到 CUDA {cuda13Version}（{cuda13Bin}）。GPU 推理需要 CUDA 12.x（提供 cudart64_12.dll 等）。")
                : $"检测到 CUDA {cuda12Version}（{cuda12Bin}）。" +
                  (missingCuda12Dlls.Count == 0
                      ? "已找到 CUDA 12.x/cuDNN 依赖 DLL。"
                      : $"未在该目录找到以下 DLL：{string.Join(", ", missingCuda12Dlls)}");

            var details = string.Join("\n", new[]
            {
                driverDetected
                    ? "已检测到 NVIDIA 驱动 (nvcuda.dll)。"
                    : "未检测到 NVIDIA 驱动 (nvcuda.dll)，请先安装/更新显卡驱动。",
                cuda12Summary,
                cudaEpDetails
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return new CudaCheckResult
            {
                NvidiaDriverDetected = driverDetected,
                CudaAppendApiSupported = cudaAppendApiSupported,
                CudaExecutionProviderAvailable = cudaEpAvailable,
                Details = details
            };
        }

        internal static bool TryConfigureCuda12ForCurrentProcess(Action<string>? log, out string? cudaBinDirectory, out Version? cudaVersion, out IReadOnlyList<string> missingRequiredDlls)
        {
            cudaBinDirectory = null;
            cudaVersion = null;
            missingRequiredDlls = Array.Empty<string>();

            if (!TryGetCudaBinDirectory(12, out var bin, out var version))
            {
                log?.Invoke("未检测到 CUDA 12.x 安装目录。");
                return false;
            }

            cudaBinDirectory = bin;
            cudaVersion = version;
            var missing = GetMissingDlls(bin, Cuda12RequiredDlls);
            missingRequiredDlls = missing;

            if (TryPrependToProcessPath(bin, out var reason))
            {
                log?.Invoke($"已将 CUDA {version} 的 bin 目录加入当前进程 PATH：{bin}");
            }
            else if (!string.IsNullOrWhiteSpace(reason))
            {
                log?.Invoke(reason);
            }

            if (missing.Count > 0)
            {
                log?.Invoke($"CUDA 依赖可能不完整，未在 {bin} 找到：{string.Join(", ", missing)}");
            }

            return true;
        }

        private static IReadOnlyList<string> GetMissingDlls(string directory, IEnumerable<string> dllNames)
        {
            var missing = new List<string>();
            foreach (var dllName in dllNames)
            {
                var path = Path.Combine(directory, dllName);
                if (!File.Exists(path))
                {
                    missing.Add(dllName);
                }
            }
            return missing;
        }

        private static bool TryPrependToProcessPath(string directory, out string? reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(directory))
            {
                reason = "CUDA bin 目录为空，无法加入 PATH。";
                return false;
            }

            if (!Directory.Exists(directory))
            {
                reason = $"CUDA bin 目录不存在：{directory}";
                return false;
            }

            try
            {
                var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                string Normalize(string value)
                {
                    value = value.Trim().Trim('"');
                    try
                    {
                        value = Path.GetFullPath(value);
                    }
                    catch
                    {

                    }
                    return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }

                var normalizedTarget = Normalize(directory);
                var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Normalize)
                    .Where(p => !string.IsNullOrWhiteSpace(p));

                if (parts.Contains(normalizedTarget, StringComparer.OrdinalIgnoreCase))
                {
                    reason = $"CUDA bin 目录已在 PATH 中：{directory}";
                    return false;
                }

                Environment.SetEnvironmentVariable("PATH", directory + ";" + current);
                return true;
            }
            catch (Exception ex)
            {
                reason = $"加入 PATH 失败：{ex.Message}";
                return false;
            }
        }

        private static bool TryGetCudaBinDirectory(int majorVersion, out string? binDirectory, out Version? version)
        {
            binDirectory = null;
            version = null;

            var candidates = new List<(Version version, string bin)>();

            foreach (var candidate in EnumerateCudaPathsFromEnvironment(majorVersion))
            {
                var bin = Path.Combine(candidate.path, "bin");
                if (Directory.Exists(bin) && File.Exists(Path.Combine(bin, $"cudart64_{majorVersion}.dll")))
                {
                    candidates.Add((candidate.version, bin));
                }
            }

            var defaultRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA GPU Computing Toolkit",
                "CUDA");
            if (Directory.Exists(defaultRoot))
            {
                foreach (var dir in Directory.GetDirectories(defaultRoot, "v*"))
                {
                    var name = Path.GetFileName(dir);
                    if (!TryParseCudaVersionDirectoryName(name, out var parsed))
                    {
                        continue;
                    }

                    if (parsed.Major != majorVersion)
                    {
                        continue;
                    }

                    var bin = Path.Combine(dir, "bin");
                    if (Directory.Exists(bin) && File.Exists(Path.Combine(bin, $"cudart64_{majorVersion}.dll")))
                    {
                        candidates.Add((parsed, bin));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            var best = candidates.OrderByDescending(c => c.version).First();
            version = best.version;
            binDirectory = best.bin;
            return true;
        }

        private static IEnumerable<(Version version, string path)> EnumerateCudaPathsFromEnvironment(int majorVersion)
        {
            var env = Environment.GetEnvironmentVariables();
            foreach (DictionaryEntry entry in env)
            {
                if (entry.Key is not string key || entry.Value is not string value)
                {
                    continue;
                }

                if (!key.StartsWith($"CUDA_PATH_V{majorVersion}_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryParseCudaVersionFromEnvVarKey(key, out var ver) && Directory.Exists(value))
                {
                    yield return (ver, value);
                }
            }

            var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            if (!string.IsNullOrWhiteSpace(cudaPath) && Directory.Exists(cudaPath))
            {

                var dirName = Path.GetFileName(cudaPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (TryParseCudaVersionDirectoryName(dirName, out var ver))
                {
                    yield return (ver, cudaPath);
                }
                else
                {
                    yield return (new Version(majorVersion, 0), cudaPath);
                }
            }
        }

        private static bool TryParseCudaVersionFromEnvVarKey(string key, out Version version)
        {
            version = new Version();

            var prefix = "CUDA_PATH_V";
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var tail = key.Substring(prefix.Length);
            var parts = tail.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            {
                return false;
            }

            version = new Version(major, minor);
            return true;
        }

        private static bool TryParseCudaVersionDirectoryName(string name, out Version version)
        {
            version = new Version();
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            name = name.Trim();
            if (name.Length >= 2 && (name[0] == 'v' || name[0] == 'V'))
            {
                name = name.Substring(1);
            }

            return Version.TryParse(name, out version);
        }

        public async Task InitializeAsync(string modelUrl, string tagsUrl, string modelDisplayName, bool preferGpu, IProgress<string> progress)
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
                try
                {
                    _session?.Dispose();
                }
                catch
                {

                }

                _session = CreateSession(_modelPath, preferGpu, progress);


















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

        private static InferenceSession CreateSession(string modelPath, bool preferGpu, IProgress<string> progress)
        {
            if (preferGpu)
            {
                var gpuOptions = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                progress.Report("尝试使用 GPU (CUDA) 执行提供器...");
                try
                {
                    TryConfigureCuda12ForCurrentProcess(progress.Report, out _, out _, out _);
                    gpuOptions.AppendExecutionProvider_CUDA();
                    gpuOptions.AppendExecutionProvider_CPU();
                    var session = new InferenceSession(modelPath, gpuOptions);
                    progress.Report("CUDA 会话创建成功，将使用 GPU 打标。");
                    return session;
                }
                catch (EntryPointNotFoundException ex)
                {
                    progress.Report(
                        $"当前 onnxruntime.dll 不支持 CUDA 执行提供器（{ex.Message}），回退到 CPU。\n" +
                        "请确认使用 `Microsoft.ML.OnnxRuntime.Gpu` 版本并重新发布/更新程序。");
                }
                catch (DllNotFoundException ex)
                {
                    progress.Report($"CUDA 依赖缺失，回退到 CPU。原因: {ex.Message}");
                }
                catch (OnnxRuntimeException ex)
                {
                    progress.Report($"CUDA 执行提供器不可用，回退到 CPU。原因: {ex.Message}");
                }
                catch (Exception ex)
                {
                    progress.Report($"GPU 会话创建失败，回退到 CPU。原因: {ex.Message}");
                }
            }

            var cpuOptions = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            cpuOptions.AppendExecutionProvider_CPU();
            progress.Report("创建 CPU 会话进行打标。");
            return new InferenceSession(modelPath, cpuOptions);
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
