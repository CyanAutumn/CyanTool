using System;

namespace CyanTools.Utils
{
    public static class UnlockState
    {
        private static bool _downloadersUnlocked;

        public static event Action? DownloadersUnlockedChanged;

        public static bool DownloadersUnlocked
        {
            get => _downloadersUnlocked;
            set
            {
                if (_downloadersUnlocked == value)
                {
                    return;
                }

                _downloadersUnlocked = value;
                DownloadersUnlockedChanged?.Invoke();
            }
        }
    }
}
