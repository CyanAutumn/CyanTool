using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public static class SklearnKMeansClusterer
    {
        public static int[] Cluster(IReadOnlyList<float[]> data2d, int numClusters, int randomState = 42)
        {
            if (data2d == null || data2d.Count == 0)
            {
                return Array.Empty<int>();
            }

            int n = data2d.Count;
            if (numClusters <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(numClusters), "numClusters must be > 0.");
            }

            if (numClusters > n)
            {
                return Enumerable.Range(0, n).ToArray();
            }

            for (int i = 0; i < n; i++)
            {
                if (data2d[i] == null || data2d[i].Length != 2)
                {
                    throw new ArgumentException("KMeans 仅支持 2D 输入。", nameof(data2d));
                }
            }

            var rng = new NumpyRandom(randomState);


            var (centers, labels) = RunKMeans(data2d, numClusters, rng);
            _ = centers;
            return labels;
        }

        private static (double[,] centers, int[] labels) RunKMeans(IReadOnlyList<float[]> data2d, int k, NumpyRandom rng)
        {
            int n = data2d.Count;
            int maxIter = 300;
            double tol = 1e-4;

            int[] initialCenterIndices = InitKMeansPlusPlus(data2d, k, rng);

            var centers = new double[k, 2];
            for (int c = 0; c < k; c++)
            {
                var p = data2d[initialCenterIndices[c]];
                centers[c, 0] = p[0];
                centers[c, 1] = p[1];
            }

            var labels = new int[n];
            var distsToAssigned = new double[n];

            for (int iter = 0; iter < maxIter; iter++)
            {

                Array.Fill(distsToAssigned, 0);
                for (int i = 0; i < n; i++)
                {
                    var p = data2d[i];
                    double bestDist = double.PositiveInfinity;
                    int bestCluster = 0;
                    for (int c = 0; c < k; c++)
                    {
                        double dx = p[0] - centers[c, 0];
                        double dy = p[1] - centers[c, 1];
                        double dist = dx * dx + dy * dy;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestCluster = c;
                        }
                    }
                    labels[i] = bestCluster;
                    distsToAssigned[i] = bestDist;
                }


                var sumX = new double[k];
                var sumY = new double[k];
                var counts = new int[k];
                for (int i = 0; i < n; i++)
                {
                    int c = labels[i];
                    counts[c]++;
                    sumX[c] += data2d[i][0];
                    sumY[c] += data2d[i][1];
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
                        sumX[oldCluster] -= data2d[farthestIndex][0];
                        sumY[oldCluster] -= data2d[farthestIndex][1];
                    }

                    labels[farthestIndex] = c;
                    counts[c] = 1;
                    sumX[c] = data2d[farthestIndex][0];
                    sumY[c] = data2d[farthestIndex][1];
                    distsToAssigned[farthestIndex] = 0;
                }

                double shift = 0;
                for (int c = 0; c < k; c++)
                {
                    double newX = counts[c] > 0 ? sumX[c] / counts[c] : centers[c, 0];
                    double newY = counts[c] > 0 ? sumY[c] / counts[c] : centers[c, 1];

                    double dx = centers[c, 0] - newX;
                    double dy = centers[c, 1] - newY;
                    shift += dx * dx + dy * dy;

                    centers[c, 0] = newX;
                    centers[c, 1] = newY;
                }

                if (shift <= tol)
                {
                    break;
                }
            }

            return (centers, labels);
        }

        private static int[] InitKMeansPlusPlus(IReadOnlyList<float[]> data2d, int k, NumpyRandom rng)
        {
            int n = data2d.Count;

            var centers = new int[k];
            centers[0] = rng.NextInt(n);

            var closestDistSq = new double[n];
            for (int i = 0; i < n; i++)
            {
                closestDistSq[i] = SquaredDistance(data2d[i], data2d[centers[0]]);
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
                        double d = SquaredDistance(data2d[i], data2d[candidate]);
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
            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            return dx * dx + dy * dy;
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

