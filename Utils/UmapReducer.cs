using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UMAP;

namespace CyanTools.Utils
{
    public static class UmapReducer
    {
        private const float PythonDefaultMinDist = 0.1f;
        private const float PythonDefaultSpread = 1.0f;

        private sealed class UmapRandomProvider : IProvideRandomValues
        {
            private readonly NumpyRandom _rng;

            public UmapRandomProvider(int seed)
            {
                _rng = new NumpyRandom(seed);
            }

            public bool IsThreadSafe => false;

            public double NextDouble() => _rng.NextDouble();

            public float NextFloat() => (float)_rng.NextDouble();

            public void NextFloats(Span<float> buffer)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = NextFloat();
                }
            }

            public int NextInt(int exclusiveMax) => _rng.NextInt(exclusiveMax);

            public int Next(int inclusiveMin, int exclusiveMax) => _rng.NextInt(inclusiveMin, exclusiveMax);
        }

        public sealed class Options
        {
            public int NNeighbors { get; set; } = 15;
            public int NComponents { get; set; } = 2;
            public string Metric { get; set; } = "cosine";
            public int RandomState { get; set; } = 42;

            public float MinDist { get; set; } = 0.1f;
            public float Spread { get; set; } = 1.0f;

            public float LearningRate { get; set; } = 1.0f;
            public float RepulsionStrength { get; set; } = 1.0f;
            public int NegativeSampleRate { get; set; } = 5;

            public int? NEpochs { get; set; } = null;
        }

        public static List<float[]> ReduceToTwoDimensions(IReadOnlyList<float[]> dataPoints, Options? options = null, CancellationToken token = default, Action<double>? progress = null)
        {
            options ??= new Options();

            if (CanUsePackageReducer(options))
            {
                try
                {
                    return ReduceToTwoDimensionsWithPackage(dataPoints, options, token, progress);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {

                }
            }

            return ReduceToTwoDimensionsLegacy(dataPoints, options, token, progress);
        }

        private static bool CanUsePackageReducer(Options options)
        {
            if (options.NComponents != 2)
            {
                return false;
            }

            bool defaultMinDist = Math.Abs(options.MinDist - PythonDefaultMinDist) <= 1e-6f;
            bool defaultSpread = Math.Abs(options.Spread - PythonDefaultSpread) <= 1e-6f;
            return defaultMinDist && defaultSpread;
        }

        private static List<float[]> ReduceToTwoDimensionsWithPackage(IReadOnlyList<float[]> dataPoints, Options options, CancellationToken token, Action<double>? progress)
        {
            if (dataPoints == null || dataPoints.Count == 0)
            {
                return new List<float[]>();
            }

            if (options.NComponents != 2)
            {
                throw new NotSupportedException("当前仅支持降维到 2 维（与参考项目一致）。");
            }

            int sampleCount = dataPoints.Count;
            int dim = dataPoints[0]?.Length ?? 0;
            if (dim == 0 || dataPoints.Any(v => v == null || v.Length != dim))
            {
                throw new ArgumentException("输入特征维度不一致或为空。", nameof(dataPoints));
            }

            int nNeighbors = Math.Clamp(options.NNeighbors, 2, Math.Max(2, sampleCount - 1));
            int? nEpochs = options.NEpochs;
            DistanceCalculation distanceFn = options.Metric.Equals("cosine", StringComparison.OrdinalIgnoreCase)
                ? Umap.DistanceFunctions.CosineForNormalizedVectors
                : Umap.DistanceFunctions.Euclidean;
            IProvideRandomValues randomProvider = new UmapRandomProvider(options.RandomState);

            progress?.Invoke(0);

            var umap = new Umap(
                distanceFn,
                randomProvider,
                options.NComponents,
                nNeighbors,
                nEpochs,
                p => progress?.Invoke(Math.Clamp(p, 0f, 1f)));

            float[][] vectors = dataPoints.Select(v => v.ToArray()).ToArray();
            int epochs = umap.InitializeFit(vectors);
            int totalEpochs = nEpochs ?? epochs;
            for (int epoch = 0; epoch < totalEpochs; epoch++)
            {
                token.ThrowIfCancellationRequested();
                umap.Step();
                progress?.Invoke((double)(epoch + 1) / totalEpochs);
            }

            float[][] embedding = umap.GetEmbedding();
            var result = new List<float[]>(embedding.Length);
            for (int i = 0; i < embedding.Length; i++)
            {
                result.Add(new[] { embedding[i][0], embedding[i][1] });
            }

            progress?.Invoke(1.0);
            return result;
        }

        private static List<float[]> ReduceToTwoDimensionsLegacy(IReadOnlyList<float[]> dataPoints, Options options, CancellationToken token, Action<double>? progress)
        {
            if (dataPoints == null || dataPoints.Count == 0)
            {
                return new List<float[]>();
            }

            if (options.NComponents != 2)
            {
                throw new NotSupportedException("当前仅支持降维到 2 维（与参考项目一致）。");
            }

            int sampleCount = dataPoints.Count;
            int dim = dataPoints[0]?.Length ?? 0;
            if (dim == 0 || dataPoints.Any(v => v == null || v.Length != dim))
            {
                throw new ArgumentException("输入特征维度不一致或为空。", nameof(dataPoints));
            }

            int nNeighbors = Math.Clamp(options.NNeighbors, 2, Math.Max(2, sampleCount - 1));
            int nEpochs = options.NEpochs ?? (sampleCount <= 10_000 ? 500 : 200);



            (double a, double b) = GetDefaultAB(options.Spread, options.MinDist);

            progress?.Invoke(0);


            token.ThrowIfCancellationRequested();
            var (knnIndices, knnDists) = ComputeKnn(dataPoints, nNeighbors, options.Metric, token, p => progress?.Invoke(p * 0.35));


            token.ThrowIfCancellationRequested();
            var (rho, sigma) = SmoothKnn(knnDists, nNeighbors, token, p => progress?.Invoke(0.35 + p * 0.10));


            token.ThrowIfCancellationRequested();
            var (heads, tails, weights) = BuildSymmetricGraph(knnIndices, knnDists, rho, sigma, token);
            progress?.Invoke(0.50);


            token.ThrowIfCancellationRequested();
            var embedding = InitializeEmbeddingSpectralLike(sampleCount, heads, tails, weights, options.RandomState, token);
            progress?.Invoke(0.55);


            token.ThrowIfCancellationRequested();
            OptimizeLayout(embedding, heads, tails, weights, nEpochs, a, b, options, token, p => progress?.Invoke(0.55 + p * 0.45));


            var result = new List<float[]>(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                result.Add(new[] { embedding[i, 0], embedding[i, 1] });
            }

            progress?.Invoke(1.0);
            return result;
        }

        private static (double a, double b) GetDefaultAB(float spread, float minDist)
        {



            _ = spread;
            _ = minDist;
            return (1.576943460405378, 0.8950608781227859);
        }

        private static (int[][] indices, float[][] dists) ComputeKnn(IReadOnlyList<float[]> data, int k, string metric, CancellationToken token, Action<double>? progress)
        {
            int n = data.Count;
            var indices = new int[n][];
            var dists = new float[n][];


            double[] norms = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                var v = data[i];
                for (int t = 0; t < v.Length; t++)
                {
                    double x = v[t];
                    sum += x * x;
                }
                norms[i] = Math.Sqrt(sum);
            }

            for (int i = 0; i < n; i++)
            {
                token.ThrowIfCancellationRequested();

                int[] bestIdx = Enumerable.Repeat(-1, k).ToArray();
                float[] bestDist = Enumerable.Repeat(float.PositiveInfinity, k).ToArray();

                for (int j = 0; j < n; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    float dist = metric.Equals("cosine", StringComparison.OrdinalIgnoreCase)
                        ? CosineDistance(data[i], norms[i], data[j], norms[j])
                        : EuclideanDistance(data[i], data[j]);


                    if (dist >= bestDist[k - 1])
                    {
                        continue;
                    }

                    int pos = k - 1;
                    while (pos > 0 && (dist < bestDist[pos - 1] || (dist == bestDist[pos - 1] && j < bestIdx[pos - 1])))
                    {
                        bestDist[pos] = bestDist[pos - 1];
                        bestIdx[pos] = bestIdx[pos - 1];
                        pos--;
                    }

                    bestDist[pos] = dist;
                    bestIdx[pos] = j;
                }

                indices[i] = bestIdx;
                dists[i] = bestDist;

                progress?.Invoke((double)(i + 1) / n);
            }

            return (indices, dists);
        }

        private static float CosineDistance(float[] a, double normA, float[] b, double normB)
        {
            double denom = normA * normB;
            if (denom <= 0)
            {
                return 1f;
            }

            double dot = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += (double)a[i] * b[i];
            }

            double cosine = dot / denom;

            cosine = Math.Clamp(cosine, -1.0, 1.0);
            return (float)(1.0 - cosine);
        }

        private static float EuclideanDistance(float[] a, float[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double d = a[i] - b[i];
                sum += d * d;
            }
            return (float)Math.Sqrt(sum);
        }

        private static (double[] rho, double[] sigma) SmoothKnn(float[][] knnDists, int k, CancellationToken token, Action<double>? progress)
        {
            int n = knnDists.Length;
            var rho = new double[n];
            var sigma = new double[n];

            double target = Math.Log(k, 2);
            const int maxIters = 64;

            for (int i = 0; i < n; i++)
            {
                token.ThrowIfCancellationRequested();
                var distances = knnDists[i];


                double rhoI = distances[0];

                rho[i] = rhoI;

                double lo = 0;
                double hi = double.PositiveInfinity;
                double mid = 1.0;

                for (int iter = 0; iter < maxIters; iter++)
                {
                    double psum = 0;
                    for (int t = 0; t < k; t++)
                    {
                        double d = distances[t] - rhoI;
                        if (d > 0)
                        {
                            psum += Math.Exp(-d / mid);
                        }
                        else
                        {
                            psum += 1.0;
                        }
                    }

                    if (Math.Abs(psum - target) < 1e-5)
                    {
                        break;
                    }

                    if (psum > target)
                    {
                        hi = mid;
                        mid = (lo + hi) / 2.0;
                    }
                    else
                    {
                        lo = mid;
                        mid = double.IsInfinity(hi) ? mid * 2.0 : (lo + hi) / 2.0;
                    }
                }

                sigma[i] = mid;
                progress?.Invoke((double)(i + 1) / n);
            }

            return (rho, sigma);
        }

        private struct DirectedPairWeights
        {
            public float AtoB;
            public float BtoA;
        }

        private static (int[] heads, int[] tails, float[] weights) BuildSymmetricGraph(int[][] knnIndices, float[][] knnDists, double[] rho, double[] sigma, CancellationToken token)
        {
            int n = knnIndices.Length;
            int k = knnIndices[0].Length;

            var pairs = new Dictionary<long, DirectedPairWeights>(capacity: n * k);

            for (int i = 0; i < n; i++)
            {
                token.ThrowIfCancellationRequested();
                for (int t = 0; t < k; t++)
                {
                    int j = knnIndices[i][t];
                    if (j < 0)
                    {
                        continue;
                    }

                    double d = knnDists[i][t];
                    double sig = sigma[i];
                    double r = rho[i];

                    float w;
                    if (sig <= 0)
                    {
                        w = 0f;
                    }
                    else if (d - r <= 0)
                    {
                        w = 1f;
                    }
                    else
                    {
                        w = (float)Math.Exp(-(d - r) / sig);
                    }

                    int a = i < j ? i : j;
                    int b = i < j ? j : i;
                    long key = ((long)a << 32) | (uint)b;

                    if (!pairs.TryGetValue(key, out var pair))
                    {
                        pair = default;
                    }

                    if (i == a)
                    {
                        if (w > pair.AtoB)
                        {
                            pair.AtoB = w;
                        }
                    }
                    else
                    {
                        if (w > pair.BtoA)
                        {
                            pair.BtoA = w;
                        }
                    }

                    pairs[key] = pair;
                }
            }

            var edges = new List<(int Head, int Tail, float Weight)>(pairs.Count * 2);

            foreach (var kvp in pairs)
            {
                token.ThrowIfCancellationRequested();
                int a = (int)(kvp.Key >> 32);
                int b = unchecked((int)(uint)kvp.Key);
                var pair = kvp.Value;

                float w1 = pair.AtoB;
                float w2 = pair.BtoA;


                float w = w1 + w2 - w1 * w2;
                if (w <= 0)
                {
                    continue;
                }

                edges.Add((a, b, w));
                edges.Add((b, a, w));
            }


            edges.Sort(static (x, y) =>
            {
                int c = x.Head.CompareTo(y.Head);
                return c != 0 ? c : x.Tail.CompareTo(y.Tail);
            });

            var heads = new int[edges.Count];
            var tails = new int[edges.Count];
            var weights = new float[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                heads[i] = edges[i].Head;
                tails[i] = edges[i].Tail;
                weights[i] = edges[i].Weight;
            }

            return (heads, tails, weights);
        }

        private static float[,] InitializeEmbeddingSpectralLike(int n, int[] heads, int[] tails, float[] weights, int randomState, CancellationToken token)
        {
            var degree = new double[n];
            for (int e = 0; e < weights.Length; e++)
            {
                degree[heads[e]] += weights[e];
            }

            double[] MultiplyNormalizedAdjacency(double[] vector)
            {
                var result = new double[n];
                for (int e = 0; e < weights.Length; e++)
                {
                    int i = heads[e];
                    int j = tails[e];
                    double di = degree[i];
                    double dj = degree[j];
                    if (di <= 0 || dj <= 0)
                    {
                        continue;
                    }

                    double w = weights[e] / Math.Sqrt(di * dj);
                    result[i] += w * vector[j];
                }
                return result;
            }

            static void Normalize(double[] vector)
            {
                double norm = 0;
                for (int i = 0; i < vector.Length; i++)
                {
                    norm += vector[i] * vector[i];
                }

                norm = Math.Sqrt(norm);
                if (norm <= 0)
                {
                    return;
                }

                for (int i = 0; i < vector.Length; i++)
                {
                    vector[i] /= norm;
                }
            }

            static double Dot(double[] a, double[] b)
            {
                double sum = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    sum += a[i] * b[i];
                }
                return sum;
            }

            static void OrthogonalizeInPlace(double[] vector, List<double[]> basis)
            {
                for (int i = 0; i < basis.Count; i++)
                {
                    var b = basis[i];
                    double proj = Dot(vector, b);
                    for (int t = 0; t < vector.Length; t++)
                    {
                        vector[t] -= proj * b[t];
                    }
                }
            }

            double[] RandomVector(NumpyRandom rng)
            {
                var v = new double[n];
                for (int i = 0; i < n; i++)
                {
                    v[i] = rng.NextDouble() - 0.5;
                }
                return v;
            }

            double[] PowerIteration(List<double[]> basis, NumpyRandom rng, int iterations)
            {
                var v = RandomVector(rng);
                OrthogonalizeInPlace(v, basis);
                Normalize(v);

                for (int it = 0; it < iterations; it++)
                {
                    token.ThrowIfCancellationRequested();
                    v = MultiplyNormalizedAdjacency(v);
                    OrthogonalizeInPlace(v, basis);
                    Normalize(v);
                }

                basis.Add(v);
                return v;
            }

            var rng = new NumpyRandom(randomState);
            var basis = new List<double[]>(capacity: 3);


            _ = PowerIteration(basis, rng, iterations: 50);
            var v2 = PowerIteration(basis, rng, iterations: 50);
            var v3 = PowerIteration(basis, rng, iterations: 50);

            var embedding = new float[n, 2];
            for (int i = 0; i < n; i++)
            {
                embedding[i, 0] = (float)v2[i];
                embedding[i, 1] = (float)v3[i];
            }

            return embedding;
        }

        private static void OptimizeLayout(float[,] embedding, int[] heads, int[] tails, float[] weights, int nEpochs, double a, double b, Options options, CancellationToken token, Action<double>? progress)
        {
            int nEdges = weights.Length;
            if (nEdges == 0)
            {
                return;
            }

            var rng = new NumpyRandom(options.RandomState);

            var epochsPerSample = new float[nEdges];
            var epochOfNextSample = new float[nEdges];
            var epochsPerNegativeSample = new float[nEdges];
            var epochOfNextNegativeSample = new float[nEdges];

            float maxWeight = 0f;
            for (int i = 0; i < nEdges; i++)
            {
                if (weights[i] > maxWeight)
                {
                    maxWeight = weights[i];
                }
            }
            if (maxWeight <= 0)
            {
                return;
            }

            for (int i = 0; i < nEdges; i++)
            {
                float w = weights[i];
                if (w <= 0)
                {
                    epochsPerSample[i] = -1;
                    epochOfNextSample[i] = float.PositiveInfinity;
                    epochsPerNegativeSample[i] = -1;
                    epochOfNextNegativeSample[i] = float.PositiveInfinity;
                    continue;
                }


                float wScaled = w / maxWeight;
                epochsPerSample[i] = 1f / wScaled;


                epochOfNextSample[i] = epochsPerSample[i];
            }

            int nSamples = embedding.GetLength(0);
            float learningRate = options.LearningRate;
            float gamma = options.RepulsionStrength;
            int negativeSampleRate = Math.Max(1, options.NegativeSampleRate);

            for (int i = 0; i < nEdges; i++)
            {
                float eps = epochsPerSample[i];
                if (eps > 0 && !float.IsInfinity(eps))
                {
                    epochsPerNegativeSample[i] = eps / negativeSampleRate;
                    epochOfNextNegativeSample[i] = epochsPerNegativeSample[i];
                }
                else
                {
                    epochsPerNegativeSample[i] = -1;
                    epochOfNextNegativeSample[i] = float.PositiveInfinity;
                }
            }

            for (int epoch = 0; epoch < nEpochs; epoch++)
            {
                token.ThrowIfCancellationRequested();

                float alpha = learningRate * (1f - (float)epoch / nEpochs);

                for (int e = 0; e < nEdges; e++)
                {
                    float eps = epochsPerSample[e];
                    if (eps <= 0)
                    {
                        continue;
                    }

                    if (epochOfNextSample[e] > epoch)
                    {
                        continue;
                    }

                    int i = heads[e];
                    int j = tails[e];


                    ApplyAttraction(embedding, i, j, alpha, a, b);
                    epochOfNextSample[e] += eps;


                    float epsNeg = epochsPerNegativeSample[e];
                    if (epsNeg > 0 && !float.IsInfinity(epsNeg))
                    {
                        int nNegSamples = (int)(((float)epoch - epochOfNextNegativeSample[e]) / epsNeg);
                        if (nNegSamples > 0)
                        {
                            for (int t = 0; t < nNegSamples; t++)
                            {
                                int k = rng.NextInt(nSamples);
                                if (k == i)
                                {
                                    continue;
                                }
                                ApplyRepulsion(embedding, i, k, alpha, a, b, gamma);
                            }

                            epochOfNextNegativeSample[e] += nNegSamples * epsNeg;
                        }
                    }
                }

                progress?.Invoke((double)(epoch + 1) / nEpochs);
            }
        }

        private static void ApplyAttraction(float[,] embedding, int head, int tail, float alpha, double a, double b)
        {
            float hx = embedding[head, 0];
            float hy = embedding[head, 1];
            float tx = embedding[tail, 0];
            float ty = embedding[tail, 1];

            double dx = hx - tx;
            double dy = hy - ty;
            double distSquared = dx * dx + dy * dy;
            if (distSquared <= 0)
            {
                return;
            }

            double distPowB = Math.Pow(distSquared, b);
            double gradCoeff = (-2.0 * a * b * Math.Pow(distSquared, b - 1.0)) / (a * distPowB + 1.0);

            double gx = gradCoeff * dx;
            double gy = gradCoeff * dy;

            gx = Math.Clamp(gx, -4.0, 4.0);
            gy = Math.Clamp(gy, -4.0, 4.0);

            embedding[head, 0] = (float)(hx + gx * alpha);
            embedding[head, 1] = (float)(hy + gy * alpha);


            embedding[tail, 0] = (float)(tx - gx * alpha);
            embedding[tail, 1] = (float)(ty - gy * alpha);
        }

        private static void ApplyRepulsion(float[,] embedding, int head, int other, float alpha, double a, double b, float gamma)
        {
            float hx = embedding[head, 0];
            float hy = embedding[head, 1];
            float ox = embedding[other, 0];
            float oy = embedding[other, 1];

            double dx = hx - ox;
            double dy = hy - oy;
            double distSquared = dx * dx + dy * dy;
            if (distSquared <= 0)
            {
                return;
            }

            double distPowB = Math.Pow(distSquared, b);
            double gradCoeff = (2.0 * gamma * b) / ((0.001 + distSquared) * (a * distPowB + 1.0));

            double gx = gradCoeff * dx;
            double gy = gradCoeff * dy;

            gx = Math.Clamp(gx, -4.0, 4.0);
            gy = Math.Clamp(gy, -4.0, 4.0);

            embedding[head, 0] = (float)(hx + gx * alpha);
            embedding[head, 1] = (float)(hy + gy * alpha);
        }
    }
}
