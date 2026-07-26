using System;

namespace CyanTools.Utils
{




    public static class DatasetDownloaderCredentialState
    {
        private static readonly object SyncRoot = new object();
        private static bool _hasDanbooruToken;
        private static bool _hasPixivRefreshToken;
        private static string _danbooruToken = string.Empty;
        private static string _pixivRefreshToken = string.Empty;

        public static void PublishDanbooruToken(string? value)
        {
            lock (SyncRoot)
            {
                _danbooruToken = value ?? string.Empty;
                _hasDanbooruToken = true;
            }
        }

        public static void PublishPixivRefreshToken(string? value)
        {
            lock (SyncRoot)
            {
                _pixivRefreshToken = value ?? string.Empty;
                _hasPixivRefreshToken = true;
            }
        }

        public static bool TryGetDanbooruToken(out string value)
        {
            lock (SyncRoot)
            {
                value = _danbooruToken;
                return _hasDanbooruToken;
            }
        }

        public static bool TryGetPixivRefreshToken(out string value)
        {
            lock (SyncRoot)
            {
                value = _pixivRefreshToken;
                return _hasPixivRefreshToken;
            }
        }
    }
}
