using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using OpenCvSharp;
using OpenCvSharp.Features2D;

namespace CyanTools.Utils
{
        public sealed class SameSourceGroupingOptions
        {
            public bool SyncDanbooruTagsToRoot { get; set; } = true;
            public bool IncludeSubfolders { get; set; } = true;
            public bool EnableFastMode { get; set; } = false;
            public bool EnablePatchHash { get; set; } = true;
            public bool EnableHashMatch { get; set; } = false;
            public bool EnableOrbMatch { get; set; } = true;
            public int PatchGrid { get; set; } = 5;
            public int PhashThreshold { get; set; } = 12;
            public int PatchPhashThreshold { get; set; } = 16;
            public int LowResHashThreshold { get; set; } = 12;
            public int OrbFeatures { get; set; } = 2000;
            public int OrbMaxSize { get; set; } = 1024;
            public float LoweRatio { get; set; } = 0.75f;
            public int MinGoodMatches { get; set; } = 25;
            public int MinInliers { get; set; } = 20;
            public double MinInlierRatio { get; set; } = 0.30;
            public double RansacReprojThreshold { get; set; } = 3.0;
            public double CropAreaRatioThreshold { get; set; } = 0.90;
            public double LowResRatioThreshold { get; set; } = 0.85;
            public double EditedDiffRatioThreshold { get; set; } = 0.15;
            public int DiffCheckSize { get; set; } = 512;
            public int DiffPixelThreshold { get; set; } = 20;
            public bool EnableTemplateMatch { get; set; } = true;
            public double TemplateMatchThreshold { get; set; } = 0.85;
            public int TemplateMaxSize { get; set; } = 1024;
            public int TemplateMinSize { get; set; } = 64;
            public bool SkipMatchedNonRoot { get; set; } = true;
            public int MaxCandidatesPerImage { get; set; } = 600;
            public int MaxDegreeOfParallelism { get; set; } = 4;
        }

    public static class SameSourceImageGroupingProcessor
    {
        private const string RootFolderName = "原图";
        private const string CropFolderName = "被包含";
        private const string LowResFolderName = "缩略图";
        private const string EditedFolderName = "差分";
        private const string UnknownFolderName = "未知";
        private const double CandidateAspectRatioTolerance = 0.22;
        private const double DirectLowResAspectRatioTolerance = 0.08;
        private const double DirectLowResDiffRatioThreshold = 0.12;
        private const int DirectLowResCompareSize = 320;
        private const double TemplateVerifyDiffRatioThreshold = 0.30;
        private const double TemplateStrictVerifyDiffRatioThreshold = 0.20;
        private const double TemplateStrictMatchThreshold = 0.92;
        private const double MinInlierCoverageRatioForMatch = 0.05;
        private const double MinMappedAreaRatioForMatch = 0.08;
        private const double OrbSanityAspectRatioTolerance = 0.12;
        private const double OrbSanityAreaRatioThreshold = 0.45;
        private const double OrbSanityDiffMargin = 0.55;
        private const double OrbSanityMinDiffThreshold = 0.55;
        private const double OrbSanityMaxDiffThreshold = 0.85;

        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff"
        };

        private enum RelationType
        {
            Same,
            Crop,
            LowRes,
            Edited,
            Unknown
        }

        private readonly struct CandidateMatch
        {
            public CandidateMatch(int index, int hashDistance)
            {
                Index = index;
                HashDistance = hashDistance;
            }

            public int Index { get; }
            public int HashDistance { get; }
        }

