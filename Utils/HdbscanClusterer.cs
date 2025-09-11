using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace CyanTools.Utils {
    public static class HdbscanClusterer {

        public class InputData
        {
            [VectorType(100)]
            public float[] Features { get; set; }
        }


        public class ClusterPrediction
        {
            [ColumnName("PredictedLabel")]
            public uint PredictedLabel { get; set; }

            [ColumnName("Score")]
            public float[] Score { get; set; }
        }

        public static int[] Cluster(List<float[]> data, int minClusterSize) {
            if (data == null || !data.Any()) {
                return Array.Empty<int>();
            }


            var mlContext = new MLContext();


            var inputData = data.Select(arr => new InputData { Features = arr });


            var dataView = mlContext.Data.LoadFromEnumerable(inputData);








            var trainer = mlContext.Clustering.Trainers.KMeans(
                featureColumnName: "Features",
                numberOfClusters: minClusterSize);


            var model = trainer.Fit(dataView);


            var predictions = mlContext.Data.CreateEnumerable<ClusterPrediction>(
                model.Transform(dataView), reuseRowObject: false);


            var labels = predictions.Select(p => (int)p.PredictedLabel).ToArray();

            return labels;
        }
    }
}