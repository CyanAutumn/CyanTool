using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public static class SklearnKMeansClusterer
    {
        public sealed class AutoKResult
        {
            public int BestK { get; set; }
            public double BestScore { get; set; }
            public int MinK { get; set; }
            public int MaxK { get; set; }
            public int EvaluationCount { get; set; }
            public bool UsedSample { get; set; }
        }

        public static int[] Cluster(IReadOnlyList<float[]> data, int numClusters, int randomState = 42)
        {
            if (data == null || data.Count == 0)
            {
                return Array.Empty<int>();
            }

            int n = data.Count;
            if (numClusters <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(numClusters), "numClusters must be > 0.");
            }

            if (numClusters > n)
            {
                return Enumerable.Range(0, n).ToArray();
            }

            int dimension = data[0]?.Length ?? 0;
            if (dimension <= 0)
            {
                throw new ArgumentException("输入特征为空。", nameof(data));
            }

            for (int i = 0; i < n; i++)
            {
                if (data[i] == null || data[i].Length != dimension)
                {
                    throw new ArgumentException("输入特征维度不一致。", nameof(data));
                }
            }

            var rng = new NumpyRandom(randomState);


            var (centers, labels) = RunKMeans(data, numClusters, dimension, rng);
            _ = centers;
            return labels;
        }

        public static AutoKResult FindBestKBySilhouette(
            IReadOnlyList<float[]> data,
            int minClusters,
            int maxClusters,
            int randomState = 42,
            int evaluationSampleSize = 600,
            Action<string>? log = null)
        {
            if (data == null || data.Count == 0)
            {
                throw new ArgumentException("输入特征为空。", nameof(data));
            }

            int n = data.Count;
            int dimension = data[0]?.Length ?? 0;
            if (dimension <= 0)
            {
                throw new ArgumentException("输入特征为空。", nameof(data));
            }

            for (int i = 0; i < n; i++)
            {
                if (data[i] == null || data[i].Length != dimension)
                {
                    throw new ArgumentException("输入特征维度不一致。", nameof(data));
                }
            }

            if (n < 2)
            {
                return new AutoKResult
                {
                    BestK = 1,
                    BestScore = double.NaN,
                    MinK = 1,
                    MaxK = 1,
                    EvaluationCount = n,
                    UsedSample = false
                };
            }

            var evalData = BuildEvaluationSample(data, evaluationSampleSize, randomState, out bool usedSample);
            int evalCount = evalData.Count;
            int resolvedMin = Math.Max(2, minClusters);
            int resolvedMax = Math.Min(Math.Max(resolvedMin, maxClusters), Math.Max(2, evalCount - 1));

            if (usedSample)
            {
                log?.Invoke($"轮廓系数评估将使用随机样本：{evalCount}/{n}");
            }

            if (resolvedMax < resolvedMin)
            {
                int fallbackK = Math.Clamp(Math.Min(2, n), 1, n);
                return new AutoKResult
                {
                    BestK = fallbackK,
                    BestScore = double.NaN,
                    MinK = resolvedMin,
                    MaxK = resolvedMax,
                    EvaluationCount = evalCount,
                    UsedSample = usedSample
                };
            }

            var distanceMatrix = BuildDistanceMatrix(evalData);
            double bestScore = double.NegativeInfinity;
            int bestK = resolvedMin;

            for (int k = resolvedMin; k <= resolvedMax; k++)
            {
                int[] labels = Cluster(evalData, k, randomState);
                double score = ComputeSilhouetteScore(distanceMatrix, labels);
                log?.Invoke($"轮廓系数评估: K={k}, score={score:F4}");

                if (score > bestScore + 1e-12 ||
                    (Math.Abs(score - bestScore) <= 1e-12 && k < bestK))
                {
                    bestScore = score;
                    bestK = k;
                }
            }

            return new AutoKResult
            {
                BestK = bestK,
                BestScore = bestScore,
                MinK = resolvedMin,
                MaxK = resolvedMax,
                EvaluationCount = evalCount,
                UsedSample = usedSample
            };
        }

        private static (double[,] centers, int[] labels) RunKMeans(IReadOnlyList<float[]> data, int k, int dimension, NumpyRandom rng)
        {
            int n = data.Count;
            int maxIter = 300;
            double tol = 1e-4;

            int[] initialCenterIndices = InitKMeansPlusPlus(data, k, rng);

            var centers = new double[k, dimension];
            for (int c = 0; c < k; c++)
            {
                var p = data[initialCenterIndices[c]];
                for (int d = 0; d < dimension; d++)
                {
                    centers[c, d] = p[d];
                }
            }

            var labels = new int[n];
            var distsToAssigned = new double[n];

            for (int iter = 0; iter < maxIter; iter++)
            {

                Array.Fill(distsToAssigned, 0);
                for (int i = 0; i < n; i++)
                {
                    var p = data[i];
                    double bestDist = double.PositiveInfinity;
                    int bestCluster = 0;
                    for (int c = 0; c < k; c++)
                    {
                        double dist = 0;
                        for (int d = 0; d < dimension; d++)
                        {
                            double delta = p[d] - centers[c, d];
                            dist += delta * delta;
                        }

                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestCluster = c;
                        }
                    }
                    labels[i] = bestCluster;
                    distsToAssigned[i] = bestDist;
                }


                var sums = new double[k, dimension];
                var counts = new int[k];
                for (int i = 0; i < n; i++)
                {
                    int c = labels[i];
                    counts[c]++;
                    for (int d = 0; d < dimension; d++)
                    {
                        sums[c, d] += data[i][d];
                    }
                }


                for (int c = 0; c < k; c++)
                {
                    if (counts[c] != 0)
                    {
                        continue;
                    }

                    int farthestIndex = 0;
                    double farthestDist = -1;
                    for (int i = 0; i < n; i++)
                    {
                        if (distsToAssigned[i] > farthestDist)
                        {
                            farthestDist = distsToAssigned[i];
                            farthestIndex = i;
                        }
                    }

                    int oldCluster = labels[farthestIndex];
                    if (counts[oldCluster] > 1)
                    {
                        counts[oldCluster]--;
                        for (int d = 0; d < dimension; d++)
                        {
                            sums[oldCluster, d] -= data[farthestIndex][d];
                        }
                    }

                    labels[farthestIndex] = c;
                    counts[c] = 1;
                    for (int d = 0; d < dimension; d++)
                    {
                        sums[c, d] = data[farthestIndex][d];
                    }
                    distsToAssigned[farthestIndex] = 0;
                }

                double shift = 0;
                for (int c = 0; c < k; c++)
                {
                    for (int d = 0; d < dimension; d++)
                    {
                        double newValue = counts[c] > 0 ? sums[c, d] / counts[c] : centers[c, d];
                        double delta = centers[c, d] - newValue;
                        shift += delta * delta;
                        centers[c, d] = newValue;
                    }
                }

                if (shift <= tol)
                {
                    break;
                }
            }

            return (centers, labels);
        }

        private static int[] InitKMeansPlusPlus(IReadOnlyList<float[]> data, int k, NumpyRandom rng)
        {
            int n = data.Count;

            var centers = new int[k];
            centers[0] = rng.NextInt(n);

            var closestDistSq = new double[n];
            for (int i = 0; i < n; i++)
            {
                closestDistSq[i] = SquaredDistance(data[i], data[centers[0]]);
            }

            double currentPot = closestDistSq.Sum();
            int nLocalTrials = 2 + (int)Math.Log(k);

            for (int c = 1; c < k; c++)
            {

                var randVals = new double[nLocalTrials];
                for (int t = 0; t < nLocalTrials; t++)
                {
                    randVals[t] = rng.NextDouble() * currentPot;
                }

                var cumsum = new double[n];
                double running = 0;
                for (int i = 0; i < n; i++)
                {
                    running += closestDistSq[i];
                    cumsum[i] = running;
                }

                int bestCandidate = -1;
                double bestPot = double.PositiveInfinity;
                double[]? bestDistances = null;

                for (int t = 0; t < nLocalTrials; t++)
                {
                    int candidate = SearchSorted(cumsum, randVals[t]);
                    if (candidate >= n)
                    {
                        candidate = n - 1;
                    }

                    var newDist = new double[n];
                    double newPot = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double d = SquaredDistance(data[i], data[candidate]);
                        double nd = d < closestDistSq[i] ? d : closestDistSq[i];
                        newDist[i] = nd;
                        newPot += nd;
                    }

                    if (newPot < bestPot)
                    {
                        bestPot = newPot;
                        bestCandidate = candidate;
                        bestDistances = newDist;
                    }
                }

                if (bestCandidate < 0)
                {
                    bestCandidate = 0;
                    bestDistances = closestDistSq.ToArray();
                    bestPot = currentPot;
                }

                centers[c] = bestCandidate;
                closestDistSq = bestDistances!;
                currentPot = bestPot;
            }

            return centers;
        }

        private static double SquaredDistance(float[] a, float[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double delta = a[i] - b[i];
                sum += delta * delta;
            }

            return sum;
        }

        private static int SearchSorted(double[] cumsum, double value)
        {
            int lo = 0;
            int hi = cumsum.Length;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (value <= cumsum[mid])
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }
            return lo;
        }

        private static List<float[]> BuildEvaluationSample(
            IReadOnlyList<float[]> data,
            int evaluationSampleSize,
            int randomState,
            out bool usedSample)
        {
            int n = data.Count;
            int sampleSize = Math.Min(n, Math.Max(2, evaluationSampleSize));
            if (n <= sampleSize)
            {
                usedSample = false;
                return data.ToList();
            }

            usedSample = true;
            var rng = new NumpyRandom(randomState + 997);
            int[] indices = Enumerable.Range(0, n).ToArray();
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            var sampled = new List<float[]>(sampleSize);
            for (int i = 0; i < sampleSize; i++)
            {
                sampled.Add(data[indices[i]]);
            }

            return sampled;
        }

        private static double[,] BuildDistanceMatrix(IReadOnlyList<float[]> data)
        {
            int n = data.Count;
            var matrix = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dist = Math.Sqrt(SquaredDistance(data[i], data[j]));
                    matrix[i, j] = dist;
                    matrix[j, i] = dist;
                }
            }

            return matrix;
        }

        private static double ComputeSilhouetteScore(double[,] distances, IReadOnlyList<int> labels)
        {
            int n = labels.Count;
            if (n <= 1)
            {
                return 0;
            }

            var clusters = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int label = labels[i];
                if (!clusters.TryGetValue(label, out var members))
                {
                    members = new List<int>();
                    clusters[label] = members;
                }

                members.Add(i);
            }

            if (clusters.Count <= 1)
            {
                return 0;
            }

            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                int ownLabel = labels[i];
                var ownMembers = clusters[ownLabel];
                if (ownMembers.Count <= 1)
                {
                    continue;
                }

                double a = 0;
                for (int m = 0; m < ownMembers.Count; m++)
                {
                    int j = ownMembers[m];
                    if (j == i)
                    {
                        continue;
                    }

                    a += distances[i, j];
                }
                a /= (ownMembers.Count - 1);

                double b = double.PositiveInfinity;
                foreach (var pair in clusters)
                {
                    if (pair.Key == ownLabel)
                    {
                        continue;
                    }

                    var otherMembers = pair.Value;
                    double avg = 0;
                    for (int m = 0; m < otherMembers.Count; m++)
                    {
                        avg += distances[i, otherMembers[m]];
                    }
                    avg /= otherMembers.Count;

                    if (avg < b)
                    {
                        b = avg;
                    }
                }

                if (double.IsInfinity(b))
                {
                    continue;
                }

                double denom = Math.Max(a, b);
                if (denom <= 1e-12)
                {
                    continue;
                }

                sum += (b - a) / denom;
            }

            return sum / n;
        }
    }
}
