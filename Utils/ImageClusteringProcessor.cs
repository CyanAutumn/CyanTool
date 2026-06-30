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
                bool useEyeValidation = useHeadDetector &&
                    options.RequireEyeForReliableHeadCrop &&
                    !string.IsNullOrWhiteSpace(options.HeadValidationEyeModelPath) &&
                    File.Exists(options.HeadValidationEyeModelPath);
                if (options.IncludeHeadLineStyle)
                {
                    if (useHeadDetector)
                    {
                        _log($"已启用头部渲染/线条 Style 分支：使用头部检测模型 {options.HeadDetectionModelPath}");
                        if (useEyeValidation)
                        {
                            _log($"已启用头部可靠性校验：使用眼部检测模型 {options.HeadValidationEyeModelPath}");
                        }
                    }
                    else
                    {
                        _log("已启用头部渲染/线条 Style 分支，但未找到头部检测模型，将全部使用中心裁剪兜底。");
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

                        if (options.IncludeGrayStructureStyle)
                        {
                            result.GrayRenderStyleVector = ExtractRenderStyleDescriptor(original);
                            result.GrayLineStyleVector = ExtractLineTextureDescriptor(original, StyleDescriptorCropMode.Center);
                            result.GrayToneEdgeStyleVector = ExtractToneEdgeDescriptor(original);
                            result.GraySketchStyleVector = ExtractSketchStyleDescriptor(original);
                        }

                        if (options.IncludeHeadLineStyle)
                        {
                            using var headCrop = CreateStyleHeadCrop(original, options, useHeadDetector, useEyeValidation, out bool usedFallback);
                            var headGrayTensor = PreprocessImage(headCrop, grayscale: true);
                            (result.HeadGrayStyleVector, _) = ExtractEmbeddings(headGrayTensor);
                            if (result.HeadGrayStyleVector.Length == 0)
                            {
                                throw new InvalidOperationException("头部灰度 Style 分支输出为空，请检查模型输出或预处理流程。");
                            }

                            result.HeadRenderStyleVector = ExtractRenderStyleDescriptor(headCrop);
                            result.HeadLineStyleVector = ExtractLineTextureDescriptor(headCrop, StyleDescriptorCropMode.Full);
                            result.HeadUpperLineStyleVector = ExtractLineTextureDescriptor(headCrop, StyleDescriptorCropMode.Upper);
                            result.HeadCenterLineStyleVector = ExtractLineTextureDescriptor(headCrop, StyleDescriptorCropMode.Center);
                            result.HeadFaceLineStyleVector = ExtractLineTextureDescriptor(headCrop, StyleDescriptorCropMode.FaceCore);
                            result.HeadEyeLineStyleVector = ExtractLineTextureDescriptor(headCrop, StyleDescriptorCropMode.EyeBand);
                            result.HeadToneEdgeStyleVector = ExtractToneEdgeDescriptor(headCrop);
                            result.HeadFaceToneEdgeStyleVector = ExtractToneEdgeDescriptor(headCrop, StyleDescriptorCropMode.FaceCore);
                            result.UsedHeadFallbackCrop = usedFallback;
                            result.HeadCropReliabilityVector = new[] { usedFallback ? 0f : 1f };
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
            bool useEyeValidation,
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
                    var ordered = detections
                        .OrderByDescending(d => d.Score)
                        .ThenByDescending(d => d.BoundingBox.Width * d.BoundingBox.Height)
                        .ToArray();

                    foreach (var detection in ordered.Take(5))
                    {
                        cropRect = ExpandSquare(detection.BoundingBox, oriented.Width, oriented.Height, options.HeadCropScale);
                        using var candidate = oriented.Clone(ctx => ctx.Crop(cropRect));
                        if (!useEyeValidation || HasReliableEyeCue(candidate, options))
                        {
                            usedFallback = false;
                            return oriented.Clone(ctx => ctx.Crop(cropRect));
                        }
                    }

                    var best = ordered[0];
                    cropRect = ExpandSquare(best.BoundingBox, oriented.Width, oriented.Height, options.HeadCropScale);
                    usedFallback = true;
                    return oriented.Clone(ctx => ctx.Crop(cropRect));
                }
            }

            cropRect = CenterSquare(oriented.Width, oriented.Height, options.HeadFallbackCenterScale);
            usedFallback = true;
            return oriented.Clone(ctx => ctx.Crop(cropRect));
        }

        private static bool HasReliableEyeCue(Image<Rgb24> headCrop, ImageClusteringExtractionOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.HeadValidationEyeModelPath) ||
                !File.Exists(options.HeadValidationEyeModelPath))
            {
                return true;
            }

            var eyes = YoloDetection.Predict(
                headCrop,
                options.HeadValidationEyeModelPath,
                options.HeadValidationEyeConfidence,
                options.HeadValidationEyeIou,
                options.HeadValidationEyeMaxInferSize);
            return eyes.Count > 0;
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

        private enum StyleDescriptorCropMode
        {
            Full,
            Upper,
            Center,
            FaceCore,
            EyeBand
        }

        private static float[] ExtractLineTextureDescriptor(Image<Rgb24> original, StyleDescriptorCropMode mode)
        {
            const int size = 112;

            var gray = SquareResizeGray(original, mode, size);
            var blur = GaussianBlur(gray, 2.0f);
            var detail = new float[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    detail[y, x] = Math.Clamp((gray[y, x] - blur[y, x]) * 2.5f + 0.5f, 0f, 1f);
                }
            }

            var features = new List<float>(430);
            bool preserveLayout = mode == StyleDescriptorCropMode.FaceCore || mode == StyleDescriptorCropMode.EyeBand;
            AddGradientStats(features, detail, cells: 1, sortedCells: false);
            AddGradientStats(features, detail, cells: 3, sortedCells: !preserveLayout);
            if (preserveLayout)
            {
                AddGradientStats(features, detail, cells: 4, sortedCells: false);
                AddSpatialEdgeProfile(features, detail, bands: 8);
            }

            AddLbpHistogram(features, detail);
            AddEdgeDensityProfile(features, detail);
            return L2Normalize(features.ToArray());
        }

        private static float[] ExtractToneEdgeDescriptor(Image<Rgb24> original, StyleDescriptorCropMode mode = StyleDescriptorCropMode.Full)
        {
            const int size = 112;

            var gray = SquareResizeGray(original, mode, size);
            var low = GaussianBlur(gray, 4.0f);
            var high = new float[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    high[y, x] = Math.Clamp(gray[y, x] - low[y, x] + 0.5f, 0f, 1f);
                }
            }

            var features = new List<float>(100);
            AddValueHistogramL2(features, Flatten(high), bins: 32);
            AddGradientQuantiles(features, gray);
            AddGradientStats(features, gray, cells: 2, sortedCells: true);
            return L2Normalize(features.ToArray());
        }

        private static float[] ExtractRenderStyleDescriptor(Image<Rgb24> original)
        {
            const int size = 112;

            var rgb = SquareResizeRgb(original, StyleDescriptorCropMode.Full, size);
            var lum = new float[size, size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = rgb[y, x, 0];
                    float g = rgb[y, x, 1];
                    float b = rgb[y, x, 2];
                    lum[y, x] = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                }
            }

            var (mag, _) = ComputeGradients(lum);
            var broad = GaussianBlur(lum, 5.0f);
            var blur = GaussianBlur(lum, 2.5f);
            var flat = GaussianBlur(lum, 1.0f);
            var detailValues = new List<float>(size * size);
            var blockToneValues = new List<float>(size * size);
            var softEdgeValues = new List<float>(size * size);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    detailValues.Add(MathF.Abs(lum[y, x] - blur[y, x]));
                    blockToneValues.Add(Math.Clamp(lum[y, x] - broad[y, x] + 0.5f, 0f, 1f));
                    softEdgeValues.Add(MathF.Abs(lum[y, x] - flat[y, x]));
                }
            }

            var lumValues = Flatten(lum);
            var magValues = Flatten(mag);
            var features = new List<float>(130);
            AddValueHistogramL2(features, lumValues, bins: 24);
            AddValueHistogramL2(features, blockToneValues, bins: 24);
            AddValueHistogramL2(features, detailValues, bins: 18);
            AddValueHistogramL2(features, softEdgeValues, bins: 18);
            AddQuantilesL2(features, lumValues, new[] { 0.05f, 0.15f, 0.35f, 0.50f, 0.65f, 0.85f, 0.95f });
            AddQuantilesL2(features, magValues, new[] { 0.50f, 0.70f, 0.85f, 0.93f, 0.98f });
            AddQuantilesL2(features, detailValues, new[] { 0.50f, 0.70f, 0.85f, 0.93f, 0.98f });
            AddGradientStats(features, lum, cells: 2, sortedCells: true);
            var summary = new[]
            {
                ValueMean(lumValues),
                ValueStdDev(lumValues),
                ValueMean(detailValues),
                ValueStdDev(detailValues),
                ValueMean(softEdgeValues),
                ValueStdDev(softEdgeValues)
            };
            features.AddRange(L2Normalize(summary));
            return L2Normalize(features.ToArray());
        }

        private static float[] ExtractSketchStyleDescriptor(Image<Rgb24> original)
        {
            const int size = 112;

            var gray = SquareResizeGray(original, StyleDescriptorCropMode.Full, size, autoContrast: false);
            var blur = GaussianBlur(gray, 2.0f);
            var (mag, _) = ComputeGradients(gray);
            var lumValues = Flatten(gray);
            var magValues = Flatten(mag);
            var detailValues = new List<float>(size * size);
            int nearWhitePixels = 0;
            int paleQuietPixels = 0;
            int darkInkPixels = 0;
            int thinLinePixels = 0;
            int strongEdgePixels = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float lum = gray[y, x];
                    float edge = mag[y, x];
                    float detail = MathF.Abs(lum - blur[y, x]);
                    detailValues.Add(detail);
                    if (lum > 0.92f)
                    {
                        nearWhitePixels++;
                        if (edge < 0.025f)
                        {
                            paleQuietPixels++;
                        }
                    }

                    if (lum < 0.58f)
                    {
                        darkInkPixels++;
                    }

                    if (edge > 0.035f && detail < 0.11f)
                    {
                        thinLinePixels++;
                    }

                    if (edge > 0.12f)
                    {
                        strongEdgePixels++;
                    }
                }
            }

            float total = size * size;
            var features = new List<float>(120);
            AddValueHistogramL2(features, lumValues, bins: 24);
            AddValueHistogramL2(features, detailValues, bins: 18);
            AddQuantilesL2(features, lumValues, new[] { 0.05f, 0.20f, 0.50f, 0.80f, 0.92f, 0.98f });
            AddQuantilesL2(features, magValues, new[] { 0.50f, 0.75f, 0.88f, 0.95f, 0.985f });
            var summary = new[]
            {
                nearWhitePixels / total,
                paleQuietPixels / total,
                darkInkPixels / total,
                thinLinePixels / total,
                strongEdgePixels / total,
                ValueMean(lumValues),
                ValueStdDev(lumValues),
                ValueMean(detailValues),
                ValueStdDev(detailValues)
            };
            features.AddRange(L2Normalize(summary));
            return L2Normalize(features.ToArray());
        }

        private static float[,] SquareResizeGray(Image<Rgb24> original, StyleDescriptorCropMode mode, int size, bool autoContrast = true)
        {
            using var resized = CreateSquareCanvas(original, mode, size);
            resized.Mutate(ctx => ctx.Grayscale());
            var gray = new float[size, size];
            resized.ProcessPixelRows(accessor =>
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

            if (autoContrast)
            {
                AutoContrast(gray, cutoff: 0.01f);
            }

            return gray;
        }

        private static float[,,] SquareResizeRgb(Image<Rgb24> original, StyleDescriptorCropMode mode, int size)
        {
            using var resized = CreateSquareCanvas(original, mode, size);
            var rgb = new float[size, size, 3];
            resized.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < size; y++)
                {
                    Span<Rgb24> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < size; x++)
                    {
                        rgb[y, x, 0] = row[x].R / 255f;
                        rgb[y, x, 1] = row[x].G / 255f;
                        rgb[y, x, 2] = row[x].B / 255f;
                    }
                }
            });
            return rgb;
        }

        private static Image<Rgb24> CreateSquareCanvas(Image<Rgb24> original, StyleDescriptorCropMode mode, int size)
        {
            using var crop = CreateDescriptorCrop(original, mode);
            int resizedWidth;
            int resizedHeight;
            if (crop.Width >= crop.Height)
            {
                resizedWidth = size;
                resizedHeight = Math.Max(1, (int)MathF.Round(crop.Height * (size / (float)crop.Width)));
            }
            else
            {
                resizedHeight = size;
                resizedWidth = Math.Max(1, (int)MathF.Round(crop.Width * (size / (float)crop.Height)));
            }

            using var resized = crop.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(resizedWidth, resizedHeight),
                Sampler = KnownResamplers.Lanczos3
            }));

            var canvas = new Image<Rgb24>(size, size, new Rgb24(255, 255, 255));
            canvas.Mutate(ctx => ctx.DrawImage(
                resized,
                new Point((size - resized.Width) / 2, (size - resized.Height) / 2),
                1f));
            return canvas;
        }

        private static Image<Rgb24> CreateDescriptorCrop(Image<Rgb24> original, StyleDescriptorCropMode mode)
        {
            if (mode == StyleDescriptorCropMode.Upper)
            {
                int height = Math.Max(1, (int)MathF.Round(original.Height * 0.62f));
                return original.Clone(ctx => ctx.Crop(new Rectangle(0, 0, original.Width, height)));
            }

            if (mode == StyleDescriptorCropMode.Center)
            {
                int size = Math.Max(1, (int)MathF.Round(Math.Min(original.Width, original.Height) * 0.76f));
                int x = Math.Max(0, (original.Width - size) / 2);
                int y = Math.Max(0, (original.Height - size) / 2);
                return original.Clone(ctx => ctx.Crop(new Rectangle(x, y, Math.Min(size, original.Width - x), Math.Min(size, original.Height - y))));
            }

            if (mode == StyleDescriptorCropMode.FaceCore)
            {
                int width = Math.Max(1, (int)MathF.Round(original.Width * 0.58f));
                int height = Math.Max(1, (int)MathF.Round(original.Height * 0.60f));
                int x = Math.Max(0, (original.Width - width) / 2);
                int centerY = (int)MathF.Round(original.Height * 0.52f);
                int y = Math.Clamp(centerY - height / 2, 0, Math.Max(0, original.Height - height));
                return original.Clone(ctx => ctx.Crop(new Rectangle(x, y, Math.Min(width, original.Width - x), Math.Min(height, original.Height - y))));
            }

            if (mode == StyleDescriptorCropMode.EyeBand)
            {
                int width = Math.Max(1, (int)MathF.Round(original.Width * 0.72f));
                int height = Math.Max(1, (int)MathF.Round(original.Height * 0.34f));
                int x = Math.Max(0, (original.Width - width) / 2);
                int centerY = (int)MathF.Round(original.Height * 0.40f);
                int y = Math.Clamp(centerY - height / 2, 0, Math.Max(0, original.Height - height));
                return original.Clone(ctx => ctx.Crop(new Rectangle(x, y, Math.Min(width, original.Width - x), Math.Min(height, original.Height - y))));
            }

            return original.Clone();
        }

        private static void AutoContrast(float[,] values, float cutoff)
        {
            var flat = Flatten(values);
            flat.Sort();
            float low = Quantile(flat, cutoff);
            float high = Quantile(flat, 1f - cutoff);
            float range = high - low;
            if (range <= 1e-6f)
            {
                return;
            }

            int height = values.GetLength(0);
            int width = values.GetLength(1);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    values[y, x] = Math.Clamp((values[y, x] - low) / range, 0f, 1f);
                }
            }
        }

        private static void AddGradientStats(List<float> features, float[,] gray, int cells, bool sortedCells)
        {
            var (mag, angle) = ComputeGradients(gray);
            var magValues = Flatten(mag);
            magValues.Sort();
            float threshold = Quantile(magValues, 0.70f);
            float clip = Quantile(magValues, 0.985f) + 1e-6f;
            int height = gray.GetLength(0);
            int width = gray.GetLength(1);
            var parts = new List<float[]>(cells * cells);

            for (int cy = 0; cy < cells; cy++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    int y0 = cy * height / cells;
                    int y1 = (cy + 1) * height / cells;
                    int x0 = cx * width / cells;
                    int x1 = (cx + 1) * width / cells;
                    var hist = new float[12];
                    var cellMagnitudes = new List<float>((y1 - y0) * (x1 - x0));
                    int edgeCount = 0;

                    for (int y = y0; y < y1; y++)
                    {
                        for (int x = x0; x < x1; x++)
                        {
                            float clippedMag = Math.Min(mag[y, x], clip);
                            cellMagnitudes.Add(clippedMag);
                            if (clippedMag > threshold)
                            {
                                edgeCount++;
                            }

                            int bin = Math.Min(hist.Length - 1, (int)(angle[y, x] / MathF.PI * hist.Length));
                            hist[bin] += MathF.Sqrt(clippedMag + 1e-8f);
                        }
                    }

                    float total = Math.Max(1, cellMagnitudes.Count);
                    var part = new float[hist.Length + 3];
                    Array.Copy(hist, part, hist.Length);
                    part[^3] = edgeCount / total;
                    part[^2] = ValueMean(cellMagnitudes);
                    part[^1] = ValueStdDev(cellMagnitudes);
                    parts.Add(L2Normalize(part));
                }
            }

            if (sortedCells && parts.Count > 1)
            {
                parts = parts.OrderByDescending(part => part[^3]).ToList();
            }

            features.AddRange(L2Normalize(parts.SelectMany(part => part).ToArray()));
        }

        private static void AddGradientQuantiles(List<float> features, float[,] gray)
        {
            var (mag, _) = ComputeGradients(gray);
            AddQuantilesL2(features, Flatten(mag), new[] { 0.25f, 0.50f, 0.70f, 0.85f, 0.93f, 0.98f });
        }

        private static void AddLbpHistogram(List<float> features, float[,] gray)
        {
            int height = gray.GetLength(0);
            int width = gray.GetLength(1);
            var hist = new float[256];
            int samples = 0;
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
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

            if (samples > 0)
            {
                for (int i = 0; i < hist.Length; i++)
                {
                    hist[i] /= samples;
                }
            }

            features.AddRange(L2Normalize(hist));
        }

        private static void AddEdgeDensityProfile(List<float> features, float[,] gray)
        {
            var (mag, _) = ComputeGradients(gray);
            var values = Flatten(mag);
            values.Sort();
            var thresholds = new[] { 0.60f, 0.75f, 0.88f, 0.95f }.Select(q => Quantile(values, q)).ToArray();
            var result = new List<float>(thresholds.Length * 2);
            foreach (float threshold in thresholds)
            {
                int count = 0;
                double sum = 0d;
                foreach (float value in values)
                {
                    if (value > threshold)
                    {
                        count++;
                        sum += value;
                    }
                }

                result.Add(count / (float)Math.Max(1, values.Count));
                result.Add(count == 0 ? 0f : (float)(sum / count));
            }

            features.AddRange(L2Normalize(result.ToArray()));
        }

        private static void AddSpatialEdgeProfile(List<float> features, float[,] gray, int bands)
        {
            var (mag, _) = ComputeGradients(gray);
            var values = Flatten(mag);
            values.Sort();
            float threshold = Quantile(values, 0.78f);
            int height = gray.GetLength(0);
            int width = gray.GetLength(1);
            var result = new List<float>(bands * 4);

            for (int band = 0; band < bands; band++)
            {
                int x0 = band * width / bands;
                int x1 = (band + 1) * width / bands;
                AddBandEdgeStats(result, mag, threshold, x0, x1, 0, height);
            }

            for (int band = 0; band < bands; band++)
            {
                int y0 = band * height / bands;
                int y1 = (band + 1) * height / bands;
                AddBandEdgeStats(result, mag, threshold, 0, width, y0, y1);
            }

            features.AddRange(L2Normalize(result.ToArray()));
        }

        private static void AddBandEdgeStats(
            List<float> result,
            float[,] mag,
            float threshold,
            int x0,
            int x1,
            int y0,
            int y1)
        {
            int count = 0;
            double sum = 0d;
            int total = Math.Max(1, (x1 - x0) * (y1 - y0));
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    float value = mag[y, x];
                    if (value > threshold)
                    {
                        count++;
                        sum += value;
                    }
                }
            }

            result.Add(count / (float)total);
            result.Add(count == 0 ? 0f : (float)(sum / count));
        }

        private static void AddValueHistogramL2(List<float> features, IReadOnlyList<float> values, int bins)
        {
            var hist = new float[bins];
            foreach (float value in values)
            {
                int bin = Math.Min(bins - 1, (int)(Math.Clamp(value, 0f, 0.999999f) * bins));
                hist[bin] += 1f;
            }

            float sum = hist.Sum();
            if (sum > 1e-6f)
            {
                for (int i = 0; i < hist.Length; i++)
                {
                    hist[i] /= sum;
                }
            }

            features.AddRange(L2Normalize(hist));
        }

        private static void AddWeightedValueHistogramL2(List<float> features, float[,] values, float[,] weights, int bins)
        {
            var hist = new float[bins];
            int height = values.GetLength(0);
            int width = values.GetLength(1);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int bin = Math.Min(bins - 1, (int)(Math.Clamp(values[y, x], 0f, 0.999999f) * bins));
                    hist[bin] += weights[y, x];
                }
            }

            features.AddRange(L2Normalize(hist));
        }

        private static void AddQuantilesL2(List<float> features, IReadOnlyList<float> values, IReadOnlyList<float> quantiles)
        {
            var sorted = values.ToList();
            sorted.Sort();
            var result = new float[quantiles.Count];
            for (int i = 0; i < quantiles.Count; i++)
            {
                result[i] = Quantile(sorted, quantiles[i]);
            }

            features.AddRange(L2Normalize(result));
        }

        private static (float[,] Mag, float[,] Angle) ComputeGradients(float[,] gray)
        {
            int height = gray.GetLength(0);
            int width = gray.GetLength(1);
            var mag = new float[height, width];
            var angle = new float[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float gx = x == 0
                        ? gray[y, Math.Min(width - 1, x + 1)] - gray[y, x]
                        : x == width - 1
                            ? gray[y, x] - gray[y, x - 1]
                            : (gray[y, x + 1] - gray[y, x - 1]) * 0.5f;
                    float gy = y == 0
                        ? gray[Math.Min(height - 1, y + 1), x] - gray[y, x]
                        : y == height - 1
                            ? gray[y, x] - gray[y - 1, x]
                            : (gray[y + 1, x] - gray[y - 1, x]) * 0.5f;
                    mag[y, x] = MathF.Sqrt(gx * gx + gy * gy);
                    float a = MathF.Atan2(gy, gx);
                    while (a < 0f)
                    {
                        a += MathF.PI;
                    }
                    while (a >= MathF.PI)
                    {
                        a -= MathF.PI;
                    }

                    angle[y, x] = a;
                }
            }

            return (mag, angle);
        }

        private static float[,] GaussianBlur(float[,] source, float sigma)
        {
            int height = source.GetLength(0);
            int width = source.GetLength(1);
            int radius = Math.Max(1, (int)MathF.Ceiling(sigma * 3f));
            var kernel = new float[radius * 2 + 1];
            float kernelSum = 0f;
            for (int i = -radius; i <= radius; i++)
            {
                float value = MathF.Exp(-(i * i) / (2f * sigma * sigma));
                kernel[i + radius] = value;
                kernelSum += value;
            }

            for (int i = 0; i < kernel.Length; i++)
            {
                kernel[i] /= kernelSum;
            }

            var temp = new float[height, width];
            var result = new float[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int xx = Math.Clamp(x + k, 0, width - 1);
                        sum += source[y, xx] * kernel[k + radius];
                    }

                    temp[y, x] = sum;
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int yy = Math.Clamp(y + k, 0, height - 1);
                        sum += temp[yy, x] * kernel[k + radius];
                    }

                    result[y, x] = sum;
                }
            }

            return result;
        }

        private static List<float> Flatten(float[,] values)
        {
            int height = values.GetLength(0);
            int width = values.GetLength(1);
            var result = new List<float>(height * width);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    result.Add(values[y, x]);
                }
            }

            return result;
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
        public float[] GrayRenderStyleVector { get; set; } = Array.Empty<float>();
        public float[] GrayLineStyleVector { get; set; } = Array.Empty<float>();
        public float[] GrayToneEdgeStyleVector { get; set; } = Array.Empty<float>();
        public float[] GraySketchStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadGrayStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadRenderStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadLineStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadUpperLineStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadCenterLineStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadFaceLineStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadEyeLineStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadToneEdgeStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadFaceToneEdgeStyleVector { get; set; } = Array.Empty<float>();
        public float[] HeadCropReliabilityVector { get; set; } = Array.Empty<float>();
        public bool UsedHeadFallbackCrop { get; set; }
        public float[] ContentVector { get; set; } = Array.Empty<float>();
    }

    public sealed class ImageClusteringExtractionOptions
    {
        public bool IncludeBaseEmbeddings { get; init; } = true;
        public bool IncludeGrayStyle { get; init; }
        public bool IncludeGrayStructureStyle { get; init; }
        public bool IncludeHeadLineStyle { get; init; }
        public string? HeadDetectionModelPath { get; init; }
        public string? HeadValidationEyeModelPath { get; init; }
        public bool RequireEyeForReliableHeadCrop { get; init; } = true;
        public float HeadDetectionConfidence { get; init; } = 0.35f;
        public float HeadDetectionIou { get; init; } = 0.70f;
        public int HeadDetectionMaxInferSize { get; init; } = 640;
        public float HeadValidationEyeConfidence { get; init; } = 0.28f;
        public float HeadValidationEyeIou { get; init; } = 0.60f;
        public int HeadValidationEyeMaxInferSize { get; init; } = 384;
        public float HeadCropScale { get; init; } = 1.15f;
        public float HeadFallbackCenterScale { get; init; } = 0.42f;
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
