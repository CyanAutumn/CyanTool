using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public static class SphericalKMeansClusterer
    {
        private const int DefaultNInit = 12;

        public sealed class AutoKResult
        {
            public int BestK { get; set; }
            public double BestScore { get; set; }
            public int MinK { get; set; }
            public int MaxK { get; set; }
            public int EvaluationCount { get; set; }
            public bool UsedSample { get; set; }
        }

        public static int[] Cluster(IReadOnlyList<float[]> data, int numClusters, int randomState = 42, int nInit = DefaultNInit)
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

            var normalizedData = NormalizeData(data, dimension);
            int resolvedNInit = Math.Max(1, nInit);
            var rng = new NumpyRandom(randomState);
            int[]? bestLabels = null;
            double bestInertia = double.PositiveInfinity;

            for (int init = 0; init < resolvedNInit; init++)
            {
                var (_, labels, inertia) = RunSphericalKMeans(normalizedData, numClusters, dimension, rng);
                if (bestLabels == null || inertia < bestInertia)
                {
                    bestLabels = labels;
                    bestInertia = inertia;
                }
            }

            return bestLabels ?? Array.Empty<int>();
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

            var normalizedData = NormalizeData(data, dimension);
            var evalData = BuildEvaluationSample(normalizedData, evaluationSampleSize, randomState, out bool usedSample);
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

            var distanceMatrix = BuildCosineDistanceMatrix(evalData);
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

        private static List<float[]> NormalizeData(IReadOnlyList<float[]> data, int dimension)
        {
            var normalized = new List<float[]>(data.Count);
            for (int i = 0; i < data.Count; i++)
            {
                if (data[i] == null || data[i].Length != dimension)
                {
                    throw new ArgumentException("输入特征维度不一致。", nameof(data));
                }

                normalized.Add(NormalizeVectorCopy(data[i]));
            }

            return normalized;
        }

        private static (double[,] centers, int[] labels, double inertia) RunSphericalKMeans(
            IReadOnlyList<float[]> data,
            int k,
            int dimension,
            NumpyRandom rng)
        {
            int n = data.Count;
            const int maxIter = 300;
            const double tol = 1e-6;

            int[] initialCenterIndices = InitSphericalKMeansPlusPlus(data, k, rng);
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
            var scoresToAssigned = new double[n];
            var lossesToAssigned = new double[n];

            for (int iter = 0; iter < maxIter; iter++)
            {
                AssignLabels(data, centers, labels, scoresToAssigned, lossesToAssigned);

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
                    double worstLoss = double.NegativeInfinity;
                    for (int i = 0; i < n; i++)
                    {
                        if (lossesToAssigned[i] > worstLoss)
                        {
                            worstLoss = lossesToAssigned[i];
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
                    scoresToAssigned[farthestIndex] = 1.0;
                    lossesToAssigned[farthestIndex] = 0.0;
                }

                double shift = 0;
                for (int c = 0; c < k; c++)
                {
                    var newCenter = new double[dimension];
                    if (counts[c] > 0)
                    {
                        for (int d = 0; d < dimension; d++)
                        {
                            newCenter[d] = sums[c, d];
                        }

                        NormalizeVectorInPlace(newCenter);
                    }
                    else
                    {
                        for (int d = 0; d < dimension; d++)
                        {
                            newCenter[d] = centers[c, d];
                        }
                    }

                    for (int d = 0; d < dimension; d++)
                    {
                        double delta = centers[c, d] - newCenter[d];
                        shift += delta * delta;
                        centers[c, d] = newCenter[d];
                    }
                }

                if (shift <= tol)
                {
                    break;
                }
            }

            AssignLabels(data, centers, labels, scoresToAssigned, lossesToAssigned);
            return (centers, labels, lossesToAssigned.Sum());
        }

        private static void AssignLabels(
            IReadOnlyList<float[]> data,
            double[,] centers,
            int[] labels,
            double[] scoresToAssigned,
            double[] lossesToAssigned)
        {
            int n = data.Count;
            int k = centers.GetLength(0);
            int dimension = centers.GetLength(1);

            for (int i = 0; i < n; i++)
            {
                var p = data[i];
                double bestScore = double.NegativeInfinity;
                int bestCluster = 0;
                for (int c = 0; c < k; c++)
                {
                    double dot = 0;
                    for (int d = 0; d < dimension; d++)
                    {
                        dot += p[d] * centers[c, d];
                    }

                    if (dot > bestScore)
                    {
                        bestScore = dot;
                        bestCluster = c;
                    }
                }

                labels[i] = bestCluster;
                scoresToAssigned[i] = bestScore;
                lossesToAssigned[i] = 1.0 - bestScore;
            }
        }

        private static int[] InitSphericalKMeansPlusPlus(IReadOnlyList<float[]> data, int k, NumpyRandom rng)
        {
            int n = data.Count;
            var centers = new int[k];
            centers[0] = rng.NextInt(n);

            var closestLoss = new double[n];
            for (int i = 0; i < n; i++)
            {
                closestLoss[i] = CosineLoss(data[i], data[centers[0]]);
            }

            double currentPot = closestLoss.Sum();
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
                    running += closestLoss[i];
                    cumsum[i] = running;
                }

                int bestCandidate = -1;
                double bestPot = double.PositiveInfinity;
                double[]? bestLosses = null;

                for (int t = 0; t < nLocalTrials; t++)
                {
                    int candidate = SearchSorted(cumsum, randVals[t]);
                    if (candidate >= n)
                    {
                        candidate = n - 1;
                    }

                    var newLoss = new double[n];
                    double newPot = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double loss = CosineLoss(data[i], data[candidate]);
                        double minLoss = loss < closestLoss[i] ? loss : closestLoss[i];
                        newLoss[i] = minLoss;
                        newPot += minLoss;
                    }

                    if (newPot < bestPot)
                    {
                        bestPot = newPot;
                        bestCandidate = candidate;
                        bestLosses = newLoss;
                    }
                }

                if (bestCandidate < 0)
                {
                    bestCandidate = 0;
                    bestLosses = closestLoss.ToArray();
                    bestPot = currentPot;
                }

                centers[c] = bestCandidate;
                closestLoss = bestLosses!;
                currentPot = bestPot;
            }

            return centers;
        }

        private static double CosineLoss(float[] a, float[] b)
        {
            double dot = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
            }

            return 1.0 - dot;
        }

        private static double[,] BuildCosineDistanceMatrix(IReadOnlyList<float[]> data)
        {
            int n = data.Count;
            var matrix = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dist = CosineLoss(data[i], data[j]);
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

        private static float[] NormalizeVectorCopy(float[] vector)
        {
            var copy = new float[vector.Length];
            Array.Copy(vector, copy, vector.Length);

            double sum = 0;
            for (int i = 0; i < copy.Length; i++)
            {
                float value = copy[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new ArgumentException("输入特征包含非法数值（NaN/Infinity）。", nameof(vector));
                }

                sum += value * value;
            }

            if (sum <= 1e-12)
            {
                return copy;
            }

            float invNorm = (float)(1.0 / Math.Sqrt(sum));
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] *= invNorm;
            }

            return copy;
        }

        private static void NormalizeVectorInPlace(double[] vector)
        {
            double sum = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                sum += vector[i] * vector[i];
            }

            if (sum <= 1e-12)
            {
                return;
            }

            double invNorm = 1.0 / Math.Sqrt(sum);
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] *= invNorm;
            }
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
    }
}
