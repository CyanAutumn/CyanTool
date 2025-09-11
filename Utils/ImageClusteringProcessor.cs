using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyanTools.Utils
{
    public class ImageClusteringProcessor
    {
        private const int ImageWidth = 224;
        private const int ImageHeight = 224;
        private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
        private static readonly float[] StdDev = { 0.26862954f, 0.26130258f, 0.27577711f };

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly string _outputName;

        public ImageClusteringProcessor(string modelPath)
        {
            var sessionOptions = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            _session = new InferenceSession(modelPath, sessionOptions);
            _inputName = _session.InputMetadata.Keys.First();
            _outputName = _session.OutputMetadata.Keys.First();
        }

        public List<ImageStyleResult> ProcessFolder(string folderPath, Action<double> onProgress, Action<string> onError)
        {
            try
            {
                var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
                var imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();

                if (!imageFiles.Any())
                {
                    return new List<ImageStyleResult>();
                }

                var results = new List<ImageStyleResult>();
                for (int i = 0; i < imageFiles.Count; i++)
                {
                    var imagePath = imageFiles[i];
                    try
                    {
                        var inputTensor = PreprocessImage(imagePath);
                        var embedding = ExtractEmbedding(inputTensor);
                        results.Add(new ImageStyleResult { FilePath = imagePath, StyleVector = embedding });
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke($"处理图片失败: {imagePath}\n错误: {ex.Message}");
                    }
                    onProgress?.Invoke((double)(i + 1) / imageFiles.Count * 50);
                }
                return results;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"读取文件时发生错误: {ex.Message}");
                return new List<ImageStyleResult>();
            }
        }

        private DenseTensor<Float16> PreprocessImage(string imagePath)
        {
            using var image = Image.Load<Rgb24>(imagePath);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(ImageWidth, ImageHeight),
                Sampler = KnownResamplers.Bicubic
            }));

            var tensor = new DenseTensor<Float16>(new[] { 1, 3, ImageHeight, ImageWidth });
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++)
                    {
                        Rgb24 pixel = pixelRow[x];
                        tensor[0, 0, y, x] = (Float16)((pixel.R / 255f - Mean[0]) / StdDev[0]);
                        tensor[0, 1, y, x] = (Float16)((pixel.G / 255f - Mean[1]) / StdDev[1]);
                        tensor[0, 2, y, x] = (Float16)((pixel.B / 255f - Mean[2]) / StdDev[2]);
                    }
                }
            });
            return tensor;
        }

        private float[] ExtractEmbedding(DenseTensor<Float16> inputTensor)
        {
            var inputs = new NamedOnnxValue[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) };
            using var results = _session.Run(inputs);
            var outputTensor = results.First(r => r.Name == _outputName).AsTensor<Float16>();
            var floatList = new List<float>();
            foreach (var val in outputTensor)
            {
                floatList.Add((float)val);
            }
            return floatList.ToArray();
        }
    }

    public class ImageStyleResult
    {
        public string FilePath { get; set; } = string.Empty;
        public float[] StyleVector { get; set; } = Array.Empty<float>();
    }

    public static class KMeansClusterer
    {
        public static int[] Cluster(List<float[]> data, int numClusters, int maxIterations = 100)
        {
            if (data.Count < numClusters)
            {
                return Enumerable.Range(0, data.Count).ToArray();
            }

            var random = new Random();
            var centroids = data.OrderBy(x => random.Next()).Take(numClusters).ToList();
            var assignments = new int[data.Count];

            for (int iter = 0; iter < maxIterations; iter++)
            {
                bool changed = false;
                for (int i = 0; i < data.Count; i++)
                {
                    int bestCluster = 0;
                    double minDistance = double.MaxValue;
                    for (int j = 0; j < numClusters; j++)
                    {
                        double distance = SquaredEuclideanDistance(data[i], centroids[j]);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            bestCluster = j;
                        }
                    }
                    if (assignments[i] != bestCluster)
                    {
                        assignments[i] = bestCluster;
                        changed = true;
                    }
                }

                if (!changed) break;

                for (int j = 0; j < numClusters; j++)
                {
                    var pointsInCluster = data.Where((p, i) => assignments[i] == j).ToList();
                    if (pointsInCluster.Any())
                    {
                        centroids[j] = CalculateMean(pointsInCluster);
                    }
                }
            }
            return assignments;
        }

        private static double SquaredEuclideanDistance(float[] v1, float[] v2)
        {
            double sum = 0;
            for (int i = 0; i < v1.Length; i++)
            {
                sum += (v1[i] - v2[i]) * (v1[i] - v2[i]);
            }
            return sum;
        }

        private static float[] CalculateMean(List<float[]> points)
        {
            int dimensions = points[0].Length;
            var mean = new float[dimensions];
            foreach (var point in points)
            {
                for (int i = 0; i < dimensions; i++)
                {
                    mean[i] += point[i];
                }
            }
            for (int i = 0; i < dimensions; i++)
            {
                mean[i] /= points.Count;
            }
            return mean;
        }
    }
}
