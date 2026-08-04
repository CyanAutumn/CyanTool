using CyanTools.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public enum InferenceFrontendFolder
    {
        Root,
        Checkpoint,
        Lora,
        Vae,
        Nodes
    }

    public sealed record InferenceFrontendLaunchPlan(
        string FileName,
        string WorkingDirectory,
        IReadOnlyList<string> Arguments,
        string DisplayCommand);






    public static class InferenceFrontendLaunchPlanner
    {
        public static string GetDefaultCondaEnvironment(string backendId)
            => backendId switch
            {
                RuntimePathBindingBackendIds.ComfyUiWsl => "comfyui-cu130",
                RuntimePathBindingBackendIds.ForgeNeoWsl => "forge-neo-cu128",
                _ => string.Empty
            };

        public static string GetRelativeFolder(string backendId, InferenceFrontendFolder folder)
        {
            if (!RuntimePathBindingBackendIds.IsInferenceFrontend(backendId))
            {
                throw new InvalidOperationException($"运行环境 {backendId} 不是跑图管理后端。");
            }

            return (backendId, folder) switch
            {
                (_, InferenceFrontendFolder.Root) => string.Empty,
                (RuntimePathBindingBackendIds.ComfyUiWindows or RuntimePathBindingBackendIds.ComfyUiWsl,
                    InferenceFrontendFolder.Checkpoint) => Path.Combine("models", "checkpoints"),
                (RuntimePathBindingBackendIds.ComfyUiWindows or RuntimePathBindingBackendIds.ComfyUiWsl,
                    InferenceFrontendFolder.Lora) => Path.Combine("models", "loras"),
                (RuntimePathBindingBackendIds.ComfyUiWindows or RuntimePathBindingBackendIds.ComfyUiWsl,
                    InferenceFrontendFolder.Vae) => Path.Combine("models", "vae"),
                (RuntimePathBindingBackendIds.ComfyUiWindows or RuntimePathBindingBackendIds.ComfyUiWsl,
                    InferenceFrontendFolder.Nodes) => "custom_nodes",
                (_, InferenceFrontendFolder.Checkpoint) => Path.Combine("models", "Stable-diffusion"),
                (_, InferenceFrontendFolder.Lora) => Path.Combine("models", "Lora"),
                (_, InferenceFrontendFolder.Vae) => Path.Combine("models", "VAE"),
                (_, InferenceFrontendFolder.Nodes) => throw new InvalidOperationException("节点文件夹只适用于 ComfyUI。"),
                _ => throw new ArgumentOutOfRangeException(nameof(folder))
            };
        }

        public static string ResolveFolder(
            TrainingBackendBinding binding,
            InferenceFrontendFolder folder,
            WslPathResolver wslPathResolver)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentNullException.ThrowIfNull(wslPathResolver);
            string root = binding.IsWsl
                ? wslPathResolver.ResolveWindowsPath(binding, binding.TrainerPath)
                : Path.GetFullPath(binding.TrainerPath);
            string relative = GetRelativeFolder(binding.BackendId, folder);
            return relative.Length == 0 ? root : Path.Combine(root, relative);
        }

        public static InferenceFrontendLaunchPlan Create(
            TrainingBackendBinding binding,
            Func<string, bool>? fileExists = null,
            string? localAppData = null)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (!RuntimePathBindingBackendIds.IsInferenceFrontend(binding.BackendId))
            {
                throw new InvalidOperationException("请选择 ComfyUI 或 Webui Forge Neo 的路径绑定。");
            }
            if (string.IsNullOrWhiteSpace(binding.TrainerPath))
            {
                throw new InvalidOperationException("跑图管理根目录为空。");
            }

            return binding.IsWsl
                ? CreateWsl(binding)
                : CreateWindows(binding, fileExists ?? File.Exists, localAppData);
        }

        private static InferenceFrontendLaunchPlan CreateWindows(
            TrainingBackendBinding binding,
            Func<string, bool> fileExists,
            string? localAppData)
        {
            string root = Path.GetFullPath(binding.TrainerPath);
            string scriptName = IsComfy(binding.BackendId) ? "main.py" : "launch.py";
            string scriptPath = Path.Combine(root, scriptName);
            string appData = string.IsNullOrWhiteSpace(localAppData)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : localAppData;
            string? parent = Directory.GetParent(root)?.FullName;

            string[] pythonCandidates =
            [
                Path.Combine(root, ".venv", "Scripts", "python.exe"),
                Path.Combine(root, "venv", "Scripts", "python.exe"),
                Path.Combine(root, "python_embeded", "python.exe"),
                parent == null ? string.Empty : Path.Combine(parent, "python_embeded", "python.exe"),
                Path.Combine(appData, "AnimaTrainer", "venvs", binding.BackendId, "Scripts", "python.exe")
            ];
            string python = pythonCandidates.FirstOrDefault(path => path.Length > 0 && fileExists(path))
                ?? "python.exe";
            string[] arguments = IsComfy(binding.BackendId)
                ? [scriptPath, "--listen", "127.0.0.1", "--port", "8188"]
                : [scriptPath, "--listen", "--port", "7860"];
            return new InferenceFrontendLaunchPlan(
                python,
                root,
                arguments,
                FormatDisplayCommand(python, arguments));
        }

        private static InferenceFrontendLaunchPlan CreateWsl(TrainingBackendBinding binding)
        {
            string distribution = string.IsNullOrWhiteSpace(binding.WslDistribution)
                ? "Ubuntu"
                : binding.WslDistribution.Trim();
            string condaEnvironment = string.IsNullOrWhiteSpace(binding.CondaEnvironment)
                ? GetDefaultCondaEnvironment(binding.BackendId)
                : binding.CondaEnvironment.Trim();
            string scriptName = IsComfy(binding.BackendId) ? "main.py" : "launch.py";
            string appArguments = IsComfy(binding.BackendId)
                ? "--listen 127.0.0.1 --port 8188"
                : "--listen --port 7860";
            string rootExpression = ToShellPathExpression(binding.TrainerPath);
            string command =
                "set -e; " +
                "if command -v conda >/dev/null 2>&1; then " +
                "CONDA_BASE=\"$(conda info --base)\"; . \"$CONDA_BASE/etc/profile.d/conda.sh\"; " +
                "elif [ -f \"$HOME/miniconda3/etc/profile.d/conda.sh\" ]; then . \"$HOME/miniconda3/etc/profile.d/conda.sh\"; " +
                "elif [ -f \"$HOME/anaconda3/etc/profile.d/conda.sh\" ]; then . \"$HOME/anaconda3/etc/profile.d/conda.sh\"; " +
                "else echo 'Conda was not found in this WSL distribution.' >&2; exit 127; fi; " +
                $"conda activate {QuoteShell(condaEnvironment)}; " +
                $"cd {rootExpression}; exec env PYTHONUNBUFFERED=1 PYTHONUTF8=1 python {QuoteShell(scriptName)} {appArguments}";
            string[] arguments = ["-d", distribution, "--", "sh", "-lc", command];
            return new InferenceFrontendLaunchPlan(
                "wsl.exe",
                string.Empty,
                arguments,
                FormatDisplayCommand("wsl.exe", arguments));
        }

        private static bool IsComfy(string backendId)
            => backendId is RuntimePathBindingBackendIds.ComfyUiWindows or RuntimePathBindingBackendIds.ComfyUiWsl;

        private static string ToShellPathExpression(string path)
        {
            string value = path.Trim().Replace('\\', '/');
            if (value == "~")
            {
                return "\"$HOME\"";
            }
            if (value.StartsWith("~/", StringComparison.Ordinal))
            {
                return "\"$HOME\"/" + QuoteShell(value[2..]);
            }
            return QuoteShell(value);
        }

        private static string QuoteShell(string value)
            => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

        private static string FormatDisplayCommand(string fileName, IReadOnlyList<string> arguments)
            => string.Join(" ", new[] { fileName }.Concat(arguments.Select(QuoteDisplayArgument)));

        private static string QuoteDisplayArgument(string value)
            => value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;
    }

    public sealed class InferenceFrontendManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, Process> _processes =
            new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public event EventHandler<InferenceFrontendLogEventArgs>? LogReceived;
        public event EventHandler<InferenceFrontendStateEventArgs>? StateChanged;

        public bool IsRunning(string bindingId)
        {
            if (!_processes.TryGetValue(bindingId, out Process? process))
            {
                return false;
            }

            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        public Task StartAsync(TrainingBackendBinding binding)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(binding);
            if (IsRunning(binding.Id))
            {
                throw new InvalidOperationException($"{binding.Name} 已经在运行。");
            }

            var resolver = new WslPathResolver();
            string resolvedRoot = InferenceFrontendLaunchPlanner.ResolveFolder(
                binding,
                InferenceFrontendFolder.Root,
                resolver);
            if (!Directory.Exists(resolvedRoot))
            {
                throw new DirectoryNotFoundException($"跑图管理根目录不存在：{resolvedRoot}");
            }

            string entryScript = Path.Combine(
                resolvedRoot,
                binding.BackendId is RuntimePathBindingBackendIds.ComfyUiWindows or RuntimePathBindingBackendIds.ComfyUiWsl
                    ? "main.py"
                    : "launch.py");
            if (!File.Exists(entryScript))
            {
                throw new FileNotFoundException("跑图管理后端缺少启动入口。", entryScript);
            }

            InferenceFrontendLaunchPlan plan = InferenceFrontendLaunchPlanner.Create(binding);
            var startInfo = new ProcessStartInfo
            {
                FileName = plan.FileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.Environment["PYTHONUNBUFFERED"] = "1";
            startInfo.Environment["PYTHONUTF8"] = "1";
            if (!string.IsNullOrWhiteSpace(plan.WorkingDirectory))
            {
                startInfo.WorkingDirectory = plan.WorkingDirectory;
            }
            foreach (string argument in plan.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (_, args) => PublishLog(binding, args.Data);
            process.ErrorDataReceived += (_, args) => PublishLog(binding, args.Data);
            process.Exited += (_, _) => HandleExited(binding, process);
            if (!_processes.TryAdd(binding.Id, process))
            {
                process.Dispose();
                throw new InvalidOperationException($"{binding.Name} 正在启动。");
            }

            try
            {
                PublishLog(binding, $"启动命令：{plan.DisplayCommand}");
                if (!process.Start())
                {
                    throw new InvalidOperationException("操作系统未启动跑图进程。");
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                StateChanged?.Invoke(this, new InferenceFrontendStateEventArgs(binding.Id, true, null));
                PublishLog(binding, $"已启动，PID {process.Id}。");
                return Task.CompletedTask;
            }
            catch
            {
                _processes.TryRemove(binding.Id, out _);
                process.Dispose();
                throw;
            }
        }

        public async Task StopAsync(TrainingBackendBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (!_processes.TryRemove(binding.Id, out Process? process))
            {
                PublishLog(binding, "当前没有正在运行的进程。");
                StateChanged?.Invoke(this, new InferenceFrontendStateEventArgs(binding.Id, false, null));
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    PublishLog(binding, "正在停止跑图进程…");
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                PublishLog(binding, "跑图进程已停止。");
            }
            finally
            {
                StateChanged?.Invoke(this, new InferenceFrontendStateEventArgs(binding.Id, false, null));
                process.Dispose();
            }
        }

        private void HandleExited(TrainingBackendBinding binding, Process process)
        {
            try
            {
                process.WaitForExit();
                int exitCode = process.ExitCode;
                if (_processes.TryGetValue(binding.Id, out Process? current) && ReferenceEquals(current, process))
                {
                    _processes.TryRemove(binding.Id, out _);
                    PublishLog(binding, $"进程已退出，代码 {exitCode}。");
                    StateChanged?.Invoke(this, new InferenceFrontendStateEventArgs(binding.Id, false, exitCode));
                }
            }
            catch
            {

            }
            finally
            {
                if (!_processes.TryGetValue(binding.Id, out Process? current) || !ReferenceEquals(current, process))
                {
                    process.Dispose();
                }
            }
        }

        private void PublishLog(TrainingBackendBinding binding, string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                LogReceived?.Invoke(this, new InferenceFrontendLogEventArgs(binding.Id, binding.Name, message));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach ((string id, Process process) in _processes.ToArray())
            {
                _processes.TryRemove(id, out _);
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { }
                process.Dispose();
            }
        }
    }

    public sealed class InferenceFrontendLogEventArgs : EventArgs
    {
        public InferenceFrontendLogEventArgs(string bindingId, string bindingName, string message)
        {
            BindingId = bindingId;
            BindingName = bindingName;
            Message = message;
        }

        public string BindingId { get; }
        public string BindingName { get; }
        public string Message { get; }
    }

    public sealed class InferenceFrontendStateEventArgs : EventArgs
    {
        public InferenceFrontendStateEventArgs(string bindingId, bool isRunning, int? exitCode)
        {
            BindingId = bindingId;
            IsRunning = isRunning;
            ExitCode = exitCode;
        }

        public string BindingId { get; }
        public bool IsRunning { get; }
        public int? ExitCode { get; }
    }
}
