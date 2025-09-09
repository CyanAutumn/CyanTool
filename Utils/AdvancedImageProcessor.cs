using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public class AdvancedImageProcessor
    {
        public Task MirrorImagesAsync(string targetDirectory, IProgress<string> progress)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory))
                {
                    progress.Report("错误: 无效的目标文件夹。");
                    return;
                }

                progress.Report("开始镜像处理...");
                var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };
                var imageFiles = Directory.GetFiles(targetDirectory)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();

                if (!imageFiles.Any())
                {
                    progress.Report("在目标文件夹中未找到支持的图片。");
                    return;
                }

                int successCount = 0;
                foreach (var imagePath in imageFiles)
                {

                    if (Path.GetFileNameWithoutExtension(imagePath).EndsWith("_mirror"))
                    {
                        continue;
                    }

                    try
                    {
                        using (var originalImage = Image.Load(imagePath))
                        {
                            originalImage.Mutate(x => x.Flip(FlipMode.Horizontal));

                            var originalFileName = Path.GetFileNameWithoutExtension(imagePath);
                            var extension = Path.GetExtension(imagePath);
                            var newFileName = $"{originalFileName}_mirror{extension}";
                            var newFilePath = Path.Combine(targetDirectory, newFileName);

                            originalImage.Save(newFilePath);
                            progress.Report($"  -> 已镜像: {Path.GetFileName(imagePath)} -> {newFileName}");


                            var companionPath = Path.ChangeExtension(imagePath, ".txt");
                            if (File.Exists(companionPath))
                            {
                                var newCompanionPath = Path.Combine(targetDirectory, $"{originalFileName}_mirror.txt");
                                File.Copy(companionPath, newCompanionPath, true);
                                progress.Report($"  -> 已复制: {Path.GetFileName(companionPath)} -> {Path.GetFileName(newCompanionPath)}");
                            }
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        progress.Report($"处理失败: {Path.GetFileName(imagePath)}. 原因: {ex.Message}");
                    }
                }
                progress.Report($"--- 镜像处理完成 --- 共处理 {successCount} 张图片。");
            });
        }
    }
}
