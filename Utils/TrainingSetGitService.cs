using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CyanTools.Utils
{
    public interface ITrainingSetGitCommandRunner
    {
        TrainingSetGitCommandResult Run(string repositoryPath, IReadOnlyList<string> arguments);
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

        public TrainingSetGitCommandResult Commit(string repositoryPath, string message)
        {
            var add = AddAll(repositoryPath);
            if (!add.IsSuccess)
            {
                return add;
            }

            return Run(repositoryPath, "commit", "-m", string.IsNullOrWhiteSpace(message) ? "Save training set node" : message);
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

        public string GetCurrentBranch(string repositoryPath)
        {
            var result = Run(repositoryPath, "branch", "--show-current");
            return result.IsSuccess ? result.StandardOutput.Trim() : string.Empty;
        }

        public string GetShortStatus(string repositoryPath)
        {
            var result = Run(repositoryPath, "status", "--short");
            return result.IsSuccess ? result.StandardOutput.Trim() : result.StandardError.Trim();
        }

        private TrainingSetGitCommandResult Run(string repositoryPath, params string[] arguments)
        {
            return _runner.Run(repositoryPath, arguments);
        }
    }

    public sealed class ProcessTrainingSetGitCommandRunner : ITrainingSetGitCommandRunner
    {
        public TrainingSetGitCommandResult Run(string repositoryPath, IReadOnlyList<string> arguments)
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

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new TrainingSetGitCommandResult(-1, string.Empty, "无法启动 git。");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new TrainingSetGitCommandResult(process.ExitCode, stdout, stderr);
        }
    }
}
