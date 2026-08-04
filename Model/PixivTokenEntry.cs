using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CyanTools.Model
{
    public sealed class PixivTokenEntry : INotifyPropertyChanged
    {
        private string _displayName;
        private string _refreshToken;
        private int _usageCount;

        public PixivTokenEntry(string id, string displayName, string refreshToken, string userDataDirectory)
        {
            Id = id;
            _displayName = string.IsNullOrWhiteSpace(displayName) ? $"Pixiv {id[..Math.Min(6, id.Length)]}" : displayName.Trim();
            _refreshToken = refreshToken?.Trim() ?? string.Empty;
            UserDataDirectory = userDataDirectory;
        }

        public string Id { get; }
        public string DisplayName
        {
            get => _displayName;
            set
            {
                var normalized = value?.Trim() ?? string.Empty;
                if (_displayName == normalized)
                {
                    return;
                }

                _displayName = normalized;
                OnPropertyChanged();
            }
        }

        public string RefreshToken
        {
            get => _refreshToken;
            set
            {
                var normalized = value?.Trim() ?? string.Empty;
                if (_refreshToken == normalized)
                {
                    return;
                }

                _refreshToken = normalized;
                OnPropertyChanged();
            }
        }

        public string UserDataDirectory { get; }
        public int UsageCount
        {
            get => _usageCount;
            private set
            {
                if (_usageCount == value)
                {
                    return;
                }

                _usageCount = Math.Max(0, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInUse));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool IsInUse => UsageCount > 0;
        public string StatusText => IsInUse ? $"{UsageCount}使用中" : "可用";
        public void Acquire() => UsageCount++;
        public void Release() => UsageCount = Math.Max(0, UsageCount - 1);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
