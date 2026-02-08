using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{




    public static class HdbscanClusterer
    {
        public static int[] Cluster(List<float[]> data, int minClusterSize)
        {
            if (data == null || data.Count == 0)
            {
                return Array.Empty<int>();
            }

            if (minClusterSize <= 1)
            {

                return Enumerable.Repeat(0, data.Count).ToArray();
            }

            int n = data.Count;
            if (n < minClusterSize)
            {
                return Enumerable.Repeat(-1, n).ToArray();
            }

            for (int i = 0; i < n; i++)
            {
                if (data[i] == null || data[i].Length != 2)
                {
                    throw new ArgumentException("HDBSCAN 仅支持 2D 输入。", nameof(data));
                }
            }

            int minSamples = minClusterSize;


            var xs = new double[n];
            var ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = data[i][0];
                ys[i] = data[i][1];
            }


            var coreDist = ComputeCoreDistances(xs, ys, minSamples);


            var mstEdges = BuildMutualReachabilityMst(xs, ys, coreDist);


            var tree = BuildLinkageTree(n, mstEdges);


            var condensed = CondenseTree(tree, minClusterSize);


            var selected = SelectClustersEom(condensed);


            return AssignLabels(condensed, selected, n);
        }

        #region Core distances

        private static double[] ComputeCoreDistances(double[] xs, double[] ys, int minSamples)
        {
            int n = xs.Length;
            var core = new double[n];
            var tmp = new double[n - 1];

            for (int i = 0; i < n; i++)
            {
                int idx = 0;
                double xi = xs[i];
                double yi = ys[i];
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double dx = xi - xs[j];
                    double dy = yi - ys[j];
                    tmp[idx++] = Math.Sqrt(dx * dx + dy * dy);
                }

                Array.Sort(tmp, 0, idx);


                int pick = Math.Min(Math.Max(minSamples - 2, 0), idx - 1);
                core[i] = tmp[pick];
            }

            return core;
        }

        #endregion

        #region MST (Prim)

        private readonly record struct Edge(int From, int To, double Weight);

        private static List<Edge> BuildMutualReachabilityMst(double[] xs, double[] ys, double[] coreDist)
        {
            int n = xs.Length;
            var inMst = new bool[n];
            var bestCost = new double[n];
            var parent = new int[n];

            for (int i = 0; i < n; i++)
            {
                bestCost[i] = double.PositiveInfinity;
                parent[i] = -1;
            }

            inMst[0] = true;
            for (int v = 1; v < n; v++)
            {
                bestCost[v] = MutualReachability(0, v, xs, ys, coreDist);
                parent[v] = 0;
            }

            var edges = new List<Edge>(n - 1);

            for (int iter = 0; iter < n - 1; iter++)
            {
                int next = -1;
                double nextCost = double.PositiveInfinity;

                for (int v = 0; v < n; v++)
                {
                    if (!inMst[v] && bestCost[v] < nextCost)
                    {
                        nextCost = bestCost[v];
                        next = v;
                    }
                }

                if (next < 0)
                {
                    break;
                }

                inMst[next] = true;
                edges.Add(new Edge(parent[next], next, nextCost));

                for (int v = 0; v < n; v++)
                {
                    if (inMst[v]) continue;
                    double cost = MutualReachability(next, v, xs, ys, coreDist);
                    if (cost < bestCost[v])
                    {
                        bestCost[v] = cost;
                        parent[v] = next;
                    }
                }
            }

            edges.Sort(static (a, b) =>
            {
                int c = a.Weight.CompareTo(b.Weight);
                if (c != 0) return c;

                int aMin = a.From < a.To ? a.From : a.To;
                int aMax = a.From < a.To ? a.To : a.From;
                int bMin = b.From < b.To ? b.From : b.To;
                int bMax = b.From < b.To ? b.To : b.From;

                c = aMin.CompareTo(bMin);
                return c != 0 ? c : aMax.CompareTo(bMax);
            });
            return edges;
        }

        private static double MutualReachability(int i, int j, double[] xs, double[] ys, double[] coreDist)
        {
            double dx = xs[i] - xs[j];
            double dy = ys[i] - ys[j];
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double cd = coreDist[i] > coreDist[j] ? coreDist[i] : coreDist[j];
            return dist > cd ? dist : cd;
        }

        #endregion

        #region Linkage tree

        private sealed class LinkageTree
        {
            public int NPoints { get; init; }
            public int TotalNodes => 2 * NPoints - 1;
            public int RootNodeId => 2 * NPoints - 2;

            public int[] LeftChild { get; init; } = Array.Empty<int>();
            public int[] RightChild { get; init; } = Array.Empty<int>();
            public double[] Distance { get; init; } = Array.Empty<double>();
            public int[] Size { get; init; } = Array.Empty<int>();
        }

        private static LinkageTree BuildLinkageTree(int nPoints, List<Edge> mstEdges)
        {

            var parent = new int[nPoints];
            var rank = new int[nPoints];
            var compNodeId = new int[nPoints];
            var compSize = new int[nPoints];

            for (int i = 0; i < nPoints; i++)
            {
                parent[i] = i;
                rank[i] = 0;
                compNodeId[i] = i;
                compSize[i] = 1;
            }

            int totalNodes = 2 * nPoints - 1;
            var left = new int[totalNodes];
            var right = new int[totalNodes];
            var dist = new double[totalNodes];
            var size = new int[totalNodes];

            for (int i = 0; i < nPoints; i++)
            {
                left[i] = -1;
                right[i] = -1;
                dist[i] = 0;
                size[i] = 1;
            }

            int mergeIndex = 0;
            foreach (var edge in mstEdges)
            {
                int r1 = Find(parent, edge.From);
                int r2 = Find(parent, edge.To);
                if (r1 == r2)
                {
                    continue;
                }

                int node1 = compNodeId[r1];
                int node2 = compNodeId[r2];
                int newNodeId = nPoints + mergeIndex;
                int newSize = compSize[r1] + compSize[r2];

                left[newNodeId] = node1;
                right[newNodeId] = node2;
                dist[newNodeId] = edge.Weight;
                size[newNodeId] = newSize;

                int newRoot = Union(parent, rank, r1, r2);
                compNodeId[newRoot] = newNodeId;
                compSize[newRoot] = newSize;
                mergeIndex++;

                if (mergeIndex == nPoints - 1)
                {
                    break;
                }
            }

            return new LinkageTree
            {
                NPoints = nPoints,
                LeftChild = left,
                RightChild = right,
                Distance = dist,
                Size = size
            };
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static int Union(int[] parent, int[] rank, int a, int b)
        {
            a = Find(parent, a);
            b = Find(parent, b);
            if (a == b) return a;

            if (rank[a] < rank[b])
            {
                parent[a] = b;
                return b;
            }

            if (rank[a] > rank[b])
            {
                parent[b] = a;
                return a;
            }


            if (a <= b)
            {
                parent[b] = a;
                rank[a]++;
                return a;
            }

            parent[a] = b;
            rank[b]++;
            return b;
        }

        #endregion

        #region Condensed tree

        private readonly record struct CondensedEdge(int ParentCluster, int ChildId, bool ChildIsPoint, double Lambda, int ChildSize);

        private sealed class CondensedTree
        {
            public int NPoints { get; init; }
            public int ClusterCount { get; init; }
            public double[] BirthLambda { get; init; } = Array.Empty<double>();
            public int[] ClusterSize { get; init; } = Array.Empty<int>();

            public List<CondensedEdge> Edges { get; init; } = new();
            public List<int>[] ChildClusters { get; init; } = Array.Empty<List<int>>();
            public List<int>[] ChildPoints { get; init; } = Array.Empty<List<int>>();
            public double[] Stability { get; init; } = Array.Empty<double>();
        }

        private static CondensedTree CondenseTree(LinkageTree tree, int minClusterSize)
        {
            int n = tree.NPoints;
            int totalNodes = tree.TotalNodes;

            var nodeToCluster = new int[totalNodes];
            Array.Fill(nodeToCluster, -1);

            var birthLambda = new List<double> { 0.0 };
            var clusterSizes = new List<int> { tree.Size[tree.RootNodeId] };

            var edges = new List<CondensedEdge>();

            int nextClusterLabel = 0;
            int rootNode = tree.RootNodeId;
            nodeToCluster[rootNode] = 0;

            var stack = new Stack<int>();
            stack.Push(rootNode);

            while (stack.Count > 0)
            {
                int node = stack.Pop();
                int parentCluster = nodeToCluster[node];

                int left = tree.LeftChild[node];
                int right = tree.RightChild[node];
                if (left < 0 && right < 0)
                {
                    continue;
                }

                double lambda = tree.Distance[node] > 0 ? 1.0 / tree.Distance[node] : double.PositiveInfinity;


                if (right >= 0)
                {
                    ProcessChild(node, right, parentCluster, lambda);
                }
                if (left >= 0)
                {
                    ProcessChild(node, left, parentCluster, lambda);
                }
            }

            int clusterCount = nextClusterLabel + 1;
            var childClusters = new List<int>[clusterCount];
            var childPoints = new List<int>[clusterCount];
            for (int i = 0; i < clusterCount; i++)
            {
                childClusters[i] = new List<int>();
                childPoints[i] = new List<int>();
            }

            var stability = new double[clusterCount];
            var birthArr = birthLambda.ToArray();

            foreach (var e in edges)
            {
                if (!e.ChildIsPoint)
                {
                    childClusters[e.ParentCluster].Add(e.ChildId);
                }
                else
                {
                    childPoints[e.ParentCluster].Add(e.ChildId);
                }

                double birth = birthArr[e.ParentCluster];
                stability[e.ParentCluster] += (e.Lambda - birth) * e.ChildSize;
            }

            return new CondensedTree
            {
                NPoints = n,
                ClusterCount = clusterCount,
                BirthLambda = birthArr,
                ClusterSize = clusterSizes.ToArray(),
                Edges = edges,
                ChildClusters = childClusters,
                ChildPoints = childPoints,
                Stability = stability
            };

            void ProcessChild(int parentNode, int childNode, int parentCluster, double lambda)
            {
                _ = parentNode;
                int childSize = childNode < n ? 1 : tree.Size[childNode];

                if (childSize >= minClusterSize)
                {
                    int childClusterLabel = ++nextClusterLabel;
                    if (childClusterLabel >= birthLambda.Count)
                    {
                        birthLambda.Add(lambda);
                        clusterSizes.Add(childSize);
                    }
                    else
                    {
                        birthLambda[childClusterLabel] = lambda;
                        clusterSizes[childClusterLabel] = childSize;
                    }

                    nodeToCluster[childNode] = childClusterLabel;
                    edges.Add(new CondensedEdge(parentCluster, childClusterLabel, ChildIsPoint: false, lambda, childSize));
                    stack.Push(childNode);
                }
                else
                {
                    foreach (var point in EnumerateLeaves(childNode, tree.LeftChild, tree.RightChild, n))
                    {
                        edges.Add(new CondensedEdge(parentCluster, point, ChildIsPoint: true, lambda, 1));
                    }
                }
            }
        }

        private static IEnumerable<int> EnumerateLeaves(int nodeId, int[] left, int[] right, int nPoints)
        {
            var stack = new Stack<int>();
            stack.Push(nodeId);
            while (stack.Count > 0)
            {
                int node = stack.Pop();
                if (node < nPoints)
                {
                    yield return node;
                    continue;
                }

                int l = left[node];
                int r = right[node];
                if (r >= 0) stack.Push(r);
                if (l >= 0) stack.Push(l);
            }
        }

        #endregion

        #region Cluster selection (EOM)

        private static bool[] SelectClustersEom(CondensedTree tree)
        {
            var selected = new bool[tree.ClusterCount];


            foreach (var child in tree.ChildClusters[0])
            {
                SelectRecursive(child);
            }

            selected[0] = false;
            return selected;

            double SelectRecursive(int cluster)
            {
                var children = tree.ChildClusters[cluster];
                if (children.Count == 0)
                {
                    selected[cluster] = true;
                    return tree.Stability[cluster];
                }

                double sumChildren = 0;
                foreach (var child in children)
                {
                    sumChildren += SelectRecursive(child);
                }

                if (tree.Stability[cluster] >= sumChildren)
                {
                    selected[cluster] = true;
                    foreach (var child in children)
                    {
                        UnselectAll(child);
                    }
                    return tree.Stability[cluster];
                }

                selected[cluster] = false;
                return sumChildren;
            }

            void UnselectAll(int cluster)
            {
                selected[cluster] = false;
                foreach (var child in tree.ChildClusters[cluster])
                {
                    UnselectAll(child);
                }
            }
        }

        #endregion

        #region Labels

        private static int[] AssignLabels(CondensedTree tree, bool[] selectedClusters, int nPoints)
        {
            var labels = Enumerable.Repeat(-1, nPoints).ToArray();

            var selected = new List<int>();
            for (int i = 1; i < selectedClusters.Length; i++)
            {
                if (selectedClusters[i])
                {
                    selected.Add(i);
                }
            }

            if (selected.Count == 0)
            {
                return labels;
            }

            selected.Sort();
            var map = new Dictionary<int, int>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                map[selected[i]] = i;
            }

            foreach (var cluster in selected)
            {
                int outLabel = map[cluster];
                var stack = new Stack<int>();
                stack.Push(cluster);
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();

                    foreach (var p in tree.ChildPoints[cur])
                    {
                        labels[p] = outLabel;
                    }

                    foreach (var child in tree.ChildClusters[cur])
                    {
                        stack.Push(child);
                    }
                }
            }

            return labels;
        }

        #endregion
    }
}
