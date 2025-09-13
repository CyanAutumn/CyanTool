using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils {
    public static class HdbscanClusterer {
        public static int[] Cluster(List<float[]> data, int minClusterSize) {
            if (data == null || data.Count == 0)
                return new int[0];

            if (data.Count < minClusterSize)
                return Enumerable.Repeat(-1, data.Count).ToArray();

            var points = data.Select((d, i) => new HdbscanPoint(d, i)).ToList();
            var distances = CalculateDistances(points);
            var coreDistances = CalculateCoreDistances(distances, minClusterSize);
            var mutualReachabilityDistances = CalculateMutualReachability(distances, coreDistances);
            var mst = BuildMst(mutualReachabilityDistances, points.Count);


            mst.Sort((a, b) => b.Weight.CompareTo(a.Weight));

            var hierarchy = BuildHierarchy(mst, points.Count);
            var clusters = ExtractClusters(hierarchy, minClusterSize);
            return AssignClusters(points, clusters, hierarchy);
        }

        private static double[,] CalculateDistances(List<HdbscanPoint> points) {
            int numPoints = points.Count;
            double[,] distances = new double[numPoints, numPoints];
            for (int i = 0; i < numPoints; i++) {
                for (int j = i; j < numPoints; j++) {
                    if (i == j) {
                        distances[i, j] = 0;
                    }
                    else {
                        double dist = EuclideanDistance(points[i].Features, points[j].Features);
                        distances[i, j] = dist;
                        distances[j, i] = dist;
                    }
                }
            }
            return distances;
        }

        private static double EuclideanDistance(float[] a, float[] b) {
            double sum = 0;
            for (int i = 0; i < a.Length; i++) {
                sum += (a[i] - b[i]) * (a[i] - b[i]);
            }
            return Math.Sqrt(sum);
        }

        private static double[] CalculateCoreDistances(double[,] distances, int k) {
            int numPoints = distances.GetLength(0);
            double[] coreDistances = new double[numPoints];

            for (int i = 0; i < numPoints; i++) {
                var sortedDistances = new List<double>();
                for (int j = 0; j < numPoints; j++) {
                    if (i != j)
                        sortedDistances.Add(distances[i, j]);
                }
                sortedDistances.Sort();


                coreDistances[i] = k - 1 < sortedDistances.Count ? sortedDistances[k - 1] : double.MaxValue;
            }
            return coreDistances;
        }

        private static double[,] CalculateMutualReachability(double[,] distances, double[] coreDistances) {
            int numPoints = distances.GetLength(0);
            double[,] mrd = new double[numPoints, numPoints];

            for (int i = 0; i < numPoints; i++) {
                for (int j = i; j < numPoints; j++) {
                    if (i == j) {
                        mrd[i, j] = 0;
                    }
                    else {
                        double val = Math.Max(coreDistances[i], Math.Max(coreDistances[j], distances[i, j]));
                        mrd[i, j] = val;
                        mrd[j, i] = val;
                    }
                }
            }
            return mrd;
        }

        private static List<HdbscanEdge> BuildMst(double[,] mrd, int numPoints) {
            var edges = new List<HdbscanEdge>();
            for (int i = 0; i < numPoints; i++) {
                for (int j = i + 1; j < numPoints; j++) {
                    edges.Add(new HdbscanEdge(i, j, mrd[i, j]));
                }
            }
            edges.Sort((a, b) => a.Weight.CompareTo(b.Weight));

            var mst = new List<HdbscanEdge>();
            var disjointSet = new DisjointSet(numPoints);

            foreach (var edge in edges) {
                if (disjointSet.Find(edge.From) != disjointSet.Find(edge.To)) {
                    mst.Add(edge);
                    disjointSet.Union(edge.From, edge.To);
                    if (mst.Count == numPoints - 1)
                        break;
                }
            }
            return mst;
        }

        private static HierarchyNode BuildHierarchy(List<HdbscanEdge> mst, int numPoints) {
            var nodes = new Dictionary<int, HierarchyNode>();


            for (int i = 0; i < numPoints; i++) {
                nodes[i] = new HierarchyNode(i, 1, 0);
            }

            var disjointSet = new DisjointSet(numPoints);
            int nextClusterId = numPoints;


            foreach (var edge in mst.OrderBy(e => e.Weight)) {
                int root1 = disjointSet.Find(edge.From);
                int root2 = disjointSet.Find(edge.To);

                if (root1 != root2) {
                    var node1 = nodes[root1];
                    var node2 = nodes[root2];


                    var parentNode = new HierarchyNode(
                        nextClusterId++,
                        node1.Size + node2.Size,
                        edge.Weight
                    );

                    parentNode.Children.Add(node1);
                    parentNode.Children.Add(node2);
                    node1.Parent = parentNode;
                    node2.Parent = parentNode;


                    disjointSet.Union(root1, root2);
                    int newRoot = disjointSet.Find(root1);
                    nodes[newRoot] = parentNode;
                }
            }


            return nodes.Values.FirstOrDefault(n => n.Parent == null) ?? nodes[0];
        }

        private static List<HierarchyNode> ExtractClusters(HierarchyNode root, int minClusterSize) {
            var clusters = new List<HierarchyNode>();
            var toProcess = new Queue<HierarchyNode>();
            toProcess.Enqueue(root);

            while (toProcess.Count > 0) {
                var node = toProcess.Dequeue();

                if (node.Size < minClusterSize) {

                    continue;
                }

                if (node.Children.Count == 0) {

                    if (node.Size >= minClusterSize)
                        clusters.Add(node);
                }
                else {

                    bool hasValidChildren = node.Children.Any(c => c.Size >= minClusterSize);

                    if (!hasValidChildren) {

                        clusters.Add(node);
                    }
                    else {

                        foreach (var child in node.Children) {
                            toProcess.Enqueue(child);
                        }
                    }
                }
            }

            return clusters;
        }

        private static int[] AssignClusters(List<HdbscanPoint> points, List<HierarchyNode> clusters, HierarchyNode root) {
            var assignments = new int[points.Count];
            Array.Fill(assignments, -1);

            int clusterId = 0;
            foreach (var cluster in clusters) {
                var pointIndices = GetPointsInNode(cluster);
                foreach (var idx in pointIndices) {
                    if (idx < assignments.Length)
                        assignments[idx] = clusterId;
                }
                clusterId++;
            }

            return assignments;
        }

        private static List<int> GetPointsInNode(HierarchyNode node) {
            var points = new List<int>();
            var toProcess = new Queue<HierarchyNode>();
            toProcess.Enqueue(node);

            while (toProcess.Count > 0) {
                var current = toProcess.Dequeue();

                if (current.Children.Count == 0) {

                    if (current.Id < int.MaxValue / 2)
                        points.Add(current.Id);
                }
                else {
                    foreach (var child in current.Children) {
                        toProcess.Enqueue(child);
                    }
                }
            }

            return points;
        }


        private class HierarchyNode {
            public int Id { get; }
            public int Size { get; }
            public double Lambda { get; }
            public HierarchyNode Parent { get; set; }
            public List<HierarchyNode> Children { get; }

            public HierarchyNode(int id, int size, double lambda) {
                Id = id;
                Size = size;
                Lambda = lambda;
                Children = new List<HierarchyNode>();
            }
        }
    }


    public class HdbscanPoint {
        public float[] Features { get; }
        public int Id { get; }
        public HdbscanPoint(float[] features, int id) {
            Features = features;
            Id = id;
        }
    }

    public class HdbscanEdge {
        public int From { get; }
        public int To { get; }
        public double Weight { get; }
        public HdbscanEdge(int from, int to, double weight) {
            From = from;
            To = to;
            Weight = weight;
        }
    }

    public class DisjointSet {
        private int[] parent;
        private int[] rank;

        public DisjointSet(int size) {
            parent = new int[size];
            rank = new int[size];
            for (int i = 0; i < size; i++) {
                parent[i] = i;
                rank[i] = 0;
            }
        }

        public int Find(int i) {
            if (parent[i] != i)
                parent[i] = Find(parent[i]);
            return parent[i];
        }

        public void Union(int i, int j) {
            int rootI = Find(i);
            int rootJ = Find(j);

            if (rootI != rootJ) {

                if (rank[rootI] < rank[rootJ]) {
                    parent[rootI] = rootJ;
                }
                else if (rank[rootI] > rank[rootJ]) {
                    parent[rootJ] = rootI;
                }
                else {
                    parent[rootJ] = rootI;
                    rank[rootI]++;
                }
            }
        }
    }
}