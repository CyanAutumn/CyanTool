using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CyanTools.Utils
{
    public sealed record TrainingBridgeBucketSize(int Width, int Height);

    public sealed record TrainingBridgeBucketCalculation(
        IReadOnlyList<TrainingBridgeBucketSize> Buckets,
        int ImageCount,
        int SkippedFileCount);






    public static class TrainingBridgeBucketCalculator
    {
        private const int BucketMultiple = 16;

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"
        };

        public static TrainingBridgeBucketCalculation CalculateFromDirectory(string datasetDirectory, int resolution)
        {
            if (string.IsNullOrWhiteSpace(datasetDirectory) || !Directory.Exists(datasetDirectory))
            {
                throw new DirectoryNotFoundException("训练集路径不存在或无法访问。");
            }

            var imageSizes = new List<TrainingBridgeBucketSize>();
            int skippedFileCount = 0;
            foreach (string filePath in Directory.EnumerateFiles(datasetDirectory, "*.*", SearchOption.AllDirectories)
                         .Where(path => SupportedExtensions.Contains(Path.GetExtension(path))))
            {
                try
                {
                    var imageInfo = Image.Identify(filePath);
                    if (imageInfo is null || imageInfo.Width <= 0 || imageInfo.Height <= 0)
                    {
                        skippedFileCount++;
                        continue;
                    }

                    imageSizes.Add(new TrainingBridgeBucketSize(imageInfo.Width, imageInfo.Height));
                }
                catch
                {


                    skippedFileCount++;
                }
            }

            return new TrainingBridgeBucketCalculation(
                Calculate(imageSizes, resolution),
                imageSizes.Count,
                skippedFileCount);
        }

        public static IReadOnlyList<TrainingBridgeBucketSize> Calculate(
            IEnumerable<TrainingBridgeBucketSize> imageSizes,
            int resolution)
        {
            ArgumentNullException.ThrowIfNull(imageSizes);

            int normalizedResolution = Math.Max(BucketMultiple, resolution);
            double targetArea = (double)normalizedResolution * normalizedResolution;
            var buckets = new HashSet<TrainingBridgeBucketSize>();

            foreach (TrainingBridgeBucketSize imageSize in imageSizes)
            {
                if (imageSize.Width <= 0 || imageSize.Height <= 0)
                {
                    continue;
                }

                double aspectRatio = imageSize.Width / (double)imageSize.Height;
                int width;
                int height;
                if (aspectRatio <= 1d)
                {
                    width = RoundToBucketMultiple(Math.Sqrt(targetArea * aspectRatio));
                    height = RoundToBucketMultiple(targetArea / width);
                }
                else
                {
                    height = RoundToBucketMultiple(Math.Sqrt(targetArea / aspectRatio));
                    width = RoundToBucketMultiple(targetArea / height);
                }

                buckets.Add(new TrainingBridgeBucketSize(width, height));
            }

            return buckets
                .OrderBy(bucket => bucket.Width)
                .ThenBy(bucket => bucket.Height)
                .ToArray();
        }

        public static string FormatTomlArray(IEnumerable<TrainingBridgeBucketSize> buckets)
        {
            ArgumentNullException.ThrowIfNull(buckets);

            TrainingBridgeBucketSize[] values = buckets.ToArray();
            if (values.Length == 0)
            {
                return "[]";
            }

            var text = new StringBuilder("[\n");
            foreach (TrainingBridgeBucketSize bucket in values)
            {
                text.Append("    [")
                    .Append(bucket.Width)
                    .Append(", ")
                    .Append(bucket.Height)
                    .Append(", 1],\n");
            }
            text.Append(']');
            return text.ToString();
        }

        private static int RoundToBucketMultiple(double value)
            => Math.Max(
                BucketMultiple,
                (int)Math.Round(value / BucketMultiple, MidpointRounding.AwayFromZero) * BucketMultiple);
    }
}
