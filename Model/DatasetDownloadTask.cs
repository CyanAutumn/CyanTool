using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Input;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public sealed class DatasetDownloadTask : INotifyPropertyChanged
    {
        private readonly RelayCommand _stopCommand;
        private CancellationTokenSource? _cancellation;
        private bool _isRunning = true;
        private string _statusText = "排队中";
        private string _danbooruSummary = "Danbooru：等待下载";
        private string _pixivSummary = "Pixiv：等待下载";
        private string _kemonoSummary = "Kemono：等待下载";
        private int _danbooruCurrent;
        private int _danbooruTotal;
        private int _pixivCurrent;
        private int _pixivTotal;
        private int _kemonoCurrent;
        private int _kemonoTotal;
        private string _artistName;

        public DatasetDownloadTask(string artistName, bool enableDanbooru, bool enablePixiv, bool enableKemono)
        {
            _artistName = artistName;
            EnableDanbooru = enableDanbooru;
            EnablePixiv = enablePixiv;
            EnableKemono = enableKemono;
            _stopCommand = new RelayCommand(_ => _cancellation?.Cancel(), _ => IsRunning);
        }

        public string ArtistName { get => _artistName; private set => SetField(ref _artistName, value); }
        public string PixivId { get; set; } = string.Empty;
        public bool EnableDanbooru { get; }
        public bool EnablePixiv { get; }
        public bool EnableKemono { get; }
        public string SelectedProxyName { get; set; } = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (!SetField(ref _isRunning, value))
                {
                    return;
                }

                _stopCommand.RaiseCanExecuteChanged();
            }
        }

        public string DanbooruSummary { get => _danbooruSummary; private set => SetField(ref _danbooruSummary, value); }
        public string PixivSummary { get => _pixivSummary; private set => SetField(ref _pixivSummary, value); }
        public string KemonoSummary { get => _kemonoSummary; private set => SetField(ref _kemonoSummary, value); }
        public int DanbooruCurrent { get => _danbooruCurrent; private set => SetField(ref _danbooruCurrent, value); }
        public int DanbooruTotal { get => _danbooruTotal; private set => SetField(ref _danbooruTotal, value); }
        public int PixivCurrent { get => _pixivCurrent; private set => SetField(ref _pixivCurrent, value); }
        public int PixivTotal { get => _pixivTotal; private set => SetField(ref _pixivTotal, value); }
        public int KemonoCurrent { get => _kemonoCurrent; private set => SetField(ref _kemonoCurrent, value); }
        public int KemonoTotal { get => _kemonoTotal; private set => SetField(ref _kemonoTotal, value); }

        public double DanbooruPercent => ToPercent(DanbooruCurrent, DanbooruTotal);
        public double PixivPercent => ToPercent(PixivCurrent, PixivTotal);
        public double KemonoPercent => ToPercent(KemonoCurrent, KemonoTotal);
        public string DanbooruProgressText => FormatProgress(DanbooruCurrent, DanbooruTotal);
        public string PixivProgressText => FormatProgress(PixivCurrent, PixivTotal);
        public string KemonoProgressText => FormatProgress(KemonoCurrent, KemonoTotal);
        public ICommand StopCommand => _stopCommand;

        internal void AttachCancellation(CancellationTokenSource cancellation) => _cancellation = cancellation;

        internal void UpdateFrom(DatasetDownloaderViewModel worker)
        {
            if (!string.IsNullOrWhiteSpace(worker.ResolvedArtistName))
            {
                ArtistName = worker.ResolvedArtistName;
            }
            DanbooruSummary = worker.DanbooruDownloadSummary;
            PixivSummary = worker.PixivDownloadSummary;
            KemonoSummary = worker.KemonoDownloadSummary;
            DanbooruCurrent = worker.DanbooruProgressCurrent;
            DanbooruTotal = worker.DanbooruProgressTotal;
            PixivCurrent = worker.PixivProgressCurrent;
            PixivTotal = worker.PixivProgressTotal;
            KemonoCurrent = worker.KemonoProgressCurrent;
            KemonoTotal = worker.KemonoProgressTotal;
            OnPropertyChanged(nameof(DanbooruPercent));
            OnPropertyChanged(nameof(PixivPercent));
            OnPropertyChanged(nameof(KemonoPercent));
            OnPropertyChanged(nameof(DanbooruProgressText));
            OnPropertyChanged(nameof(PixivProgressText));
            OnPropertyChanged(nameof(KemonoProgressText));
            if (IsRunning)
            {
                StatusText = "下载中";
            }
        }

        private static double ToPercent(int current, int total)
            => total <= 0 ? 0 : Math.Min(100, Math.Max(0, current * 100d / total));

        private static string FormatProgress(int current, int total)
            => total <= 0 ? $"{current}/?" : $"{current}/{total}";

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
