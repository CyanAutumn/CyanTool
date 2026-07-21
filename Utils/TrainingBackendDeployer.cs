using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
        DeleteUbuntu
    }

    public sealed record TrainingBackendOptions(
        string BackendId,
        string MirrorId,
        int? LocalProxyPort,
        string? ManagedRoot = null);

    public sealed class TrainingBackendDeployer
    {
        private const string ScriptRelativePath = "scripts\\Install-TrainingBackends.ps1";

        public async Task RunAsync(
            TrainingBackendAction action,
            TrainingBackendOptions options,
            IProgress<string> progress,
            CancellationToken cancellationToken = default)
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

            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("-Backend");
            startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(options.BackendId) ? "diffusion-pipe" : options.BackendId);
            startInfo.ArgumentList.Add("-Action");
            startInfo.ArgumentList.Add(action.ToString());
            startInfo.ArgumentList.Add("-Mirror");
            startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(options.MirrorId) ? "default" : options.MirrorId);

            if (options.LocalProxyPort is int proxyPort && proxyPort > 0)
            {
                startInfo.ArgumentList.Add("-LocalProxyPort");
                startInfo.ArgumentList.Add(proxyPort.ToString());
            }

            if (!string.IsNullOrWhiteSpace(options.ManagedRoot))
            {
                startInfo.ArgumentList.Add("-ManagedRoot");
                startInfo.ArgumentList.Add(options.ManagedRoot);
            }

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    progress.Report(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    progress.Report(args.Data);
                }
            };

            progress.Report($"启动训练后端部署脚本: {options.BackendId} / {action}");
            progress.Report($"脚本路径: {scriptPath}");

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
        }

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
    }
}
