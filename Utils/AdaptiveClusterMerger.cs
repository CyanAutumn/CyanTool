using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public static class AdaptiveClusterMerger
    {
        public sealed class Result
        {
            public int[] Labels { get; init; } = Array.Empty<int>();
            public int OriginalClusterCount { get; init; }
            public int FinalClusterCount { get; init; }
            public double CutDistance { get; init; }
            public IReadOnlyList<MstEdge> CutEdges { get; init; } = Array.Empty<MstEdge>();
            public bool Changed => FinalClusterCount > 0 && FinalClusterCount < OriginalClusterCount;
        }

        public readonly record struct MstEdge(int A, int B, double Distance);

        public static Result MergeNearbyClusters(
            IReadOnlyList<float[]> points,
            IReadOnlyList<int> labels,
            bool useCosineDistance = false,
            int minClustersToMerge = 4,
            double minJumpRatio = 1.35d,
            double nearestNeighborCapPercentile = 0.50d,
            double nearestNeighborCapMultiplier = 1.10d)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (labels == null)
            {
                throw new ArgumentNullException(nameof(labels));
            }

            if (points.Count != labels.Count)
            {
                throw new ArgumentException("Point and label counts must match.", nameof(labels));
            }

            var clusterIds = labels
                .Where(label => label >= 0)
                .Distinct()
                .OrderBy(label => label)
                .ToArray();

            if (clusterIds.Length < minClustersToMerge)
            {
                return new Result
                {
                    Labels = RelabelSequentially(labels),
                    OriginalClusterCount = clusterIds.Length,
                    FinalClusterCount = clusterIds.Length,
                    CutDistance = double.PositiveInfinity
                };
            }

            var idToIndex = clusterIds
                .Select((id, index) => new { id, index })
                .ToDictionary(item => item.id, item => item.index);

            var groupedIndices = clusterIds
                .Select(id => Enumerable.Range(0, labels.Count).Where(index => labels[index] == id).ToArray())
                .ToArray();

            var centroids = groupedIndices.Select(indices => ComputeCentroid(points, indices, useCosineDistance)).ToArray();
            var edges = BuildMinimumSpanningTree(centroids, useCosineDistance)
                .OrderBy(edge => edge.Distance)
                .ToArray();

            if (edges.Length == 0)
            {
                return new Result
                {
                    Labels = RelabelSequentially(labels),
                    OriginalClusterCount = clusterIds.Length,
                    FinalClusterCount = clusterIds.Length,
                    CutDistance = double.PositiveInfinity
                };
            }

            int cutStart = FindCutStart(edges.Select(edge => edge.Distance).ToArray(), minJumpRatio);
            if (cutStart < 0)
            {
                return new Result
                {
                    Labels = RelabelSequentially(labels),
                    OriginalClusterCount = clusterIds.Length,
                    FinalClusterCount = clusterIds.Length,
                    CutDistance = double.PositiveInfinity
                };
            }

            var distanceMatrix = BuildDistanceMatrix(centroids, useCosineDistance);
            double rawCutDistance = (edges[cutStart - 1].Distance + edges[cutStart].Distance) / 2d;
            double nearestNeighborCap = ComputeNearestNeighborCap(
                distanceMatrix,
                nearestNeighborCapPercentile,
                nearestNeighborCapMultiplier);
            double cutDistance = Math.Min(rawCutDistance, nearestNeighborCap);
            var mergedGroups = MergeByCompleteLink(distanceMatrix, cutDistance)
                .OrderBy(group => group.Min())
                .ToArray();
            var clusterIndexToMergedLabel = new Dictionary<int, int>();
            for (int groupIndex = 0; groupIndex < mergedGroups.Length; groupIndex++)
            {
                foreach (int clusterIndex in mergedGroups[groupIndex])
                {
                    clusterIndexToMergedLabel[clusterIndex] = groupIndex;
                }
            }

            var mergedLabels = new int[labels.Count];
            for (int i = 0; i < labels.Count; i++)
            {
                int label = labels[i];
                if (label < 0)
                {
                    mergedLabels[i] = label;
                    continue;
                }

                mergedLabels[i] = clusterIndexToMergedLabel[idToIndex[label]];
            }

            return new Result
            {
                Labels = mergedLabels,
                OriginalClusterCount = clusterIds.Length,
                FinalClusterCount = mergedGroups.Length,
                CutDistance = cutDistance,
                CutEdges = edges.Skip(cutStart).ToArray()
            };
        }

        public static Result MergeToPreferredClusterCount(
            IReadOnlyList<float[]> points,
            IReadOnlyList<int> labels,
            int preferredMaxClusters,
            bool useCosineDistance = false,
            double maxCompleteLinkDistance = double.PositiveInfinity)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (labels == null)
            {
                throw new ArgumentNullException(nameof(labels));
            }

            if (points.Count != labels.Count)
            {
                throw new ArgumentException("Point and label counts must match.", nameof(labels));
            }

            var clusterIds = labels
                .Where(label => label >= 0)
                .Distinct()
                .OrderBy(label => label)
                .ToArray();

            if (clusterIds.Length == 0)
            {
                return new Result
                {
                    Labels = labels.ToArray(),
                    OriginalClusterCount = 0,
                    FinalClusterCount = 0,
                    CutDistance = double.PositiveInfinity
                };
            }

            int targetCount = Math.Clamp(preferredMaxClusters, 1, clusterIds.Length);
            if (clusterIds.Length <= targetCount)
            {
                return new Result
                {
                    Labels = RelabelSequentially(labels),
                    OriginalClusterCount = clusterIds.Length,
                    FinalClusterCount = clusterIds.Length,
                    CutDistance = double.PositiveInfinity
                };
            }

            var idToIndex = clusterIds
                .Select((id, index) => new { id, index })
                .ToDictionary(item => item.id, item => item.index);

            var groupedIndices = clusterIds
                .Select(id => Enumerable.Range(0, labels.Count).Where(index => labels[index] == id).ToArray())
                .ToArray();

            var centroids = groupedIndices.Select(indices => ComputeCentroid(points, indices, useCosineDistance)).ToArray();
            var distanceMatrix = BuildDistanceMatrix(centroids, useCosineDistance);
            var mergedGroups = MergeByCompleteLinkToCount(
                    distanceMatrix,
                    targetCount,
                    maxCompleteLinkDistance,
                    out double lastMergeDistance)
                .OrderBy(group => group.Min())
                .ToArray();
            var clusterIndexToMergedLabel = new Dictionary<int, int>();
            for (int groupIndex = 0; groupIndex < mergedGroups.Length; groupIndex++)
            {
                foreach (int clusterIndex in mergedGroups[groupIndex])
                {
                    clusterIndexToMergedLabel[clusterIndex] = groupIndex;
                }
            }

            var mergedLabels = new int[labels.Count];
            for (int i = 0; i < labels.Count; i++)
            {
                int label = labels[i];
                if (label < 0)
                {
                    mergedLabels[i] = label;
                    continue;
                }

                mergedLabels[i] = clusterIndexToMergedLabel[idToIndex[label]];
            }

            return new Result
            {
                Labels = mergedLabels,
                OriginalClusterCount = clusterIds.Length,
                FinalClusterCount = mergedGroups.Length,
                CutDistance = lastMergeDistance
            };
        }

        private static int FindCutStart(IReadOnlyList<double> sortedDistances, double minJumpRatio)
        {
            if (sortedDistances.Count < 3)
            {
                return -1;
            }

            double bestScore = 0d;
            int bestIndex = -1;
            for (int i = 1; i < sortedDistances.Count; i++)
            {
                double previous = sortedDistances[i - 1];
                double current = sortedDistances[i];
                double gap = current - previous;
                if (gap <= 1e-12d)
                {
                    continue;
                }

                double ratio = (current + 1e-9d) / (previous + 1e-9d);
                double absoluteFloor = Math.Max(0.02d, previous * 0.20d);
                if (ratio < minJumpRatio || gap < absoluteFloor)
                {
                    continue;
                }

                double score = gap * Math.Log(ratio);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static double ComputeNearestNeighborCap(
            double[,] distances,
            double percentile,
            double multiplier)
        {
            int count = distances.GetLength(0);
            if (count <= 1)
            {
                return double.PositiveInfinity;
            }

            var nearestDistances = new List<double>(count);
            for (int i = 0; i < count; i++)
            {
                double nearest = double.PositiveInfinity;
                for (int j = 0; j < count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    nearest = Math.Min(nearest, distances[i, j]);
                }

                if (!double.IsInfinity(nearest))
                {
                    nearestDistances.Add(nearest);
                }
            }

            nearestDistances.Sort();
            if (nearestDistances.Count == 0)
            {
                return double.PositiveInfinity;
            }

            double baseDistance = ComputePercentile(nearestDistances, percentile);
            return baseDistance * Math.Max(0.1d, multiplier);
        }

        private static List<int>[] MergeByCompleteLink(double[,] distances, double threshold)
        {
            int count = distances.GetLength(0);
            var groups = Enumerable.Range(0, count)
                .Select(index => new List<int> { index })
                .ToList();

            while (groups.Count > 1)
            {
                int bestA = -1;
                int bestB = -1;
                double bestDistance = double.PositiveInfinity;

                for (int a = 0; a < groups.Count; a++)
                {
                    for (int b = a + 1; b < groups.Count; b++)
                    {
                        double distance = CompleteLinkDistance(groups[a], groups[b], distances);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestA = a;
                            bestB = b;
                        }
                    }
                }

                if (bestA < 0 || bestB < 0 || bestDistance > threshold)
                {
                    break;
                }

                groups[bestA].AddRange(groups[bestB]);
                groups[bestA].Sort();
                groups.RemoveAt(bestB);
            }

            return groups.ToArray();
        }

        private static List<int>[] MergeByCompleteLinkToCount(
            double[,] distances,
            int targetCount,
            double maxMergeDistance,
            out double lastMergeDistance)
        {
            int count = distances.GetLength(0);
            var groups = Enumerable.Range(0, count)
                .Select(index => new List<int> { index })
                .ToList();
            lastMergeDistance = double.PositiveInfinity;

            while (groups.Count > Math.Max(1, targetCount))
            {
                int bestA = -1;
                int bestB = -1;
                double bestDistance = double.PositiveInfinity;

                for (int a = 0; a < groups.Count; a++)
                {
                    for (int b = a + 1; b < groups.Count; b++)
                    {
                        double distance = CompleteLinkDistance(groups[a], groups[b], distances);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestA = a;
                            bestB = b;
                        }
                    }
                }

                if (bestA < 0 || bestB < 0)
                {
                    break;
                }

                if (bestDistance > maxMergeDistance)
                {
                    break;
                }

                lastMergeDistance = bestDistance;
                groups[bestA].AddRange(groups[bestB]);
                groups[bestA].Sort();
                groups.RemoveAt(bestB);
            }

            return groups.ToArray();
        }

        private static double CompleteLinkDistance(IReadOnlyList<int> left, IReadOnlyList<int> right, double[,] distances)
        {
            double maxDistance = 0d;
            for (int i = 0; i < left.Count; i++)
            {
                for (int j = 0; j < right.Count; j++)
                {
                    maxDistance = Math.Max(maxDistance, distances[left[i], right[j]]);
                }
            }

            return maxDistance;
        }

        private static double ComputePercentile(IReadOnlyList<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0)
            {
                return 0d;
            }

            double position = Math.Clamp(percentile, 0d, 1d) * (sortedValues.Count - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
            {
                return sortedValues[lower];
            }

            double weight = position - lower;
            return sortedValues[lower] * (1d - weight) + sortedValues[upper] * weight;
        }

        private static int[] RelabelSequentially(IReadOnlyList<int> labels)
        {
            var remap = new Dictionary<int, int>();
            int nextLabel = 0;
            var relabeled = new int[labels.Count];
            for (int i = 0; i < labels.Count; i++)
            {
                int label = labels[i];
                if (label < 0)
                {
                    relabeled[i] = label;
                    continue;
                }

                if (!remap.TryGetValue(label, out int mapped))
                {
                    mapped = nextLabel++;
                    remap[label] = mapped;
                }

                relabeled[i] = mapped;
            }

            return relabeled;
        }

        private static float[] ComputeCentroid(IReadOnlyList<float[]> points, IReadOnlyList<int> indices, bool normalize)
        {
            if (indices.Count == 0)
            {
                return Array.Empty<float>();
            }

            int dimension = points[indices[0]].Length;
            var sum = new double[dimension];
            for (int i = 0; i < indices.Count; i++)
            {
                var point = points[indices[i]];
                if (point.Length != dimension)
                {
                    throw new ArgumentException("Point dimensions must match.", nameof(points));
                }

                for (int d = 0; d < dimension; d++)
                {
                    sum[d] += point[d];
                }
            }

            var centroid = new float[dimension];
            double invCount = 1d / indices.Count;
            for (int d = 0; d < dimension; d++)
            {
                centroid[d] = (float)(sum[d] * invCount);
            }

            return normalize ? NormalizeVectorCopy(centroid) : centroid;
        }

        private static MstEdge[] BuildMinimumSpanningTree(IReadOnlyList<float[]> centroids, bool useCosineDistance)
        {
            int count = centroids.Count;
            if (count <= 1)
            {
                return Array.Empty<MstEdge>();
            }

            var inTree = new bool[count];
            var bestDistance = Enumerable.Repeat(double.PositiveInfinity, count).ToArray();
            var bestParent = Enumerable.Repeat(-1, count).ToArray();
            var edges = new List<MstEdge>(count - 1);

            bestDistance[0] = 0d;
            for (int step = 0; step < count; step++)
            {
                int current = -1;
                double currentDistance = double.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    if (!inTree[i] && bestDistance[i] < currentDistance)
                    {
                        current = i;
                        currentDistance = bestDistance[i];
                    }
                }

                if (current < 0)
                {
                    break;
                }

                inTree[current] = true;
                if (bestParent[current] >= 0)
                {
                    edges.Add(new MstEdge(bestParent[current], current, bestDistance[current]));
                }

                for (int next = 0; next < count; next++)
                {
                    if (inTree[next] || next == current)
                    {
                        continue;
                    }

                    double distance = useCosineDistance
                        ? CosineDistance(centroids[current], centroids[next])
                        : EuclideanDistance(centroids[current], centroids[next]);
                    if (distance < bestDistance[next])
                    {
                        bestDistance[next] = distance;
                        bestParent[next] = current;
                    }
                }
            }

            return edges.ToArray();
        }

        private static double[,] BuildDistanceMatrix(IReadOnlyList<float[]> centroids, bool useCosineDistance)
        {
            int count = centroids.Count;
            var distances = new double[count, count];
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    double distance = useCosineDistance
                        ? CosineDistance(centroids[i], centroids[j])
                        : EuclideanDistance(centroids[i], centroids[j]);
                    distances[i, j] = distance;
                    distances[j, i] = distance;
                }
            }

            return distances;
        }

        private static double EuclideanDistance(IReadOnlyList<float> a, IReadOnlyList<float> b)
        {
            int dimension = Math.Min(a.Count, b.Count);
            double sum = 0d;
            for (int i = 0; i < dimension; i++)
            {
                double delta = a[i] - b[i];
                sum += delta * delta;
            }

            return Math.Sqrt(sum);
        }

        private static double CosineDistance(IReadOnlyList<float> a, IReadOnlyList<float> b)
        {
            int dimension = Math.Min(a.Count, b.Count);
            double dot = 0d;
            for (int i = 0; i < dimension; i++)
            {
                dot += a[i] * b[i];
            }

            return Math.Max(0d, 1d - dot);
        }

        private static float[] NormalizeVectorCopy(float[] vector)
        {
            var copy = new float[vector.Length];
            Array.Copy(vector, copy, vector.Length);

            double sum = 0d;
            for (int i = 0; i < copy.Length; i++)
            {
                sum += copy[i] * copy[i];
            }

            if (sum <= 1e-12d)
            {
                return copy;
            }

            float invNorm = (float)(1d / Math.Sqrt(sum));
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] *= invNorm;
            }

            return copy;
        }

    }
}
