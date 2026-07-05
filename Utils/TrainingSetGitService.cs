using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public interface ITrainingSetGitCommandRunner
    {
        TrainingSetGitCommandResult Run(string repositoryPath, IReadOnlyList<string> arguments);
        Task<TrainingSetGitCommandResult> RunAsync(string repositoryPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
    }

    public sealed class TrainingSetGitCommandResult
    {
        public TrainingSetGitCommandResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public bool IsSuccess => ExitCode == 0;
    }

    public sealed class TrainingSetGitService
    {
        private readonly ITrainingSetGitCommandRunner _runner;

        public TrainingSetGitService()
            : this(new ProcessTrainingSetGitCommandRunner())
        {
        }

        public TrainingSetGitService(ITrainingSetGitCommandRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public TrainingSetGitCommandResult InitializeRepository(string repositoryPath)
        {
            var init = Run(repositoryPath, "init");
            if (!init.IsSuccess)
            {
                return init;
            }

            return Run(repositoryPath, "lfs", "install", "--local");
        }

        public TrainingSetGitCommandResult AddAll(string repositoryPath)
        {
            return Run(repositoryPath, "add", "-A");
        }

        public Task<TrainingSetGitCommandResult> AddAllAsync(string repositoryPath, CancellationToken cancellationToken = default)
        {
            return RunAsync(repositoryPath, cancellationToken, "add", "-A");
        }

        public TrainingSetGitCommandResult Commit(string repositoryPath, string message)
        {
            var add = AddAll(repositoryPath);
            if (!add.IsSuccess)
            {
                return add;
            }

            return Run(repositoryPath, "commit", "-m", string.IsNullOrWhiteSpace(message) ? "Save training set node" : message);
        }

        public async Task<TrainingSetGitCommandResult> CommitAsync(string repositoryPath, string message, CancellationToken cancellationToken = default)
        {
            var add = await AddAllAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
            if (!add.IsSuccess)
            {
                return add;
            }

            return await RunAsync(
                repositoryPath,
                cancellationToken,
                "commit",
                "-m",
                string.IsNullOrWhiteSpace(message) ? "Save training set node" : message).ConfigureAwait(false);
        }

        public TrainingSetGitCommandResult CheckoutCommit(string repositoryPath, string commit)
        {
            return Run(repositoryPath, "checkout", commit);
        }

        public TrainingSetGitCommandResult CreateBranchFromCommit(string repositoryPath, string branch, string commit)
        {
            return Run(repositoryPath, "checkout", "-b", branch, commit);
        }

        public TrainingSetGitCommandResult Diff(string repositoryPath, string fromCommit, string toCommit, string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? Run(repositoryPath, "diff", fromCommit, toCommit)
                : Run(repositoryPath, "diff", fromCommit, toCommit, "--", path);
        }

        public TrainingSetGitCommandResult ShowFile(string repositoryPath, string commit, string path)
        {
            return Run(repositoryPath, "show", $"{commit}:{path.Replace('\\', '/')}");
        }

        public string GetHeadCommit(string repositoryPath)
        {
            var result = Run(repositoryPath, "rev-parse", "HEAD");
            return result.IsSuccess ? result.StandardOutput.Trim() : string.Empty;
        }

        public async Task<string> GetHeadCommitAsync(string repositoryPath, CancellationToken cancellationToken = default)
        {
            var result = await RunAsync(repositoryPath, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false);
            return result.IsSuccess ? result.StandardOutput.Trim() : string.Empty;
        }

        public string GetCurrentBranch(string repositoryPath)
        {
            var result = Run(repositoryPath, "branch", "--show-current");
            return result.IsSuccess ? result.StandardOutput.Trim() : string.Empty;
        }

        public async Task<string> GetCurrentBranchAsync(string repositoryPath, CancellationToken cancellationToken = default)
        {
            var result = await RunAsync(repositoryPath, cancellationToken, "branch", "--show-current").ConfigureAwait(false);
            return result.IsSuccess ? result.StandardOutput.Trim() : string.Empty;
        }

        public string GetShortStatus(string repositoryPath)
        {
            var result = Run(repositoryPath, "status", "--short");
            return result.IsSuccess ? result.StandardOutput.Trim() : result.StandardError.Trim();
        }

        public async Task<string> GetShortStatusAsync(string repositoryPath, CancellationToken cancellationToken = default)
        {
            var result = await RunAsync(repositoryPath, cancellationToken, "status", "--short").ConfigureAwait(false);
            return result.IsSuccess ? result.StandardOutput.Trim() : result.StandardError.Trim();
        }

        private TrainingSetGitCommandResult Run(string repositoryPath, params string[] arguments)
        {
            return _runner.Run(repositoryPath, arguments);
        }

        private Task<TrainingSetGitCommandResult> RunAsync(string repositoryPath, CancellationToken cancellationToken, params string[] arguments)
        {
            return _runner.RunAsync(repositoryPath, arguments, cancellationToken);
        }
    }

    public sealed class ProcessTrainingSetGitCommandRunner : ITrainingSetGitCommandRunner
    {
        public TrainingSetGitCommandResult Run(string repositoryPath, IReadOnlyList<string> arguments)
        {
            return RunAsync(repositoryPath, arguments).GetAwaiter().GetResult();
        }

        public async Task<TrainingSetGitCommandResult> RunAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            var startInfo = CreateStartInfo(repositoryPath, arguments);
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new TrainingSetGitCommandResult(-1, string.Empty, "无法启动 git。");
            }

            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                return new TrainingSetGitCommandResult(process.ExitCode, stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }
        }

        private static ProcessStartInfo CreateStartInfo(string repositoryPath, IReadOnlyList<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repositoryPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
