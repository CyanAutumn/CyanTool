using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using System.Collections.Concurrent;

namespace CyanTools.Utils {
    public struct Detection {
        public SixLabors.ImageSharp.Rectangle BoundingBox { get; set; }
        public string Type { get; set; }
        public float Score { get; set; }

        public Detection(SixLabors.ImageSharp.Rectangle boundingBox, string type, float score) {
            BoundingBox = boundingBox;
            Type = type;
            Score = score;
        }
    }

    public static class YoloDetection {
        private static readonly ConcurrentDictionary<string, InferenceSession> _modelCache = new();
        private static readonly object _lockObject = new();

        public static List<Detection> Predict(
            Image<Rgb24> image,
            string modelPath,
            float confThreshold = 0.25f,
            float iouThreshold = 0.7f,
            int maxInferSize = 640) {
            var session = GetOrCreateModel(modelPath);


            var (processedImage, oldSize, newSize, scale, padLeft, padTop) = PreprocessImage(image, maxInferSize);
            using var preparedImageLifetime = processedImage;


            var inputTensor = ImageToTensor(processedImage);
            var inputs = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor("images", inputTensor)
            };


            using var results = session.Run(inputs);
            var output = results.First().AsTensor<float>();


            return PostProcess(output, confThreshold, iouThreshold, oldSize, newSize, scale, padLeft, padTop);
        }

        private static InferenceSession GetOrCreateModel(string modelPath) {
            if (!_modelCache.TryGetValue(modelPath, out var session)) {
                lock (_lockObject) {
                    if (!_modelCache.TryGetValue(modelPath, out session)) {
                        var options = new SessionOptions();
                        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;


                        var providers = new List<string>();
                        try {
                            providers.Add("CUDAExecutionProvider");
                        }
                        catch {

                        }

                        providers.Add("CPUExecutionProvider");

                        session = new InferenceSession(modelPath, options);
                        _modelCache[modelPath] = session;
                    }
                }
            }

            return session;
        }

        private static (Image<Rgb24> processedImage, SixLabors.ImageSharp.Size oldSize, SixLabors.ImageSharp.Size
            newSize, float scale, int padLeft, int padTop) PreprocessImage(
                Image<Rgb24> image, int maxInferSize) {
            var oldSize = new SixLabors.ImageSharp.Size(image.Width, image.Height);


            var scale = Math.Min((float)maxInferSize / image.Width, (float)maxInferSize / image.Height);
            var newWidth = (int)(image.Width * scale);
            var newHeight = (int)(image.Height * scale);





            var processedImage = image.Clone(ctx => ctx.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));


            var padLeft = (maxInferSize - newWidth) / 2;
            var padTop = (maxInferSize - newHeight) / 2;


            if (padLeft == 0 && padTop == 0 && newWidth == maxInferSize && newHeight == maxInferSize) {
                return (processedImage, oldSize, new SixLabors.ImageSharp.Size(maxInferSize, maxInferSize), scale,
                    padLeft, padTop);
            }

            var paddedImage = new Image<Rgb24>(Configuration.Default, maxInferSize, maxInferSize,
                SixLabors.ImageSharp.Color.Black);
            paddedImage.Mutate(x => x.DrawImage(processedImage, new SixLabors.ImageSharp.Point(padLeft, padTop), 1.0f));
            processedImage.Dispose();
            processedImage = paddedImage;

