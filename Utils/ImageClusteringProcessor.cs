using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyanTools.Utils
{
    public class ImageClusteringProcessor
    {
        private const int ProcessorResize = 336;
        private const int ModelInputSize = 224;
        private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
        private static readonly float[] StdDev = { 0.26862954f, 0.26130258f, 0.27577711f };

        private readonly InferenceSession _session;
        private readonly Action<string> _log;
        private readonly string _inputName;
        private readonly string _styleOutputName;
        private readonly string _contentOutputName;

        public ImageClusteringProcessor(string modelPath, bool useGpu, Action<string>? logAction = null)
        {
            _log = logAction ?? (_ => { });
            _session = CreateSession(modelPath, useGpu);
            _inputName = _session.InputMetadata.Keys.First();

            _styleOutputName = "style_output";
            _contentOutputName = "content_output";
        }

        private InferenceSession CreateSession(string modelPath, bool useGpu) {

            if (useGpu) {
                var gpuOptions = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                _log("请求使用 GPU (CUDA) 执行提供器");
                try {
                    TaggingProcessor.TryConfigureCuda12ForCurrentProcess(_log, out _, out _, out _);
                    gpuOptions.AppendExecutionProvider_CUDA();
                    gpuOptions.AppendExecutionProvider_CPU();
                    _log("开始创建 GPU 会话");
                    var gpuSession = new InferenceSession(modelPath, gpuOptions);
                    _log("GPU 会话创建成功");
                    return gpuSession;
                }
                catch (EntryPointNotFoundException ex) {
                    _log($"当前 onnxruntime.dll 不支持 CUDA 执行提供器，回退到 CPU。原因: {ex.Message}");
                }
                catch (DllNotFoundException ex) {
                    _log($"CUDA 依赖缺失，回退到 CPU。原因: {ex.Message}");
                }
                catch (OnnxRuntimeException ex) {
                    _log($"CUDA 执行提供器不可用，回退到 CPU。原因: {ex.Message}");
                }
                catch (Exception ex) {
                    _log($"GPU 会话创建失败，回退到 CPU。原因: {ex.Message}");
                }
            }

            var cpuOptions = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            cpuOptions.AppendExecutionProvider_CPU();
            _log("创建 CPU 会话");
            var cpuSession = new InferenceSession(modelPath, cpuOptions);
            _log("CPU 会话创建成功");
            return cpuSession;
        }

        public List<ImageStyleResult> ProcessFolder(
            string folderPath,
            Action<double> onProgress,
            Action<string> onError,
            bool includeGrayStyle = false,
            IEnumerable<string>? excludedFolders = null)
        {
            var processResult = ProcessFolderWithSummary(
                folderPath,
                onProgress,
                onError,
                new ImageClusteringExtractionOptions { IncludeGrayStyle = includeGrayStyle },
                excludedFolders);
            return processResult.Results;
        }

        public ImageClusteringProcessResult ProcessFolderWithSummary(
            string folderPath,
            Action<double> onProgress,
            Action<string> onError,
            ImageClusteringExtractionOptions? extractionOptions = null,
            IEnumerable<string>? excludedFolders = null)
        {
            try
            {
                var options = extractionOptions ?? new ImageClusteringExtractionOptions();
                var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg",
                    ".jpeg",
                    ".png",
                    ".bmp"
                };

                var excludedSet = BuildExcludedDirectorySet(excludedFolders);


                var imageFiles = EnumerateFilesOsWalkOrder(folderPath, excludedSet)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                    .ToList();

                if (!imageFiles.Any())
                {
                    return new ImageClusteringProcessResult(
                        new List<ImageStyleResult>(),
                        new ImageClusteringExtractionSummary());
                }

                bool useHeadDetector = options.IncludeHeadLineStyle &&
                    !string.IsNullOrWhiteSpace(options.HeadDetectionModelPath) &&
                    File.Exists(options.HeadDetectionModelPath);
                if (options.IncludeHeadLineStyle)
                {
                    if (useHeadDetector)
                    {
                        _log($"已启用头部线条 Style 分支：使用头部检测模型 {options.HeadDetectionModelPath}");
                    }
                    else
                    {
                        _log("已启用头部线条 Style 分支，但未找到头部检测模型，将全部使用中心裁剪兜底。");
                    }
                }

                var results = new List<ImageStyleResult>();
                var summary = new ImageClusteringExtractionSummary
                {
                    TotalImages = imageFiles.Count
                };

                for (int i = 0; i < imageFiles.Count; i++)
                {
                    var imagePath = imageFiles[i];
                    try
                    {
                        using var original = LoadImage(imagePath);
                        float[] styleVec = Array.Empty<float>();
                        float[] contentVec = Array.Empty<float>();
                        if (options.IncludeBaseEmbeddings)
                        {
                            var inputTensor = PreprocessImage(original, grayscale: false);
                            (styleVec, contentVec) = ExtractEmbeddings(inputTensor);
                            if (styleVec.Length == 0 || contentVec.Length == 0)
                            {
                                throw new InvalidOperationException("模型输出为空，请检查 ONNX 输出名称是否与 style_output/content_output 一致。");
                            }
                        }

                        float[] grayStyleVec = Array.Empty<float>();
                        if (options.IncludeGrayStyle)
                        {
                            var grayTensor = PreprocessImage(original, grayscale: true);
                            (grayStyleVec, _) = ExtractEmbeddings(grayTensor);
                            if (grayStyleVec.Length == 0)
                            {
                                throw new InvalidOperationException("灰度 Style 分支输出为空，请检查模型输出或预处理流程。");
                            }
                        }

                        var result = new ImageStyleResult
                        {
                            FilePath = imagePath,
                            StyleVector = styleVec,
                            GrayStyleVector = grayStyleVec,
                            ContentVector = contentVec
                        };

                        if (options.IncludeHeadLineStyle)
                        {
                            using var headCrop = CreateStyleHeadCrop(original, options, useHeadDetector, out bool usedFallback);
                            var headGrayTensor = PreprocessImage(headCrop, grayscale: true);
                            (result.HeadGrayStyleVector, _) = ExtractEmbeddings(headGrayTensor);
                            if (result.HeadGrayStyleVector.Length == 0)
                            {
                                throw new InvalidOperationException("头部灰度 Style 分支输出为空，请检查模型输出或预处理流程。");
                            }

                            result.HeadLineStyleVector = ExtractLineTextureDescriptor(headCrop);
                            result.UsedHeadFallbackCrop = usedFallback;
                            if (usedFallback)
                            {
                                summary.HeadFallbackCrops++;
                            }
                            else
                            {
                                summary.HeadDetectedCrops++;
                            }
                        }

                        results.Add(result);
                        summary.ProcessedImages++;
                    }
                    catch (Exception ex)
                    {
                        summary.FailedImages++;
                        onError?.Invoke($"处理图片失败: {imagePath}\n错误: {ex.Message}");
                    }
                    onProgress?.Invoke((double)(i + 1) / imageFiles.Count);
                }
                return new ImageClusteringProcessResult(results, summary);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"读取文件时发生错误: {ex.Message}");
                return new ImageClusteringProcessResult(
                    new List<ImageStyleResult>(),
                    new ImageClusteringExtractionSummary());
            }
        }

        private static IEnumerable<string> EnumerateFilesOsWalkOrder(string rootPath, HashSet<string> excludedDirs)
        {
            if (IsPathExcluded(rootPath, excludedDirs))
            {
                yield break;
            }


            IEnumerable<string> files = Enumerable.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
            }

            foreach (var file in files)
            {
                yield return file;
            }


            IEnumerable<string> dirs = Enumerable.Empty<string>();
            try
            {
                dirs = Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
            }

            foreach (var dir in dirs)
            {
                if (IsPathExcluded(dir, excludedDirs))
                {
                    continue;
                }

                foreach (var subFile in EnumerateFilesOsWalkOrder(dir, excludedDirs))
                {
                    yield return subFile;
                }
            }
        }

        private static HashSet<string> BuildExcludedDirectorySet(IEnumerable<string>? excludedFolders)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excludedFolders == null)
            {
                return set;
            }

            foreach (var folder in excludedFolders)
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                try
                {
                    set.Add(NormalizeDirectoryPath(folder));
                }
                catch
                {
                }
            }

            return set;
        }

        private static bool IsPathExcluded(string path, HashSet<string> excludedDirs)
        {
            if (excludedDirs.Count == 0)
            {
                return false;
            }

            string normalizedPath;
            try
            {
                normalizedPath = NormalizeDirectoryPath(path);
            }
            catch
            {
                return false;
            }

            foreach (var excluded in excludedDirs)
            {
                if (normalizedPath.Equals(excluded, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (normalizedPath.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeDirectoryPath(string path)
        {
            var full = Path.GetFullPath(path)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var root = Path.GetPathRoot(full)?
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                ?? string.Empty;
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }

            return full.TrimEnd(Path.DirectorySeparatorChar);
        }

        private static Image<Rgb24> LoadImage(string imagePath)
        {
            byte[] imageBytes = File.ReadAllBytes(imagePath);
            using var ms = new MemoryStream(imageBytes);
            return Image.Load<Rgb24>(ms);
        }

        private DenseTensor<Float16> PreprocessImage(Image<Rgb24> original, bool grayscale) {
            int canvasSize = Math.Max(original.Width, original.Height);
            using var canvas = new Image<Rgb24>(canvasSize, canvasSize, new Rgb24(255, 255, 255));
            canvas.Mutate(ctx => ctx.DrawImage(original, new Point((canvasSize - original.Width) / 2, (canvasSize - original.Height) / 2), 1f));
            if (grayscale)
            {
                canvas.Mutate(ctx => ctx.Grayscale());
            }


            canvas.Mutate(ctx => ctx.Resize(new ResizeOptions {
                Size = new SixLabors.ImageSharp.Size(ProcessorResize, ProcessorResize),
                Sampler = KnownResamplers.Lanczos3
            }).Resize(new ResizeOptions {
                Size = new SixLabors.ImageSharp.Size(ModelInputSize, ModelInputSize),
                Sampler = KnownResamplers.Bicubic
            }));

            var tensor = new DenseTensor<Float16>(new[] { 1, 3, ModelInputSize, ModelInputSize });
            canvas.ProcessPixelRows(accessor => {
                for (int y = 0; y < accessor.Height; y++) {
                    Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++) {
                        Rgb24 pixel = pixelRow[x];
                        tensor[0, 0, y, x] = (Float16)((pixel.R / 255f - Mean[0]) / StdDev[0]);
                        tensor[0, 1, y, x] = (Float16)((pixel.G / 255f - Mean[1]) / StdDev[1]);
                        tensor[0, 2, y, x] = (Float16)((pixel.B / 255f - Mean[2]) / StdDev[2]);
                    }
                }
            });
            return tensor;
        }

        private static Image<Rgb24> CreateStyleHeadCrop(
            Image<Rgb24> original,
            ImageClusteringExtractionOptions options,
            bool useHeadDetector,
            out bool usedFallback)
        {
            using var oriented = original.Clone(ctx => ctx.AutoOrient());

            Rectangle cropRect;
            if (useHeadDetector)
            {
                var detections = YoloDetection.Predict(
                    oriented,
                    options.HeadDetectionModelPath!,
                    options.HeadDetectionConfidence,
                    options.HeadDetectionIou,
                    options.HeadDetectionMaxInferSize);

                if (detections.Count > 0)
                {
                    var best = detections
                        .OrderByDescending(d => d.Score)
                        .ThenByDescending(d => d.BoundingBox.Width * d.BoundingBox.Height)
                        .First();
                    cropRect = ExpandSquare(best.BoundingBox, oriented.Width, oriented.Height, options.HeadCropScale);
                    usedFallback = false;
                    return oriented.Clone(ctx => ctx.Crop(cropRect));
                }
            }

            cropRect = CenterSquare(oriented.Width, oriented.Height, options.HeadFallbackCenterScale);
            usedFallback = true;
            return oriented.Clone(ctx => ctx.Crop(cropRect));
        }

        private static Rectangle ExpandSquare(Rectangle rect, int imageWidth, int imageHeight, float scale)
        {
            float centerX = rect.X + rect.Width / 2f;
            float centerY = rect.Y + rect.Height / 2f;
            float size = Math.Max(rect.Width, rect.Height) * Math.Clamp(scale, 0.1f, 6f);
            int x0 = (int)MathF.Floor(centerX - size / 2f);
            int y0 = (int)MathF.Floor(centerY - size / 2f);
            int x1 = (int)MathF.Ceiling(centerX + size / 2f);
            int y1 = (int)MathF.Ceiling(centerY + size / 2f);
            return ClampRect(x0, y0, x1, y1, imageWidth, imageHeight);
        }

        private static Rectangle CenterSquare(int imageWidth, int imageHeight, float scale)
        {
            float size = Math.Min(imageWidth, imageHeight) * Math.Clamp(scale, 0.1f, 1f);
            float centerX = imageWidth / 2f;
            float centerY = imageHeight / 2f;
            int x0 = (int)MathF.Floor(centerX - size / 2f);
            int y0 = (int)MathF.Floor(centerY - size / 2f);
            int x1 = (int)MathF.Ceiling(centerX + size / 2f);
            int y1 = (int)MathF.Ceiling(centerY + size / 2f);
            return ClampRect(x0, y0, x1, y1, imageWidth, imageHeight);
        }

        private static Rectangle ClampRect(int x0, int y0, int x1, int y1, int imageWidth, int imageHeight)
        {
            x0 = Math.Clamp(x0, 0, Math.Max(0, imageWidth - 1));
            y0 = Math.Clamp(y0, 0, Math.Max(0, imageHeight - 1));
            x1 = Math.Clamp(x1, x0 + 1, imageWidth);
            y1 = Math.Clamp(y1, y0 + 1, imageHeight);
            return new Rectangle(x0, y0, x1 - x0, y1 - y0);
        }

        private static float[] ExtractLineTextureDescriptor(Image<Rgb24> original)
        {
            const int size = 128;

            int canvasSize = Math.Max(original.Width, original.Height);
            using var canvas = new Image<Rgb24>(canvasSize, canvasSize, new Rgb24(255, 255, 255));
            canvas.Mutate(ctx =>
            {
                ctx.DrawImage(original, new Point((canvasSize - original.Width) / 2, (canvasSize - original.Height) / 2), 1f);
                ctx.Grayscale();
                ctx.Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(size, size),
                    Sampler = KnownResamplers.Lanczos3
                });
            });

            var gray = new float[size, size];
            canvas.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < size; y++)
                {
                    Span<Rgb24> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < size; x++)
                    {
                        gray[y, x] = row[x].R / 255f;
                    }
                }
            });

            var magnitudes = new List<float>((size - 2) * (size - 2));
            var gradients = new (float Mag, float Angle)[size, size];
            var laplacians = new List<float>((size - 2) * (size - 2));

            for (int y = 1; y < size - 1; y++)
            {
                for (int x = 1; x < size - 1; x++)
                {
                    float gx =
                        -gray[y - 1, x - 1] + gray[y - 1, x + 1]
                        - 2f * gray[y, x - 1] + 2f * gray[y, x + 1]
                        - gray[y + 1, x - 1] + gray[y + 1, x + 1];
                    float gy =
                        -gray[y - 1, x - 1] - 2f * gray[y - 1, x] - gray[y - 1, x + 1]
                        + gray[y + 1, x - 1] + 2f * gray[y + 1, x] + gray[y + 1, x + 1];
                    float mag = MathF.Sqrt(gx * gx + gy * gy);
                    float angle = MathF.Atan2(gy, gx);
                    while (angle < 0f)
                    {
                        angle += MathF.PI;
                    }
                    while (angle >= MathF.PI)
                    {
                        angle -= MathF.PI;
                    }

                    gradients[y, x] = (mag, angle);
                    magnitudes.Add(mag);

                    float lap = Math.Abs(
                        -4f * gray[y, x]
                        + gray[y - 1, x]
                        + gray[y + 1, x]
                        + gray[y, x - 1]
                        + gray[y, x + 1]);
                    laplacians.Add(lap);
                }
            }

            magnitudes.Sort();
            laplacians.Sort();
            float maxMag = Math.Max(1e-6f, magnitudes[^1]);
            float maxLap = Math.Max(1e-6f, laplacians[^1]);
            float q50 = Quantile(magnitudes, 0.50f);
            float q75 = Quantile(magnitudes, 0.75f);
            float q90 = Quantile(magnitudes, 0.90f);
            float q95 = Quantile(magnitudes, 0.95f);

            var features = new List<float>(360);
            AddGradientOrientationHistogram(features, gradients, size, bins: 16);
            AddValueHistogram(features, magnitudes, maxMag, bins: 16);
            AddValueHistogram(features, laplacians, maxLap, bins: 16);
            features.Add(q50 / maxMag);
            features.Add(q75 / maxMag);
            features.Add(q90 / maxMag);
            features.Add(q95 / maxMag);
            features.Add(ValueMean(magnitudes) / maxMag);
            features.Add(ValueStdDev(magnitudes) / maxMag);
            features.Add(ValueMean(laplacians) / maxLap);
            features.Add(ValueStdDev(laplacians) / maxLap);
            AddSortedLocalEdgeDensities(features, gradients, size, threshold: q75);
            AddMaskedLbpHistogram(features, gray, gradients, size, threshold: q75);

            return L2Normalize(features.ToArray());
        }

        private static void AddGradientOrientationHistogram(List<float> features, (float Mag, float Angle)[,] gradients, int size, int bins)
        {
            var hist = new float[bins];
            for (int y = 1; y < size - 1; y++)
            {
                for (int x = 1; x < size - 1; x++)
                {
                    var (mag, angle) = gradients[y, x];
                    int bin = Math.Min(bins - 1, (int)(angle / MathF.PI * bins));
                    hist[bin] += MathF.Sqrt(Math.Max(0f, mag));
                }
            }

            AddNormalizedHistogram(features, hist);
        }

        private static void AddValueHistogram(List<float> features, IReadOnlyList<float> values, float maxValue, int bins)
        {
            var hist = new float[bins];
            foreach (float value in values)
            {
                int bin = Math.Min(bins - 1, (int)(Math.Clamp(value / maxValue, 0f, 0.999999f) * bins));
                hist[bin] += 1f;
            }

            AddNormalizedHistogram(features, hist);
        }

        private static void AddSortedLocalEdgeDensities(List<float> features, (float Mag, float Angle)[,] gradients, int size, float threshold)
        {
            const int cells = 4;
            int cellSize = size / cells;
            var densities = new List<float>(cells * cells);
            for (int cy = 0; cy < cells; cy++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    int edge = 0;
                    int total = 0;
                    for (int y = Math.Max(1, cy * cellSize); y < Math.Min(size - 1, (cy + 1) * cellSize); y++)
                    {
                        for (int x = Math.Max(1, cx * cellSize); x < Math.Min(size - 1, (cx + 1) * cellSize); x++)
                        {
                            total++;
                            if (gradients[y, x].Mag >= threshold)
                            {
                                edge++;
                            }
                        }
                    }

                    densities.Add(total == 0 ? 0f : (float)edge / total);
                }
            }

            densities.Sort();
            features.AddRange(densities);
        }

        private static void AddMaskedLbpHistogram(List<float> features, float[,] gray, (float Mag, float Angle)[,] gradients, int size, float threshold)
        {
            var hist = new float[256];
            int samples = 0;
            for (int y = 1; y < size - 1; y++)
            {
                for (int x = 1; x < size - 1; x++)
                {
                    if (gray[y, x] > 0.97f && gradients[y, x].Mag < threshold)
                    {
                        continue;
                    }

                    float center = gray[y, x];
                    int code = 0;
                    code |= gray[y - 1, x - 1] >= center ? 1 : 0;
                    code |= gray[y - 1, x] >= center ? 2 : 0;
                    code |= gray[y - 1, x + 1] >= center ? 4 : 0;
                    code |= gray[y, x + 1] >= center ? 8 : 0;
                    code |= gray[y + 1, x + 1] >= center ? 16 : 0;
                    code |= gray[y + 1, x] >= center ? 32 : 0;
                    code |= gray[y + 1, x - 1] >= center ? 64 : 0;
                    code |= gray[y, x - 1] >= center ? 128 : 0;
                    hist[code] += 1f;
                    samples++;
                }
            }

            if (samples == 0)
            {
                features.AddRange(hist);
                return;
            }

            for (int i = 0; i < hist.Length; i++)
            {
                hist[i] /= samples;
            }

            features.AddRange(hist);
        }

        private static void AddNormalizedHistogram(List<float> features, float[] hist)
        {
            float sum = hist.Sum();
            if (sum <= 1e-6f)
            {
                features.AddRange(hist);
                return;
            }

            for (int i = 0; i < hist.Length; i++)
            {
                features.Add(hist[i] / sum);
            }
        }

        private static float Quantile(IReadOnlyList<float> sortedValues, float q)
        {
            if (sortedValues.Count == 0)
            {
                return 0f;
            }

            float position = Math.Clamp(q, 0f, 1f) * (sortedValues.Count - 1);
            int lower = (int)Math.Floor(position);
            int upper = Math.Min(sortedValues.Count - 1, lower + 1);
            float frac = position - lower;
            return sortedValues[lower] * (1f - frac) + sortedValues[upper] * frac;
        }

        private static float ValueMean(IReadOnlyList<float> values)
        {
            return values.Count == 0 ? 0f : values.Sum() / values.Count;
        }

        private static float ValueStdDev(IReadOnlyList<float> values)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            float mean = ValueMean(values);
            double sum = 0d;
            foreach (float value in values)
            {
                double delta = value - mean;
                sum += delta * delta;
            }

            return (float)Math.Sqrt(sum / values.Count);
        }

        private (float[] styleVector, float[] contentVector) ExtractEmbeddings(DenseTensor<Float16> inputTensor)
        {
            var inputs = new NamedOnnxValue[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) };
            using var results = _session.Run(inputs);

            float[] styleVector = Array.Empty<float>();
            float[] contentVector = Array.Empty<float>();

            foreach (var r in results)
            {
                if (r.Name == _styleOutputName)
                {
                    styleVector = L2Normalize(r.AsTensor<Float16>().ToArray().Select(v => (float)v).ToArray());
                }
                else if (r.Name == _contentOutputName)
                {
                    contentVector = L2Normalize(r.AsTensor<Float16>().ToArray().Select(v => (float)v).ToArray());
                }
            }
            return (styleVector, contentVector);
        }

        private static float[] L2Normalize(float[] vector)
        {
            if (vector.Length == 0)
            {
                return vector;
            }

            double sum = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                double v = vector[i];
                sum += v * v;
            }

            if (sum <= 1e-12)
            {
                return vector;
            }

            float invNorm = (float)(1.0 / Math.Sqrt(sum));
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] *= invNorm;
            }

            return vector;
        }
    }

    public class ImageStyleResult
    {
        public string FilePath { get; set; } = string.Empty;
        public float[] StyleVector { get; set; } = Array.Empty<float>();
        public float[] GrayStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadGrayStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadLineStyleVector { get; set; } = Array.Empty<float>();
        public bool UsedHeadFallbackCrop { get; set; }
        public float[] ContentVector { get; set; } = Array.Empty<float>();
    }

    public sealed class ImageClusteringExtractionOptions
    {
        public bool IncludeBaseEmbeddings { get; init; } = true;
        public bool IncludeGrayStyle { get; init; }
        public bool IncludeHeadLineStyle { get; init; }
        public string? HeadDetectionModelPath { get; init; }
        public float HeadDetectionConfidence { get; init; } = 0.35f;
        public float HeadDetectionIou { get; init; } = 0.70f;
        public int HeadDetectionMaxInferSize { get; init; } = 640;
        public float HeadCropScale { get; init; } = 1.8f;
        public float HeadFallbackCenterScale { get; init; } = 0.62f;
    }

    public sealed class ImageClusteringExtractionSummary
    {
        public int TotalImages { get; set; }
        public int ProcessedImages { get; set; }
        public int FailedImages { get; set; }
        public int HeadDetectedCrops { get; set; }
        public int HeadFallbackCrops { get; set; }
    }

    public sealed class ImageClusteringProcessResult
    {
        public ImageClusteringProcessResult(List<ImageStyleResult> results, ImageClusteringExtractionSummary summary)
        {
            Results = results;
            Summary = summary;
        }

        public List<ImageStyleResult> Results { get; }
        public ImageClusteringExtractionSummary Summary { get; }
    }
}
