using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CyanTools.Utils
{
    public sealed class SameSourceGroupingOptions
    {
        public string ComputeBackend { get; set; } = "cpu";
    }

    public static class SameSourceImageGroupingProcessor
    {
        private const double ThumbnailRatioTolerance = 0.03;
        private const int ThumbnailPreviewSize = 32;
        private const double ThumbnailPreviewMeanDiffThreshold = 0.20;
        private const double ThumbnailPreviewCorrelationThreshold = 0.55;
        private const double ContainmentRecallThreshold = 0.75;
        private const double ContainmentMatchThreshold = 0.87;
        private const double ContainmentMatchMaxMeanDiff = 0.12;
        private const double ContainmentRecallFallbackScore = 0.70;
        private const double ContainmentRecallFallbackMargin = 0.20;
        private const double ContainmentMatchFallbackScore = 0.70;
        private const double ContainmentMatchFallbackMargin = 0.20;
        private const double DifferenceMaxChangedRatio = 0.10;
        private const double DifferenceVisualMaxChangedRatio = 0.40;
        private const double DifferenceVisualMaxMeanDiff = 0.08;
        private const double DifferenceVisualCorrelationThreshold = 0.73;
        private const double DifferenceVisualEdgeCorrelationThreshold = 0.73;
        private const int DifferenceVisualPreviewSize = 128;

        private static readonly string[] ContainmentRecallScales = BuildScaleRange(100, 250, 10);
        private static readonly string[] ContainmentMatchScales = BuildScaleRange(100, 250, 5);

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".bmp"
        };

        private static readonly Dictionary<string, string> OutputFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            ["original"] = "原图",
            ["thumbnail"] = "缩略图",
            ["duplicate"] = "副本",
            ["contained"] = "被包含",
            ["difference"] = "差分",
            ["other"] = "其他"
        };

        public static string ResolveComputeBackend(string requestedDevice)
        {
            string normalized = string.IsNullOrWhiteSpace(requestedDevice)
                ? "cpu"
                : requestedDevice.Trim().ToLowerInvariant();

            if (normalized is not ("auto" or "cpu" or "cuda"))
            {
                throw new ArgumentOutOfRangeException(nameof(requestedDevice), $"unsupported device: {requestedDevice}");
            }



            return "cpu";
        }

        public static void Process(
            string inputPath,
            string outputPath,
            SameSourceGroupingOptions options,
            IProgress<double>? progress,
            Action<string>? logAction,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
            {
                throw new DirectoryNotFoundException("输入文件夹不存在。");
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new DirectoryNotFoundException("输出文件夹不能为空。");
            }

            string inputFull = Path.GetFullPath(inputPath);
            string outputFull = Path.GetFullPath(outputPath);

            if (string.Equals(inputFull, outputFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("输出目录不能与输入目录相同。");
            }

            if (IsSubPath(inputFull, outputFull))
            {
                throw new InvalidOperationException("输出目录不能位于输入目录内。");
            }

            string requestedBackend = options?.ComputeBackend ?? "cpu";
            string computeBackend = ResolveComputeBackend(requestedBackend);
            if (string.Equals(requestedBackend?.Trim(), "cuda", StringComparison.OrdinalIgnoreCase))
            {
                logAction?.Invoke("当前同源图片归类实现使用 CPU；旧版 CUDA 设置已自动切换为 CPU。");
            }
            logAction?.Invoke($"使用计算后端: {computeBackend}");

            ClassificationResult result = ClassifyDirectory(inputFull, progress, logAction, token, computeBackend);
            ExportGroups(result, outputFull, progress, logAction, token);

            progress?.Report(100);
            logAction?.Invoke("处理完成。");
        }

        private static ClassificationResult ClassifyDirectory(
            string inputDir,
            IProgress<double>? progress,
            Action<string>? logAction,
            CancellationToken token,
            string computeBackend)
        {
            var stageTimings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Stopwatch totalWatch = Stopwatch.StartNew();

            token.ThrowIfCancellationRequested();
            logAction?.Invoke("读取图片元数据...");
            Stopwatch stageWatch = Stopwatch.StartNew();
            Dictionary<string, LoadedImage> images = LoadImageRecords(inputDir, logAction, token);
            stageTimings["load_records"] = stageWatch.Elapsed.TotalSeconds;
            progress?.Report(15);

            token.ThrowIfCancellationRequested();
            logAction?.Invoke("生成缩略图预览和灰度视图...");
            stageWatch.Restart();
            foreach (LoadedImage image in images.Values)
            {
                image.EnsureDerivedViews();
            }

            stageTimings["load_rgb_arrays"] = stageWatch.Elapsed.TotalSeconds;
            progress?.Report(25);

            var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var classMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var scoreMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var reasonMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                token.ThrowIfCancellationRequested();
                logAction?.Invoke("缩略图判定...");
                stageWatch.Restart();
                ClassifyThumbnails(images, parentMap, classMap, scoreMap, reasonMap, token);
                stageTimings["thumbnail"] = stageWatch.Elapsed.TotalSeconds;
                progress?.Report(40);

                token.ThrowIfCancellationRequested();
                logAction?.Invoke("副本判定...");
                stageWatch.Restart();
                ClassifyDuplicates(images, parentMap, classMap, scoreMap, reasonMap, token);
                stageTimings["duplicate"] = stageWatch.Elapsed.TotalSeconds;
                progress?.Report(55);

                token.ThrowIfCancellationRequested();
                logAction?.Invoke("被包含候选召回...");
                stageWatch.Restart();
                var candidateMap = BuildContainmentCandidates(images, parentMap, ContainmentRecallScales, token);
                stageTimings["containment_candidates"] = stageWatch.Elapsed.TotalSeconds;
                progress?.Report(70);

                token.ThrowIfCancellationRequested();
                logAction?.Invoke("被包含精判...");
                stageWatch.Restart();
                ApplyContainmentMatches(images, parentMap, classMap, scoreMap, reasonMap, candidateMap, ContainmentMatchScales, token);
                stageTimings["containment_match"] = stageWatch.Elapsed.TotalSeconds;
                progress?.Report(82);

                token.ThrowIfCancellationRequested();
                logAction?.Invoke("差分判定...");
                stageWatch.Restart();
                ClassifyDifferences(images, parentMap, classMap, scoreMap, reasonMap, token);
                stageTimings["difference"] = stageWatch.Elapsed.TotalSeconds;
                progress?.Report(90);

                var entries = new Dictionary<string, ClassificationEntry>(StringComparer.OrdinalIgnoreCase);
                foreach ((string name, LoadedImage image) in images.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string? parentName = parentMap.GetValueOrDefault(name);
                    if (parentName is null)
                    {
                        entries[name] = new ClassificationEntry(
                            image.Path,
                            "original",
                            null,
                            name,
                            1.0,
                            "group_anchor");
                    }
                    else
                    {
                        entries[name] = new ClassificationEntry(
                            image.Path,
                            classMap[name],
                            parentName,
                            ResolveRoot(name, parentMap),
                            scoreMap[name],
                            reasonMap[name]);
                    }
                }

                var classCounts = entries.Values
                    .GroupBy(entry => entry.ClassName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

                return new ClassificationResult(
                    entries,
                    classCounts,
                    computeBackend,
                    stageTimings,
                    totalWatch.Elapsed.TotalSeconds);
            }
            finally
            {
                foreach (LoadedImage image in images.Values)
                {
                    image.Dispose();
                }
            }
        }

        private static Dictionary<string, LoadedImage> LoadImageRecords(string inputDir, Action<string>? logAction, CancellationToken token)
        {
            string cacheDir = Path.Combine(inputDir, ".image_relation_pipeline_cache");
            string cachePath = Path.Combine(cacheDir, "records.json");
            Dictionary<string, CachedRecord> existingCache = LoadRecordCache(cachePath);
            var updatedCache = new Dictionary<string, CachedRecord>(StringComparer.OrdinalIgnoreCase);
            var records = new Dictionary<string, LoadedImage>(StringComparer.OrdinalIgnoreCase);

            IEnumerable<string> files = Directory.EnumerateFiles(inputDir, "*.*", MaterialClassificationSession.Current != null ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    FileInfo info = new(file);
                    string fileName = MaterialClassificationSession.Current != null ? Path.GetRelativePath(inputDir, file) : Path.GetFileName(file);
                    long mtimeNs = ToUnixTimeNanoseconds(info.LastWriteTimeUtc);
                    CachedRecord? cached = existingCache.GetValueOrDefault(fileName);

                    LoadedImage image = LoadImage(file, cached, info.Length, mtimeNs);
                    records[fileName] = image;
                    updatedCache[fileName] = new CachedRecord
                    {
                        FileSize = info.Length,
                        MtimeNs = mtimeNs,
                        Width = image.Width,
                        Height = image.Height,
                        PixelSha256 = image.PixelSha256
                    };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logAction?.Invoke($"跳过无法读取图片: {Path.GetFileName(file)} ({ex.Message})");
                }
            }

            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(cachePath, JsonConvert.SerializeObject(updatedCache, Formatting.Indented), Encoding.UTF8);
            logAction?.Invoke($"发现图片数量: {records.Count}");
            return records;
        }

        private static LoadedImage LoadImage(string filePath, CachedRecord? cached, long fileSize, long mtimeNs)
        {
            using Image<Rgb24> image = SixLabors.ImageSharp.Image.Load<Rgb24>(filePath);
            image.Mutate(x => x.AutoOrient());

            byte[] pixels = new byte[image.Width * image.Height * 3];
            image.CopyPixelDataTo(pixels);

            string pixelSha256 = cached is not null
                                 && cached.FileSize == fileSize
                                 && cached.MtimeNs == mtimeNs
                                 && cached.Width == image.Width
                                 && cached.Height == image.Height
                                 && !string.IsNullOrWhiteSpace(cached.PixelSha256)
                ? cached.PixelSha256
                : ComputePixelSha256(pixels);

            using Mat rgbMat = new(image.Height, image.Width, MatType.CV_8UC3, pixels);
            Mat stored = rgbMat.Clone();
            return new LoadedImage(filePath, image.Width, image.Height, pixelSha256, stored);
        }

        private static Dictionary<string, CachedRecord> LoadRecordCache(string cachePath)
        {
            if (!File.Exists(cachePath))
            {
                return new Dictionary<string, CachedRecord>(StringComparer.OrdinalIgnoreCase);
            }

            string json = File.ReadAllText(cachePath, Encoding.UTF8);
            return JsonConvert.DeserializeObject<Dictionary<string, CachedRecord>>(json)
                   ?? new Dictionary<string, CachedRecord>(StringComparer.OrdinalIgnoreCase);
        }

        private static void ClassifyThumbnails(
            IReadOnlyDictionary<string, LoadedImage> images,
            IDictionary<string, string> parentMap,
            IDictionary<string, string> classMap,
            IDictionary<string, double> scoreMap,
            IDictionary<string, string> reasonMap,
            CancellationToken token)
        {
            foreach (string parentName in GetOrderedNames(images))
            {
                token.ThrowIfCancellationRequested();
                if (parentMap.ContainsKey(parentName))
                {
                    continue;
                }

                LoadedImage parent = images[parentName];
                foreach (string childName in GetOrderedNames(images))
                {
                    if (childName == parentName || parentMap.ContainsKey(childName))
                    {
                        continue;
                    }

                    LoadedImage child = images[childName];
                    if (parent.Area <= child.Area)
                    {
                        continue;
                    }

                    double ratioDelta = RatioDelta(parent, child);
                    if (ratioDelta > ThumbnailRatioTolerance)
                    {
                        continue;
                    }

                    (double previewMeanDiff, double previewCorrelation) = ThumbnailPreviewMetrics(parent.ThumbnailPreview!, child.ThumbnailPreview!);
                    if (previewMeanDiff > ThumbnailPreviewMeanDiffThreshold
                        || previewCorrelation < ThumbnailPreviewCorrelationThreshold)
                    {
                        continue;
                    }

                    (double meanDiff, double correlation) = ThumbnailMetrics(parent.Rgb, child.Rgb);
                    if (meanDiff <= 0.12 && correlation >= 0.72)
                    {
                        parentMap[childName] = parentName;
                        classMap[childName] = "thumbnail";
                        scoreMap[childName] = correlation;
                        reasonMap[childName] = $"ratio_delta={ratioDelta:F4}, mean_diff={meanDiff:F4}, corr={correlation:F4}";
                    }
                }
            }
        }

        private static void ClassifyDuplicates(
            IReadOnlyDictionary<string, LoadedImage> images,
            IDictionary<string, string> parentMap,
            IDictionary<string, string> classMap,
            IDictionary<string, double> scoreMap,
            IDictionary<string, string> reasonMap,
            CancellationToken token)
        {
            List<string> orderedNames = GetOrderedNames(images);
            foreach (string parentName in orderedNames)
            {
                token.ThrowIfCancellationRequested();
                if (parentMap.ContainsKey(parentName))
                {
                    continue;
                }

                LoadedImage parent = images[parentName];
                foreach (string childName in orderedNames)
                {
                    if (childName == parentName || parentMap.ContainsKey(childName))
                    {
                        continue;
                    }

                    LoadedImage child = images[childName];
                    if (child.Width != parent.Width || child.Height != parent.Height)
                    {
                        continue;
                    }

                    if (!string.Equals(child.PixelSha256, parent.PixelSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    parentMap[childName] = parentName;
                    classMap[childName] = "duplicate";
                    scoreMap[childName] = 1.0;
                    reasonMap[childName] = "same_dimensions_and_pixel_hash";
                }
            }
        }

        private static List<(string ChildName, List<string> CandidateParents)> BuildContainmentCandidates(
            IReadOnlyDictionary<string, LoadedImage> images,
            IDictionary<string, string> parentMap,
            string[] scaleCandidates,
            CancellationToken token)
        {
            List<string> activeNames = GetActiveNames(images, parentMap);
            var result = new List<(string ChildName, List<string> CandidateParents)>();

            foreach (string childName in activeNames.AsEnumerable().Reverse())
            {
                token.ThrowIfCancellationRequested();
                if (parentMap.ContainsKey(childName))
                {
                    continue;
                }

                LoadedImage child = images[childName];
                var scoredCandidates = new List<(string ParentName, double Score)>();
                foreach (string parentName in activeNames)
                {
                    if (parentName == childName || parentMap.ContainsKey(parentName))
                    {
                        continue;
                    }

                    LoadedImage parent = images[parentName];
                    if (parent.Area <= child.Area)
                    {
                        continue;
                    }

                    if (parent.Width < child.Width || parent.Height < child.Height)
                    {
                        continue;
                    }

                    double coarseScore = ContainmentBestScore(
                        parent.ContainmentRecallGray!,
                        parent.ContainmentRecallScale,
                        child.ContainmentRecallGray!,
                        child.ContainmentRecallScale,
                        scaleCandidates,
                        out _);

                    scoredCandidates.Add((parentName, coarseScore));
                }

                if (scoredCandidates.Count == 0)
                {
                    continue;
                }

                scoredCandidates.Sort((left, right) =>
                {
                    int scoreCompare = right.Score.CompareTo(left.Score);
                    return scoreCompare != 0 ? scoreCompare : string.Compare(left.ParentName, right.ParentName, StringComparison.OrdinalIgnoreCase);
                });

                List<string> candidates = scoredCandidates
                    .Where(candidate => candidate.Score >= ContainmentRecallThreshold)
                    .Select(candidate => candidate.ParentName)
                    .ToList();

                if (candidates.Count == 0)
                {
                    double bestScore = scoredCandidates[0].Score;
                    double secondBestScore = scoredCandidates.Count > 1 ? scoredCandidates[1].Score : 0.0;
                    if (IsClearBestMatch(bestScore, secondBestScore, ContainmentRecallFallbackScore, ContainmentRecallFallbackMargin))
                    {
                        candidates.Add(scoredCandidates[0].ParentName);
                    }
                }

                if (candidates.Count > 0)
                {
                    result.Add((childName, candidates));
                }
            }

            return result;
        }

        private static void ApplyContainmentMatches(
            IReadOnlyDictionary<string, LoadedImage> images,
            IDictionary<string, string> parentMap,
            IDictionary<string, string> classMap,
            IDictionary<string, double> scoreMap,
            IDictionary<string, string> reasonMap,
            IReadOnlyList<(string ChildName, List<string> CandidateParents)> candidateMap,
            string[] scaleCandidates,
            CancellationToken token)
        {
            foreach ((string childName, List<string> candidateParents) in candidateMap)
            {
                token.ThrowIfCancellationRequested();
                if (parentMap.ContainsKey(childName))
                {
                    continue;
                }

                LoadedImage child = images[childName];
                string? bestParentName = null;
                double bestScore = 0.0;
                double secondBestScore = 0.0;
                double bestScale = 1.0;
                double bestMeanDiff = 1.0;

                foreach (string parentName in candidateParents)
                {
                    if (parentMap.ContainsKey(parentName))
                    {
                        continue;
                    }

                    LoadedImage parent = images[parentName];
                    (double score, double scale, double meanDiff) = ContainmentMatchMetrics(
                        parent.ContainmentGray!,
                        parent.ContainmentScale,
                        child.ContainmentGray!,
                        child.ContainmentScale,
                        scaleCandidates);

                    if (score > bestScore)
                    {
                        secondBestScore = bestScore;
                        bestScore = score;
                        bestParentName = parentName;
                        bestScale = scale;
                        bestMeanDiff = meanDiff;
                    }
                    else if (score > secondBestScore)
                    {
                        secondBestScore = score;
                    }
                }

                if (bestParentName is not null
                    && IsAcceptableContainmentMatch(
                        bestScore,
                        secondBestScore,
                        bestMeanDiff,
                        ContainmentMatchThreshold,
                        ContainmentMatchMaxMeanDiff,
                        ContainmentMatchFallbackScore,
                        ContainmentMatchFallbackMargin))
                {
                    parentMap[childName] = bestParentName;
                    classMap[childName] = "contained";
                    scoreMap[childName] = bestScore;
                    reasonMap[childName] = $"match_template_score={bestScore:F4}, runner_up_score={secondBestScore:F4}, mean_diff={bestMeanDiff:F4}, scale={bestScale:F2}";
                }
            }
        }

        private static void ClassifyDifferences(
            IReadOnlyDictionary<string, LoadedImage> images,
            IDictionary<string, string> parentMap,
            IDictionary<string, string> classMap,
            IDictionary<string, double> scoreMap,
            IDictionary<string, string> reasonMap,
            CancellationToken token)
        {
            foreach (string parentName in GetActiveNames(images, parentMap))
            {
                token.ThrowIfCancellationRequested();
                if (parentMap.ContainsKey(parentName))
                {
                    continue;
                }

                LoadedImage parent = images[parentName];
                foreach (string childName in GetActiveNames(images, parentMap))
                {
                    if (childName == parentName || parentMap.ContainsKey(childName))
                    {
                        continue;
                    }

                    LoadedImage child = images[childName];
                    if (child.Width != parent.Width || child.Height != parent.Height)
                    {
                        continue;
                    }

                    (double meanDiff, double changedRatio) = DifferenceMetrics(parent.Rgb, child.Rgb);
                    bool isDifference = IsPixelDifferenceMatch(meanDiff, changedRatio);
                    double? visualCorrelation = null;
                    double? edgeCorrelation = null;

                    if (!isDifference && CanTryVisualDifference(meanDiff, changedRatio))
                    {
                        (double visual, double edge) = VisualDifferenceMetrics(parent.Rgb, child.Rgb);
                        visualCorrelation = visual;
                        edgeCorrelation = edge;
                        isDifference = IsVisualDifferenceMatch(visual, edge);
                    }

                    if (isDifference)
                    {
                        parentMap[childName] = parentName;
                        classMap[childName] = "difference";
                        double score = 1.0 - changedRatio;
                        string reason = $"mean_diff={meanDiff:F4}, changed_ratio={changedRatio:F4}";
                        if (visualCorrelation.HasValue && edgeCorrelation.HasValue)
                        {
                            score = Math.Max(score, Math.Min(visualCorrelation.Value, edgeCorrelation.Value));
                            reason = $"{reason}, visual_corr={visualCorrelation.Value:F4}, edge_corr={edgeCorrelation.Value:F4}";
                        }

                        scoreMap[childName] = score;
                        reasonMap[childName] = reason;
                    }
                }
            }
        }

        private static void ExportGroups(
            ClassificationResult result,
            string outputDir,
            IProgress<double>? progress,
            Action<string>? logAction,
            CancellationToken token)
        {
            Directory.CreateDirectory(outputDir);
            foreach (string folderName in MaterialClassificationSession.Current == null ? OutputFolders.Values : Enumerable.Empty<string>())
            {
                string folderPath = Path.Combine(outputDir, folderName);
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, recursive: true);
                }

                Directory.CreateDirectory(folderPath);
            }

            string manifestPath = Path.Combine(outputDir, "manifest.json");
            if (MaterialClassificationSession.Current == null && File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }

            var groupedCounters = new Dictionary<(string GroupId, string ClassName), int>();
            var manifestRows = new List<object>();
            int processed = 0;

            foreach ((string name, ClassificationEntry entry) in result.Entries.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();

                string destinationPath;
                if (string.Equals(entry.ClassName, "original", StringComparison.OrdinalIgnoreCase))
                {
                    destinationPath = Path.Combine(outputDir, OutputFolders["original"], Path.GetFileName(entry.SrcPath));
                }
                else
                {
                    string folderName = OutputFolders.GetValueOrDefault(entry.ClassName, OutputFolders["other"]);
                    ClassificationEntry groupOriginal = result.Entries[entry.GroupId];
                    string groupLabel = Path.GetFileNameWithoutExtension(groupOriginal.SrcPath);
                    (string GroupId, string ClassName) counterKey = (entry.GroupId, entry.ClassName);
                    groupedCounters[counterKey] = groupedCounters.GetValueOrDefault(counterKey) + 1;
                    int index = groupedCounters[counterKey];
                    string fileName = $"{groupLabel}_{folderName}{index}{Path.GetExtension(entry.SrcPath)}";
                    destinationPath = Path.Combine(outputDir, folderName, fileName);
                }

                if (MaterialClassificationSession.Current?.Builder is { } materialIndex)
                    materialIndex.RecordImage(entry.SrcPath, destinationPath);
                else
                {
                    File.Copy(entry.SrcPath, destinationPath, overwrite: true);
                    CopySidecarTxt(entry.SrcPath, destinationPath);
                }

                manifestRows.Add(new
                {
                    src_path = ToManifestPath(entry.SrcPath),
                    group_id = entry.GroupId,
                    @class = entry.ClassName,
                    parent_path = entry.ParentName is null ? null : ToManifestPath(result.Entries[entry.ParentName].SrcPath),
                    score = entry.Score,
                    reason = entry.Reason
                });

                processed++;
                progress?.Report(90 + 10.0 * processed / Math.Max(1, result.Entries.Count));
            }

            if (MaterialClassificationSession.Current == null) File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifestRows, Formatting.Indented), Encoding.UTF8);
            logAction?.Invoke($"导出完成，结果数量: {result.Entries.Count}");
        }

        private static void CopySidecarTxt(string sourcePath, string destinationPath)
        {
            string sourceTxt = Path.ChangeExtension(sourcePath, ".txt");
            if (!File.Exists(sourceTxt))
            {
                return;
            }

            string destinationTxt = Path.ChangeExtension(destinationPath, ".txt");
            File.Copy(sourceTxt, destinationTxt, overwrite: true);
        }

        private static List<string> GetOrderedNames(IReadOnlyDictionary<string, LoadedImage> images)
        {
            return images.Keys
                .OrderByDescending(name => images[name].Area)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> GetActiveNames(IReadOnlyDictionary<string, LoadedImage> images, IDictionary<string, string> parentMap)
        {
            return GetOrderedNames(images)
                .Where(name => !parentMap.ContainsKey(name))
                .ToList();
        }

        private static string ResolveRoot(string name, IDictionary<string, string> parentMap)
        {
            string current = name;
            while (parentMap.TryGetValue(current, out string? parent))
            {
                current = parent;
            }

            return current;
        }

        private static double RatioDelta(LoadedImage parent, LoadedImage child)
        {
            return Math.Abs(parent.AspectRatio - child.AspectRatio) / parent.AspectRatio;
        }

        private static (double MeanDiff, double Correlation) ThumbnailMetrics(Mat parentRgb, Mat childRgb)
        {
            using Mat resized = new();
            Cv2.Resize(parentRgb, resized, new OpenCvSharp.Size(childRgb.Width, childRgb.Height), 0, 0, InterpolationFlags.Area);

            using Mat diff = new();
            Cv2.Absdiff(resized, childRgb, diff);
            Scalar diffMean = Cv2.Mean(diff);
            double meanDiff = (diffMean.Val0 + diffMean.Val1 + diffMean.Val2) / (3.0 * 255.0);

            using Mat parentGray = new();
            using Mat childGray = new();
            Cv2.CvtColor(resized, parentGray, ColorConversionCodes.RGB2GRAY);
            Cv2.CvtColor(childRgb, childGray, ColorConversionCodes.RGB2GRAY);

            return (meanDiff, ComputeCorrelation(ReadMatBytes(parentGray), ReadMatBytes(childGray)));
        }

        private static (double MeanDiff, double Correlation) ThumbnailPreviewMetrics(byte[] parentPreview, byte[] childPreview)
        {
            double meanDiff = 0;
            for (int i = 0; i < parentPreview.Length; i++)
            {
                meanDiff += Math.Abs(parentPreview[i] - childPreview[i]) / 255.0;
            }

            meanDiff /= Math.Max(1, parentPreview.Length);
            return (meanDiff, ComputeCorrelation(parentPreview, childPreview));
        }

        private static (double MeanDiff, double ChangedRatio) DifferenceMetrics(Mat leftRgb, Mat rightRgb)
        {
            using Mat diff = new();
            Cv2.Absdiff(leftRgb, rightRgb, diff);
            using Mat gray = new();
            Cv2.CvtColor(diff, gray, ColorConversionCodes.RGB2GRAY);

            byte[] bytes = ReadMatBytes(gray);
            double meanDiff = bytes.Length == 0 ? 0.0 : bytes.Average(value => value) / 255.0;
            double changedRatio = bytes.Length == 0 ? 0.0 : bytes.Count(value => value > 16) / (double)bytes.Length;
            return (meanDiff, changedRatio);
        }

        private static (double VisualCorrelation, double EdgeCorrelation) VisualDifferenceMetrics(Mat leftRgb, Mat rightRgb)
        {
            using Mat leftGray = new();
            using Mat rightGray = new();
            using Mat leftPreview = new();
            using Mat rightPreview = new();
            Cv2.CvtColor(leftRgb, leftGray, ColorConversionCodes.RGB2GRAY);
            Cv2.CvtColor(rightRgb, rightGray, ColorConversionCodes.RGB2GRAY);
            Cv2.Resize(leftGray, leftPreview, new OpenCvSharp.Size(DifferenceVisualPreviewSize, DifferenceVisualPreviewSize), 0, 0, InterpolationFlags.Area);
            Cv2.Resize(rightGray, rightPreview, new OpenCvSharp.Size(DifferenceVisualPreviewSize, DifferenceVisualPreviewSize), 0, 0, InterpolationFlags.Area);

            double visualCorrelation = ComputeCorrelation(ReadMatBytes(leftPreview), ReadMatBytes(rightPreview));

            using Mat leftEdgeX = new();
            using Mat leftEdgeY = new();
            using Mat rightEdgeX = new();
            using Mat rightEdgeY = new();
            Cv2.Sobel(leftPreview, leftEdgeX, MatType.CV_32F, 1, 0, ksize: 3);
            Cv2.Sobel(leftPreview, leftEdgeY, MatType.CV_32F, 0, 1, ksize: 3);
            Cv2.Sobel(rightPreview, rightEdgeX, MatType.CV_32F, 1, 0, ksize: 3);
            Cv2.Sobel(rightPreview, rightEdgeY, MatType.CV_32F, 0, 1, ksize: 3);

            double edgeCorrelation = (
                ComputeCorrelation(ReadMatSingles(leftEdgeX), ReadMatSingles(rightEdgeX))
                + ComputeCorrelation(ReadMatSingles(leftEdgeY), ReadMatSingles(rightEdgeY))) / 2.0;

            return (visualCorrelation, edgeCorrelation);
        }

        private static double ContainmentBestScore(
            Mat parentGray,
            double parentScale,
            Mat childGray,
            double childScale,
            IEnumerable<string> scaleCandidates,
            out double bestScale)
        {
            double bestScore = 0.0;
            bestScale = 1.0;
            double scaleFactor = parentScale / childScale;
            double maxOriginalScale = Math.Min(
                parentGray.Width / (double)childGray.Width,
                parentGray.Height / (double)childGray.Height) / scaleFactor;
            maxOriginalScale = Math.Max(1.0, maxOriginalScale);

            foreach (string scaleText in scaleCandidates)
            {
                double originalScale = double.Parse(scaleText, System.Globalization.CultureInfo.InvariantCulture);
                if (originalScale > maxOriginalScale)
                {
                    break;
                }

                double effectiveScale = originalScale * scaleFactor;
                int scaledWidth = Math.Max(1, (int)Math.Round(childGray.Width * effectiveScale));
                int scaledHeight = Math.Max(1, (int)Math.Round(childGray.Height * effectiveScale));
                if (scaledWidth > parentGray.Width || scaledHeight > parentGray.Height)
                {
                    continue;
                }

                using Mat scaledChild = new();
                Cv2.Resize(childGray, scaledChild, new OpenCvSharp.Size(scaledWidth, scaledHeight), 0, 0, InterpolationFlags.Linear);
                using Mat result = new();
                Cv2.MatchTemplate(parentGray, scaledChild, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxScore, out _, out _);

                if (maxScore > bestScore)
                {
                    bestScore = maxScore;
                    bestScale = originalScale;
                }
            }

            return bestScore;
        }

        private static (double Score, double Scale, double MeanDiff) ContainmentMatchMetrics(
            Mat parentGray,
            double parentScale,
            Mat childGray,
            double childScale,
            IEnumerable<string> scaleCandidates)
        {
            double bestScore = 0.0;
            double bestScale = 1.0;
            double bestMeanDiff = 1.0;
            double scaleFactor = parentScale / childScale;
            double maxOriginalScale = Math.Min(
                parentGray.Width / (double)childGray.Width,
                parentGray.Height / (double)childGray.Height) / scaleFactor;
            maxOriginalScale = Math.Max(1.0, maxOriginalScale);

            foreach (string scaleText in scaleCandidates)
            {
                double originalScale = double.Parse(scaleText, System.Globalization.CultureInfo.InvariantCulture);
                if (originalScale > maxOriginalScale)
                {
                    break;
                }

                double effectiveScale = originalScale * scaleFactor;
                int scaledWidth = Math.Max(1, (int)Math.Round(childGray.Width * effectiveScale));
                int scaledHeight = Math.Max(1, (int)Math.Round(childGray.Height * effectiveScale));
                if (scaledWidth > parentGray.Width || scaledHeight > parentGray.Height)
                {
                    continue;
                }

                using Mat scaledChild = new();
                Cv2.Resize(childGray, scaledChild, new OpenCvSharp.Size(scaledWidth, scaledHeight), 0, 0, InterpolationFlags.Linear);
                using Mat result = new();
                Cv2.MatchTemplate(parentGray, scaledChild, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxScore, out _, out OpenCvSharp.Point maxLocation);

                if (maxScore > bestScore)
                {
                    using Mat parentWindow = new(parentGray, new Rect(maxLocation.X, maxLocation.Y, scaledWidth, scaledHeight));
                    using Mat diff = new();
                    Cv2.Absdiff(parentWindow, scaledChild, diff);
                    Scalar diffMean = Cv2.Mean(diff);

                    bestScore = maxScore;
                    bestScale = originalScale;
                    bestMeanDiff = diffMean.Val0 / 255.0;
                }
            }

            return (bestScore, bestScale, bestMeanDiff);
        }

        private static bool IsClearBestMatch(double bestScore, double secondBestScore, double minimumScore, double minimumMargin)
        {
            return bestScore >= minimumScore && (bestScore - secondBestScore) >= minimumMargin;
        }

        private static bool IsAcceptableContainmentMatch(
            double bestScore,
            double secondBestScore,
            double meanDiff,
            double matchThreshold,
            double maxMeanDiff,
            double fallbackScore,
            double fallbackMargin)
        {
            if (meanDiff > maxMeanDiff)
            {
                return false;
            }

            return bestScore >= matchThreshold || IsClearBestMatch(bestScore, secondBestScore, fallbackScore, fallbackMargin);
        }

        private static bool IsPixelDifferenceMatch(double meanDiff, double changedRatio)
        {
            return changedRatio > 0.0 && changedRatio <= DifferenceMaxChangedRatio && meanDiff <= 0.05;
        }

        private static bool CanTryVisualDifference(double meanDiff, double changedRatio)
        {
            return changedRatio > 0.0
                   && changedRatio <= DifferenceVisualMaxChangedRatio
                   && meanDiff <= DifferenceVisualMaxMeanDiff;
        }

        private static bool IsVisualDifferenceMatch(double visualCorrelation, double edgeCorrelation)
        {
            return visualCorrelation >= DifferenceVisualCorrelationThreshold
                   && edgeCorrelation >= DifferenceVisualEdgeCorrelationThreshold;
        }

        private static bool IsSubPath(string basePath, string candidatePath)
        {
            string normalizedBase = basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;
            return candidatePath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase);
        }

        private static long ToUnixTimeNanoseconds(DateTime utcDateTime)
        {
            DateTime normalizedUtc = utcDateTime.Kind == DateTimeKind.Utc
                ? utcDateTime
                : utcDateTime.ToUniversalTime();
            return checked((normalizedUtc - DateTime.UnixEpoch).Ticks * 100L);
        }

        private static string ToManifestPath(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        private static string ComputePixelSha256(byte[] pixels)
        {
            byte[] hash = SHA256.HashData(pixels);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static byte[] BuildThumbnailPreview(Mat rgb)
        {
            using Mat gray = new();
            using Mat preview = new();
            Cv2.CvtColor(rgb, gray, ColorConversionCodes.RGB2GRAY);
            Cv2.Resize(gray, preview, new OpenCvSharp.Size(ThumbnailPreviewSize, ThumbnailPreviewSize), 0, 0, InterpolationFlags.Area);
            return ReadMatBytes(preview);
        }

        private static (Mat Gray, double Scale) PrepareContainmentGray(Mat rgb, int maxDim)
        {
            using Mat gray = new();
            Cv2.CvtColor(rgb, gray, ColorConversionCodes.RGB2GRAY);

            int width = gray.Width;
            int height = gray.Height;
            double scale = Math.Min(1.0, maxDim / (double)Math.Max(width, height));
            if (scale >= 1.0)
            {
                return (gray.Clone(), 1.0);
            }

            int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(height * scale));
            Mat resized = new();
            Cv2.Resize(gray, resized, new OpenCvSharp.Size(targetWidth, targetHeight), 0, 0, InterpolationFlags.Area);
            return (resized, scale);
        }

        private static double ComputeCorrelation(IReadOnlyList<byte> left, IReadOnlyList<byte> right)
        {
            if (left.Count == 0 || right.Count == 0 || left.Count != right.Count)
            {
                return 0.0;
            }

            double leftMean = left.Average(value => value);
            double rightMean = right.Average(value => value);
            double numerator = 0.0;
            double leftVariance = 0.0;
            double rightVariance = 0.0;

            for (int i = 0; i < left.Count; i++)
            {
                double leftCentered = left[i] - leftMean;
                double rightCentered = right[i] - rightMean;
                numerator += leftCentered * rightCentered;
                leftVariance += leftCentered * leftCentered;
                rightVariance += rightCentered * rightCentered;
            }

            double denominator = Math.Sqrt(leftVariance * rightVariance);
            if (denominator <= 1e-12)
            {
                return left.SequenceEqual(right) ? 1.0 : 0.0;
            }

            return numerator / denominator;
        }

        private static double ComputeCorrelation(IReadOnlyList<float> left, IReadOnlyList<float> right)
        {
            if (left.Count == 0 || right.Count == 0 || left.Count != right.Count)
            {
                return 0.0;
            }

            double leftMean = left.Average(value => value);
            double rightMean = right.Average(value => value);
            double numerator = 0.0;
            double leftVariance = 0.0;
            double rightVariance = 0.0;

            for (int i = 0; i < left.Count; i++)
            {
                double leftCentered = left[i] - leftMean;
                double rightCentered = right[i] - rightMean;
                numerator += leftCentered * rightCentered;
                leftVariance += leftCentered * leftCentered;
                rightVariance += rightCentered * rightCentered;
            }

            double denominator = Math.Sqrt(leftVariance * rightVariance);
            if (denominator <= 1e-12)
            {
                return AreClose(left, right) ? 1.0 : 0.0;
            }

            return numerator / denominator;
        }

        private static bool AreClose(IReadOnlyList<float> left, IReadOnlyList<float> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (Math.Abs(left[i] - right[i]) > 1e-6f)
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] ReadMatBytes(Mat mat)
        {
            if (mat.Empty())
            {
                return Array.Empty<byte>();
            }

            int byteCount = checked((int)(mat.Total() * mat.ElemSize()));
            if (byteCount == 0)
            {
                return Array.Empty<byte>();
            }

            byte[] bytes = new byte[byteCount];
            Marshal.Copy(mat.Data, bytes, 0, byteCount);
            return bytes;
        }

        private static float[] ReadMatSingles(Mat mat)
        {
            if (mat.Empty())
            {
                return Array.Empty<float>();
            }

            if (mat.Depth() != MatType.CV_32F)
            {
                throw new ArgumentException("Mat must contain 32-bit float values.", nameof(mat));
            }

            int valueCount = checked((int)(mat.Total() * mat.Channels()));
            if (valueCount == 0)
            {
                return Array.Empty<float>();
            }

            float[] values = new float[valueCount];
            Marshal.Copy(mat.Data, values, 0, valueCount);
            return values;
        }

        private static string[] BuildScaleRange(int startPercent, int endPercent, int stepPercent)
        {
            return Enumerable.Range(0, ((endPercent - startPercent) / stepPercent) + 1)
                .Select(index => ((startPercent + index * stepPercent) / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
        }

        private sealed class LoadedImage : IDisposable
        {
            public LoadedImage(string path, int width, int height, string pixelSha256, Mat rgb)
            {
                Path = path;
                Name = System.IO.Path.GetFileName(path);
                Width = width;
                Height = height;
                PixelSha256 = pixelSha256;
                Rgb = rgb;
            }

            public string Path { get; }
            public string Name { get; }
            public int Width { get; }
            public int Height { get; }
            public string PixelSha256 { get; }
            public Mat Rgb { get; }
            public long Area => (long)Width * Height;
            public double AspectRatio => Width / (double)Height;

            public byte[]? ThumbnailPreview { get; private set; }
            public Mat? ContainmentGray { get; private set; }
            public double ContainmentScale { get; private set; }
            public Mat? ContainmentRecallGray { get; private set; }
            public double ContainmentRecallScale { get; private set; }

            public void EnsureDerivedViews()
            {
                ThumbnailPreview ??= BuildThumbnailPreview(Rgb);

                if (ContainmentGray is null)
                {
                    (ContainmentGray, ContainmentScale) = PrepareContainmentGray(Rgb, 1200);
                }

                if (ContainmentRecallGray is null)
                {
                    (ContainmentRecallGray, ContainmentRecallScale) = PrepareContainmentGray(Rgb, 256);
                }
            }

            public void Dispose()
            {
                Rgb.Dispose();
                ContainmentGray?.Dispose();
                ContainmentRecallGray?.Dispose();
            }
        }

        private sealed class CachedRecord
        {
            [JsonProperty("file_size")]
            public long FileSize { get; set; }

            [JsonProperty("mtime_ns")]
            public long MtimeNs { get; set; }

            [JsonProperty("width")]
            public int Width { get; set; }

            [JsonProperty("height")]
            public int Height { get; set; }

            [JsonProperty("pixel_sha256")]
            public string PixelSha256 { get; set; } = string.Empty;
        }

        private sealed record ClassificationEntry(
            string SrcPath,
            string ClassName,
            string? ParentName,
            string GroupId,
            double Score,
            string Reason);

        private sealed record ClassificationResult(
            Dictionary<string, ClassificationEntry> Entries,
            Dictionary<string, int> ClassCounts,
            string ComputeBackend,
            Dictionary<string, double> StageTimings,
            double TotalElapsed);
    }
}