            return (processedImage, oldSize, new SixLabors.ImageSharp.Size(maxInferSize, maxInferSize), scale, padLeft,
                padTop);
        }

        private static Tensor<float> ImageToTensor(Image<Rgb24> image) {
            var width = image.Width;
            var height = image.Height;
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

            image.ProcessPixelRows(accessor => {
                for (int y = 0; y < height; y++) {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++) {
                        var pixel = pixelRow[x];

                        tensor[0, 0, y, x] = pixel.R / 255.0f;
                        tensor[0, 1, y, x] = pixel.G / 255.0f;
                        tensor[0, 2, y, x] = pixel.B / 255.0f;
                    }
                }
            });

            return tensor;
        }

        private static List<Detection> PostProcess(
            Tensor<float> output,
            float confThreshold,
            float iouThreshold,
            SixLabors.ImageSharp.Size oldSize,
            SixLabors.ImageSharp.Size newSize,
            float scale,
            int padLeft,
            int padTop) {
            var detections = new List<Detection>();


            var dimensions = output.Dimensions.ToArray();

            if (dimensions.Length == 3 && dimensions[2] == 6)
            {
                return PostProcessEnd2End(output, confThreshold, oldSize, newSize, scale, padLeft, padTop);
            }
            else
            {
                return PostProcessNms(output, confThreshold, iouThreshold, oldSize, newSize, scale, padLeft, padTop);
            }
        }

        private static List<Detection> PostProcessEnd2End(
            Tensor<float> output,
            float confThreshold,
            SixLabors.ImageSharp.Size oldSize,
            SixLabors.ImageSharp.Size newSize,
            float scale,
            int padLeft,
            int padTop) {
            var detections = new List<Detection>();
            var dimensions = output.Dimensions.ToArray();
            var numDetections = dimensions[1];

            for (int i = 0; i < numDetections; i++) {
                var confidence = output[0, i, 4];
                if (confidence > confThreshold) {
                    var x1 = output[0, i, 0];
                    var y1 = output[0, i, 1];
                    var x2 = output[0, i, 2];
                    var y2 = output[0, i, 3];
                    var classId = (int)output[0, i, 5];


                    var (nx1, ny1) = PostProcessCoordinate(x1, y1, oldSize, newSize, scale, padLeft, padTop);
                    var (nx2, ny2) = PostProcessCoordinate(x2, y2, oldSize, newSize, scale, padLeft, padTop);

                    var bbox = new SixLabors.ImageSharp.Rectangle(nx1, ny1, nx2 - nx1, ny2 - ny1);
                    detections.Add(new Detection(bbox, classId.ToString(), confidence));
                }
            }

            return ApplyNms(detections, 0.7f);
        }

        private static List<Detection> PostProcessNms(
            Tensor<float> output,
            float confThreshold,
            float iouThreshold,
            SixLabors.ImageSharp.Size oldSize,
            SixLabors.ImageSharp.Size newSize,
            float scale,
            int padLeft,
            int padTop) {
            var detections = new List<Detection>();
            var dimensions = output.Dimensions.ToArray();

            if (dimensions.Length != 3) return detections;



            var propsCount = dimensions[1];
            var numDetections = dimensions[2];


            var transposedDetections = new List<float[]>();
            for (int i = 0; i < numDetections; i++) {
                var props = new float[propsCount];
                for (int j = 0; j < propsCount; j++) {
                    props[j] = output[0, j, i];
                }

                transposedDetections.Add(props);
            }


            var numClasses = propsCount - 4;

            foreach (var props in transposedDetections) {

                float maxScore = 0;
                int maxClassId = -1;

                for (int j = 4; j < propsCount; j++) {
                    var score = props[j];
                    if (score > maxScore) {
                        maxScore = score;
                        maxClassId = j - 4;
                    }
                }

                if (maxScore > confThreshold) {

                    var centerX = props[0];
                    var centerY = props[1];
                    var width = props[2];
                    var height = props[3];


                    var x1 = centerX - width / 2;
                    var y1 = centerY - height / 2;
                    var x2 = centerX + width / 2;
                    var y2 = centerY + height / 2;


                    var (nx1, ny1) = PostProcessCoordinate(x1, y1, oldSize, newSize, scale, padLeft, padTop);
                    var (nx2, ny2) = PostProcessCoordinate(x2, y2, oldSize, newSize, scale, padLeft, padTop);

                    var bbox = new SixLabors.ImageSharp.Rectangle(nx1, ny1, nx2 - nx1, ny2 - ny1);
                    detections.Add(new Detection(bbox, maxClassId.ToString(), maxScore));
                }
            }

            return ApplyNms(detections, iouThreshold);
        }

        private static (int x, int y) PostProcessCoordinate(float x, float y, SixLabors.ImageSharp.Size oldSize,
            SixLabors.ImageSharp.Size newSize, float scale, int padLeft, int padTop) {

            var adjustedX = x - padLeft;
            var adjustedY = y - padTop;


            var newX = (int)Math.Max(0, Math.Min(adjustedX / scale, (float)oldSize.Width));
            var newY = (int)Math.Max(0, Math.Min(adjustedY / scale, (float)oldSize.Height));
            return (newX, newY);
        }

        private static List<Detection> ApplyNms(List<Detection> detections, float iouThreshold) {
            if (detections.Count == 0) return detections;


            detections.Sort((a, b) => b.Score.CompareTo(a.Score));

            var keep = new List<Detection>();
            var suppressed = new HashSet<int>();

            for (int i = 0; i < detections.Count; i++) {
                if (suppressed.Contains(i)) continue;

                keep.Add(detections[i]);

                for (int j = i + 1; j < detections.Count; j++) {
                    if (suppressed.Contains(j)) continue;

                    var iou = CalculateIoU(detections[i].BoundingBox, detections[j].BoundingBox);
                    if (iou > iouThreshold) {
                        suppressed.Add(j);
                    }
                }
            }

            return keep;
        }

        private static float CalculateIoU(SixLabors.ImageSharp.Rectangle box1, SixLabors.ImageSharp.Rectangle box2) {
            var intersection = SixLabors.ImageSharp.Rectangle.Intersect(box1, box2);
            if (intersection.IsEmpty) return 0f;

            var intersectionArea = intersection.Width * intersection.Height;
            var unionArea = box1.Width * box1.Height + box2.Width * box2.Height - intersectionArea;

            return unionArea > 0 ? (float)intersectionArea / unionArea : 0f;
        }

        public static void ClearModelCache() {
            lock (_lockObject) {
                foreach (var session in _modelCache.Values) {
                    session?.Dispose();
                }

                _modelCache.Clear();
            }
        }
    }
}