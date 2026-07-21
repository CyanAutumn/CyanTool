using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public enum TrainingBackendAction
    {
        Check,
        InstallWsl,
        InstallUbuntu,
        Install,
        Update,
        Reinstall,
        Validate,
        InjectPreview,
        DeleteUbuntu
    }

    public enum TrainingBackendState
    {
        Available,
        Unavailable,
        DetectedButUnusable
    }

    public sealed record TrainingBackendOptions(
        string BackendId,
        string MirrorId,
        int? LocalProxyPort,
        string? ManagedRoot = null,
        bool ForceReinstallDistribution = false);

    public sealed record TrainingBackendStatus
    {
        [JsonPropertyName("backend_id")]
        public string BackendId { get; init; } = "";

        [JsonPropertyName("runtime_target")]
        public string RuntimeTarget { get; init; } = "";

        [JsonPropertyName("state")]
        public string StateValue { get; init; } = "unavailable";

        [JsonPropertyName("summary_zh")]
        public string SummaryZh { get; init; } = "";

        [JsonPropertyName("details")]
        public Dictionary<string, JsonElement> Details { get; init; } = new();

        [JsonIgnore]
        public TrainingBackendState State => StateValue switch
        {
            "available" => TrainingBackendState.Available,
            "detected_but_unusable" => TrainingBackendState.DetectedButUnusable,
            _ => TrainingBackendState.Unavailable
        };
    }

    public sealed class TrainingBackendDeployer
    {
        private const string ScriptRelativePath = "scripts\\Install-TrainingBackends.ps1";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public async Task<TrainingBackendStatus?> RunAsync(
            TrainingBackendAction action,
            TrainingBackendOptions options,
            IProgress<string> progress,
            CancellationToken cancellationToken = default)
        {
            if (action == TrainingBackendAction.Check)
            {
                return await GetStatusAsync(options, progress, cancellationToken);
            }

            string managedRoot = ResolveManagedRoot(options.ManagedRoot);
            var taskStore = new TrainingDeploymentTaskStore(managedRoot);
            TrainingDeploymentTaskRecord task = taskStore.Create(options.BackendId, action.ToString());
            void Report(string message)
            {
                progress.Report(message);
                taskStore.AppendLog(task.Id, message);
            }

            taskStore.UpdateState(task.Id, TrainingDeploymentTaskState.Running, "正在执行部署任务。");
            try
            {
                await RunProcessAsync(action, options with { ManagedRoot = managedRoot }, Report, null, cancellationToken);
                taskStore.UpdateState(task.Id, TrainingDeploymentTaskState.Succeeded, "部署任务已完成。");
                return await GetStatusAsync(options with { ManagedRoot = managedRoot }, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                taskStore.AppendLog(task.Id, "任务已取消。");
                taskStore.UpdateState(task.Id, TrainingDeploymentTaskState.Cancelled, "任务已取消。");
                throw;
            }
            catch (Exception ex)
            {
                taskStore.AppendLog(task.Id, ex.Message);
                taskStore.UpdateState(task.Id, TrainingDeploymentTaskState.Failed, ex.Message);
                throw;
            }
        }

        public async Task<TrainingBackendStatus> GetStatusAsync(
            TrainingBackendOptions options,
            IProgress<string> progress,
            CancellationToken cancellationToken = default)
        {
            string managedRoot = ResolveManagedRoot(options.ManagedRoot);
            string resultDirectory = Path.Combine(managedRoot, "state", "status-results");
            Directory.CreateDirectory(resultDirectory);
            string resultPath = Path.Combine(resultDirectory, $"{options.BackendId}-{Guid.NewGuid():N}.json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            try
            {
                await RunProcessAsync(
                    TrainingBackendAction.Check,
                    options with { ManagedRoot = managedRoot },
                    progress.Report,
                    resultPath,
                    timeout.Token);

                if (!File.Exists(resultPath))
                {
                    throw new InvalidDataException("部署脚本未返回结构化状态。");
                }
                await using FileStream stream = File.OpenRead(resultPath);
                return await JsonSerializer.DeserializeAsync<TrainingBackendStatus>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("部署脚本返回了空状态。");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new TrainingBackendStatus
                {
                    BackendId = options.BackendId,
                    RuntimeTarget = options.BackendId is "sd-scripts" or "anima-fast-lora" ? "windows" : "wsl2",
                    StateValue = "detected_but_unusable",
                    SummaryZh = $"{options.BackendId} 环境检测超时，请稍后重试。",
                    Details = new Dictionary<string, JsonElement>
                    {
                        ["detect_timeout_seconds"] = JsonSerializer.SerializeToElement(6)
                    }
                };
            }
            finally
            {
                TryDelete(resultPath);
            }
        }

        public IReadOnlyList<TrainingDeploymentTaskRecord> RestoreTasksAndLogs(IProgress<string> progress, string? managedRoot = null)
        {
            var store = new TrainingDeploymentTaskStore(ResolveManagedRoot(managedRoot));
            store.RecoverInterruptedTasks();
            IReadOnlyList<TrainingDeploymentTaskRecord> tasks = store.ListAll();
            foreach (TrainingDeploymentTaskRecord task in tasks
                         .GroupBy(item => item.BackendId, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
                         .OrderBy(item => item.UpdatedAt))
            {
                foreach (string line in task.Logs)
                {
                    progress.Report(line);
                }
            }
            return tasks;
        }

        private static async Task RunProcessAsync(
            TrainingBackendAction action,
            TrainingBackendOptions options,
            Action<string> report,
            string? resultPath,
            CancellationToken cancellationToken)
        {
            string scriptPath = ResolveScriptPath();
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory
            };

            AddArgument(startInfo, "-NoLogo");
            AddArgument(startInfo, "-NoProfile");
            AddArgument(startInfo, "-ExecutionPolicy");
            AddArgument(startInfo, "Bypass");
            AddArgument(startInfo, "-File");
            AddArgument(startInfo, scriptPath);
            AddOption(startInfo, "-Backend", string.IsNullOrWhiteSpace(options.BackendId) ? "diffusion-pipe" : options.BackendId);
            AddOption(startInfo, "-Action", action.ToString());
            AddOption(startInfo, "-Mirror", string.IsNullOrWhiteSpace(options.MirrorId) ? "default" : options.MirrorId);
            if (options.LocalProxyPort is int proxyPort && proxyPort > 0)
            {
                AddOption(startInfo, "-LocalProxyPort", proxyPort.ToString());
            }
            if (!string.IsNullOrWhiteSpace(options.ManagedRoot))
            {
                AddOption(startInfo, "-ManagedRoot", options.ManagedRoot);
            }
            if (!string.IsNullOrWhiteSpace(resultPath))
            {
                AddOption(startInfo, "-ResultPath", resultPath);
            }
            if (options.ForceReinstallDistribution)
            {
                AddArgument(startInfo, "-ForceReinstallDistribution");
            }

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => ReportLine(args.Data);
            process.ErrorDataReceived += (_, args) => ReportLine(args.Data);

            report($"启动训练后端部署脚本: {options.BackendId} / {action}");
            report($"脚本路径: {scriptPath}");
            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 PowerShell 部署脚本。");
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(process);
                throw;
            }
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{options.BackendId} 部署脚本失败，退出码 {process.ExitCode}。请查看上方日志。");
            }

            void ReportLine(string? line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    report(line);
                }
            }
        }

        private static void AddArgument(ProcessStartInfo info, string argument) => info.ArgumentList.Add(argument);

        private static void AddOption(ProcessStartInfo info, string name, string value)
        {
            info.ArgumentList.Add(name);
            info.ArgumentList.Add(value);
        }

        private static string ResolveManagedRoot(string? managedRoot) =>
            string.IsNullOrWhiteSpace(managedRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyanTools", "training-backends")
                : Path.GetFullPath(managedRoot);

        private static string ResolveScriptPath()
        {
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, ScriptRelativePath),
                Path.GetFullPath(ScriptRelativePath)
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            throw new FileNotFoundException("未找到训练后端统一部署脚本。", candidates[0]);
        }

        private static void TryKillProcess(Process process)
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

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch { }
        }
    }
}
