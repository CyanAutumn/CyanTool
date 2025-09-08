using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace CyanTools.Utils {
    public class ImageItem {
        public Image<Rgb24> Image { get; set; }
        public Dictionary<string, object> Meta { get; set; }

        public ImageItem(Image<Rgb24> image, Dictionary<string, object>? meta = null) {
            Image = image;
            Meta = meta ?? new Dictionary<string, object>();
        }
    }

    public class ThreeStageProcessor {
        private readonly string _modelsPath;


        public bool SplitPerson { get; set; } = true;
        public bool SplitEyes { get; set; } = false;
        public bool KeepOriginTags { get; set; } = false;


        public float PersonConfThreshold { get; set; } = 0.3f;
        public float PersonIouThreshold { get; set; } = 0.5f;


        public float HalfbodyConfThreshold { get; set; } = 0.5f;
        public float HalfbodyIouThreshold { get; set; } = 0.7f;


        public float HeadConfThreshold { get; set; } = 0.4f;
        public float HeadIouThreshold { get; set; } = 0.7f;
        public float HeadScale { get; set; } = 1.5f;


        public float EyeConfThreshold { get; set; } = 0.3f;
        public float EyeIouThreshold { get; set; } = 0.3f;
        public float EyeScale { get; set; } = 2.4f;

        public ThreeStageProcessor(string? modelsPath = null) {
            _modelsPath = modelsPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
        }

        public async Task<List<ImageItem>> ProcessImageAsync(ImageItem inputItem,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) {
            var results = new List<ImageItem>();

            var filename = inputItem.Meta.TryGetValue("filename", out var fn) ? fn.ToString() : null;
            var filebody = filename != null ? Path.GetFileNameWithoutExtension(filename) : null;
            var ext = filename != null ? Path.GetExtension(filename) : null;

            progress?.Report($"开始处理图片: {filename ?? "未知"}");


            var personItems = await SplitPersonAsync(inputItem, filebody, ext, progress, cancellationToken);

            foreach (var (personIndex, personItem) in personItems.Select((item, index) => (index + 1, item))) {
                if (cancellationToken.IsCancellationRequested) break;

                results.Add(personItem);
                progress?.Report($"处理第{personIndex}个人物");


                var halfbodyItems = await DetectHalfbodyAsync(personItem, personIndex, filebody, ext, progress,
                    cancellationToken);
                results.AddRange(halfbodyItems);


                var headItems =
                    await DetectHeadAsync(personItem, personIndex, filebody, ext, progress, cancellationToken);
                results.AddRange(headItems);


                if (SplitEyes) {
                    foreach (var headItem in headItems) {
                        if (cancellationToken.IsCancellationRequested) break;

                        var eyeItems = await DetectEyesAsync(headItem, personIndex, filebody, ext, progress,
                            cancellationToken);
                        results.AddRange(eyeItems);
                    }
                }
            }

            return results;
        }

        private async Task<List<ImageItem>> SplitPersonAsync(ImageItem inputItem, string? filebody, string? ext,
            IProgress<string>? progress, CancellationToken cancellationToken) {
            if (!SplitPerson) {
                return new List<ImageItem> { inputItem };
            }

            progress?.Report("检测人物...");

            var modelPath = Path.Combine(_modelsPath, "anime_person_detection", "person_detect_v1.1_m.onnx");
            if (!File.Exists(modelPath)) {
                throw new FileNotFoundException($"人物检测模型未找到: {modelPath}");
            }

            var detections = await Task.Run(() =>
                    YoloDetection.Predict(inputItem.Image, modelPath, PersonConfThreshold, PersonIouThreshold),
                cancellationToken);

            var results = new List<ImageItem>();

            if (detections.Count == 0) {
                progress?.Report("未检测到人物，使用原图");
                return new List<ImageItem> { inputItem };
            }

            for (int i = 0; i < detections.Count; i++) {
                if (cancellationToken.IsCancellationRequested) break;

                var detection = detections[i];
                var cropRect = detection.BoundingBox;


                cropRect.Intersect(
                    new SixLabors.ImageSharp.Rectangle(0, 0, inputItem.Image.Width, inputItem.Image.Height));

                if (cropRect.Width > 0 && cropRect.Height > 0) {
                    var personImage = inputItem.Image.Clone(ctx => ctx.Crop(cropRect));

                    var personMeta = new Dictionary<string, object>(inputItem.Meta);
                    personMeta["crop"] = new { type = detection.Type, score = detection.Score };

                    if (personMeta.ContainsKey("tags") && !KeepOriginTags) {
                        personMeta.Remove("tags");
                    }

                    if (filebody != null) {
                        personMeta["filename"] = $"{filebody}_person{i + 1}{ext}";
                    }

                    results.Add(new ImageItem(personImage, personMeta));
                    progress?.Report($"检测到第{i + 1}个人物，置信度: {detection.Score:F3}");
                }
            }

            return results;
        }

        private async Task<List<ImageItem>> DetectHalfbodyAsync(ImageItem personItem, int personIndex,
            string? filebody, string? ext, IProgress<string>? progress, CancellationToken cancellationToken) {
            progress?.Report($"检测第{personIndex}个人物的半身...");

            var modelPath = Path.Combine(_modelsPath, "anime_halfbody_detection", "halfbody_detect_v1.0_s.onnx");
            if (!File.Exists(modelPath)) {
                throw new FileNotFoundException($"半身检测模型未找到: {modelPath}");
            }

            var detections = await Task.Run(() =>
                    YoloDetection.Predict(personItem.Image, modelPath, HalfbodyConfThreshold, HalfbodyIouThreshold),
                cancellationToken);

            var results = new List<ImageItem>();

            if (detections.Count > 0) {
                var detection = detections[0];
                var cropRect = detection.BoundingBox;

                cropRect.Intersect(
                    new SixLabors.ImageSharp.Rectangle(0, 0, personItem.Image.Width, personItem.Image.Height));

                if (cropRect.Width > 0 && cropRect.Height > 0) {
                    var halfbodyImage = personItem.Image.Clone(ctx => ctx.Crop(cropRect));

                    var halfbodyMeta = new Dictionary<string, object>(personItem.Meta);
                    halfbodyMeta["crop"] = new { type = detection.Type, score = detection.Score };

                    if (halfbodyMeta.ContainsKey("tags") && !KeepOriginTags) {
                        halfbodyMeta.Remove("tags");
                    }

                    if (filebody != null) {
                        halfbodyMeta["filename"] = $"{filebody}_person{personIndex}_halfbody{ext}";
                    }

                    results.Add(new ImageItem(halfbodyImage, halfbodyMeta));
                    progress?.Report($"检测到半身，置信度: {detection.Score:F3}");
                }
            }

            return results;
        }

        private async Task<List<ImageItem>> DetectHeadAsync(ImageItem personItem, int personIndex,
            string? filebody, string? ext, IProgress<string>? progress, CancellationToken cancellationToken) {
            progress?.Report($"检测第{personIndex}个人物的头部...");

            var modelPath = Path.Combine(_modelsPath, "anime_head_detection", "head_detect_v0_s.onnx");
            if (!File.Exists(modelPath)) {
                throw new FileNotFoundException($"头部检测模型未找到: {modelPath}");
            }

            var detections = await Task.Run(() =>
                    YoloDetection.Predict(personItem.Image, modelPath, HeadConfThreshold, HeadIouThreshold),
                cancellationToken);

            var results = new List<ImageItem>();

            if (detections.Count > 0) {
                var detection = detections[0];
                var originalRect = detection.BoundingBox;


                var centerX = originalRect.X + originalRect.Width / 2.0f;
                var centerY = originalRect.Y + originalRect.Height / 2.0f;
                var size = Math.Max(originalRect.Width, originalRect.Height) * HeadScale;

                var x0 = (int)Math.Max(centerX - size / 2, 0);
                var y0 = (int)Math.Max(centerY - size / 2, 0);
                var x1 = (int)Math.Min(centerX + size / 2, personItem.Image.Width);
                var y1 = (int)Math.Min(centerY + size / 2, personItem.Image.Height);

                var expandedRect = new SixLabors.ImageSharp.Rectangle(x0, y0, x1 - x0, y1 - y0);


                var finalWidth = Math.Max(expandedRect.Width, 2);
                var finalHeight = Math.Max(expandedRect.Height, 2);

                expandedRect =
                    new SixLabors.ImageSharp.Rectangle(expandedRect.X, expandedRect.Y, finalWidth, finalHeight);


                var clampedX1 = Math.Min(expandedRect.X + expandedRect.Width, personItem.Image.Width);
                var clampedY1 = Math.Min(expandedRect.Y + expandedRect.Height, personItem.Image.Height);

                expandedRect = new SixLabors.ImageSharp.Rectangle(expandedRect.X, expandedRect.Y,
                    clampedX1 - expandedRect.X, clampedY1 - expandedRect.Y);

                if (expandedRect.Width > 0 && expandedRect.Height > 0) {
                    var headImage = personItem.Image.Clone(ctx => ctx.Crop(expandedRect));

                    var headMeta = new Dictionary<string, object>(personItem.Meta);
                    headMeta["crop"] = new { type = detection.Type, score = detection.Score };

                    if (headMeta.ContainsKey("tags") && !KeepOriginTags) {
                        headMeta.Remove("tags");
                    }

                    if (filebody != null) {
                        headMeta["filename"] = $"{filebody}_person{personIndex}_head{ext}";
                    }

                    results.Add(new ImageItem(headImage, headMeta));
                    progress?.Report($"检测到头部，置信度: {detection.Score:F3}");
                }
            }

            return results;
        }

        private async Task<List<ImageItem>> DetectEyesAsync(ImageItem headItem, int personIndex,
            string? filebody, string? ext, IProgress<string>? progress, CancellationToken cancellationToken) {
            progress?.Report($"检测第{personIndex}个人物的眼部...");

            var modelPath = Path.Combine(_modelsPath, "anime_eye_detection", "eye_detect_v1.0_s.onnx");
            if (!File.Exists(modelPath)) {
                throw new FileNotFoundException($"眼部检测模型未找到: {modelPath}");
            }

            var detections = await Task.Run(() =>
                    YoloDetection.Predict(headItem.Image, modelPath, EyeConfThreshold, EyeIouThreshold),
                cancellationToken);

            var results = new List<ImageItem>();

            for (int j = 0; j < detections.Count; j++) {
                if (cancellationToken.IsCancellationRequested) break;

                var detection = detections[j];
                var originalRect = detection.BoundingBox;


                var centerX = originalRect.X + originalRect.Width / 2.0f;
                var centerY = originalRect.Y + originalRect.Height / 2.0f;
                var size = Math.Max(originalRect.Width, originalRect.Height) * EyeScale;


                var x0 = (int)Math.Max(centerX - size / 2, 0);
                var y0 = (int)Math.Max(centerY - size / 2, 0);
                var x1 = (int)Math.Min(centerX + size / 2, headItem.Image.Width);
                var y1 = (int)Math.Min(centerY + size / 2, headItem.Image.Height);


                var cropRect = new SixLabors.ImageSharp.Rectangle(x0, y0, x1 - x0, y1 - y0);

                if (cropRect.Width > 0 && cropRect.Height > 0) {
                    var eyeImage = headItem.Image.Clone(ctx => ctx.Crop(cropRect));

                    var eyeMeta = new Dictionary<string, object>(headItem.Meta);
                    eyeMeta["crop"] = new { type = detection.Type, score = detection.Score };

                    if (eyeMeta.ContainsKey("tags") && !KeepOriginTags) {
                        eyeMeta.Remove("tags");
                    }

                    if (filebody != null) {

                        eyeMeta["filename"] = $"{filebody}_person{personIndex}_head_eye{j}{ext}";
                    }

                    results.Add(new ImageItem(eyeImage, eyeMeta));

                    progress?.Report($"检测到第{j + 1}个眼部，置信度: {detection.Score:F3}");
                }
            }

            return results;
        }
    }
}