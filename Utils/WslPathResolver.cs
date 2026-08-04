using CyanTools.Model;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace CyanTools.Utils
{





    public sealed class WslPathResolver
    {
        private readonly Func<string, string> _homeDirectoryResolver;
        private readonly ConcurrentDictionary<string, string> _homeDirectories =
            new(StringComparer.OrdinalIgnoreCase);

        public WslPathResolver()
            : this(QueryHomeDirectory)
        {
        }

        public WslPathResolver(Func<string, string> homeDirectoryResolver)
        {
            _homeDirectoryResolver = homeDirectoryResolver ?? throw new ArgumentNullException(nameof(homeDirectoryResolver));
        }

        public string ResolveWindowsPath(TrainingBackendBinding binding, string path)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (!binding.IsWsl || string.IsNullOrWhiteSpace(path))
            {
                return path?.Trim() ?? string.Empty;
            }

            return ResolveWindowsPath(binding.WslDistribution, path);
        }

        public string ResolveWindowsPath(string distribution, string path)
        {
            string normalizedDistribution = NormalizeDistribution(distribution);
            string normalizedPath = path?.Trim() ?? string.Empty;
            if (normalizedPath.Length == 0 || IsWindowsPath(normalizedPath))
            {
                return normalizedPath;
            }

            string linuxPath = ResolveLinuxPath(normalizedDistribution, normalizedPath);
            return BuildUncPath(normalizedDistribution, linuxPath);
        }

        public string ToPortableWslPath(string distribution, string windowsPath)
        {
            string normalizedDistribution = NormalizeDistribution(distribution);
            if (!TryParseWslUncPath(windowsPath, out string uncDistribution, out string linuxPath) ||
                !string.Equals(normalizedDistribution, uncDistribution, StringComparison.OrdinalIgnoreCase))
            {
                return windowsPath;
            }

            string home = GetHomeDirectory(normalizedDistribution);
            if (string.Equals(linuxPath, home, StringComparison.Ordinal))
            {
                return "~";
            }
            if (linuxPath.StartsWith(home.TrimEnd('/') + "/", StringComparison.Ordinal))
            {
                return "~/" + linuxPath[(home.TrimEnd('/').Length + 1)..];
            }

            return linuxPath;
        }

        public string GetHomeDirectory(string distribution)
            => _homeDirectories.GetOrAdd(NormalizeDistribution(distribution), _homeDirectoryResolver);

        private string ResolveLinuxPath(string distribution, string path)
        {
            if (path == "~")
            {
                return GetHomeDirectory(distribution);
            }
            if (path.StartsWith("~/", StringComparison.Ordinal))
            {
                return GetHomeDirectory(distribution).TrimEnd('/') + "/" + path[2..].TrimStart('/');
            }
            if (path.Length > 0 && path[0] == '/')
            {
                return NormalizeLinuxPath(path);
            }

            return GetHomeDirectory(distribution).TrimEnd('/') + "/" + path.Replace('\\', '/').TrimStart('/');
        }

        private static string BuildUncPath(string distribution, string linuxPath)
            => $@"\\wsl.localhost\{distribution}\{linuxPath.TrimStart('/').Replace('/', '\\')}";

        private static bool TryParseWslUncPath(string path, out string distribution, out string linuxPath)
        {
            distribution = string.Empty;
            linuxPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path) ||
                (!path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase) &&
                 !path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            string[] segments = path.TrimStart('\\')
                .Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return false;
            }

            distribution = segments[1];
            linuxPath = segments.Length == 2
                ? "/"
                : "/" + string.Join("/", segments, 2, segments.Length - 2);
            return true;
        }

        private static bool IsWindowsPath(string path)
            => path.StartsWith(@"\\", StringComparison.Ordinal) ||
               (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/'));

        private static string NormalizeDistribution(string distribution)
        {
            string value = string.IsNullOrWhiteSpace(distribution) ? "Ubuntu" : distribution.Trim();
            if (value.IndexOfAny(['\\', '/', '\0']) >= 0)
            {
                throw new InvalidOperationException("WSL 发行版名称无效。");
            }
            return value;
        }

        private static string NormalizeLinuxPath(string path)
        {
            string normalized = path.Replace('\\', '/');
            while (normalized.Contains("//", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
            }
            return normalized;
        }

        private static string QueryHomeDirectory(string distribution)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(distribution);
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("sh");
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add("printf %s \"$HOME\"");

            try
            {
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("无法启动 wsl.exe。");
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch { }
                    throw new TimeoutException($"读取 WSL 发行版 {distribution} 的用户目录超时。");
                }

                string home = process.StandardOutput.ReadToEnd().Trim();
                string error = process.StandardError.ReadToEnd().Trim();
                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(home) || !home.StartsWith('/'))
                {
                    throw new InvalidOperationException(
                        $"无法读取 WSL 发行版 {distribution} 当前用户的 HOME：{error}".TrimEnd('：'));
                }
                return NormalizeLinuxPath(home).TrimEnd('/');
            }
            catch (Exception exception) when (exception is not InvalidOperationException and not TimeoutException)
            {
                throw new InvalidOperationException(
                    $"无法访问 WSL 发行版 {distribution}。请确认该发行版已安装并能正常启动。",
                    exception);
            }
        }
    }
}
