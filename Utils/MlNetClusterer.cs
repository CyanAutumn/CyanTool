
using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public class ImageData
    {
        [VectorType]
        public float[]? Features { get; set; }
    }

    public class ClusterPrediction
    {
        [ColumnName("PredictedLabel")]
        public uint PredictedClusterId { get; set; }

        [ColumnName("Score")]
        public float[]? Distances { get; set; }
    }

    public static class MlNetClusterer
    {
        public static int[] Cluster(List<float[]> data, int numClusters)
        {
            if (data == null || !data.Any() || data.Count < numClusters)
            {

                return Enumerable.Range(0, data?.Count ?? 0).ToArray();
            }

            var mlContext = new MLContext();


            var imageData = data.Select(d => new ImageData { Features = d }).ToList();
            var dataView = mlContext.Data.LoadFromEnumerable(imageData);


            var pipeline = mlContext.Clustering.Trainers.KMeans(
                new Microsoft.ML.Trainers.KMeansTrainer.Options
                {
                    NumberOfClusters = numClusters,
                    FeatureColumnName = "Features"
                });


            var model = pipeline.Fit(dataView);


            var predictions = model.Transform(dataView);
            var clusterAssignments = mlContext.Data.CreateEnumerable<ClusterPrediction>(predictions, false).ToArray();


            return clusterAssignments.Select(p => (int)p.PredictedClusterId).ToArray();
        }
    }
}
