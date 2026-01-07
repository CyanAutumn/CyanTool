using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CyanTools.Utils
{
    public sealed class HandIntegrityFilterOptions
    {
        public string InputDirectory { get; set; } = string.Empty;
        public string OutputDirectory { get; set; } = string.Empty;
        public string GoodOutputDirectory { get; set; } = string.Empty;
        public bool IncludeSubfolders { get; set; } = true;
        public bool MoveBadImages { get; set; } = true;
        public bool MoveGoodImages { get; set; } = true;
        public bool CopyCompanionText { get; set; } = true;
        public string HandDetectorModelPath { get; set; } = string.Empty;
        public string HandLandmarkModelPath { get; set; } = string.Empty;
        public int HandClassId { get; set; } = 0;
        public float DetectorConfidence { get; set; } = 0.25f;
        public float DetectorIouThreshold { get; set; } = 0.7f;
        public int DetectorInputSize { get; set; } = 640;
        public int LandmarkInputSize { get; set; } = 224;
        public string LandmarkInputFormat { get; set; } = "NHWC";
        public float CropScale { get; set; } = 1.2f;
        public float MinFingerLengthRatio { get; set; } = 0.25f;
        public float MinTipToMcpRatio { get; set; } = 1.05f;
        public float MinPresenceScore { get; set; } = 0.5f;
        public bool FailOnLandmarkMissing { get; set; } = true;
    }

    public sealed class HandIntegrityFilter
    {
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

        public Task ProcessAsync(HandIntegrityFilterOptions options, IProgress<double> progress, Action<string> logAction)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.InputDirectory) || !Directory.Exists(options.InputDirectory))
            {
                logAction("错误: 输入目录无效。");
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                logAction("错误: 输出目录未设置。");
                return Task.CompletedTask;
            }

            if (!File.Exists(options.HandDetectorModelPath))
            {
                logAction($"错误: 手部检测模型不存在: {options.HandDetectorModelPath}");
                return Task.CompletedTask;
            }

            if (!File.Exists(options.HandLandmarkModelPath))
            {
                logAction($"错误: 手指关键点模型不存在: {options.HandLandmarkModelPath}");
                return Task.CompletedTask;
            }

            Directory.CreateDirectory(options.OutputDirectory);
            var goodOutputDirectory = NormalizePath(options.GoodOutputDirectory);
            if (!string.IsNullOrWhiteSpace(goodOutputDirectory))
            {
                var inputRoot = NormalizePath(options.InputDirectory);
                var badRoot = NormalizePath(options.OutputDirectory);
                if (string.Equals(goodOutputDirectory, inputRoot, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(goodOutputDirectory, badRoot, StringComparison.OrdinalIgnoreCase))
                {
                    logAction("错误: 好图输出目录不能与输入目录或坏图输出目录相同，将忽略好图输出。");
                    goodOutputDirectory = string.Empty;
                }
                else
                {
                    Directory.CreateDirectory(goodOutputDirectory);
                }
            }

            var searchOption = options.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(options.InputDirectory, "*.*", searchOption)
                                 .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                 .ToList();

            var excludedRoots = new List<string> { NormalizePath(options.OutputDirectory) };
            if (!string.IsNullOrWhiteSpace(goodOutputDirectory))
            {
                excludedRoots.Add(goodOutputDirectory);
            }

            files = files.Where(f => !IsUnderRoots(f, excludedRoots)).ToList();

            if (files.Count == 0)
            {
                logAction("未找到可处理的图片。");
                return Task.CompletedTask;
            }

            int badCount = 0;
            int processedCount = 0;
            int goodCount = 0;
            bool shouldOutputGood = !string.IsNullOrWhiteSpace(goodOutputDirectory);
            var inputRootPath = NormalizePath(options.InputDirectory);

            using var predictor = new HandLandmarkPredictor(options.HandLandmarkModelPath, options.LandmarkInputSize, options.LandmarkInputFormat);

            foreach (var file in files)
            {
                bool isBad = false;
                try
                {
                    using var image = Image.Load<Rgb24>(file);
                    var detections = YoloDetection.Predict(image, options.HandDetectorModelPath, options.DetectorConfidence, options.DetectorIouThreshold, options.DetectorInputSize);
                    var handDetections = FilterHandDetections(detections, options.HandClassId);

                    if (handDetections.Count == 0)
                    {
                        if (shouldOutputGood)
                        {
                            logAction($"无手，视为好图: {Path.GetFileName(file)}");
                        }
                        else
                        {
                            logAction($"跳过(无手): {Path.GetFileName(file)}");
                        }
                    }
                    else
                    {
                        foreach (var detection in handDetections)
                        {
                            using var cropped = CropHand(image, detection.BoundingBox, options.CropScale);
                            if (!predictor.TryPredict(cropped, out var landmarks, out var presenceScore))
                            {
                                if (options.FailOnLandmarkMissing)
                                {
                                    isBad = true;
                                }
                                continue;
                            }

                            if (presenceScore.HasValue && presenceScore.Value < options.MinPresenceScore)
                            {
                                if (options.FailOnLandmarkMissing)
                                {
                                    isBad = true;
                                }
                                continue;
                            }

                            if (HandLandmarkAnalyzer.IsHandBroken(landmarks, options.MinFingerLengthRatio, options.MinTipToMcpRatio))
                            {
                                isBad = true;
                                break;
                            }
                        }
                    }

                    if (isBad)
                    {
                        badCount++;
                        MoveOrCopy(file, inputRootPath, options.OutputDirectory, options.MoveBadImages, options.CopyCompanionText, "坏图", logAction);
                    }
                    else if (shouldOutputGood)
                    {
                        goodCount++;
                        MoveOrCopy(file, inputRootPath, goodOutputDirectory, options.MoveGoodImages, options.CopyCompanionText, "好图", logAction);
                    }
                }
                catch (Exception ex)
                {
                    logAction($"处理失败: {Path.GetFileName(file)}. 原因: {ex.Message}");
                }
                finally
                {
                    processedCount++;
                    progress.Report((double)processedCount / files.Count * 100);
                }
            }

            if (shouldOutputGood)
            {
                logAction($"筛选完成。共处理 {processedCount} 张，坏图 {badCount} 张，好图 {goodCount} 张。");
            }
            else
            {
                logAction($"筛选完成。共处理 {processedCount} 张，坏图 {badCount} 张。");
            }
            return Task.CompletedTask;
        }

        private static List<Detection> FilterHandDetections(List<Detection> detections, int handClassId)
        {
            if (handClassId < 0)
            {
                return detections;
            }

            var classIdText = handClassId.ToString();
            return detections.Where(d => d.Type == classIdText).ToList();
        }

        private static Image<Rgb24> CropHand(Image<Rgb24> image, SixLabors.ImageSharp.Rectangle box, float scale)
        {
            var centerX = box.X + box.Width / 2f;
            var centerY = box.Y + box.Height / 2f;
            var size = Math.Max(box.Width, box.Height) * scale;
            if (size < 1)
            {
                size = Math.Max(box.Width, box.Height);
            }

            var left = centerX - size / 2f;
            var top = centerY - size / 2f;
            var rect = new Rectangle((int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(size), (int)Math.Ceiling(size));

            rect = ClampRect(rect, image.Width, image.Height);
            var cropped = image.Clone(ctx => ctx.Crop(rect));

            int targetSize = Math.Max(cropped.Width, cropped.Height);
            if (cropped.Width == targetSize && cropped.Height == targetSize)
            {
                return cropped;
            }

            var square = new Image<Rgb24>(Configuration.Default, targetSize, targetSize, Color.Black);
            var offsetX = (targetSize - cropped.Width) / 2;
            var offsetY = (targetSize - cropped.Height) / 2;
            square.Mutate(x => x.DrawImage(cropped, new Point(offsetX, offsetY), 1f));
            cropped.Dispose();
            return square;
        }

        private static Rectangle ClampRect(Rectangle rect, int width, int height)
        {
            int x = Math.Max(0, rect.X);
            int y = Math.Max(0, rect.Y);
            int right = Math.Min(width, rect.X + rect.Width);
            int bottom = Math.Min(height, rect.Y + rect.Height);
            int w = Math.Max(1, right - x);
            int h = Math.Max(1, bottom - y);
            return new Rectangle(x, y, w, h);
        }

        private static void MoveOrCopy(string imagePath, string inputRoot, string destinationRoot, bool moveFile, bool copyCompanionText, string label, Action<string> logAction)
        {
            var relativePath = Path.GetRelativePath(inputRoot, imagePath);
            var targetPath = Path.Combine(destinationRoot, relativePath);
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            try
            {
                if (moveFile)
                {
                    File.Move(imagePath, targetPath, true);
                }
                else
                {
                    File.Copy(imagePath, targetPath, true);
                }

                if (copyCompanionText)
                {
                    var sourceTxt = Path.ChangeExtension(imagePath, ".txt");
                    if (!string.IsNullOrEmpty(sourceTxt) && File.Exists(sourceTxt))
                    {
                        var destTxt = Path.ChangeExtension(targetPath, ".txt");
                        if (moveFile)
                        {
                            File.Move(sourceTxt, destTxt, true);
                        }
                        else
                        {
                            File.Copy(sourceTxt, destTxt, true);
                        }
                    }
                }

                logAction($"{label}已{(moveFile ? "移动" : "复制")}: {relativePath}");
            }
            catch (Exception ex)
            {
                logAction($"{label}处理失败: {Path.GetFileName(imagePath)}. 原因: {ex.Message}");
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        private static bool IsUnderRoots(string filePath, IEnumerable<string> roots)
        {
            var fullPath = Path.GetFullPath(filePath);
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class HandLandmarkPredictor : IDisposable
    {
        private static readonly object SessionLock = new();
        private static readonly ConcurrentDictionary<string, InferenceSession> SessionCache = new();

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly int _inputSize;
        private readonly string _inputFormat;

        public HandLandmarkPredictor(string modelPath, int inputSize, string inputFormat)
        {
            _session = GetOrCreateSession(modelPath);
            _inputName = _session.InputMetadata.Keys.First();
            _inputSize = inputSize;
            _inputFormat = string.IsNullOrWhiteSpace(inputFormat) ? "NHWC" : inputFormat.Trim().ToUpperInvariant();
        }

        public bool TryPredict(Image<Rgb24> image, out Vector2[] landmarks, out float? presenceScore)
        {
            presenceScore = null;
            using var resized = image.Clone(ctx => ctx.Resize(_inputSize, _inputSize, KnownResamplers.Bicubic));
            var tensor = ImageToTensor(resized, _inputFormat);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, tensor)
            };

            using var results = _session.Run(inputs);
            if (!TryReadLandmarks(results, out landmarks))
            {
                return false;
            }

            presenceScore = TryReadPresenceScore(results);
            return true;
        }

        public void Dispose()
        {
        }

        private static DenseTensor<float> ImageToTensor(Image<Rgb24> image, string inputFormat)
        {
            int width = image.Width;
            int height = image.Height;
            var isNhwc = string.Equals(inputFormat, "NHWC", StringComparison.OrdinalIgnoreCase);
            var tensor = isNhwc
                ? new DenseTensor<float>(new[] { 1, height, width, 3 })
                : new DenseTensor<float>(new[] { 1, 3, height, width });

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var pixel = row[x];
                        float r = pixel.R / 255f;
                        float g = pixel.G / 255f;
                        float b = pixel.B / 255f;

                        if (isNhwc)
                        {
                            tensor[0, y, x, 0] = r;
                            tensor[0, y, x, 1] = g;
                            tensor[0, y, x, 2] = b;
                        }
                        else
                        {
                            tensor[0, 0, y, x] = r;
                            tensor[0, 1, y, x] = g;
                            tensor[0, 2, y, x] = b;
                        }
                    }
                }
            });

            return tensor;
        }

        private static bool TryReadLandmarks(IReadOnlyCollection<DisposableNamedOnnxValue> results, out Vector2[] landmarks)
        {
            foreach (var result in results)
            {
                if (result.Value is Tensor<float> tensor)
                {
                    int elementCount = GetElementCount(tensor.Dimensions);
                    if (elementCount == 63)
                    {
                        var values = tensor.ToArray();
                        var points = new Vector2[21];
                        for (int i = 0; i < 21; i++)
                        {
                            points[i] = new Vector2(values[i * 3], values[i * 3 + 1]);
                        }
                        landmarks = points;
                        return true;
                    }
                }
            }

            landmarks = Array.Empty<Vector2>();
            return false;
        }

        private static float? TryReadPresenceScore(IReadOnlyCollection<DisposableNamedOnnxValue> results)
        {
            foreach (var result in results)
            {
                if (result.Value is Tensor<float> tensor)
                {
                    int elementCount = GetElementCount(tensor.Dimensions);
                    if (elementCount == 1)
                    {
                        var values = tensor.ToArray();
                        return values.Length == 1 ? values[0] : null;
                    }
                }
            }

            return null;
        }

        private static InferenceSession GetOrCreateSession(string modelPath)
        {
            if (SessionCache.TryGetValue(modelPath, out var cached))
            {
                return cached;
            }

            lock (SessionLock)
            {
                if (SessionCache.TryGetValue(modelPath, out cached))
                {
                    return cached;
                }

                var options = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };
                cached = new InferenceSession(modelPath, options);
                SessionCache[modelPath] = cached;
                return cached;
            }
        }

        private static int GetElementCount(ReadOnlySpan<int> dimensions)
        {
            int count = 1;
            for (int i = 0; i < dimensions.Length; i++)
            {
                count *= dimensions[i];
            }
            return count;
        }
    }

    internal static class HandLandmarkAnalyzer
    {
        public static bool IsHandBroken(IReadOnlyList<Vector2> landmarks, float minFingerLengthRatio, float minTipToMcpRatio)
        {
            if (landmarks == null || landmarks.Count < 21)
            {
                return true;
            }

            if (!AreLandmarksValid(landmarks))
            {
                return true;
            }

            var palmWidth = Distance(landmarks[5], landmarks[17]);
            var palmHeight = Distance(landmarks[0], landmarks[9]);
            var palmSize = MathF.Max(palmWidth, palmHeight);
            if (palmSize <= 0)
            {
                return true;
            }

            bool thumbOk = IsFingerOk(landmarks, 2, 4, palmSize, minFingerLengthRatio, minTipToMcpRatio);
            bool indexOk = IsFingerOk(landmarks, 5, 8, palmSize, minFingerLengthRatio, minTipToMcpRatio);
            bool middleOk = IsFingerOk(landmarks, 9, 12, palmSize, minFingerLengthRatio, minTipToMcpRatio);
            bool ringOk = IsFingerOk(landmarks, 13, 16, palmSize, minFingerLengthRatio, minTipToMcpRatio);
            bool pinkyOk = IsFingerOk(landmarks, 17, 20, palmSize, minFingerLengthRatio, minTipToMcpRatio);

            return !(thumbOk && indexOk && middleOk && ringOk && pinkyOk);
        }

        private static bool AreLandmarksValid(IReadOnlyList<Vector2> landmarks)
        {
            foreach (var point in landmarks)
            {
                if (float.IsNaN(point.X) || float.IsNaN(point.Y) || float.IsInfinity(point.X) || float.IsInfinity(point.Y))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsFingerOk(IReadOnlyList<Vector2> landmarks, int mcpIndex, int tipIndex, float palmSize, float minFingerLengthRatio, float minTipToMcpRatio)
        {
            var wrist = landmarks[0];
            var mcp = landmarks[mcpIndex];
            var tip = landmarks[tipIndex];

            if (!IsNormalizedPoint(mcp) || !IsNormalizedPoint(tip))
            {
                return false;
            }

            var fingerLength = Distance(mcp, tip);
            if (fingerLength < palmSize * minFingerLengthRatio)
            {
                return false;
            }

            var wristToTip = Distance(wrist, tip);
            var wristToMcp = Distance(wrist, mcp);
            if (wristToTip < wristToMcp * minTipToMcpRatio)
            {
                return false;
            }

            return true;
        }

        private static bool IsNormalizedPoint(Vector2 point)
        {
            const float tolerance = 0.1f;
            return point.X >= -tolerance && point.X <= 1f + tolerance && point.Y >= -tolerance && point.Y <= 1f + tolerance;
        }

        private static float Distance(Vector2 a, Vector2 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}
