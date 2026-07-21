using CyanTools.Model;
using CyanTools.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.ViewModel
{
    public sealed class SystemMonitorWidgetViewModel : INotifyPropertyChanged
    {
        private CancellationTokenSource? _monitorCancellation;
        private string _monitorStateText = "读取中";
        private string _gpuName = "正在检测 NVIDIA GPU";
        private string _gpuTemperatureText = "--";
        private double _gpuLoadPercent;
        private string _gpuLoadText = "--";
        private double _vramLoadPercent;
        private string _vramText = "--";
        private string _cpuText = "--";
        private string _cpuDetailText = "本机处理器";
        private string _ramText = "--";
        private string _ramDetailText = "--";
        private double _diskLoadPercent;
        private string _diskPathText = "--";
        private string _diskUsageText = "--";
        private string _monitorSampleText = "正在读取本机负载...";

        public string MonitorStateText { get => _monitorStateText; private set => SetField(ref _monitorStateText, value); }
        public string GpuName { get => _gpuName; private set => SetField(ref _gpuName, value); }
        public string GpuTemperatureText { get => _gpuTemperatureText; private set => SetField(ref _gpuTemperatureText, value); }
        public double GpuLoadPercent { get => _gpuLoadPercent; private set => SetField(ref _gpuLoadPercent, value); }
        public string GpuLoadText { get => _gpuLoadText; private set => SetField(ref _gpuLoadText, value); }
        public double VramLoadPercent { get => _vramLoadPercent; private set => SetField(ref _vramLoadPercent, value); }
        public string VramText { get => _vramText; private set => SetField(ref _vramText, value); }
        public string CpuText { get => _cpuText; private set => SetField(ref _cpuText, value); }
        public string CpuDetailText { get => _cpuDetailText; private set => SetField(ref _cpuDetailText, value); }
        public string RamText { get => _ramText; private set => SetField(ref _ramText, value); }
        public string RamDetailText { get => _ramDetailText; private set => SetField(ref _ramDetailText, value); }
        public double DiskLoadPercent { get => _diskLoadPercent; private set => SetField(ref _diskLoadPercent, value); }
        public string DiskPathText { get => _diskPathText; private set => SetField(ref _diskPathText, value); }
        public string DiskUsageText { get => _diskUsageText; private set => SetField(ref _diskUsageText, value); }
        public string MonitorSampleText { get => _monitorSampleText; private set => SetField(ref _monitorSampleText, value); }

        public void StartMonitoring()
        {
            if (_monitorCancellation != null)
            {
                return;
            }

            _monitorCancellation = new CancellationTokenSource();
            MonitorStateText = "读取中";
            MonitorSampleText = "正在读取本机负载...";
            _ = MonitorSystemAsync(_monitorCancellation.Token);
        }

        public void StopMonitoring()
        {
            CancellationTokenSource? cancellation = _monitorCancellation;
            _monitorCancellation = null;
            if (cancellation == null)
            {
                return;
            }

            cancellation.Cancel();
            cancellation.Dispose();
        }

        private async Task MonitorSystemAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TrainingBridgeSystemMetrics metrics = await TrainingBridgeSystemMetricsService.CollectAsync(
                        preferredDiskPath: null,
                        cancellationToken);
                    ApplySystemMetrics(metrics);
                    await Task.Delay(TimeSpan.FromSeconds(2.5), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    MonitorStateText = "离线";
                    MonitorSampleText = "系统监控暂不可用";
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }

        private void ApplySystemMetrics(TrainingBridgeSystemMetrics metrics)
        {
            TrainingBridgeGpuMetrics? gpu = metrics.PrimaryGpu;
            MonitorStateText = "在线";
            GpuName = gpu?.Name ?? "未检测到 NVIDIA GPU";
            GpuTemperatureText = gpu?.TemperatureC is double temperature ? $"{temperature:0.#}°C" : "--";
            GpuLoadPercent = gpu?.LoadPercent ?? 0d;
            GpuLoadText = FormatPercent(gpu?.LoadPercent);
            VramLoadPercent = gpu?.MemoryLoadPercent ?? 0d;
            VramText = FormatGbPair(gpu?.MemoryUsedGb, gpu?.MemoryTotalGb);
            CpuText = FormatPercent(metrics.CpuLoadPercent);
            CpuDetailText = metrics.LogicalProcessors > 0 ? $"{metrics.LogicalProcessors} 线程" : "本机处理器";
            RamText = FormatGbPair(metrics.Memory.UsedGb, metrics.Memory.TotalGb);
            RamDetailText = FormatPercent(metrics.Memory.LoadPercent);
            DiskLoadPercent = metrics.Disk.LoadPercent ?? 0d;
            DiskPathText = string.IsNullOrWhiteSpace(metrics.Disk.Path) ? "--" : metrics.Disk.Path;
            DiskUsageText = FormatGbPair(metrics.Disk.UsedGb, metrics.Disk.TotalGb);
            MonitorSampleText = $"更新于 {metrics.SampledAt:HH:mm:ss}";
        }

        private static string FormatPercent(double? value)
            => value.HasValue ? $"{Math.Clamp(value.Value, 0d, 100d):0.#}%" : "--";

        private static string FormatGbPair(double? used, double? total)
            => used.HasValue && total.HasValue ? $"{used.Value:0.#} GB / {total.Value:0.#} GB" : "--";

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
