using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace CyanTools.Utils
{
    public class ImagePathThumbnailConverter : IValueConverter
    {
        private const int ThumbnailCacheCapacity = 512;
        private static readonly ThumbnailCache Cache = new ThumbnailCache(ThumbnailCacheCapacity);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BitmapSource bitmapSource)
            {
                return bitmapSource;
            }

            if (value is not string path || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (!File.Exists(path))
            {
                return null;
            }

            int decodeWidth = 96;
            if (parameter is string parameterText && int.TryParse(parameterText, out var parsedWidth))
            {
                decodeWidth = parsedWidth;
            }
            else if (parameter is int parameterWidth)
            {
                decodeWidth = parameterWidth;
            }

            try
            {
                long lastWriteTicks = 0;
                try
                {
                    lastWriteTicks = File.GetLastWriteTimeUtc(path).Ticks;
                }
                catch
                {
                }

                var cacheKey = new ThumbnailCache.ThumbnailKey(path, decodeWidth, lastWriteTicks);
                if (Cache.TryGet(in cacheKey, out var cached) && cached != null)
                {
                    return cached;
                }

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodeWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodeWidth;
                }
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                Cache.Add(in cacheKey, bitmap);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
