using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace CyanTools.Utils
{
    public sealed class ManagedMihomoService : IDisposable
    {
        internal const string CoreVersion = "1.19.29";
        internal const string ArchiveSha256 = "322AAA5957BA9E72AFDDA9B71CC4329F691D2D45EC39E70BBCA3F7BF5AA93D52";
        internal const string ExecutableSha256 = "82CD796A23492F43A71C1EC27E4E5E0B3D58932014DA5A36E79ED9B11FEE8162";
        private const string RepositoryPath = "MetaCubeX/mihomo";
        private const string LatestVersionUrl = "https://github.com/MetaCubeX/mihomo/releases/latest/download/version.txt";
        private const string ArchiveExecutableName = "mihomo-windows-amd64-compatible.exe";
        private const int MaximumVersionResponseBytes = 128;
        private const int MaximumReleasePageBytes = 12_000_000;
        private static readonly Lazy<ManagedMihomoService> Lazy = new(() => new ManagedMihomoService());
        private static readonly HttpClient DownloadClient = CreateDownloadClient();
        private readonly string _managedRoot;
        private readonly string _coreRoot;
        private readonly string _currentCoreMetadataPath;
        private readonly string _instancesRoot;
        private readonly SemaphoreSlim _installGate = new(1, 1);
        private readonly SemaphoreSlim _instanceGate = new(1, 1);
        private readonly Dictionary<string, ManagedInstance> _instances = new(StringComparer.Ordinal);
        private readonly object _disposeSync = new();
        private volatile bool _disposed;

        public ManagedMihomoService(string? managedRoot = null)
        {
            _managedRoot = string.IsNullOrWhiteSpace(managedRoot)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CyanTools",
                    "mihomo")
                : Path.GetFullPath(managedRoot);
            _coreRoot = Path.Combine(_managedRoot, "core");
            _currentCoreMetadataPath = Path.Combine(_coreRoot, "current.json");
            _instancesRoot = Path.Combine(_managedRoot, "instances");
        }

        public static ManagedMihomoService Shared => Lazy.Value;

        public static void DisposeSharedIfCreated()
        {
            if (Lazy.IsValueCreated)
            {
                Lazy.Value.Dispose();
            }
        }

        public string CoreExecutablePath
            => ResolveInstalledCore()?.ExecutablePath ?? GetVersionExecutablePath(CoreVersion);

        public bool IsCorePresent => ResolveInstalledCore() != null;
        public string? InstalledVersion => ResolveInstalledCore()?.Version;

        public async Task<string> EnsureCoreInstalledAsync(
            CancellationToken cancellationToken = default,
            IProgress<string>? progress = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureSupportedPlatform();
            await _installGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var installed = ResolveInstalledCore();
                if (installed != null
                    && await HasExpectedSha256Async(
                        installed.ExecutablePath,
                        installed.ExecutableSha256,
                        cancellationToken).ConfigureAwait(false))
                {
                    if (!File.Exists(_currentCoreMetadataPath))
                    {
                        WriteCurrentCoreMetadata(new InstalledCoreMetadata
                        {
                            Version = installed.Version,
                            ArchiveSha256 = string.Equals(installed.Version, CoreVersion, StringComparison.Ordinal)
                                ? ArchiveSha256
                                : string.Empty,
                            ExecutableSha256 = installed.ExecutableSha256
                        });
                    }

                    progress?.Report($"Mihomo {installed.Version} 已就绪。");
                    return installed.ExecutablePath;
                }

                if (installed != null && !string.Equals(installed.Version, CoreVersion, StringComparison.Ordinal))
                {
                    try
                    {
                        progress?.Report($"正在修复 Mihomo {installed.Version}...");
                        var release = await GetReleaseAsync(installed.Version, cancellationToken).ConfigureAwait(false);
                        return await InstallReleaseUnderLockAsync(release, cancellationToken, progress).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        progress?.Report($"已安装版本修复失败，正在恢复内置基线版本：{ex.Message}");
                    }
                }

                var baseline = new MihomoCoreRelease(
                    CoreVersion,
                    BuildAssetName(CoreVersion),
                    BuildDownloadUri(CoreVersion),
                    ArchiveSha256);
                return await InstallReleaseUnderLockAsync(
                    baseline,
                    cancellationToken,
                    progress,
                    ExecutableSha256).ConfigureAwait(false);
            }
            finally
            {
                _installGate.Release();
            }
        }

        public async Task<MihomoCoreUpdateInfo> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureSupportedPlatform();
            var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            var installedVersion = InstalledVersion;
            return new MihomoCoreUpdateInfo(
                installedVersion,
                release.Version,
                installedVersion == null || CompareVersions(release.Version, installedVersion) > 0);
        }

        public async Task<MihomoCoreUpdateInfo> InstallLatestAsync(
            CancellationToken cancellationToken = default,
            IProgress<string>? progress = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureSupportedPlatform();
            progress?.Report("正在查询 Mihomo 最新稳定版...");
            var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            await _installGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var installed = ResolveInstalledCore();
                if (installed != null && CompareVersions(installed.Version, release.Version) > 0)
                {
                    progress?.Report($"当前 Mihomo {installed.Version} 高于最新稳定版 {release.Version}，已保留当前版本。");
                    return new MihomoCoreUpdateInfo(installed.Version, release.Version, false);
                }

                if (installed != null
                    && string.Equals(installed.Version, release.Version, StringComparison.Ordinal)
                    && await HasExpectedSha256Async(
                        installed.ExecutablePath,
                        installed.ExecutableSha256,
                        cancellationToken).ConfigureAwait(false))
                {
                    progress?.Report($"Mihomo {release.Version} 已是最新版并通过校验。");
                    return new MihomoCoreUpdateInfo(installed.Version, release.Version, false);
                }

                await InstallReleaseUnderLockAsync(release, cancellationToken, progress).ConfigureAwait(false);
                return new MihomoCoreUpdateInfo(release.Version, release.Version, false);
            }
            finally
            {
                _installGate.Release();
            }
        }

        private async Task<string> InstallReleaseUnderLockAsync(
            MihomoCoreRelease release,
            CancellationToken cancellationToken,
            IProgress<string>? progress,
            string? expectedExecutableSha256 = null)
        {
            var targetExecutablePath = GetVersionExecutablePath(release.Version);
            Directory.CreateDirectory(Path.GetDirectoryName(targetExecutablePath)!);
            Directory.CreateDirectory(_managedRoot);
            var temporaryArchive = Path.Combine(_managedRoot, $"mihomo-{Guid.NewGuid():N}.zip");
            var temporaryExecutable = targetExecutablePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                progress?.Report($"正在下载 Mihomo {release.Version}...");
                using (var response = await DownloadClient.GetAsync(
                           release.DownloadUri,
                           HttpCompletionOption.ResponseHeadersRead,
                           cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var output = new FileStream(
                        temporaryArchive,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        useAsync: true);
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                if (!await HasExpectedSha256Async(
                    temporaryArchive,
                    release.ArchiveSha256,
                    cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException("Mihomo 下载包 SHA-256 与官方发布页不一致，已拒绝执行。");
                }

                progress?.Report($"正在校验并安装 Mihomo {release.Version}...");
                using (var archive = ZipFile.OpenRead(temporaryArchive))
                {
                    var executableEntry = archive.Entries.SingleOrDefault(entry =>
                        string.Equals(entry.Name, ArchiveExecutableName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(entry.FullName, entry.Name, StringComparison.Ordinal));
                    if (executableEntry == null || executableEntry.Length <= 0)
                    {
                        throw new InvalidDataException("Mihomo 下载包中缺少预期的可执行文件。");
                    }

                    await using var input = executableEntry.Open();
                    await using var output = new FileStream(
                        temporaryExecutable,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        useAsync: true);
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                var executableSha256 = await ComputeSha256Async(temporaryExecutable, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(expectedExecutableSha256)
                    && !string.Equals(executableSha256, expectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Mihomo 可执行文件 SHA-256 校验失败，已拒绝执行。");
                }

                await VerifyExecutableVersionAsync(
                    temporaryExecutable,
                    release.Version,
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporaryExecutable, targetExecutablePath, overwrite: true);
                WriteCurrentCoreMetadata(new InstalledCoreMetadata
                {
                    Version = release.Version,
                    ArchiveSha256 = release.ArchiveSha256,
                    ExecutableSha256 = executableSha256
                });
                progress?.Report($"Mihomo {release.Version} 已安装并通过官方 SHA-256 与版本校验。");
                return targetExecutablePath;
            }
            finally
            {
                TryDeleteFile(temporaryArchive);
                TryDeleteFile(temporaryExecutable);
            }
        }

        public async Task<ManagedMihomoLease> AcquireAsync(
            ProxyNode node,
            CancellationToken cancellationToken = default,
            IProgress<string>? progress = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(node);
            if (!node.UsesManagedCore)
            {
                throw new InvalidOperationException("所选节点没有可供托管内核使用的完整配置，请重新更新订阅。");
            }

            var executablePath = await EnsureCoreInstalledAsync(cancellationToken, progress).ConfigureAwait(false);
            var instanceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{executablePath}\n{node.MihomoProxyYaml}")));
            await _instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_instances.TryGetValue(instanceKey, out var existing) && existing.IsRunning)
                {
                    existing.ReferenceCount++;
                    return new ManagedMihomoLease(this, instanceKey, existing.TransportNode);
                }

                if (existing != null)
                {
                    _instances.Remove(instanceKey);
                    existing.Dispose();
                    TryDeleteDirectory(existing.WorkingDirectory);
                }

                progress?.Report($"正在为节点 {node.Name} 启动托管内核...");
                var started = await StartInstanceAsync(executablePath, node, cancellationToken).ConfigureAwait(false);
                started.ReferenceCount = 1;
                _instances[instanceKey] = started;
                progress?.Report($"托管内核已就绪：127.0.0.1:{started.Port}");
                return new ManagedMihomoLease(this, instanceKey, started.TransportNode);
            }
            finally
            {
                _instanceGate.Release();
            }
        }

        internal static string BuildRuntimeConfig(ProxyNode node, int mixedPort)
        {
            if (!node.UsesManagedCore || mixedPort is <= 0 or > 65535)
            {
                throw new ArgumentException("节点配置或托管端口无效。", nameof(node));
            }

            var deserializer = new DeserializerBuilder().Build();
            var proxy = deserializer.Deserialize<Dictionary<object, object>>(node.MihomoProxyYaml)
                ?? throw new InvalidDataException("节点 YAML 配置为空。");
            SetMappingValue(proxy, "name", node.Name);

            var config = new Dictionary<string, object>
            {
                ["mixed-port"] = mixedPort,
                ["allow-lan"] = false,
                ["bind-address"] = "127.0.0.1",
                ["mode"] = "rule",
                ["log-level"] = "warning",
                ["ipv6"] = false,
                ["find-process-mode"] = "off",
                ["proxies"] = new object[] { proxy },
                ["proxy-groups"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "CYANTOOLS",
                        ["type"] = "select",
                        ["proxies"] = new[] { node.Name }
                    }
                },
                ["rules"] = new[] { "MATCH,CYANTOOLS" }
            };

            return new SerializerBuilder()
                .DisableAliases()
                .Build()
                .Serialize(config);
        }

        private async Task<ManagedInstance> StartInstanceAsync(
            string executablePath,
            ProxyNode node,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_instancesRoot);
            var workingDirectory = Path.Combine(_instancesRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);
            var configPath = Path.Combine(workingDirectory, "config.yaml");
            Process? process = null;
            try
            {
                var port = ReserveLoopbackPort();
                File.WriteAllText(configPath, BuildRuntimeConfig(node, port), new UTF8Encoding(false));
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                startInfo.ArgumentList.Add("-d");
                startInfo.ArgumentList.Add(workingDirectory);
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add(configPath);

                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, _) => { };
                process.ErrorDataReceived += (_, _) => { };
                if (!process.Start())
                {
                    throw new InvalidOperationException("操作系统未能启动 Mihomo 内核。");
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await WaitForPortAsync(process, port, cancellationToken).ConfigureAwait(false);
                TryDeleteFile(configPath);

                var transportNode = new ProxyNode(
                    $"managed-{node.Id}",
                    node.Name,
                    "http",
                    IPAddress.Loopback.ToString(),
                    port);
                return new ManagedInstance(process, workingDirectory, port, transportNode);
            }
            catch
            {
                if (process != null)
                {
                    TryStopProcess(process);
                    process.Dispose();
                }
                TryDeleteFile(configPath);
                TryDeleteDirectory(workingDirectory);
                throw;
            }
        }

        private async ValueTask ReleaseAsync(string instanceKey)
        {
            if (_disposed)
            {
                return;
            }

            ManagedInstance? instance = null;
            await _instanceGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    return;
                }

                if (!_instances.TryGetValue(instanceKey, out var current))
                {
                    return;
                }

                current.ReferenceCount = Math.Max(0, current.ReferenceCount - 1);
                if (current.ReferenceCount == 0)
                {
                    _instances.Remove(instanceKey);
                    instance = current;
                }
            }
            finally
            {
                _instanceGate.Release();
            }

            if (instance != null)
            {
                await instance.StopAsync().ConfigureAwait(false);
                TryDeleteDirectory(instance.WorkingDirectory);
            }
        }

        private static async Task WaitForPortAsync(Process process, int port, CancellationToken cancellationToken)
        {
            using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTimeout.CancelAfter(TimeSpan.FromSeconds(12));
            while (true)
            {
                startupTimeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    throw new InvalidOperationException($"Mihomo 内核启动失败，退出代码 {process.ExitCode}。");
                }

                try
                {
                    using var client = new TcpClient();
                    using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(startupTimeout.Token);
                    connectTimeout.CancelAfter(TimeSpan.FromMilliseconds(300));
                    await client.ConnectAsync(IPAddress.Loopback, port, connectTimeout.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (!startupTimeout.IsCancellationRequested)
                {
                }
                catch (SocketException)
                {
                }

                await Task.Delay(100, startupTimeout.Token).ConfigureAwait(false);
            }
        }

        private static int ReserveLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static void SetMappingValue(Dictionary<object, object> mapping, string key, object value)
        {
            var existingKey = mapping.Keys.FirstOrDefault(candidate =>
                string.Equals(Convert.ToString(candidate, CultureInfo.InvariantCulture), key, StringComparison.OrdinalIgnoreCase));
            if (existingKey != null)
            {
                mapping[existingKey] = value;
            }
            else
            {
                mapping[key] = value;
            }
        }

        private async Task<MihomoCoreRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            using var response = await DownloadClient.GetAsync(
                LatestVersionUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var tag = (await ReadUtf8TextWithLimitAsync(
                response.Content,
                MaximumVersionResponseBytes,
                cancellationToken).ConfigureAwait(false)).Trim();
            return await GetReleaseAsync(NormalizeVersion(tag), cancellationToken).ConfigureAwait(false);
        }

        private async Task<MihomoCoreRelease> GetReleaseAsync(
            string version,
            CancellationToken cancellationToken)
        {
            var normalizedVersion = NormalizeVersion(version);
            var assetName = BuildAssetName(normalizedVersion);
            var releasePageUri = new Uri(
                $"https://github.com/{RepositoryPath}/releases/expanded_assets/v{normalizedVersion}",
                UriKind.Absolute);
            using var response = await DownloadClient.GetAsync(
                releasePageUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            var html = await ReadUtf8TextWithLimitAsync(
                response.Content,
                MaximumReleasePageBytes,
                cancellationToken).ConfigureAwait(false);

            var digest = ParseReleaseAssetSha256(html, assetName);
            return new MihomoCoreRelease(
                normalizedVersion,
                assetName,
                BuildDownloadUri(normalizedVersion),
                digest);
        }

        internal static string ParseReleaseAssetSha256(string html, string assetName)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(assetName))
            {
                throw new InvalidDataException("Mihomo 官方发布页缺少资产信息。");
            }

            var escapedAssetName = Regex.Escape(assetName);
            var assetPattern = $"<li\\b[^>]*>(?:(?!</li>).)*?{escapedAssetName}(?:(?!</li>).)*?</li>";
            var assetMatch = Regex.Match(
                html,
                assetPattern,
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(2));
            if (!assetMatch.Success)
            {
                throw new InvalidDataException("未在 Mihomo 官方发布页找到 Windows x64 compatible 资产。");
            }

            var digestMatch = Regex.Match(
                assetMatch.Value,
                "sha256:(?<hash>[0-9a-f]{64})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            if (!digestMatch.Success)
            {
                throw new InvalidDataException("未在 Mihomo 官方发布页找到该资产的 SHA-256。");
            }

            return digestMatch.Groups["hash"].Value.ToUpperInvariant();
        }

        internal static string NormalizeVersion(string value)
        {
            var normalized = value.Trim().TrimStart('v', 'V');
            if (!Regex.IsMatch(
                normalized,
                "^[0-9]+\\.[0-9]+\\.[0-9]+$",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            {
                throw new InvalidDataException("Mihomo 发布版本号格式无效。");
            }

            return normalized;
        }

        private InstalledCore? ResolveInstalledCore()
        {
            try
            {
                if (File.Exists(_currentCoreMetadataPath))
                {
                    var metadata = JsonSerializer.Deserialize<InstalledCoreMetadata>(
                        File.ReadAllText(_currentCoreMetadataPath));
                    if (metadata != null
                        && TryNormalizeVersion(metadata.Version, out var version)
                        && IsSha256(metadata.ExecutableSha256))
                    {
                        var executablePath = GetVersionExecutablePath(version);
                        if (File.Exists(executablePath))
                        {
                            return new InstalledCore(version, executablePath, metadata.ExecutableSha256);
                        }
                    }
                }

                var baselinePath = GetVersionExecutablePath(CoreVersion);
                if (File.Exists(baselinePath))
                {
                    return new InstalledCore(CoreVersion, baselinePath, ExecutableSha256);
                }
            }
            catch
            {
            }

            return null;
        }

        private void WriteCurrentCoreMetadata(InstalledCoreMetadata metadata)
        {
            Directory.CreateDirectory(_coreRoot);
            var temporaryPath = _currentCoreMetadataPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
                File.Move(temporaryPath, _currentCoreMetadataPath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }

        private string GetVersionExecutablePath(string version)
            => Path.Combine(_coreRoot, NormalizeVersion(version), "mihomo.exe");

        private static string BuildAssetName(string version)
            => $"mihomo-windows-amd64-compatible-v{NormalizeVersion(version)}.zip";

        private static Uri BuildDownloadUri(string version)
        {
            var normalizedVersion = NormalizeVersion(version);
            return new Uri(
                $"https://github.com/{RepositoryPath}/releases/download/v{normalizedVersion}/{BuildAssetName(normalizedVersion)}",
                UriKind.Absolute);
        }

        private static bool TryNormalizeVersion(string? value, out string version)
        {
            try
            {
                version = NormalizeVersion(value ?? string.Empty);
                return true;
            }
            catch
            {
                version = string.Empty;
                return false;
            }
        }

        private static int CompareVersions(string left, string right)
            => Version.Parse(NormalizeVersion(left)).CompareTo(Version.Parse(NormalizeVersion(right)));

        private static bool IsSha256(string? value)
            => value != null && Regex.IsMatch(
                value,
                "^[0-9a-f]{64}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

        private static async Task VerifyExecutableVersionAsync(
            string executablePath,
            string expectedVersion,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("-v");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            string output = await ExternalToolProcess.CaptureAsync(startInfo, timeout.Token).ConfigureAwait(false);
            if (!Regex.IsMatch(
                    output,
                    $"\\bv{Regex.Escape(NormalizeVersion(expectedVersion))}\\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)))
            {
                throw new InvalidDataException("Mihomo 可执行文件报告的版本与目标发布不一致。");
            }
        }

        private static async Task<string> ComputeSha256Async(
            string filePath,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }

        private static async Task<string> ReadUtf8TextWithLimitAsync(
            HttpContent content,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength > maximumBytes)
            {
                throw new InvalidDataException("远程响应超出安全大小限制。");
            }

            await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream(Math.Min(maximumBytes, 81920));
            var chunk = new byte[81920];
            while (true)
            {
                var read = await input.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > maximumBytes)
                {
                    throw new InvalidDataException("远程响应超出安全大小限制。");
                }

                buffer.Write(chunk, 0, read);
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        }

        private static async Task<bool> HasExpectedSha256Async(
            string filePath,
            string expected,
            CancellationToken cancellationToken)
        {
            var actual = await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static HttpClient CreateDownloadClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CyanTools/1.0");
            return client;
        }

        private static void EnsureSupportedPlatform()
        {
            if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
            {
                throw new PlatformNotSupportedException("托管 Mihomo 当前仅支持 64 位 Windows。");
            }
        }

        private static void TryStopProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, recursive: true);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            lock (_disposeSync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            ManagedInstance[] instances;
            _instanceGate.Wait();
            try
            {
                instances = _instances.Values.ToArray();
                _instances.Clear();
            }
            finally
            {
                _instanceGate.Release();
            }

            foreach (var instance in instances)
            {
                instance.Dispose();
                TryDeleteDirectory(instance.WorkingDirectory);
            }
        }

        public sealed record MihomoCoreUpdateInfo(
            string? InstalledVersion,
            string LatestVersion,
            bool IsUpdateAvailable);

        private sealed record MihomoCoreRelease(
            string Version,
            string AssetName,
            Uri DownloadUri,
            string ArchiveSha256);

        private sealed record InstalledCore(
            string Version,
            string ExecutablePath,
            string ExecutableSha256);

        private sealed class InstalledCoreMetadata
        {
            public string Version { get; set; } = string.Empty;
            public string ArchiveSha256 { get; set; } = string.Empty;
            public string ExecutableSha256 { get; set; } = string.Empty;
        }

        private sealed class ManagedInstance : IDisposable
        {
            public ManagedInstance(Process process, string workingDirectory, int port, ProxyNode transportNode)
            {
                Process = process;
                WorkingDirectory = workingDirectory;
                Port = port;
                TransportNode = transportNode;
            }

            public Process Process { get; }
            public string WorkingDirectory { get; }
            public int Port { get; }
            public ProxyNode TransportNode { get; }
            public int ReferenceCount { get; set; }
            public bool IsRunning
            {
                get
                {
                    try
                    {
                        return !Process.HasExited;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            public async Task StopAsync()
            {
                try
                {
                    if (!Process.HasExited)
                    {
                        Process.Kill(entireProcessTree: true);
                        await Process.WaitForExitAsync().ConfigureAwait(false);
                    }
                }
                catch
                {
                }
                finally
                {
                    Process.Dispose();
                }
            }

            public void Dispose()
            {
                TryStopProcess(Process);
                Process.Dispose();
            }
        }

        public sealed class ManagedMihomoLease : IAsyncDisposable
        {
            private ManagedMihomoService? _owner;
            private readonly string _instanceKey;

            internal ManagedMihomoLease(ManagedMihomoService owner, string instanceKey, ProxyNode transportNode)
            {
                _owner = owner;
                _instanceKey = instanceKey;
                TransportNode = transportNode;
            }

            public ProxyNode TransportNode { get; }

            public async ValueTask DisposeAsync()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null)
                {
                    await owner.ReleaseAsync(_instanceKey).ConfigureAwait(false);
                }
            }
        }
    }
}
