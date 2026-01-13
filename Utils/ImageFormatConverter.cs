
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageMagick;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Webp;

namespace CyanTools.Utils
{
    public static class ImageFormatConverter
    {
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".psd" };

        public static async Task ConvertAsync(string inputPath, string outputPath, string targetFormat, IProgress<double> progress)
        {
            var files = Directory.GetFiles(inputPath)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToArray();

            if (files.Length == 0)
            {
                progress.Report(100);
                return;
            }

            Directory.CreateDirectory(outputPath);

            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                await Task.Run(async () =>
                {
                    try
                    {
                        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
                        var newFileName = $"{fileNameWithoutExt}.{targetFormat.ToLowerInvariant()}";
                        var newFilePath = Path.Combine(outputPath, newFileName);
                        var extension = Path.GetExtension(file).ToLowerInvariant();

                        if (extension == ".psd")
                        {
                            using var image = new MagickImage(file);
                            image.Format = GetMagickFormat(targetFormat);
                            image.Write(newFilePath);
                        }
                        else
                        {
                            using var image = await Image.LoadAsync(file);
                            var encoder = GetEncoder(targetFormat);
                            await image.SaveAsync(newFilePath, encoder);
                        }


                        if (inputPath != outputPath)
                        {
                            var txtFile = Path.ChangeExtension(file, ".txt");
                            if (File.Exists(txtFile))
                            {
                                var destTxtFile = Path.Combine(outputPath, Path.GetFileName(txtFile));
                                File.Copy(txtFile, destTxtFile, true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {

                        Console.WriteLine($"Failed to convert {file}: {ex.Message}");
                    }
                });

                progress.Report((double)(i + 1) / files.Length * 100);
            }
        }

        private static IImageEncoder GetEncoder(string format)
        {
            switch (format.ToUpperInvariant())
            {
                case "PNG": return new PngEncoder();
                case "JPEG": return new JpegEncoder();
                case "BMP": return new BmpEncoder();
                case "WEBP": return new WebpEncoder();
                default: throw new NotSupportedException("Unsupported image format");
            }
        }

        private static MagickFormat GetMagickFormat(string format)
        {
            switch (format.ToUpperInvariant())
            {
                case "PNG": return MagickFormat.Png;
                case "JPEG": return MagickFormat.Jpeg;
                case "BMP": return MagickFormat.Bmp;
                case "WEBP": return MagickFormat.WebP;
                default: throw new NotSupportedException("Unsupported image format");
            }
        }
    }
}
