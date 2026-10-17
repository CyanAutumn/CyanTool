using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace CyanTools.Utils
{
    public static class ResolutionFilter
    {
        public class FilterOptions
        {
            public bool IsHeightFilterEnabled { get; set; }
            public int MinHeight { get; set; }
            public bool IsWidthFilterEnabled { get; set; }
            public int MinWidth { get; set; }
            public bool IsResolutionFilterEnabled { get; set; }
            public int MinResolution { get; set; }
        }

        public static void ProcessImages(string inputPath, FilterOptions options, IProgress<double> progress, Action<string> logAction)
        {
            logAction("开始进行分辨率筛选...");
            var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
            var files = Directory.GetFiles(inputPath, "*.*", SearchOption.AllDirectories)
                                 .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLower()))
                                 .ToList();

            int deletedCount = 0;

            for (int i = 0; i < files.Count; i++)
            {
                MaterialClassificationSession.Current?.Token.ThrowIfCancellationRequested();
                var file = files[i];
                bool shouldDelete = false;
                string reason = string.Empty;

                try
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        var bitmapFrame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                        int height = bitmapFrame.PixelHeight;
                        int width = bitmapFrame.PixelWidth;

                        if (options.IsHeightFilterEnabled && height < options.MinHeight)
                        {
                            shouldDelete = true;
                            reason = $"高度 {height}px < {options.MinHeight}px";
                        }
                        else if (options.IsWidthFilterEnabled && width < options.MinWidth)
                        {
                            shouldDelete = true;
                            reason = $"宽度 {width}px < {options.MinWidth}px";
                        }
                        else if (options.IsResolutionFilterEnabled && (long)width * height < options.MinResolution)
                        {
                            shouldDelete = true;
                            reason = $"分辨率 {width}x{height} < {options.MinResolution} pixels";
                        }
                    }

                    if (MaterialClassificationSession.Current?.Builder is { } index)
                    {
                        index.RecordImage(file, Path.Combine(index.OutputPath, shouldDelete ? "排除" : "保留", Path.GetRelativePath(index.InputPath, file)));
                    }
                    else if (shouldDelete)
                    {
                        File.Delete(file);
                        var txtFile = Path.ChangeExtension(file, ".txt");
                        if (!string.IsNullOrEmpty(txtFile) && File.Exists(txtFile))
                        {
                            File.Delete(txtFile);
                            logAction($"已删除: {txtFile} (与图像同名)");
                        }
                        logAction($"已删除: {file} (原因: {reason})");
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    if (MaterialClassificationSession.Current != null) throw;
                    logAction($"错误: 处理文件 {file} 时发生错误: {ex.Message}");
                }

                progress.Report((double)(i + 1) / files.Count * 100);
            }
            logAction(MaterialClassificationSession.Current != null ? "筛选索引已生成：保留 / 排除，未删除图片。" : $"筛选完成。共删除了 {deletedCount} 个文件。");
        }
    }
}
