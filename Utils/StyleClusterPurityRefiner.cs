using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public static class StyleClusterPurityRefiner
    {
        public sealed class Result
        {
            public int[] Labels { get; init; } = Array.Empty<int>();
            public int OriginalClusterCount { get; init; }
            public int FinalClusterCount { get; init; }
            public int SplitClusterCount { get; init; }
            public double CompleteLinkThreshold { get; init; }
            public bool Changed => SplitClusterCount > 0 && FinalClusterCount > OriginalClusterCount;
        }

        public static Result SplitLooseClusters(
            IReadOnlyList<float[]> points,
            IReadOnlyList<int> labels,
            bool useCosineDistance = false,
            int minClusterSizeToSplit = 6,
            double minJumpRatio = 1.65d,
            double minJumpDistance = 0.008d)
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

            var originalClusterIds = labels
                .Where(label => label >= 0)
                .Distinct()
                .OrderBy(label => label)
                .ToArray();
            if (points.Count <= 1 || originalClusterIds.Length == 0)
            {
                return new Result
                {
                    Labels = labels.ToArray(),
                    OriginalClusterCount = originalClusterIds.Length,
                    FinalClusterCount = originalClusterIds.Length,
                    CompleteLinkThreshold = double.PositiveInfinity
                };
            }

            var nextLabels = new int[labels.Count];
            Array.Fill(nextLabels, -1);
            int nextLabel = 0;
            int splitClusterCount = 0;
            var splitThresholds = new List<double>();

            foreach (int clusterId in originalClusterIds)
            {
                var indices = Enumerable.Range(0, labels.Count)
                    .Where(index => labels[index] == clusterId)
                    .ToArray();
                if (indices.Length < Math.Max(2, minClusterSizeToSplit))
                {
                    AssignLabel(nextLabels, indices, nextLabel++);
                    continue;
                }

                double threshold = FindStableLocalThreshold(
                    points,
                    indices,
                    useCosineDistance,
                    minJumpRatio,
                    minJumpDistance);
                if (double.IsInfinity(threshold))
                {
                    AssignLabel(nextLabels, indices, nextLabel++);
                    continue;
                }

                var groups = SplitByCompleteLink(points, indices, threshold, useCosineDistance);
                if (groups.Length > 1)
                {
                    splitClusterCount++;
                    splitThresholds.Add(threshold);
                }

                foreach (var group in groups.OrderBy(group => group.Min()))
                {
                    AssignLabel(nextLabels, group, nextLabel++);
                }
            }

            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] < 0)
                {
                    nextLabels[i] = labels[i];
                }
            }

            int finalClusterCount = nextLabels.Where(label => label >= 0).Distinct().Count();
            return new Result
            {
                Labels = nextLabels,
                OriginalClusterCount = originalClusterIds.Length,
                FinalClusterCount = finalClusterCount,
                SplitClusterCount = splitClusterCount,
                CompleteLinkThreshold = splitThresholds.Count == 0
                    ? double.PositiveInfinity
                    : splitThresholds.Average()
            };
        }

        private static double FindStableLocalThreshold(
            IReadOnlyList<float[]> points,
            IReadOnlyList<int> indices,
            bool useCosineDistance,
            double minJumpRatio,
            double minJumpDistance)
        {
            if (indices.Count < 3)
            {
                return double.PositiveInfinity;
            }

            var distances = new List<double>(indices.Count * (indices.Count - 1) / 2);
            for (int i = 0; i < indices.Count; i++)
            {
                for (int j = i + 1; j < indices.Count; j++)
                {
                    distances.Add(Distance(points[indices[i]], points[indices[j]], useCosineDistance));
                }
            }

            distances.Sort();
            if (distances.Count < 2)
            {
                return double.PositiveInfinity;
            }

            double bestScore = 0d;
            int bestIndex = -1;
            for (int i = 1; i < distances.Count; i++)
            {
                double previous = distances[i - 1];
                double current = distances[i];
                double gap = current - previous;
                if (gap < minJumpDistance)
                {
                    continue;
                }

                double ratio = (current + 1e-9d) / (previous + 1e-9d);
                if (ratio < minJumpRatio)
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

            return bestIndex < 0
                ? double.PositiveInfinity
                : (distances[bestIndex - 1] + distances[bestIndex]) / 2d;
        }

        private static List<int>[] SplitByCompleteLink(
            IReadOnlyList<float[]> points,
            IReadOnlyList<int> indices,
            double threshold,
            bool useCosineDistance)
        {
            var groups = indices
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
                        double distance = CompleteLinkDistance(points, groups[a], groups[b], useCosineDistance);
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

        private static double CompleteLinkDistance(
            IReadOnlyList<float[]> points,
            IReadOnlyList<int> left,
            IReadOnlyList<int> right,
            bool useCosineDistance)
        {
            double maxDistance = 0d;
            foreach (int leftIndex in left)
            {
                foreach (int rightIndex in right)
                {
                    maxDistance = Math.Max(maxDistance, Distance(points[leftIndex], points[rightIndex], useCosineDistance));
                }
            }

            return maxDistance;
        }

        private static double Distance(IReadOnlyList<float> left, IReadOnlyList<float> right, bool useCosineDistance)
        {
            return useCosineDistance
                ? CosineDistance(left, right)
                : EuclideanDistance(left, right);
        }

        private static double EuclideanDistance(IReadOnlyList<float> left, IReadOnlyList<float> right)
        {
            int dimension = Math.Min(left.Count, right.Count);
            double sum = 0d;
            for (int i = 0; i < dimension; i++)
            {
                double delta = left[i] - right[i];
                sum += delta * delta;
            }

            return Math.Sqrt(sum);
        }

        private static double CosineDistance(IReadOnlyList<float> left, IReadOnlyList<float> right)
        {
            int dimension = Math.Min(left.Count, right.Count);
            double dot = 0d;
            double leftNorm = 0d;
            double rightNorm = 0d;
            for (int i = 0; i < dimension; i++)
            {
                double a = left[i];
                double b = right[i];
                dot += a * b;
                leftNorm += a * a;
                rightNorm += b * b;
            }

            double norm = Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm);
            if (norm <= 1e-12d)
            {
                return 1d;
            }

            return Math.Max(0d, 1d - dot / norm);
        }

        private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
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

        private static void AssignLabel(IList<int> labels, IEnumerable<int> indices, int label)
        {
            foreach (int index in indices)
            {
                labels[index] = label;
            }
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
    }
}
