using CyanTools.Utils;
using Xunit;

public sealed class AdaptiveClusterMergerTests
{
    [Fact]
    public void MergeNearbyClusters_CutsLargeCentroidGapWithCompleteLinkage()
    {
        var points = new List<float[]>
        {
            new[] { 0.00f, 0.00f },
            new[] { 0.02f, 0.00f },
            new[] { 0.05f, 0.00f },
            new[] { 0.07f, 0.00f },
            new[] { 3.00f, 0.00f },
            new[] { 3.02f, 0.00f },
            new[] { 3.05f, 0.00f },
            new[] { 3.07f, 0.00f }
        };
        int[] labels = { 0, 0, 1, 1, 2, 2, 3, 3 };

        var result = AdaptiveClusterMerger.MergeNearbyClusters(points, labels, minClustersToMerge: 4);

        Assert.Equal(4, result.OriginalClusterCount);
        Assert.Equal(2, result.FinalClusterCount);
        Assert.Equal(new[] { 0, 0, 0, 0, 1, 1, 1, 1 }, result.Labels);
    }

    [Fact]
    public void MergeNearbyClusters_DoesNotChainThroughIntermediateClusters()
    {
        var points = new List<float[]>
        {
            new[] { 0.00f, 0.00f },
            new[] { 0.02f, 0.00f },
            new[] { 0.30f, 0.00f },
            new[] { 0.32f, 0.00f },
            new[] { 0.60f, 0.00f },
            new[] { 0.62f, 0.00f },
            new[] { 1.30f, 0.00f },
            new[] { 1.32f, 0.00f }
        };
        int[] labels = { 0, 0, 1, 1, 2, 2, 3, 3 };

        var result = AdaptiveClusterMerger.MergeNearbyClusters(points, labels, minClustersToMerge: 4);

        Assert.Equal(4, result.OriginalClusterCount);
        Assert.Equal(3, result.FinalClusterCount);
    }

    [Fact]
    public void MergeNearbyClusters_KeepsClustersWhenNoStableGapExists()
    {
        var points = new List<float[]>
        {
            new[] { 0.00f, 0.00f },
            new[] { 0.02f, 0.00f },
            new[] { 1.00f, 0.00f },
            new[] { 1.02f, 0.00f },
            new[] { 2.00f, 0.00f },
            new[] { 2.02f, 0.00f },
            new[] { 3.00f, 0.00f },
            new[] { 3.02f, 0.00f }
        };
        int[] labels = { 10, 10, 20, 20, 30, 30, 40, 40 };

        var result = AdaptiveClusterMerger.MergeNearbyClusters(points, labels, minClustersToMerge: 4);

        Assert.Equal(4, result.OriginalClusterCount);
        Assert.Equal(4, result.FinalClusterCount);
        Assert.Equal(new[] { 0, 0, 1, 1, 2, 2, 3, 3 }, result.Labels);
    }

    [Fact]
    public void MergeToPreferredClusterCount_ReducesOversegmentedLabels()
    {
        var points = new List<float[]>
        {
            new[] { 0.00f, 0.00f },
            new[] { 0.02f, 0.00f },
            new[] { 0.10f, 0.00f },
            new[] { 0.12f, 0.00f },
            new[] { 3.00f, 0.00f },
            new[] { 3.02f, 0.00f },
            new[] { 3.10f, 0.00f },
            new[] { 3.12f, 0.00f },
            new[] { 6.00f, 0.00f },
            new[] { 6.02f, 0.00f },
            new[] { 6.10f, 0.00f },
            new[] { 6.12f, 0.00f }
        };
        int[] labels = { 10, 10, 20, 20, 30, 30, 40, 40, 50, 50, 60, 60 };

        var result = AdaptiveClusterMerger.MergeToPreferredClusterCount(points, labels, preferredMaxClusters: 3);

        Assert.Equal(6, result.OriginalClusterCount);
        Assert.Equal(3, result.FinalClusterCount);
        Assert.Equal(new[] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2 }, result.Labels);
    }

    [Fact]
    public void MergeToPreferredClusterCount_StopsAtCompleteLinkDistanceCap()
    {
        var points = new List<float[]>
        {
            new[] { 0.00f, 0.00f },
            new[] { 0.02f, 0.00f },
            new[] { 0.10f, 0.00f },
            new[] { 0.12f, 0.00f },
            new[] { 3.00f, 0.00f },
            new[] { 3.02f, 0.00f },
            new[] { 3.10f, 0.00f },
            new[] { 3.12f, 0.00f },
            new[] { 6.00f, 0.00f },
            new[] { 6.02f, 0.00f },
            new[] { 6.10f, 0.00f },
            new[] { 6.12f, 0.00f }
        };
        int[] labels = { 10, 10, 20, 20, 30, 30, 40, 40, 50, 50, 60, 60 };

        var result = AdaptiveClusterMerger.MergeToPreferredClusterCount(
            points,
            labels,
            preferredMaxClusters: 1,
            maxCompleteLinkDistance: 0.15d);

        Assert.Equal(6, result.OriginalClusterCount);
        Assert.Equal(3, result.FinalClusterCount);
        Assert.Equal(new[] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2 }, result.Labels);
    }
}
