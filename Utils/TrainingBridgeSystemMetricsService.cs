using CyanTools.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public static class TrainingBridgeSystemMetricsService
    {
        private const double BytesPerGb = 1024d * 1024d * 1024d;
        private const double MibPerGb = 1024d;

        public static async Task<TrainingBridgeSystemMetrics> CollectAsync(
            string? preferredDiskPath,
            CancellationToken cancellationToken = default)
        {
            Task<double?> cpuTask = ReadCpuLoadAsync(cancellationToken);
            Task<TrainingBridgeGpuMetrics?> gpuTask = ReadPrimaryGpuAsync(cancellationToken);
            TrainingBridgeMemoryMetrics memory = ReadMemoryMetrics();
            TrainingBridgeDiskMetrics disk = ReadDiskMetrics(preferredDiskPath);

            await Task.WhenAll(cpuTask, gpuTask);
            return new TrainingBridgeSystemMetrics(
                DateTimeOffset.Now,
                await cpuTask,
                Environment.ProcessorCount,
                memory,
                disk,
                await gpuTask);
        }

        internal static TrainingBridgeGpuMetrics? ParsePrimaryGpuCsv(string output)
        {
            string? line = output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            IReadOnlyList<string> fields = ParseCsvLine(line);
            if (fields.Count < 5)
            {
                return null;
            }

            string name = string.IsNullOrWhiteSpace(fields[0]) ? "NVIDIA GPU" : fields[0].Trim();
            double? load = ParseMetric(fields[1]);
            double? memoryUsedMib = ParseMetric(fields[2]);
            double? memoryTotalMib = ParseMetric(fields[3]);
            double? temperature = ParseMetric(fields[4]);
            double? memoryLoad = memoryUsedMib.HasValue && memoryTotalMib > 0
                ? ClampPercent(memoryUsedMib.Value / memoryTotalMib.Value * 100d)
                : null;

            return new TrainingBridgeGpuMetrics(
                name,
                load.HasValue ? ClampPercent(load.Value) : null,
                memoryUsedMib.HasValue ? RoundOne(memoryUsedMib.Value / MibPerGb) : null,
                memoryTotalMib.HasValue ? RoundOne(memoryTotalMib.Value / MibPerGb) : null,
                memoryLoad,
                temperature.HasValue ? RoundOne(temperature.Value) : null);
        }

        private static async Task<double?> ReadCpuLoadAsync(CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows() || !TryReadSystemTimes(out CpuTimes start))
            {
                return null;
            }

            await Task.Delay(180, cancellationToken);
            if (!TryReadSystemTimes(out CpuTimes end))
            {
                return null;
            }

            ulong idle = end.Idle - start.Idle;
            ulong kernel = end.Kernel - start.Kernel;
            ulong user = end.User - start.User;
            ulong total = kernel + user;
            if (total == 0 || idle > total)
            {
                return null;
            }

            return ClampPercent((total - idle) * 100d / total);
        }

        private static TrainingBridgeMemoryMetrics ReadMemoryMetrics()
        {
            if (!OperatingSystem.IsWindows())
            {
                return new TrainingBridgeMemoryMetrics(null, null, null);
            }

            var status = new MemoryStatusEx
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };
            if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
            {
                return new TrainingBridgeMemoryMetrics(null, null, null);
            }

            ulong used = status.TotalPhysical - status.AvailablePhysical;
            return new TrainingBridgeMemoryMetrics(
                RoundOne(used / BytesPerGb),
                RoundOne(status.TotalPhysical / BytesPerGb),
                ClampPercent(status.MemoryLoad));
        }

        private static TrainingBridgeDiskMetrics ReadDiskMetrics(string? preferredDiskPath)
        {
            try
            {
                string root = ResolveDiskRoot(preferredDiskPath);
                var drive = new DriveInfo(root);
                if (!drive.IsReady || drive.TotalSize <= 0)
                {
                    return new TrainingBridgeDiskMetrics(root, null, null, null);
                }

                long used = drive.TotalSize - drive.AvailableFreeSpace;
                return new TrainingBridgeDiskMetrics(
                    drive.RootDirectory.FullName,
                    RoundOne(used / BytesPerGb),
                    RoundOne(drive.TotalSize / BytesPerGb),
                    ClampPercent(used * 100d / drive.TotalSize));
            }
            catch
            {
                return new TrainingBridgeDiskMetrics(string.Empty, null, null, null);
            }
        }

        private static async Task<TrainingBridgeGpuMetrics?> ReadPrimaryGpuAsync(
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu");
            startInfo.ArgumentList.Add("--format=csv,noheader,nounits");

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                string output = await ExternalToolProcess.CaptureAsync(startInfo, timeout.Token).ConfigureAwait(false);
                return ParsePrimaryGpuCsv(output);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveDiskRoot(string? preferredDiskPath)
        {
            string candidate = preferredDiskPath?.Trim().Trim('"') ?? string.Empty;
            if (candidate.StartsWith("/mnt/", StringComparison.OrdinalIgnoreCase) &&
                candidate.Length >= 6 &&
                char.IsLetter(candidate[5]))
            {
                candidate = char.ToUpperInvariant(candidate[5]) + @":\";
            }

            string? root = candidate.Length > 0 ? Path.GetPathRoot(candidate) : null;
            if (!string.IsNullOrWhiteSpace(root))
            {
                return root;
            }

            root = Path.GetPathRoot(Environment.SystemDirectory);
            return string.IsNullOrWhiteSpace(root) ? Directory.GetCurrentDirectory() : root;
        }

        private static bool TryReadSystemTimes(out CpuTimes times)
        {
            times = default;
            if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
            {
                return false;
            }

            times = new CpuTimes(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
            return true;
        }

        private static IReadOnlyList<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int index = 0; index < line.Length; index++)
            {
                char character = line[index];
                if (character == '"')
                {
                    if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (character == ',' && !quoted)
                {
                    fields.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(character);
                }
            }
            fields.Add(current.ToString().Trim());
            return fields;
        }

        private static double? ParseMetric(string text)
            => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : null;

        private static double ClampPercent(double value)
            => RoundOne(Math.Clamp(value, 0d, 100d));

        private static double RoundOne(double value)
            => Math.Round(value, 1, MidpointRounding.AwayFromZero);

        private static ulong ToUInt64(FileTime value)
            => ((ulong)value.High << 32) | value.Low;

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {

            }
        }

        private readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct FileTime
        {
            public readonly uint Low;
            public readonly uint High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(
            out FileTime idleTime,
            out FileTime kernelTime,
            out FileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }
}
