using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CyanTools.Utils;

public static class FarSceneDetectionProcessor
{

    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "Models", "anime_halfbody_detection", "halfbody_detect_v1.0_s.onnx");
    public const float ConfidenceThreshold = 0.5f;
    public const float IouThreshold = 0.7f;

    public static double MaximumCoveragePercent(int width, int height, IEnumerable<Detection> detections)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        double maximum = 0;
        foreach (var detection in detections)
        {
            var box = detection.BoundingBox;

            long left = Math.Clamp((long)box.X, 0, width), top = Math.Clamp((long)box.Y, 0, height);
            long right = Math.Clamp((long)box.X + box.Width, 0, width), bottom = Math.Clamp((long)box.Y + box.Height, 0, height);
            double area = Math.Max(0, right - left) * Math.Max(0, bottom - top);
            maximum = Math.Max(maximum, area * 100d / ((double)width * height));
        }
        return maximum;
    }

    public static bool IsCloseUp(double coveragePercent, double thresholdPercent)
    {
        ValidateThreshold(thresholdPercent);
        return coveragePercent > thresholdPercent;
    }

    public static void ValidateThreshold(double thresholdPercent)
    {
        if (!double.IsFinite(thresholdPercent) || thresholdPercent < 0 || thresholdPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(thresholdPercent), "面积阈值必须介于 0% 和 100% 之间。");
    }

    public static void Generate(MaterialIndexBuilder index, double thresholdPercent, IProgress<double>? progress,
        Action<string>? log, CancellationToken token, Func<Image<Rgb24>, IReadOnlyList<Detection>>? detect = null)
    {
        ValidateThreshold(thresholdPercent);
        if (detect == null)
        {
            if (!File.Exists(ModelPath)) throw new FileNotFoundException("半身检测模型不存在，请检查图片三分法的模型目录。", ModelPath);
            detect = image => YoloDetection.Predict(image, ModelPath, ConfidenceThreshold, IouThreshold);
        }
        var images = AestheticFileOrganizer.FindImages(index.InputPath, true, index.OutputPath, token);
        if (images.Count == 0) throw new InvalidOperationException("没有找到支持的图片。");
        int close = 0, distant = 0;
        foreach (string file in images)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var image = Image.Load<Rgb24>(file);
                double coverage = MaximumCoveragePercent(image.Width, image.Height, detect(image));
                token.ThrowIfCancellationRequested();
                bool isClose = IsCloseUp(coverage, thresholdPercent);
                string category = isClose ? "近景" : "远景";
                index.RecordImage(file, Path.Combine(index.OutputPath, category, Path.GetRelativePath(index.InputPath, file)));
                if (isClose) close++; else distant++;
                log?.Invoke($"{Path.GetRelativePath(index.InputPath, file)}：{category}，最大半身占比 {coverage:F2}%");
                progress?.Report((close + distant) * 100d / images.Count);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { throw new IOException($"检测失败：{file}：{ex.Message}", ex); }
        }
        log?.Invoke($"检测完成：近景 {close} 张，远景 {distant} 张。未复制、裁切或移动原图。");
    }
}