        public static void Process(string inputPath, string outputPath, SameSourceGroupingOptions options,
            IProgress<double>? progress, Action<string>? logAction, CancellationToken token)
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
                logAction?.Invoke("输出目录不能与输入目录相同。");
                return;
            }

            if (IsSubPath(inputFull, outputFull))
            {
                logAction?.Invoke("输出目录不能位于输入目录内。");
                return;
            }

            Directory.CreateDirectory(outputFull);

            var excludedRoots = BuildExcludedRoots(inputFull, outputFull);

            logAction?.Invoke("扫描图片文件...");
            var files = EnumerateImageFiles(inputFull, options.IncludeSubfolders, excludedRoots).ToList();
            if (files.Count == 0)
            {
                logAction?.Invoke("未找到支持的图片文件。");
                return;
            }

            logAction?.Invoke($"发现图片数量: {files.Count}");

            progress?.Report(0);
            token.ThrowIfCancellationRequested();

            var items = ExtractImageItems(files, options, progress, logAction, token).OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();
            if (items.Count == 0)
            {
                logAction?.Invoke("没有可处理的图片。");
                return;
            }

            logAction?.Invoke($"成功读取图片数量: {items.Count}");

            if (!options.EnableHashMatch && !options.EnableOrbMatch && !options.EnableTemplateMatch)
            {
                logAction?.Invoke("未启用任何匹配步骤，请至少勾选一种匹配方式。");
                return;
            }

            token.ThrowIfCancellationRequested();
            logAction?.Invoke("建立哈希索引...");
            var hashIndex = BuildHashIndex(items);

            token.ThrowIfCancellationRequested();
            logAction?.Invoke("开始匹配同源图片...");
            if (options.EnableFastMode)
            {
                logAction?.Invoke("加速模式已开启：将限制候选并提前停止低优先级匹配，可能漏检。");
            }

            using var orb = ORB.Create(options.OrbFeatures);
            using var featureCache = new OrbFeatureCache();
            int matchedPairs = MatchSameSource(items, hashIndex, options, orb, featureCache, progress, logAction, token, out var unionFind);

            logAction?.Invoke($"匹配完成，确认同源对数: {matchedPairs}");

            token.ThrowIfCancellationRequested();
            logAction?.Invoke("汇总分组并输出文件...");

            CopyGroupedFiles(items, unionFind, outputFull, options, orb, featureCache, progress, logAction, token);

            progress?.Report(100);
            logAction?.Invoke("处理完成。");
        }

        private static IEnumerable<string> EnumerateImageFiles(string inputFull, bool includeSubfolders, IReadOnlyList<string> excludedRoots)
        {
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(inputFull, "*.*", searchOption))
            {
                string fullPath = Path.GetFullPath(file);
                if (IsUnderAnyRoot(fullPath, excludedRoots))
                {
                    continue;
                }

                if (SupportedExtensions.Contains(Path.GetExtension(fullPath)))
                {
                    yield return fullPath;
                }
            }
        }

        private static List<string> BuildExcludedRoots(string inputFull, string outputFull)
        {
            var roots = new List<string>();
            if (string.Equals(inputFull, outputFull, StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(Path.Combine(inputFull, RootFolderName));
                roots.Add(Path.Combine(inputFull, CropFolderName));
                roots.Add(Path.Combine(inputFull, LowResFolderName));
                roots.Add(Path.Combine(inputFull, EditedFolderName));
                roots.Add(Path.Combine(inputFull, UnknownFolderName));

                roots.Add(Path.Combine(inputFull, "被包含图片"));
                roots.Add(Path.Combine(inputFull, "略缩图"));
            }
            else if (IsSubPath(inputFull, outputFull))
            {
                roots.Add(outputFull);
            }

            return roots;
        }

        private static bool IsUnderAnyRoot(string filePath, IReadOnlyList<string> roots)
        {
            foreach (var root in roots)
            {
                if (IsSubPath(root, filePath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSubPath(string basePath, string filePath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
            {
                return false;
            }

            string normalizedBase = basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return filePath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase);
        }

        private static List<ImageItem> ExtractImageItems(List<string> files, SameSourceGroupingOptions options,
            IProgress<double>? progress, Action<string>? logAction, CancellationToken token)
        {
            var items = new ConcurrentBag<ImageItem>();
            int total = files.Count;
            int processed = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                CancellationToken = token
            };

            Parallel.ForEach(files, parallelOptions, file =>
            {
                parallelOptions.CancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var mat = Cv2.ImRead(file, ImreadModes.Color);
                    if (mat.Empty())
                    {
                        logAction?.Invoke($"无法读取图片: {file}");
                        return;
                    }

                    var fullHash = ComputePHash64(mat);
                    var patchHashes = options.EnablePatchHash ? ComputePatchHashes(mat, options.PatchGrid) : Array.Empty<ulong>();
                    double sharpness = ComputeSharpness(mat);
                    long fileSize = new FileInfo(file).Length;

                    items.Add(new ImageItem(file, mat.Width, mat.Height, fileSize, sharpness, fullHash, patchHashes));
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"读取失败: {file} ({ex.Message})");
                }
                finally
                {
                    int done = Interlocked.Increment(ref processed);
                    progress?.Report(35.0 * done / total);
                }
            });

            return items.ToList();
        }

        private static Dictionary<ushort, List<int>> BuildHashIndex(IReadOnlyList<ImageItem> items)
        {
            var index = new Dictionary<ushort, List<int>>();
            for (int i = 0; i < items.Count; i++)
            {
                var parts = new HashSet<ushort>();
                foreach (var hash in items[i].AllHashes)
                {
                    foreach (var part in GetHashParts(hash))
                    {
                        parts.Add(part);
                    }
                }

                foreach (var part in parts)
                {
                    if (!index.TryGetValue(part, out var list))
                    {
                        list = new List<int>();
                        index[part] = list;
                    }

                    list.Add(i);
                }
            }

            return index;
        }

        private static int MatchSameSource(IReadOnlyList<ImageItem> items, Dictionary<ushort, List<int>> hashIndex,
            SameSourceGroupingOptions options, ORB orb, OrbFeatureCache featureCache, IProgress<double>? progress,
            Action<string>? logAction, CancellationToken token, out DisjointSet unionFind)
        {
            int total = items.Count;
            unionFind = new DisjointSet(total);
            int matchedPairs = 0;
            int maxCandidates = Math.Max(0, options.MaxCandidatesPerImage);
            bool[] skipAsSource = options.SkipMatchedNonRoot ? new bool[total] : Array.Empty<bool>();
            const int maxTrimmedLogs = 3;
            int trimmedLogCount = 0;
            var logWatch = Stopwatch.StartNew();
            var progressWatch = Stopwatch.StartNew();
            TimeSpan logInterval = TimeSpan.FromSeconds(5);
            bool enableHashMatch = options.EnableHashMatch;
            bool enableOrbMatch = options.EnableOrbMatch;
            bool enableTemplateMatch = options.EnableTemplateMatch;
            bool fastMode = options.EnableFastMode;
            int fastModeCandidateCap = 240;
            int fastModeExpensiveCheckDistance = Math.Max(Math.Max(options.PhashThreshold, options.LowResHashThreshold), options.PatchPhashThreshold) + 4;
            int fastModeEarlyStopDistance = fastModeExpensiveCheckDistance + 4;
            int fastModeEarlyStopFailures = 40;

            for (int i = 0; i < total; i++)
            {
                token.ThrowIfCancellationRequested();

                if (options.SkipMatchedNonRoot && skipAsSource[i])
                {
                    progress?.Report(35 + 45.0 * (i + 1) / total);
                    continue;
                }

                var candidates = GatherCandidates(i, items[i], hashIndex);
                var candidateList = new List<CandidateMatch>();
                foreach (var j in candidates)
                {
                    if (j <= i)
                    {
                        continue;
                    }

                    if (IsHashNear(items[i], items[j], options, out int minDistance))
                    {
                        candidateList.Add(new CandidateMatch(j, minDistance));
                    }
                }

                if (candidateList.Count == 0)
                {
                    if (!fastMode)
                    {
                        AddFallbackCandidatesByGeometry(i, items, options, candidateList);
                    }
                }

                if (candidateList.Count == 0)
                {
                    progress?.Report(35 + 45.0 * (i + 1) / total);
                    continue;
                }

                int effectiveMaxCandidates = maxCandidates;
                if (fastMode)
                {
                    effectiveMaxCandidates = effectiveMaxCandidates > 0
                        ? Math.Min(effectiveMaxCandidates, fastModeCandidateCap)
                        : fastModeCandidateCap;
                }

                if (candidateList.Count > 1
                    && (fastMode || (effectiveMaxCandidates > 0 && candidateList.Count > effectiveMaxCandidates)))
                {
                    candidateList.Sort((a, b) =>
                    {
                        int cmp = a.HashDistance.CompareTo(b.HashDistance);
                        return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
                    });
                }

                if (effectiveMaxCandidates > 0 && candidateList.Count > effectiveMaxCandidates)
                {
                    int originalCount = candidateList.Count;
                    candidateList.RemoveRange(effectiveMaxCandidates, candidateList.Count - effectiveMaxCandidates);

                    if (trimmedLogCount < maxTrimmedLogs)
                    {
                        logAction?.Invoke($"候选过多 ({originalCount})，仅保留最相近的 {candidateList.Count} 个进行匹配。");
                        trimmedLogCount++;
                        if (trimmedLogCount == maxTrimmedLogs)
                        {
                            logAction?.Invoke("候选过多提示已自动省略...");
                        }
                    }
                }

                try
                {
                    OrbFeatureData? featuresA = null;
                    int fastFarMisses = 0;

                    for (int c = 0; c < candidateList.Count; c++)
                    {
                        var candidate = candidateList[c];
                        int j = candidate.Index;
                        token.ThrowIfCancellationRequested();

                        if (unionFind.Find(i) == unionFind.Find(j))
                        {
                            continue;
                        }

                        bool matched = false;
                        bool allowExpensiveChecks = !fastMode || candidate.HashDistance <= fastModeExpensiveCheckDistance;

                        if (enableHashMatch)
                        {
                            unionFind.Union(i, j);
                            matchedPairs++;
                            matched = true;
                        }
                        else if (IsLowResMatchByHash(items[i], items[j], options)
                            || IsLowResMatchByResize(items[i], items[j], options, logAction))
                        {
                            unionFind.Union(i, j);
                            matchedPairs++;
                            matched = true;
                        }

                        if (!matched && enableOrbMatch && allowExpensiveChecks && featuresA == null)
                        {
                            featuresA = featureCache.GetOrCreate(i, () =>
                                ComputeOrbFeaturesFromFile(items[i], orb, options, logAction));
                        }

                        if (!matched && enableOrbMatch && allowExpensiveChecks && featuresA != null && !featuresA.Descriptors.Empty())
                        {
                            var featuresB = featureCache.GetOrCreate(j, () =>
                                ComputeOrbFeaturesFromFile(items[j], orb, options, logAction));

                            if (featuresB != null && !featuresB.Descriptors.Empty())
                            {
                                if (TryEstimateHomography(featuresA, featuresB, options, out var homography, out _, out _,
                                    out double inlierCoverageA, out double inlierCoverageB) && homography != null)
                                {
                                    using (homography)
                                    {
                                        double mappedAreaRatio = ComputeMappedAreaRatio(featuresA.ScaledSize, featuresB.ScaledSize, homography);
                                        bool geometryValid = inlierCoverageA >= MinInlierCoverageRatioForMatch
                                                             && inlierCoverageB >= MinInlierCoverageRatioForMatch
                                                             && mappedAreaRatio >= MinMappedAreaRatioForMatch;
                                        if (geometryValid
                                            && PassOrbAlignmentSanityCheck(items[i], featuresA, items[j], featuresB, homography, options, logAction))
                                        {
                                            unionFind.Union(i, j);
                                            matchedPairs++;
                                            matched = true;
                                        }
                                    }
                                }
                            }
                        }

                        if (!matched && enableTemplateMatch && allowExpensiveChecks)
                        {
                            if (TryTemplateMatchCrop(items[i], items[j], options, logAction, strict: true))
                            {
                                unionFind.Union(i, j);
                                matchedPairs++;
                                matched = true;
                            }
                        }

                        if (matched)
                        {
                            fastFarMisses = 0;
                            if (MarkNonRootAfterMatch(i, j, items, skipAsSource, options))
                            {
                                break;
                            }
                        }
                        else if (fastMode)
                        {
                            if (candidate.HashDistance >= fastModeEarlyStopDistance)
                            {
                                fastFarMisses++;
                                if (fastFarMisses >= fastModeEarlyStopFailures)
                                {
                                    break;
                                }
                            }
                            else
                            {
                                fastFarMisses = 0;
                            }
                        }

                        if (progress != null && progressWatch.ElapsedMilliseconds >= 500)
                        {
                            double ratio = (double)(c + 1) / candidateList.Count;
                            progress.Report(35 + 45.0 * ((i + ratio) / total));
                            progressWatch.Restart();
                        }

                        if (logAction != null && logWatch.Elapsed >= logInterval)
                        {
                            logAction($"匹配中: {i + 1}/{total}, 候选 {c + 1}/{candidateList.Count}, 已确认 {matchedPairs}");
                            logWatch.Restart();
                        }
                    }

                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"匹配失败: {items[i].Path} ({ex.Message})");
                }

                progress?.Report(35 + 45.0 * (i + 1) / total);
            }

            return matchedPairs;
        }

        private static HashSet<int> GatherCandidates(int index, ImageItem item, Dictionary<ushort, List<int>> hashIndex)
        {
            var candidates = new HashSet<int>();
            foreach (var hash in item.AllHashes)
            {
                foreach (var part in GetHashParts(hash))
                {
                    if (!hashIndex.TryGetValue(part, out var list))
                    {
                        continue;
                    }

                    foreach (var idx in list)
                    {
                        if (idx != index)
                        {
                            candidates.Add(idx);
                        }
                    }
                }
            }

            return candidates;
        }

        private static void AddFallbackCandidatesByGeometry(int index, IReadOnlyList<ImageItem> items,
            SameSourceGroupingOptions options, List<CandidateMatch> candidateList)
        {
            for (int j = index + 1; j < items.Count; j++)
            {
                if (!CouldBeContainmentOrLowRes(items[index], items[j], options))
                {
                    continue;
                }

                int score = BuildGeometryCandidateScore(items[index], items[j]);
                candidateList.Add(new CandidateMatch(j, score));
            }
        }

        private static bool CouldBeContainmentOrLowRes(ImageItem a, ImageItem b, SameSourceGroupingOptions options)
        {
            int smaller = Math.Min(a.PixelCount, b.PixelCount);
            int larger = Math.Max(a.PixelCount, b.PixelCount);
            if (larger <= 0)
            {
                return false;
            }

            double areaRatio = (double)smaller / larger;
            if (areaRatio >= 0.99 || areaRatio < 0.03)
            {
                return false;
            }

            double aspectRatioDiff = GetAspectRatioDiff(a, b);
            if (aspectRatioDiff <= CandidateAspectRatioTolerance)
            {
                return true;
            }

            return areaRatio < options.CropAreaRatioThreshold;
        }

        private static int BuildGeometryCandidateScore(ImageItem a, ImageItem b)
        {
            int smaller = Math.Min(a.PixelCount, b.PixelCount);
            int larger = Math.Max(a.PixelCount, b.PixelCount);
            double areaRatio = larger <= 0 ? 1.0 : (double)smaller / larger;
            double aspectDiff = GetAspectRatioDiff(a, b);
            int areaPenalty = (int)Math.Round((1.0 - areaRatio) * 1000);
            int aspectPenalty = (int)Math.Round(aspectDiff * 500);
            return 10_000 + areaPenalty + aspectPenalty;
        }

        private static double GetAspectRatioDiff(ImageItem a, ImageItem b)
        {
            double ratioA = (double)a.Width / Math.Max(1, a.Height);
            double ratioB = (double)b.Width / Math.Max(1, b.Height);
            return Math.Abs(ratioA - ratioB) / Math.Max(ratioA, ratioB);
        }

        private static bool MarkNonRootAfterMatch(int indexA, int indexB, IReadOnlyList<ImageItem> items,
            bool[] skipAsSource, SameSourceGroupingOptions options)
        {
            if (!options.SkipMatchedNonRoot || skipAsSource.Length == 0)
            {
                return false;
            }

            int preferred = SelectPreferredIndex(indexA, indexB, items);
            int skipIndex = preferred == indexA ? indexB : indexA;
            if (!skipAsSource[skipIndex])
            {
                skipAsSource[skipIndex] = true;
            }

            return skipIndex == indexA;
        }

        private static int SelectPreferredIndex(int indexA, int indexB, IReadOnlyList<ImageItem> items)
        {
            var a = items[indexA];
            var b = items[indexB];

            if (a.PixelCount != b.PixelCount)
            {
                return a.PixelCount > b.PixelCount ? indexA : indexB;
            }

            int sharpnessCompare = a.Sharpness.CompareTo(b.Sharpness);
            if (sharpnessCompare != 0)
            {
                return sharpnessCompare > 0 ? indexA : indexB;
            }

            if (a.FileSize != b.FileSize)
            {
                return a.FileSize > b.FileSize ? indexA : indexB;
            }

            return indexA;
        }

        private static bool IsHashNear(ImageItem a, ImageItem b, SameSourceGroupingOptions options, out int minDistance)
        {
            int fullThreshold = Math.Max(options.PhashThreshold, options.LowResHashThreshold);
            minDistance = HammingDistance(a.FullHash, b.FullHash);
            if (minDistance <= fullThreshold)
            {
                return true;
            }

            if (!options.EnablePatchHash)
            {
                return false;
            }

            if (a.PatchHashes.Length == 0 || b.PatchHashes.Length == 0)
            {
                return false;
            }

            int patchHitCount = 0;
            int strongPatchThreshold = Math.Max(0, options.PatchPhashThreshold - 3);
            int weakPatchHitRequired = 3;

            foreach (var hashA in a.PatchHashes)
            {
                foreach (var hashB in b.PatchHashes)
                {
                    int distance = HammingDistance(hashA, hashB);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                    }

                    if (distance <= strongPatchThreshold)
                    {
                        return true;
                    }

                    if (distance <= options.PatchPhashThreshold)
                    {
                        patchHitCount++;
                        if (patchHitCount >= weakPatchHitRequired)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsLowResMatchByHash(ImageItem a, ImageItem b, SameSourceGroupingOptions options)
        {
            if (HammingDistance(a.FullHash, b.FullHash) > options.LowResHashThreshold)
            {
                return false;
            }

            int smaller = Math.Min(a.PixelCount, b.PixelCount);
            int larger = Math.Max(a.PixelCount, b.PixelCount);
            return smaller < larger * options.LowResRatioThreshold;
        }

        private static bool IsLowResMatchByResize(ImageItem a, ImageItem b, SameSourceGroupingOptions options, Action<string>? logAction)
        {
            int smaller = Math.Min(a.PixelCount, b.PixelCount);
            int larger = Math.Max(a.PixelCount, b.PixelCount);
            if (smaller >= larger * options.LowResRatioThreshold)
            {
                return false;
            }

            if (GetAspectRatioDiff(a, b) > DirectLowResAspectRatioTolerance)
            {
                return false;
            }

            if (HammingDistance(a.FullHash, b.FullHash) <= options.LowResHashThreshold)
            {
                return true;
            }

            using var grayA = Cv2.ImRead(a.Path, ImreadModes.Grayscale);
            using var grayB = Cv2.ImRead(b.Path, ImreadModes.Grayscale);
            if (grayA.Empty() || grayB.Empty())
            {
                if (grayA.Empty())
                {
                    logAction?.Invoke($"无法读取图片灰度: {a.Path}");
                }

                if (grayB.Empty())
                {
                    logAction?.Invoke($"无法读取图片灰度: {b.Path}");
                }

                return false;
            }

            int width = Math.Max(1, Math.Min(DirectLowResCompareSize, Math.Min(grayA.Width, grayB.Width)));
            int height = Math.Max(1, Math.Min(DirectLowResCompareSize, Math.Min(grayA.Height, grayB.Height)));
            var compareSize = new Size(width, height);

            using var resizedA = new Mat();
            using var resizedB = new Mat();
            Cv2.Resize(grayA, resizedA, compareSize, 0, 0, InterpolationFlags.Area);
            Cv2.Resize(grayB, resizedB, compareSize, 0, 0, InterpolationFlags.Area);

            using var diff = new Mat();
            Cv2.Absdiff(resizedA, resizedB, diff);
            using var thresh = new Mat();
            Cv2.Threshold(diff, thresh, options.DiffPixelThreshold, 255, ThresholdTypes.Binary);
            double diffRatio = Cv2.CountNonZero(thresh) / (double)(compareSize.Width * compareSize.Height);

            return diffRatio <= DirectLowResDiffRatioThreshold;
        }

        private static bool TryTemplateMatchCrop(ImageItem a, ImageItem b, SameSourceGroupingOptions options, Action<string>? logAction, bool strict = false)
        {
            ImageItem larger = a.PixelCount >= b.PixelCount ? a : b;
            ImageItem smaller = a.PixelCount >= b.PixelCount ? b : a;

            double areaRatio = (double)smaller.PixelCount / Math.Max(1, larger.PixelCount);
            if (areaRatio >= options.CropAreaRatioThreshold)
            {
                return false;
            }

            if (smaller.Width < options.TemplateMinSize || smaller.Height < options.TemplateMinSize)
            {
                return false;
            }

            double threshold = strict
                ? Math.Max(options.TemplateMatchThreshold, TemplateStrictMatchThreshold)
                : options.TemplateMatchThreshold;
            double verifyDiffThreshold = strict
                ? TemplateStrictVerifyDiffRatioThreshold
                : TemplateVerifyDiffRatioThreshold;

            using var largeGray = Cv2.ImRead(larger.Path, ImreadModes.Grayscale);
            using var smallGray = Cv2.ImRead(smaller.Path, ImreadModes.Grayscale);
            if (largeGray.Empty() || smallGray.Empty())
            {
                if (largeGray.Empty())
                {
                    logAction?.Invoke($"无法读取图片灰度: {larger.Path}");
                }
                if (smallGray.Empty())
                {
                    logAction?.Invoke($"无法读取图片灰度: {smaller.Path}");
                }
                return false;
            }

            int maxDim = Math.Max(largeGray.Width, largeGray.Height);
            if (options.TemplateMaxSize > 0 && maxDim > options.TemplateMaxSize)
            {
                double scale = (double)options.TemplateMaxSize / maxDim;
                int largeWidth = Math.Max(1, (int)Math.Round(largeGray.Width * scale));
                int largeHeight = Math.Max(1, (int)Math.Round(largeGray.Height * scale));
                int smallWidth = Math.Max(1, (int)Math.Round(smallGray.Width * scale));
                int smallHeight = Math.Max(1, (int)Math.Round(smallGray.Height * scale));

                using var largeResized = new Mat();
                using var smallResized = new Mat();
                Cv2.Resize(largeGray, largeResized, new Size(largeWidth, largeHeight), 0, 0, InterpolationFlags.Area);
                Cv2.Resize(smallGray, smallResized, new Size(smallWidth, smallHeight), 0, 0, InterpolationFlags.Area);

                return TemplateMatch(largeResized, smallResized, threshold, options.DiffPixelThreshold, verifyDiffThreshold);
            }

            return TemplateMatch(largeGray, smallGray, threshold, options.DiffPixelThreshold, verifyDiffThreshold);
        }

        private static bool TemplateMatch(Mat largeGray, Mat smallGray, double threshold, int diffPixelThreshold, double verifyDiffThreshold)
        {
            if (smallGray.Width > largeGray.Width || smallGray.Height > largeGray.Height)
            {
                return false;
            }

            using var result = new Mat();
            Cv2.MatchTemplate(largeGray, smallGray, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);
            if (maxVal < threshold)
            {
                return false;
            }

            int maxX = largeGray.Width - smallGray.Width;
            int maxY = largeGray.Height - smallGray.Height;
            int x = Math.Clamp(maxLoc.X, 0, Math.Max(0, maxX));
            int y = Math.Clamp(maxLoc.Y, 0, Math.Max(0, maxY));

            using var matchedRegion = new Mat(largeGray, new Rect(x, y, smallGray.Width, smallGray.Height));
            using var diff = new Mat();
            Cv2.Absdiff(matchedRegion, smallGray, diff);
            using var thresh = new Mat();
            Cv2.Threshold(diff, thresh, diffPixelThreshold, 255, ThresholdTypes.Binary);

            double diffRatio = Cv2.CountNonZero(thresh) / (double)(smallGray.Width * smallGray.Height);
            return diffRatio <= verifyDiffThreshold;
        }

        private static void CopyGroupedFiles(IReadOnlyList<ImageItem> items, DisjointSet unionFind,
            string outputPath, SameSourceGroupingOptions options, ORB orb, OrbFeatureCache featureCache,
            IProgress<double>? progress, Action<string>? logAction, CancellationToken token)
        {
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < items.Count; i++)
            {
                int root = unionFind.Find(i);
                if (!groups.TryGetValue(root, out var list))
                {
                    list = new List<int>();
                    groups[root] = list;
                }

                list.Add(i);
            }

            var groupList = groups.Values.OrderByDescending(g => g.Count).ToList();
            int totalGroups = groupList.Count;
            int processedImages = 0;
            int totalImages = items.Count;
            int groupIndex = 1;
            var relationOutputDirs = BuildRelationOutputDirectories(outputPath);

            for (int g = 0; g < groupList.Count; g++)
            {
                token.ThrowIfCancellationRequested();

                var group = groupList[g];
                int rootIndex = SelectRootIndex(group, items);
                var rootItem = items[rootIndex];

                string groupId = BuildGroupId(rootItem);
                groupIndex = FindNextAvailableGroupIndex(relationOutputDirs.Root, groupIndex);
                string baseName = $"原图{groupIndex}";

                logAction?.Invoke($"[{g + 1}/{totalGroups}] 组 {baseName} (hash {groupId})，数量 {group.Count}");

                string? mergedDanbooruTags = options.SyncDanbooruTagsToRoot
                    ? BuildMergedDanbooruTags(group, items, logAction)
                    : null;

                var rootFeatures = featureCache.GetOrCreate(rootIndex, () =>
                    ComputeOrbFeaturesFromFile(rootItem, orb, options, logAction));

                Mat? rootGrayScaled = null;
                if (options.EditedDiffRatioThreshold > 0 && rootFeatures != null && !rootFeatures.Descriptors.Empty())
                {
                    using var rootGray = Cv2.ImRead(rootItem.Path, ImreadModes.Grayscale);
                    if (!rootGray.Empty())
                    {
                        rootGrayScaled = ResizeToSize(rootGray, rootFeatures.ScaledSize);
                    }
                    else
                    {
                        logAction?.Invoke($"无法读取原图灰度: {rootItem.Path}");
                    }
                }

                var relationCounts = new Dictionary<RelationType, int>
                {
                    [RelationType.Same] = 0,
                    [RelationType.Crop] = 0,
                    [RelationType.LowRes] = 0,
                    [RelationType.Edited] = 0,
                    [RelationType.Unknown] = 0
                };

                string rootTargetPath = Path.Combine(relationOutputDirs.Root, $"{baseName}{Path.GetExtension(rootItem.Path)}");
                string? actualRootPath = CopyWithCompanion(rootItem.Path, rootTargetPath, logAction);
                processedImages++;

                if (!string.IsNullOrWhiteSpace(actualRootPath)
                    && options.SyncDanbooruTagsToRoot
                    && !string.IsNullOrWhiteSpace(mergedDanbooruTags))
                {
                    WriteCompanionTags(actualRootPath, mergedDanbooruTags, logAction);
                }

                int modifyIndex = 1;
                foreach (var idx in group)
                {
                    if (idx == rootIndex)
                    {
                        continue;
                    }

                    token.ThrowIfCancellationRequested();

                    var item = items[idx];
                    RelationType relation = RelationType.Unknown;

                    var otherFeatures = featureCache.GetOrCreate(idx, () =>
                        ComputeOrbFeaturesFromFile(item, orb, options, logAction));

                    Mat? otherGrayScaled = null;
                    if (options.EditedDiffRatioThreshold > 0 && rootGrayScaled != null && otherFeatures != null && !otherFeatures.Descriptors.Empty())
                    {
                        using var otherGray = Cv2.ImRead(item.Path, ImreadModes.Grayscale);
                        if (!otherGray.Empty())
                        {
                            otherGrayScaled = ResizeToSize(otherGray, otherFeatures.ScaledSize);
                        }
                        else
                        {
                            logAction?.Invoke($"无法读取图片灰度: {item.Path}");
                        }
                    }

                    if (rootFeatures != null && otherFeatures != null
                        && !rootFeatures.Descriptors.Empty() && !otherFeatures.Descriptors.Empty())
                    {
                        relation = ClassifyRelation(rootItem, rootFeatures, item, otherFeatures, options, rootGrayScaled, otherGrayScaled);
                    }

                    otherGrayScaled?.Dispose();
                    relationCounts[relation] += 1;

                    string targetDir = ResolveRelationOutputDirectory(relationOutputDirs, relation);
                    string relationSuffix = relation switch
                    {
                        RelationType.Crop => "被包含",
                        RelationType.LowRes => "缩略图",
                        RelationType.Edited => "差分",
                        RelationType.Same => "未知",
                        RelationType.Unknown => "未知",
                        _ => "未知"
                    };
                    string modifySuffix = modifyIndex == 1 ? $"{baseName}{relationSuffix}" : $"{baseName}{relationSuffix}{modifyIndex}";
                    string targetPath = Path.Combine(targetDir, $"{modifySuffix}{Path.GetExtension(item.Path)}");
                    _ = CopyWithCompanion(item.Path, targetPath, logAction);

                    modifyIndex++;
                    processedImages++;
                }

                rootGrayScaled?.Dispose();

                logAction?.Invoke($"  关系统计: 同图 {relationCounts[RelationType.Same]}, 裁剪 {relationCounts[RelationType.Crop]}, 低清 {relationCounts[RelationType.LowRes]}, 差分 {relationCounts[RelationType.Edited]}, 未知 {relationCounts[RelationType.Unknown]}");

                progress?.Report(80 + 20.0 * processedImages / totalImages);
                groupIndex++;
            }
        }

        private static int SelectRootIndex(List<int> group, IReadOnlyList<ImageItem> items)
        {
            return group
                .OrderByDescending(i => items[i].PixelCount)
                .ThenByDescending(i => items[i].Sharpness)
                .ThenByDescending(i => items[i].FileSize)
                .First();
        }

        private static string BuildGroupId(ImageItem item)
        {
            string hashPart = $"{(uint)(item.FullHash >> 32):X8}";
            return $"{hashPart}_{item.Width}x{item.Height}";
        }

        private static int FindNextAvailableGroupIndex(string rootOutputPath, int startIndex)
        {
            int index = Math.Max(1, startIndex);
            while (true)
            {
                string baseName = $"原图{index}";
                bool hasImage = SupportedExtensions.Any(ext =>
                    File.Exists(Path.Combine(rootOutputPath, $"{baseName}{ext}")));
                bool hasTxt = File.Exists(Path.Combine(rootOutputPath, $"{baseName}.txt"));

                if (!hasImage && !hasTxt)
                {
                    return index;
                }

                index++;
            }
        }

        private static RelationOutputDirectories BuildRelationOutputDirectories(string outputPath)
        {
            string rootDir = Path.Combine(outputPath, RootFolderName);
            string cropDir = Path.Combine(outputPath, CropFolderName);
            string lowResDir = Path.Combine(outputPath, LowResFolderName);
            string editedDir = Path.Combine(outputPath, EditedFolderName);
            string unknownDir = Path.Combine(outputPath, UnknownFolderName);

            Directory.CreateDirectory(rootDir);
            Directory.CreateDirectory(cropDir);
            Directory.CreateDirectory(lowResDir);
            Directory.CreateDirectory(editedDir);
            Directory.CreateDirectory(unknownDir);

            return new RelationOutputDirectories(rootDir, cropDir, lowResDir, editedDir, unknownDir);
        }

        private static string ResolveRelationOutputDirectory(RelationOutputDirectories directories, RelationType relation)
        {
            return relation switch
            {
                RelationType.Crop => directories.Crop,
                RelationType.LowRes => directories.LowRes,
                RelationType.Edited => directories.Edited,
                RelationType.Same => directories.Unknown,
                RelationType.Unknown => directories.Unknown,
                _ => directories.Unknown
            };
        }

        private static string? CopyWithCompanion(string sourceImagePath, string targetImagePath, Action<string>? logAction)
        {
            string finalImagePath;
            try
            {
                finalImagePath = EnsureUniqueFilePath(targetImagePath);
                string? targetDir = Path.GetDirectoryName(finalImagePath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(sourceImagePath, finalImagePath, overwrite: false);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"复制失败: {sourceImagePath} ({ex.Message})");
                return null;
            }

            string companion = Path.ChangeExtension(sourceImagePath, ".txt");
            if (File.Exists(companion))
            {
                string targetTxt = EnsureUniqueFilePath(Path.ChangeExtension(finalImagePath, ".txt"));
                try
                {
                    File.Copy(companion, targetTxt, overwrite: false);
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"复制TXT失败: {companion} ({ex.Message})");
                }
            }

            return finalImagePath;
        }

        private static string EnsureUniqueFilePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            int index = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(directory, $"{name}_{index}{extension}");
                index++;
            } while (File.Exists(candidate));

            return candidate;
        }

        private static string? BuildMergedDanbooruTags(IEnumerable<int> group, IReadOnlyList<ImageItem> items, Action<string>? logAction)
        {
            var merged = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var idx in group)
            {
                string txtPath = Path.ChangeExtension(items[idx].Path, ".txt");
                if (!File.Exists(txtPath))
                {
                    continue;
                }

                try
                {
                    string content = File.ReadAllText(txtPath);
                    var tags = ParseDanbooruTags(content);
                    foreach (var tag in tags)
                    {
                        if (seen.Add(tag))
                        {
                            merged.Add(tag);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"读取TXT失败: {txtPath} ({ex.Message})");
                }
            }

            return merged.Count == 0 ? null : string.Join(",", merged);
        }

        private static List<string> ParseDanbooruTags(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new List<string>();
            }

            var rawTokens = content
                .Split(new[] { ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            if (rawTokens.Count < 3)
            {
                return new List<string>();
            }

            int validCount = rawTokens.Count(IsLikelyDanbooruTagToken);
            double validRatio = (double)validCount / rawTokens.Count;
            if (validCount < 3 || validRatio < 0.85)
            {
                return new List<string>();
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in rawTokens)
            {
                if (!IsLikelyDanbooruTagToken(token))
                {
                    continue;
                }

                if (seen.Add(token))
                {
                    result.Add(token);
                }
            }

            return result;
        }

        private static bool IsLikelyDanbooruTagToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 80)
            {
                return false;
            }

            bool hasLetterOrDigit = false;
            foreach (char c in token)
            {
                if (char.IsWhiteSpace(c))
                {
                    return false;
                }

                if (char.IsLetterOrDigit(c))
                {
                    hasLetterOrDigit = true;
                    continue;
                }

                if (c is '_' or '-' or ':' or '(' or ')' or '.' or '\'' or '/')
                {
                    continue;
                }

                return false;
            }

            return hasLetterOrDigit;
        }

        private static void WriteCompanionTags(string imagePath, string tags, Action<string>? logAction)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(tags))
            {
                return;
            }

            try
            {
                string txtPath = Path.ChangeExtension(imagePath, ".txt");
                File.WriteAllText(txtPath, tags);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"写入标签TXT失败: {imagePath} ({ex.Message})");
            }
        }

        private sealed record RelationOutputDirectories(string Root, string Crop, string LowRes, string Edited, string Unknown);

        private static RelationType ClassifyRelation(ImageItem rootItem, OrbFeatureData rootFeatures, ImageItem otherItem,
            OrbFeatureData otherFeatures, SameSourceGroupingOptions options, Mat? rootGrayScaled, Mat? otherGrayScaled)
        {
            if (!TryEstimateHomography(rootFeatures, otherFeatures, options, out var homography, out _, out _, out _, out _))
            {
                if (IsLowResMatchByHash(rootItem, otherItem, options)
                    || IsLowResMatchByResize(rootItem, otherItem, options, null))
                {
                    return RelationType.LowRes;
                }

                if (options.EnableTemplateMatch && TryTemplateMatchCrop(rootItem, otherItem, options, null, strict: true))
                {
                    return RelationType.Crop;
                }

                return RelationType.Unknown;
            }

            using (homography)
            {
                var (isCrop, _) = IsCrop(rootFeatures.ScaledSize, otherFeatures.ScaledSize, homography, options.CropAreaRatioThreshold);
                if (isCrop)
                {
                    if (options.EnableTemplateMatch && TryTemplateMatchCrop(rootItem, otherItem, options, null, strict: true))
                    {
                        return RelationType.Crop;
                    }

                    bool isLowResWhenCropFailed = otherItem.PixelCount < rootItem.PixelCount * options.LowResRatioThreshold
                        && (IsLowResMatchByHash(rootItem, otherItem, options)
                            || IsLowResMatchByResize(rootItem, otherItem, options, null));
                    return isLowResWhenCropFailed ? RelationType.LowRes : RelationType.Unknown;
                }

                bool isLowRes = otherItem.PixelCount < rootItem.PixelCount * options.LowResRatioThreshold
                    && (IsLowResMatchByHash(rootItem, otherItem, options)
                        || IsLowResMatchByResize(rootItem, otherItem, options, null));
                if (isLowRes)
                {
                    return RelationType.LowRes;
                }

                if (options.EditedDiffRatioThreshold > 0 && rootGrayScaled != null && otherGrayScaled != null)
                {
                    double diffRatio = ComputeDiffRatio(rootGrayScaled, otherGrayScaled, homography, options);
                    if (diffRatio >= options.EditedDiffRatioThreshold)
                    {
                        return RelationType.Edited;
                    }
                }
            }

            return RelationType.Same;
        }

        private static (bool isCrop, Rect mappedBox) IsCrop(Size sizeA, Size sizeB, Mat homographyBToA, double areaRatioThreshold)
        {
            var cornersB = new[]
            {
                new Point2f(0, 0),
                new Point2f(sizeB.Width - 1, 0),
                new Point2f(sizeB.Width - 1, sizeB.Height - 1),
                new Point2f(0, sizeB.Height - 1)
            };

            Point2f[] projected = Cv2.PerspectiveTransform(cornersB, homographyBToA);

            float minX = projected.Min(p => p.X);
            float minY = projected.Min(p => p.Y);
            float maxX = projected.Max(p => p.X);
            float maxY = projected.Max(p => p.Y);

            var box = new Rect(
                (int)Math.Floor(minX),
                (int)Math.Floor(minY),
                (int)Math.Ceiling(maxX - minX),
                (int)Math.Ceiling(maxY - minY)
            );

            double areaBox = Math.Max(0, box.Width) * Math.Max(0, box.Height);
            double areaA = Math.Max(1, sizeA.Width * sizeA.Height);

            bool isCrop = areaBox / areaA < areaRatioThreshold;
            return (isCrop, box);
        }

        private static double ComputeDiffRatio(Mat grayA, Mat grayB, Mat homographyBToA, SameSourceGroupingOptions options)
        {
            using var warped = new Mat();
            Cv2.WarpPerspective(grayB, warped, homographyBToA, new Size(grayA.Width, grayA.Height), InterpolationFlags.Linear, BorderTypes.Constant);

            var targetSize = GetDiffSize(grayA, options.DiffCheckSize);
            using var resizedA = new Mat();
            using var resizedB = new Mat();
            Cv2.Resize(grayA, resizedA, targetSize, 0, 0, InterpolationFlags.Area);
            Cv2.Resize(warped, resizedB, targetSize, 0, 0, InterpolationFlags.Area);

            using var diff = new Mat();
            Cv2.Absdiff(resizedA, resizedB, diff);
            using var thresh = new Mat();
            Cv2.Threshold(diff, thresh, options.DiffPixelThreshold, 255, ThresholdTypes.Binary);

            double nonZero = Cv2.CountNonZero(thresh);
            return nonZero / (targetSize.Width * targetSize.Height);
        }

        private static Mat ResizeToSize(Mat src, Size targetSize)
        {
            var dst = new Mat();
            Cv2.Resize(src, dst, targetSize, 0, 0, InterpolationFlags.Area);
            return dst;
        }

        private static Size GetDiffSize(Mat img, int maxSize)
        {
            if (maxSize <= 0)
            {
                return new Size(img.Width, img.Height);
            }

            int maxDim = Math.Max(img.Width, img.Height);
            if (maxDim <= maxSize)
            {
                return new Size(img.Width, img.Height);
            }

            double scale = (double)maxSize / maxDim;
            int width = Math.Max(1, (int)Math.Round(img.Width * scale));
            int height = Math.Max(1, (int)Math.Round(img.Height * scale));
            return new Size(width, height);
        }

        private static OrbFeatureData ComputeOrbFeatures(Mat bgr, ORB orb, SameSourceGroupingOptions options)
        {
            using var gray = new Mat();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

            double scale = 1.0;
            Size targetSize = gray.Size();
            Mat? resized = null;

            if (options.OrbMaxSize > 0)
            {
                int maxDim = Math.Max(gray.Width, gray.Height);
                if (maxDim > options.OrbMaxSize)
                {
                    scale = (double)options.OrbMaxSize / maxDim;
                    int width = Math.Max(1, (int)Math.Round(gray.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(gray.Height * scale));
                    targetSize = new Size(width, height);
                    resized = new Mat();
                    Cv2.Resize(gray, resized, targetSize, 0, 0, InterpolationFlags.Area);
                }
            }

            var descriptors = new Mat();
            var input = resized ?? gray;
            orb.DetectAndCompute(input, null, out KeyPoint[] keyPoints, descriptors);
            resized?.Dispose();

            return new OrbFeatureData(keyPoints, descriptors, scale, targetSize);
        }

        private static OrbFeatureData? ComputeOrbFeaturesFromFile(ImageItem item, ORB orb, SameSourceGroupingOptions options, Action<string>? logAction)
        {
            try
            {
                using var img = Cv2.ImRead(item.Path, ImreadModes.Color);
                if (img.Empty())
                {
                    logAction?.Invoke($"无法读取图片: {item.Path}");
                    return null;
                }

                return ComputeOrbFeatures(img, orb, options);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"读取失败: {item.Path} ({ex.Message})");
                return null;
            }
        }

        private static bool TryEstimateHomography(OrbFeatureData featuresA, OrbFeatureData featuresB, SameSourceGroupingOptions options,
            out Mat? homography, out int inliers, out int matchCount, out double inlierCoverageA, out double inlierCoverageB)
        {
            homography = null;
            inliers = 0;
            matchCount = 0;
            inlierCoverageA = 0;
            inlierCoverageB = 0;

            if (featuresA.Descriptors.Empty() || featuresB.Descriptors.Empty())
            {
                return false;
            }

            using var matcher = new BFMatcher(NormTypes.Hamming, crossCheck: false);
            var knn = matcher.KnnMatch(featuresA.Descriptors, featuresB.Descriptors, k: 2);

            var good = new List<DMatch>();
            foreach (var pair in knn)
            {
                if (pair.Length < 2)
                {
                    continue;
                }

                if (pair[0].Distance < options.LoweRatio * pair[1].Distance)
                {
                    good.Add(pair[0]);
                }
            }

            matchCount = good.Count;
            if (good.Count < options.MinGoodMatches)
            {
                return false;
            }

            var ptsA = good.Select(m => featuresA.KeyPoints[m.QueryIdx].Pt).Select(p => new Point2f(p.X, p.Y)).ToArray();
            var ptsB = good.Select(m => featuresB.KeyPoints[m.TrainIdx].Pt).Select(p => new Point2f(p.X, p.Y)).ToArray();

            using var mask = new Mat();
            homography = Cv2.FindHomography(InputArray.Create(ptsB), InputArray.Create(ptsA), HomographyMethods.Ransac,
                options.RansacReprojThreshold, mask);

            if (homography.Empty())
            {
                homography.Dispose();
                homography = null;
                return false;
            }

            var inlierPointsA = new List<Point2f>();
            var inlierPointsB = new List<Point2f>();
            for (int i = 0; i < mask.Rows; i++)
            {
                if (mask.At<byte>(i, 0) != 0)
                {
                    inliers++;
                    var m = good[i];
                    var pointA = featuresA.KeyPoints[m.QueryIdx].Pt;
                    var pointB = featuresB.KeyPoints[m.TrainIdx].Pt;
                    inlierPointsA.Add(new Point2f(pointA.X, pointA.Y));
                    inlierPointsB.Add(new Point2f(pointB.X, pointB.Y));
                }
            }

            inlierCoverageA = ComputePointCoverage(inlierPointsA, featuresA.ScaledSize);
            inlierCoverageB = ComputePointCoverage(inlierPointsB, featuresB.ScaledSize);

            double inlierRatio = (double)inliers / good.Count;
            if (inliers < options.MinInliers || inlierRatio < options.MinInlierRatio)
            {
                homography.Dispose();
                homography = null;
                return false;
            }

            return true;
        }

        private static double ComputePointCoverage(IReadOnlyList<Point2f> points, Size size)
        {
            if (points == null || points.Count < 4)
            {
                return 0;
            }

            float minX = points.Min(p => p.X);
            float minY = points.Min(p => p.Y);
            float maxX = points.Max(p => p.X);
            float maxY = points.Max(p => p.Y);

            double width = Math.Max(0, maxX - minX);
            double height = Math.Max(0, maxY - minY);
            double areaBox = width * height;
            double areaAll = Math.Max(1, size.Width * size.Height);
            return areaBox / areaAll;
        }

        private static double ComputeMappedAreaRatio(Size sizeA, Size sizeB, Mat homographyBToA)
        {
            var (_, mappedBox) = IsCrop(sizeA, sizeB, homographyBToA, 0.0);
            double areaBox = Math.Max(0, mappedBox.Width) * Math.Max(0, mappedBox.Height);
            double areaA = Math.Max(1, sizeA.Width * sizeA.Height);
            return areaBox / areaA;
        }

        private static bool PassOrbAlignmentSanityCheck(ImageItem a, OrbFeatureData featuresA, ImageItem b, OrbFeatureData featuresB,
            Mat homographyBToA, SameSourceGroupingOptions options, Action<string>? logAction)
        {
            if (GetAspectRatioDiff(a, b) > OrbSanityAspectRatioTolerance)
            {
                return true;
            }

            int smaller = Math.Min(a.PixelCount, b.PixelCount);
            int larger = Math.Max(a.PixelCount, b.PixelCount);
            if (larger <= 0)
            {
                return false;
            }

            double areaRatio = (double)smaller / larger;
            if (areaRatio < OrbSanityAreaRatioThreshold)
            {
                return true;
            }

            using var grayA = Cv2.ImRead(a.Path, ImreadModes.Grayscale);
            using var grayB = Cv2.ImRead(b.Path, ImreadModes.Grayscale);
            if (grayA.Empty() || grayB.Empty())
            {
                if (grayA.Empty())
                {
                    logAction?.Invoke($"无法读取图片灰度: {a.Path}");
                }

                if (grayB.Empty())
                {
                    logAction?.Invoke($"无法读取图片灰度: {b.Path}");
                }

                return true;
            }

            using var scaledA = ResizeToSize(grayA, featuresA.ScaledSize);
            using var scaledB = ResizeToSize(grayB, featuresB.ScaledSize);

            double diffRatio = ComputeDiffRatio(scaledA, scaledB, homographyBToA, options);
            double maxAllowedDiff = Math.Clamp(options.EditedDiffRatioThreshold + OrbSanityDiffMargin,
                OrbSanityMinDiffThreshold, OrbSanityMaxDiffThreshold);

            return diffRatio <= maxAllowedDiff;
        }

        private static ulong ComputePHash64(Mat bgr)
        {
            using var gray = new Mat();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

            using var resized = new Mat();
            Cv2.Resize(gray, resized, new Size(32, 32), 0, 0, InterpolationFlags.Area);

            using var f = new Mat();
            resized.ConvertTo(f, MatType.CV_32F);

            using var dct = new Mat();
            Cv2.Dct(f, dct);

            float[] vals = new float[64];
            int idx = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    vals[idx++] = dct.At<float>(y, x);
                }
            }

            float[] ac = new float[63];
            Array.Copy(vals, 1, ac, 0, 63);
            Array.Sort(ac);
            float median = ac[31];

            ulong hash = 0;
            idx = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    float v = vals[idx++];
                    hash = (hash << 1) | (v > median ? 1UL : 0UL);
                }
            }

            return hash;
        }

        private static ulong[] ComputePatchHashes(Mat bgr, int grid)
        {
            if (grid < 2)
            {
                return Array.Empty<ulong>();
            }

            int maxGrid = Math.Min(bgr.Width, bgr.Height);
            if (grid > maxGrid)
            {
                grid = Math.Max(2, maxGrid);
            }

            int cellWidth = Math.Max(1, bgr.Width / grid);
            int cellHeight = Math.Max(1, bgr.Height / grid);

            var hashes = new List<ulong>();
            AppendPatchHashes(bgr, 0, 0, cellWidth, cellHeight, hashes);

            int offsetX = cellWidth / 2;
            int offsetY = cellHeight / 2;
            if (offsetX > 0 && offsetY > 0)
            {
                AppendPatchHashes(bgr, offsetX, offsetY, cellWidth, cellHeight, hashes);
            }

            return hashes.ToArray();
        }

        private static void AppendPatchHashes(Mat bgr, int startX, int startY, int cellWidth, int cellHeight, List<ulong> hashes)
        {
            for (int y = startY; y + cellHeight <= bgr.Height; y += cellHeight)
            {
                for (int x = startX; x + cellWidth <= bgr.Width; x += cellWidth)
                {
                    using var roi = new Mat(bgr, new Rect(x, y, cellWidth, cellHeight));
                    hashes.Add(ComputePHash64(roi));
                }
            }
        }

        private static double ComputeSharpness(Mat bgr)
        {
            using var gray = new Mat();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

            using var lap = new Mat();
            Cv2.Laplacian(gray, lap, MatType.CV_64F);

            Cv2.MeanStdDev(lap, out _, out var stddev);
            double sigma = stddev.Val0;
            return sigma * sigma;
        }

        private static ushort[] GetHashParts(ulong hash)
        {
            return new[]
            {
                (ushort)(hash >> 48),
                (ushort)(hash >> 32),
                (ushort)(hash >> 16),
                (ushort)hash
            };
        }

        private static int HammingDistance(ulong a, ulong b)
        {
            return BitOperations.PopCount(a ^ b);
        }

        private sealed class OrbFeatureCache : IDisposable
        {
            private readonly Dictionary<int, OrbFeatureData> _cache = new Dictionary<int, OrbFeatureData>();

            public OrbFeatureData? GetOrCreate(int index, Func<OrbFeatureData?> factory)
            {
                if (_cache.TryGetValue(index, out var existing))
                {
                    return existing;
                }

                var created = factory();
                if (created != null)
                {
                    _cache[index] = created;
                }

                return created;
            }

            public void Dispose()
            {
                foreach (var entry in _cache.Values)
                {
                    entry.Dispose();
                }

                _cache.Clear();
            }
        }

        private sealed class OrbFeatureData : IDisposable
        {
            public OrbFeatureData(KeyPoint[] keyPoints, Mat descriptors, double scale, Size scaledSize)
            {
                KeyPoints = keyPoints;
                Descriptors = descriptors;
                Scale = scale;
                ScaledSize = scaledSize;
            }

            public KeyPoint[] KeyPoints { get; }
            public Mat Descriptors { get; }
            public double Scale { get; }
            public Size ScaledSize { get; }

            public void Dispose()
            {
                Descriptors.Dispose();
            }
        }

        private sealed class ImageItem
        {
            public ImageItem(string path, int width, int height, long fileSize, double sharpness, ulong fullHash, ulong[] patchHashes)
            {
                Path = path;
                Width = width;
                Height = height;
                FileSize = fileSize;
                Sharpness = sharpness;
                FullHash = fullHash;
                PatchHashes = patchHashes;

                AllHashes = patchHashes.Length == 0
                    ? new[] { fullHash }
                    : new[] { fullHash }.Concat(patchHashes).ToArray();
            }

            public string Path { get; }
            public int Width { get; }
            public int Height { get; }
            public long FileSize { get; }
            public double Sharpness { get; }
            public ulong FullHash { get; }
            public ulong[] PatchHashes { get; }
            public ulong[] AllHashes { get; }

            public int PixelCount => Width * Height;
        }
    }
}
