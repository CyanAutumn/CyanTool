using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Threading.Tasks;
using AutoUpdaterDotNET;
using CyanTools.Localization;
using CyanTools.Utils;

namespace CyanTools
{



    public partial class App : System.Windows.Application
    {
        private const string UpdateRepo = "CyanAutumn/CyanToolsUpdate";
        private const string RepositoryManifestUrl = "https://raw.githubusercontent.com/CyanAutumn/CyanToolsUpdate/main/version.json";
        private const string LegacyVersionXmlUrl = "https://raw.githubusercontent.com/CyanAutumn/CyanToolsUpdate/main/version.xml";
        private static readonly HttpClient UpdateHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        protected override void OnStartup(StartupEventArgs e)
        {
            UserControlInputState.Initialize();
            LocalizationManager.InitializeApplication();
            base.OnStartup(e);
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(InitializeSystemMonitorWidget));
            _ = CheckForRepositoryUpdateOrFallbackAsync();
        }

        private void InitializeSystemMonitorWidget()
        {
            if (MainWindow != null)
            {
                MainWindow.Closing += (_, _) => SystemMonitorWidgetController.PrepareForApplicationShutdown();
            }

            SystemMonitorWidgetController.RestoreFromSettings();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            UserControlInputState.SaveAll();
            ManagedMihomoService.DisposeSharedIfCreated();
            SystemMonitorWidgetController.PrepareForApplicationShutdown();
            SystemMonitorWidgetController.Shutdown();
            base.OnExit(e);
        }

        private async Task CheckForRepositoryUpdateOrFallbackAsync()
        {
            var repositoryUpdaterHandled = await TryCheckRepositoryUpdateAsync();
            if (repositoryUpdaterHandled)
            {
                return;
            }

            StartLegacyUpdater();
        }

        private void StartLegacyUpdater()
        {
            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            AutoUpdater.Start(LegacyVersionXmlUrl);
        }

        private async Task<bool> TryCheckRepositoryUpdateAsync()
        {
            var manifest = await TryFetchRepositoryManifestAsync();
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                return false;
            }

            if (!Version.TryParse(manifest.Version, out var latestVersion))
            {
                return false;
            }

            var installedVersion = GetInstalledVersion();
            if (latestVersion <= installedVersion)
            {
                return true;
            }

