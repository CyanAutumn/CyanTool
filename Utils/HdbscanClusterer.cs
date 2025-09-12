
using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public static class HdbscanClusterer
    {
        public static int[] Cluster(List<float[]> data, int minClusterSize)
        {
            var points = data.Select((d, i) => new HdbscanPoint(d, i)).ToList();
            var distances = CalculateDistances(points);
            var coreDistances = CalculateCoreDistances(distances, minClusterSize);
            var mutualReachabilityDistances = CalculateMutualReachability(distances, coreDistances);
            var mst = BuildMst(mutualReachabilityDistances, points.Count);
            var clusters = BuildHierarchyAndExtractClusters(mst, minClusterSize);
            return AssignClusters(points, clusters);
        }

        private static double[,] CalculateDistances(List<HdbscanPoint> points)
        {
            int numPoints = points.Count;
            double[,] distances = new double[numPoints, numPoints];
            for (int i = 0; i < numPoints; i++)
            {
                for (int j = i; j < numPoints; j++)
                {
                    double dist = EuclideanDistance(points[i].Features, points[j].Features);
                    distances[i, j] = dist;
                    distances[j, i] = dist;
                }
            }
            return distances;
        }

        private static double EuclideanDistance(float[] a, float[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                sum += (a[i] - b[i]) * (a[i] - b[i]);
            }
            return Math.Sqrt(sum);
        }

        private static double[] CalculateCoreDistances(double[,] distances, int k)
        {
            int numPoints = distances.GetLength(0);
            double[] coreDistances = new double[numPoints];
            for (int i = 0; i < numPoints; i++)
            {
                var sortedDistances = new List<double>();
                for (int j = 0; j < numPoints; j++)
                {
                    sortedDistances.Add(distances[i, j]);
                }
                sortedDistances.Sort();
                coreDistances[i] = sortedDistances[k - 1];
            }
            return coreDistances;
        }

        private static double[,] CalculateMutualReachability(double[,] distances, double[] coreDistances)
        {
            int numPoints = distances.GetLength(0);
            double[,] mrd = new double[numPoints, numPoints];
            for (int i = 0; i < numPoints; i++)
            {
                for (int j = i; j < numPoints; j++)
                {
                    double val = Math.Max(coreDistances[i], Math.Max(coreDistances[j], distances[i, j]));
                    mrd[i, j] = val;
                    mrd[j, i] = val;
                }
            }
            return mrd;
        }

        private static List<HdbscanEdge> BuildMst(double[,] mrd, int numPoints)
        {
            var edges = new List<HdbscanEdge>();
            for (int i = 0; i < numPoints; i++)
            {
                for (int j = i + 1; j < numPoints; j++)
                {
                    edges.Add(new HdbscanEdge(i, j, mrd[i, j]));
                }
            }
            edges.Sort((a, b) => a.Weight.CompareTo(b.Weight));

            var mst = new List<HdbscanEdge>();
            var disjointSet = new DisjointSet(numPoints);
            foreach (var edge in edges)
            {
                if (disjointSet.Find(edge.From) != disjointSet.Find(edge.To))
                {
                    mst.Add(edge);
                    disjointSet.Union(edge.From, edge.To);
                }
            }
            return mst;
        }

        private static List<HdbscanCluster> BuildHierarchyAndExtractClusters(List<HdbscanEdge> mst, int minClusterSize)
        {
            var clusters = new List<HdbscanCluster>();
            var disjointSet = new DisjointSet(mst.Count + 1);
            var clusterMap = new Dictionary<int, HdbscanCluster>();

            for (int i = 0; i < mst.Count + 1; i++)
            {
                var cluster = new HdbscanCluster(i);
                clusters.Add(cluster);
                clusterMap[i] = cluster;
            }

            foreach (var edge in mst)
            {
                int root1 = disjointSet.Find(edge.From);
                int root2 = disjointSet.Find(edge.To);

                if (root1 != root2)
                {
                    var cluster1 = clusterMap[root1];
                    var cluster2 = clusterMap[root2];

                    if (cluster1.Size + cluster2.Size < minClusterSize)
                    {
                        disjointSet.Union(root1, root2);
                        int newRoot = disjointSet.Find(root1);
                        var newCluster = clusterMap[newRoot];
                        newCluster.Size += cluster1.Size + cluster2.Size;
                        newCluster.Stability += (edge.Weight - cluster1.DeathLevel) * cluster1.Size;
                        newCluster.Stability += (edge.Weight - cluster2.DeathLevel) * cluster2.Size;
                        cluster1.Parent = newCluster;
                        cluster2.Parent = newCluster;
                        cluster1.DeathLevel = edge.Weight;
                        cluster2.DeathLevel = edge.Weight;
                    }
                }
            }
            return clusters.Where(c => c.Size >= minClusterSize && c.Parent == null).ToList();
        }


        private static int[] AssignClusters(List<HdbscanPoint> points, List<HdbscanCluster> clusters)
        {
            var assignments = new int[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                assignments[i] = -1;
            }

            int clusterId = 0;
            foreach (var cluster in clusters)
            {
                foreach (var point in GetPointsInCluster(cluster, points))
                {
                    assignments[point.Id] = clusterId;
                }
                clusterId++;
            }
            return assignments;
        }

        private static IEnumerable<HdbscanPoint> GetPointsInCluster(HdbscanCluster cluster, List<HdbscanPoint> allPoints)
        {
            var pointsInCluster = new List<HdbscanPoint>();
            var queue = new Queue<HdbscanCluster>();
            queue.Enqueue(cluster);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.Size == 1)
                {
                    pointsInCluster.Add(allPoints[current.Id]);
                }


            }





            if (cluster.Size > 0 && cluster.Size < allPoints.Count)
            {
                return allPoints.Take(cluster.Size);
            }
            return new List<HdbscanPoint>();
        }
    }

    public class HdbscanPoint
    {
        public float[] Features { get; }
        public int Id { get; }
        public HdbscanPoint(float[] features, int id)
        {
            Features = features;
            Id = id;
        }
    }

    public class HdbscanEdge
    {
        public int From { get; }
        public int To { get; }
        public double Weight { get; }
        public HdbscanEdge(int from, int to, double weight)
        {
            From = from;
            To = to;
            Weight = weight;
        }
    }

    public class HdbscanCluster
    {
        public int Id { get; }
        public int Size { get; set; }
        public double Stability { get; set; }
        public double BirthLevel { get; set; }
        public double DeathLevel { get; set; }
        public HdbscanCluster Parent { get; set; }
        public HdbscanCluster(int id)
        {
            Id = id;
            Size = 1;
            Stability = 0;
            BirthLevel = 0;
            DeathLevel = double.PositiveInfinity;
        }
    }

    public class DisjointSet
    {
        private int[] parent;
        public DisjointSet(int size)
        {
            parent = new int[size];
            for (int i = 0; i < size; i++)
            {
                parent[i] = i;
            }
        }

        public int Find(int i)
        {
            if (parent[i] == i)
                return i;
            return parent[i] = Find(parent[i]);
        }

        public void Union(int i, int j)
        {
            int rootI = Find(i);
            int rootJ = Find(j);
            if (rootI != rootJ)
            {
                parent[rootI] = rootJ;
            }
        }
    }
}
