using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public static class TwoStageStyleClusterer
    {
        public sealed class Result
        {
            public int[] Labels { get; init; } = Array.Empty<int>();
            public int RequestedClusterCount { get; init; }
            public int ProducedClusterCount { get; init; }
            public int CoarseClusterCount { get; init; }
            public IReadOnlyList<int> CoarseClusterSizes { get; init; } = Array.Empty<int>();
            public IReadOnlyList<int> FineClusterCounts { get; init; } = Array.Empty<int>();
        }

        public static Result Cluster(
            IReadOnlyList<float[]> coarseData,
            IReadOnlyList<float[]> fineData,
            int numClusters,
            bool useSphericalKMeans,
            int randomState = 42)
        {
            if (coarseData == null || fineData == null || coarseData.Count != fineData.Count)
            {
                throw new ArgumentException("二阶段聚类输入数量不一致。");
            }

            int n = fineData.Count;
            if (n == 0)
            {
                return new Result();
            }

            if (numClusters <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(numClusters), "numClusters must be > 0.");
            }

            if (numClusters >= n)
            {
                return new Result
                {
                    Labels = Enumerable.Range(0, n).ToArray(),
                    RequestedClusterCount = numClusters,
                    ProducedClusterCount = n,
                    CoarseClusterCount = n,
                    CoarseClusterSizes = Enumerable.Repeat(1, n).ToArray(),
                    FineClusterCounts = Enumerable.Repeat(1, n).ToArray()
                };
            }

            if (numClusters == 1)
            {
                return new Result
                {
                    Labels = new int[n],
                    RequestedClusterCount = 1,
                    ProducedClusterCount = 1,
                    CoarseClusterCount = 1,
                    CoarseClusterSizes = new[] { n },
                    FineClusterCounts = new[] { 1 }
                };
            }

            int coarseK = ResolveCoarseClusterCount(numClusters, n);
            int[] coarseAssignments = coarseK > 1
                ? ClusterOnce(coarseData, coarseK, useSphericalKMeans, randomState)
                : new int[n];

            var coarseGroups = Enumerable.Range(0, n)
                .GroupBy(index => coarseAssignments[index])
                .OrderBy(group => group.Key)
                .Select(group => group.OrderBy(index => index).ToList())
                .ToList();

            int[] coarseSizes = coarseGroups.Select(group => group.Count).ToArray();
            int[] fineCounts = AllocateFineClusterCounts(coarseSizes, numClusters);
            int[] finalLabels = new int[n];
            int nextLabel = 0;

            for (int groupIndex = 0; groupIndex < coarseGroups.Count; groupIndex++)
            {
                var indices = coarseGroups[groupIndex];
                int targetFineK = Math.Min(fineCounts[groupIndex], indices.Count);
                if (targetFineK <= 1)
                {
                    for (int i = 0; i < indices.Count; i++)
                    {
                        finalLabels[indices[i]] = nextLabel;
                    }

                    nextLabel++;
                    continue;
                }

                var subset = new List<float[]>(indices.Count);
                for (int i = 0; i < indices.Count; i++)
                {
                    subset.Add(fineData[indices[i]]);
                }

                int[] localAssignments = ClusterOnce(
                    subset,
                    targetFineK,
                    useSphericalKMeans,
                    randomState + 7919 * (groupIndex + 1));

                foreach (int localClusterId in localAssignments.Distinct().OrderBy(id => id))
                {
                    for (int i = 0; i < indices.Count; i++)
                    {
                        if (localAssignments[i] == localClusterId)
                        {
                            finalLabels[indices[i]] = nextLabel;
                        }
                    }

                    nextLabel++;
                }
            }

            return new Result
            {
                Labels = finalLabels,
                RequestedClusterCount = numClusters,
                ProducedClusterCount = nextLabel,
                CoarseClusterCount = coarseGroups.Count,
                CoarseClusterSizes = coarseSizes,
                FineClusterCounts = fineCounts
            };
        }

        private static int[] ClusterOnce(IReadOnlyList<float[]> data, int k, bool useSphericalKMeans, int randomState)
        {
            return useSphericalKMeans
                ? SphericalKMeansClusterer.Cluster(data, k, randomState: randomState)
                : SklearnKMeansClusterer.Cluster(data, k, randomState: randomState);
        }

        private static int ResolveCoarseClusterCount(int totalClusters, int sampleCount)
        {
            if (totalClusters <= 1 || sampleCount <= 1)
            {
                return 1;
            }

            int upperBound = Math.Min(sampleCount, totalClusters - 1);
            if (upperBound <= 1)
            {
                return 1;
            }

            int heuristic = (int)Math.Round(Math.Sqrt(totalClusters));
            return Math.Clamp(heuristic, 1, upperBound);
        }

        private static int[] AllocateFineClusterCounts(IReadOnlyList<int> coarseSizes, int totalClusters)
        {
            int coarseCount = coarseSizes.Count;
            var allocations = Enumerable.Repeat(1, coarseCount).ToArray();
            if (coarseCount == 0)
            {
                return allocations;
            }

            int remaining = totalClusters - coarseCount;
            if (remaining <= 0)
            {
                return allocations;
            }

            int totalSamples = coarseSizes.Sum();
            var extraTargets = new double[coarseCount];
            var remainders = new double[coarseCount];
            for (int i = 0; i < coarseCount; i++)
            {
                extraTargets[i] = (double)coarseSizes[i] / totalSamples * remaining;
                int capacity = Math.Max(0, coarseSizes[i] - 1);
                int floorExtra = Math.Min(capacity, (int)Math.Floor(extraTargets[i]));
                allocations[i] += floorExtra;
                remaining -= floorExtra;
                remainders[i] = extraTargets[i] - floorExtra;
            }

            while (remaining > 0)
            {
                int bestIndex = -1;
                double bestScore = double.NegativeInfinity;
                for (int i = 0; i < coarseCount; i++)
                {
                    int capacity = Math.Max(0, coarseSizes[i] - allocations[i]);
                    if (capacity <= 0)
                    {
                        continue;
                    }

                    double score = remainders[i] + coarseSizes[i] * 1e-6;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0)
                {
                    break;
                }

                allocations[bestIndex]++;
                remainders[bestIndex] -= 1d;
                remaining--;
            }

            return allocations;
        }
    }
}