            var updateDialogTask = Dispatcher.InvokeAsync(async () =>
            {
                var window = new UpdateWindow(
                    latestVersion.ToString(),
                    installedVersion.ToString(),
                    manifest.ReleaseNotes ?? string.Empty,
                    manifest.Mandatory);

                var result = window.ShowDialog();
                if (result == true)
                {
                    try
                    {
                        if (await DownloadRepositoryUpdateAndRestartAsync(manifest))
                        {
                            Current.Shutdown();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, ex.GetType().ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            });
            await updateDialogTask.Task.Unwrap();

            return true;
        }

        private async void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            if (args.Error != null)
            {
                MessageBox.Show(args.Error.Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!args.IsUpdateAvailable)
            {
                return;
            }

            var releaseNotes = await TryFetchReleaseNotesAsync(
                args.ChangelogURL,
                args.CurrentVersion?.ToString(),
                args.DownloadURL);
            var isMandatory = args.Mandatory != null && args.Mandatory.Value;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = new UpdateWindow(
                    args.CurrentVersion?.ToString() ?? string.Empty,
                    args.InstalledVersion?.ToString() ?? string.Empty,
                    releaseNotes,
                    isMandatory);

                var result = window.ShowDialog();
                if (result == true)
                {
                    try
                    {
                        if (AutoUpdater.DownloadUpdate(args))
                        {
                            Application.Current.Shutdown();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, ex.GetType().ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            });
        }

        private static async Task<RepositoryUpdateManifest?> TryFetchRepositoryManifestAsync()
        {
            try
            {
                using var response = await UpdateHttpClient.GetAsync(RepositoryManifestUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                return await JsonSerializer.DeserializeAsync<RepositoryUpdateManifest>(
                    stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }

        private static Version GetInstalledVersion()
        {
            return typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        }

        private static async Task<bool> DownloadRepositoryUpdateAndRestartAsync(RepositoryUpdateManifest manifest)
        {
            var files = manifest.Files ?? new List<RepositoryUpdateFile>();
            if (files.Count == 0)
            {
                throw new InvalidOperationException("更新清单没有包含任何文件。");
            }

            var targetDirectory = AppContext.BaseDirectory;
            var changedFiles = files
                .Where(file => !IsLocalFileCurrent(targetDirectory, file))
                .ToList();

            if (changedFiles.Count == 0)
            {
                return false;
            }

            var updateRoot = Path.Combine(Path.GetTempPath(), "CyanToolsUpdate", Guid.NewGuid().ToString("N"));
            var payloadDirectory = Path.Combine(updateRoot, "payload");
            Directory.CreateDirectory(payloadDirectory);

            foreach (var file in changedFiles)
            {
                var relativePath = NormalizeManifestPath(file.Path);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var destinationPath = Path.Combine(payloadDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                var downloadUrl = BuildRepositoryFileUrl(manifest, relativePath);
                await DownloadRepositoryFileAsync(downloadUrl, destinationPath, file);
            }

            var manifestPath = Path.Combine(updateRoot, "version.json");
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, manifestJson, Encoding.UTF8);

            var scriptPath = Path.Combine(updateRoot, "apply-update.ps1");
            await File.WriteAllTextAsync(scriptPath, BuildApplyUpdateScript(), new UTF8Encoding(false));

            StartApplyUpdateScript(scriptPath, targetDirectory, payloadDirectory, manifestPath, updateRoot);
            return true;
        }

        private static bool IsLocalFileCurrent(string targetDirectory, RepositoryUpdateFile file)
        {
            var relativePath = NormalizeManifestPath(file.Path);
            if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(file.Sha256))
            {
                return false;
            }

            var localPath = Path.Combine(targetDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
            {
                return false;
            }

            try
            {
                return string.Equals(ComputeFileSha256(localPath), file.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeFileSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }

        private static async Task DownloadRepositoryFileAsync(string url, string destinationPath, RepositoryUpdateFile expectedFile)
        {
            using var response = await UpdateHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output);
            }

            if (!string.IsNullOrWhiteSpace(expectedFile.Sha256)
                && !string.Equals(ComputeFileSha256(destinationPath), expectedFile.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destinationPath);
                throw new InvalidOperationException($"更新文件校验失败：{expectedFile.Path}");
            }
        }

        private static string BuildRepositoryFileUrl(RepositoryUpdateManifest manifest, string relativePath)
        {
            var rawBaseUrl = manifest.RawBaseUrl;
            if (string.IsNullOrWhiteSpace(rawBaseUrl))
            {
                var repo = string.IsNullOrWhiteSpace(manifest.Repo) ? UpdateRepo : manifest.Repo;
                var branch = string.IsNullOrWhiteSpace(manifest.Branch) ? "main" : manifest.Branch;
                var payloadPath = NormalizeManifestPath(manifest.PayloadPath);
                rawBaseUrl = $"https://raw.githubusercontent.com/{repo}/{branch}/{payloadPath}";
            }

            return $"{rawBaseUrl.TrimEnd('/')}/{EscapeManifestPath(relativePath)}";
        }

        private static string NormalizeManifestPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/').Trim('/');
        }

        private static string EscapeManifestPath(string path)
        {
            return string.Join("/", NormalizeManifestPath(path)
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        }

        private static void StartApplyUpdateScript(
            string scriptPath,
            string targetDirectory,
            string payloadDirectory,
            string manifestPath,
            string updateRoot)
        {
            var exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(targetDirectory, "CyanTools.exe");

            var process = Process.GetCurrentProcess();
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("-TargetDir");
            startInfo.ArgumentList.Add(targetDirectory);
            startInfo.ArgumentList.Add("-PayloadDir");
            startInfo.ArgumentList.Add(payloadDirectory);
            startInfo.ArgumentList.Add("-ManifestPath");
            startInfo.ArgumentList.Add(manifestPath);
            startInfo.ArgumentList.Add("-ExePath");
            startInfo.ArgumentList.Add(exePath);
            startInfo.ArgumentList.Add("-ProcessId");
            startInfo.ArgumentList.Add(process.Id.ToString());
            startInfo.ArgumentList.Add("-UpdateRoot");
            startInfo.ArgumentList.Add(updateRoot);

            Process.Start(startInfo);
        }

        private static string BuildApplyUpdateScript()
        {
            return @"
param(
    [Parameter(Mandatory = $true)][string]$TargetDir,
    [Parameter(Mandatory = $true)][string]$PayloadDir,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$UpdateRoot
)

$ErrorActionPreference = 'Stop'

while (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
    Start-Sleep -Milliseconds 500
}

Get-ChildItem -LiteralPath $PayloadDir -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($PayloadDir.Length).TrimStart([char[]]@('\', '/'))
    $target = Join-Path $TargetDir $relative
    $targetParent = Split-Path -Parent $target
    if (-not [string]::IsNullOrWhiteSpace($targetParent)) {
        New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
    }
    Copy-Item -LiteralPath $_.FullName -Destination $target -Force
}

$stateDir = Join-Path $TargetDir '.updates'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $stateDir 'installed_manifest.json') -Force

Start-Process -FilePath $ExePath -WorkingDirectory $TargetDir
Start-Sleep -Seconds 2
Remove-Item -LiteralPath $UpdateRoot -Recurse -Force -ErrorAction SilentlyContinue
";
        }

        private static async Task<string> TryFetchReleaseNotesAsync(string? changelogUrl, string? versionTag, string? downloadUrl)
        {
            var directNotes = await TryFetchTextAsync(changelogUrl);
            if (!string.IsNullOrWhiteSpace(directNotes))
            {
                return directNotes;
            }

            var tag = TryDeriveTagFromDownloadUrl(downloadUrl);
            if (string.IsNullOrWhiteSpace(tag))
            {
                tag = versionTag;
            }

            if (string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            return await TryFetchReleaseNotesFromGitHubAsync(UpdateRepo, tag);
        }

        private static async Task<string> TryFetchTextAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            try
            {
                using var response = await UpdateHttpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return string.Empty;
                }

                return await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static async Task<string> TryFetchReleaseNotesFromGitHubAsync(string repo, string tag)
        {
            try
            {
                var apiUrl = $"https://api.github.com/repos/{repo}/releases/tags/{Uri.EscapeDataString(tag)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.UserAgent.ParseAdd("CyanToolsUpdater/1.0");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var response = await UpdateHttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return string.Empty;
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                if (doc.RootElement.TryGetProperty("body", out var bodyElement) &&
                    bodyElement.ValueKind == JsonValueKind.String)
                {
                    return bodyElement.GetString() ?? string.Empty;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string? TryDeriveTagFromDownloadUrl(string? downloadUrl)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return null;
            }

            try
            {
                var uri = new Uri(downloadUrl);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < segments.Length - 1; i++)
                {
                    if (string.Equals(segments[i], "download", StringComparison.OrdinalIgnoreCase) &&
                        i + 1 < segments.Length)
                    {
                        return segments[i + 1];
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private sealed class RepositoryUpdateManifest
        {
            [JsonPropertyName("version")]
            public string Version { get; set; } = string.Empty;

            [JsonPropertyName("mandatory")]
            public bool Mandatory { get; set; }

            [JsonPropertyName("releaseNotes")]
            public string? ReleaseNotes { get; set; }

            [JsonPropertyName("repo")]
            public string? Repo { get; set; }

            [JsonPropertyName("branch")]
            public string? Branch { get; set; }

            [JsonPropertyName("payloadPath")]
            public string? PayloadPath { get; set; }

            [JsonPropertyName("rawBaseUrl")]
            public string? RawBaseUrl { get; set; }

            [JsonPropertyName("files")]
            public List<RepositoryUpdateFile>? Files { get; set; }
        }

        private sealed class RepositoryUpdateFile
        {
            [JsonPropertyName("path")]
            public string Path { get; set; } = string.Empty;

            [JsonPropertyName("size")]
            public long Size { get; set; }

            [JsonPropertyName("sha256")]
            public string Sha256 { get; set; } = string.Empty;
        }
    }
}
