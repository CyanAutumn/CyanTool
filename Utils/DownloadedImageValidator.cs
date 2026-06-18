using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;

namespace CyanTools.Utils
{
    public static class DownloadedImageValidator
    {
        private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp",
            ".bmp",
            ".gif",
            ".tif",
            ".tiff"
        };

        public static bool HasSupportedImageExtension(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            return SupportedImageExtensions.Contains(Path.GetExtension(filePath));
        }

        public static bool CanDecodeImage(string filePath)
        {
            try
            {
                if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
                {
                    return false;
                }

                using var image = Image.Load(filePath);
                return image.Width > 0 && image.Height > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
