using Microsoft.ML;
using Microsoft.ML.Data;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils {
    public static class FeatureSpaceReducer {
        private class PcaInput {
            [VectorType]
            public float[]? Features { get; set; }
        }

        private class ReducedData {
            [ColumnName("ReducedFeatures")]
            public float[]? Values { get; set; }
        }

        public static List<float[]> ReduceToTwoDimensions(List<float[]> dataPoints) {
            if (dataPoints == null || dataPoints.Count == 0) {
                return new List<float[]>();
            }

            if (dataPoints[0].Length <= 2) {

                return dataPoints.Select(d => d.ToArray()).ToList();
            }

            var mlContext = new MLContext(seed: 42);
            int vectorSize = dataPoints[0].Length;

            var schema = SchemaDefinition.Create(typeof(PcaInput));
            schema["Features"].ColumnType =
                new VectorDataViewType(NumberDataViewType.Single, vectorSize);

            var rows = dataPoints.Select(d => new PcaInput { Features = d }).ToList();
            var dataView = mlContext.Data.LoadFromEnumerable(rows, schema);

            var pipeline = mlContext.Transforms.ProjectToPrincipalComponents(
                outputColumnName: "ReducedFeatures",
                inputColumnName: "Features",
                rank: 2);

            var model = pipeline.Fit(dataView);
            var transformed = model.Transform(dataView);
            var reduced = mlContext.Data.CreateEnumerable<ReducedData>(transformed, reuseRowObject: false)
                .Select(r => r.Values ?? new float[] { 0f, 0f })
                .ToList();

            return reduced;
        }
    }
}
