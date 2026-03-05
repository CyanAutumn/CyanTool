using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CyanTools.Utils
{
    public static class ClusterDistributionPlotter
    {
        private static readonly Color[] Palette =
        {
            Color.ParseHex("#4E79A7"),
            Color.ParseHex("#F28E2B"),
            Color.ParseHex("#E15759"),
            Color.ParseHex("#76B7B2"),
            Color.ParseHex("#59A14F"),
            Color.ParseHex("#EDC948"),
            Color.ParseHex("#B07AA1"),
            Color.ParseHex("#FF9DA7"),
            Color.ParseHex("#9C755F"),
            Color.ParseHex("#BAB0AC"),
            Color.ParseHex("#3A86FF"),
            Color.ParseHex("#FF006E"),
            Color.ParseHex("#8338EC"),
            Color.ParseHex("#FB5607"),
            Color.ParseHex("#06D6A0"),
            Color.ParseHex("#118AB2")
        };

        public static string SaveScatterPlot(
            IReadOnlyList<float[]> points2D,
            IReadOnlyList<int> assignments,
            string outputDirectory,
            string fileName,
            IReadOnlyList<string>? imagePaths = null,
            int maxThumbnailCount = 300,
            int thumbnailSize = 33)
        {
            if (points2D == null || assignments == null)
            {
                throw new ArgumentNullException(nameof(points2D), "分布图输入为空。");
            }

            if (points2D.Count == 0)
            {
                throw new ArgumentException("分布图输入为空。", nameof(points2D));
            }

            if (points2D.Count != assignments.Count)
            {
                throw new ArgumentException("分布图坐标数量与聚类标签数量不一致。", nameof(assignments));
            }

            Directory.CreateDirectory(outputDirectory);
            string outputPath = System.IO.Path.Combine(outputDirectory, fileName);

            const int width = 1600;
            const int height = 1200;
            const float padding = 90f;
            const int gridSteps = 10;

            var (minX, maxX, minY, maxY) = ComputeBounds(points2D);
            float spanX = Math.Max(maxX - minX, 1e-6f);
            float spanY = Math.Max(maxY - minY, 1e-6f);
            float plotWidth = width - padding * 2;
            float plotHeight = height - padding * 2;
            float left = padding;
            float top = padding;
            float right = left + plotWidth;
            float bottom = top + plotHeight;
            var mappedPoints = new PointF[points2D.Count];
            var validPoint = new bool[points2D.Count];

            for (int i = 0; i < points2D.Count; i++)
            {
                var point = points2D[i];
                if (point == null || point.Length < 2)
                {
                    continue;
                }

                float x = point[0];
                float y = point[1];
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
                {
                    continue;
                }

                float mappedX = left + (x - minX) / spanX * plotWidth;
                float mappedY = top + (1f - (y - minY) / spanY) * plotHeight;
                mappedPoints[i] = new PointF(mappedX, mappedY);
                validPoint[i] = true;
            }

            using var image = new Image<Rgba32>(width, height, Color.ParseHex("#F7FAFC"));
            image.Mutate(ctx =>
            {
                DrawGrid(ctx, left, top, right, bottom, gridSteps);
                ctx.Draw(Color.ParseHex("#AAB7C8"), 2f, new RectangularPolygon(left, top, plotWidth, plotHeight));

                for (int i = 0; i < points2D.Count; i++)
                {
                    if (!validPoint[i])
                    {
                        continue;
                    }

                    PointF mappedPoint = mappedPoints[i];

                    int clusterId = assignments[i];
                    bool isNoise = clusterId < 0;
                    Color color = isNoise
                        ? Color.ParseHex("#8F99A8")
                        : Palette[clusterId % Palette.Length];
                    float radius = isNoise ? 2.8f : 3.6f;
                    float alpha = isNoise ? 0.50f : 0.72f;

                    ctx.Fill(color.WithAlpha(alpha), new EllipsePolygon(mappedPoint.X, mappedPoint.Y, radius));
                }

                if (imagePaths == null || imagePaths.Count == 0 || maxThumbnailCount <= 0 || thumbnailSize <= 0)
                {
                    return;
                }

                var thumbnailIndexes = BuildThumbnailIndexes(points2D.Count, maxThumbnailCount);
                foreach (int index in thumbnailIndexes)
                {
                    if (index < 0 || index >= points2D.Count || index >= imagePaths.Count || !validPoint[index])
                    {
                        continue;
                    }

                    string? imagePath = imagePaths[index];
                    if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                    {
                        continue;
                    }

                    try
                    {
                        using var thumb = Image.Load<Rgba32>(imagePath);
                        thumb.Mutate(x => x.Resize(new ResizeOptions
                        {
                            Size = new Size(thumbnailSize, thumbnailSize),
                            Mode = ResizeMode.Crop,
                            Position = AnchorPositionMode.Center
                        }));

                        PointF mappedPoint = mappedPoints[index];
                        float x = Math.Clamp(mappedPoint.X - thumbnailSize / 2f, 0f, width - thumbnailSize);
                        float y = Math.Clamp(mappedPoint.Y - thumbnailSize / 2f, 0f, height - thumbnailSize);

                        ctx.Fill(Color.White.WithAlpha(0.82f), new RectangularPolygon(x - 1f, y - 1f, thumbnailSize + 2f, thumbnailSize + 2f));
                        ctx.DrawImage(thumb, new Point((int)Math.Round(x), (int)Math.Round(y)), 1f);

                        int clusterId = assignments[index];
                        bool isNoise = clusterId < 0;
                        Color borderColor = isNoise
                            ? Color.ParseHex("#8F99A8")
                            : Palette[clusterId % Palette.Length];
                        ctx.Draw(borderColor.WithAlpha(0.95f), 1.5f, new RectangularPolygon(x, y, thumbnailSize, thumbnailSize));
                    }
                    catch
                    {

                    }
                }
            });

            image.Save(outputPath, new PngEncoder());
            return outputPath;
        }

        private static List<int> BuildThumbnailIndexes(int totalCount, int maxCount)
        {
            var indexes = new List<int>();
            if (totalCount <= 0 || maxCount <= 0)
            {
                return indexes;
            }

            if (totalCount <= maxCount)
            {
                for (int i = 0; i < totalCount; i++)
                {
                    indexes.Add(i);
                }

                return indexes;
            }

            double step = (double)totalCount / maxCount;
            for (int i = 0; i < maxCount; i++)
            {
                int index = (int)Math.Floor(i * step);
                if (index < 0 || index >= totalCount)
                {
                    continue;
                }

                if (indexes.Count == 0 || indexes[indexes.Count - 1] != index)
                {
                    indexes.Add(index);
                }
            }

            return indexes;
        }

        private static (float minX, float maxX, float minY, float maxY) ComputeBounds(IReadOnlyList<float[]> points2D)
        {
            bool hasValue = false;
            float minX = 0f;
            float maxX = 0f;
            float minY = 0f;
            float maxY = 0f;

            for (int i = 0; i < points2D.Count; i++)
            {
                var point = points2D[i];
                if (point == null || point.Length < 2)
                {
                    continue;
                }

                float x = point[0];
                float y = point[1];
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
                {
                    continue;
                }

                if (!hasValue)
                {
                    minX = maxX = x;
                    minY = maxY = y;
                    hasValue = true;
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }
                else if (x > maxX)
                {
                    maxX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }
                else if (y > maxY)
                {
                    maxY = y;
                }
            }

            if (!hasValue)
            {
                return (0f, 1f, 0f, 1f);
            }

            if (Math.Abs(maxX - minX) < 1e-6f)
            {
                minX -= 0.5f;
                maxX += 0.5f;
            }

            if (Math.Abs(maxY - minY) < 1e-6f)
            {
                minY -= 0.5f;
                maxY += 0.5f;
            }

            return (minX, maxX, minY, maxY);
        }

        private static void DrawGrid(IImageProcessingContext context, float left, float top, float right, float bottom, int steps)
        {
            Color gridColor = Color.ParseHex("#E3EAF4");
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float x = left + (right - left) * t;
                float y = top + (bottom - top) * t;
                context.DrawLine(gridColor, 1f, new PointF(x, top), new PointF(x, bottom));
                context.DrawLine(gridColor, 1f, new PointF(left, y), new PointF(right, y));
            }
        }
    }
}
