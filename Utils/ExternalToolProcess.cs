using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils;


public static class ExternalToolProcess
{
    public static async Task<string> CaptureAsync(ProcessStartInfo startInfo, CancellationToken token = default)
    {
        var output = new CapturedOutput();
        var error = new CapturedOutput();
        await RunCoreAsync(startInfo, output, error, token).ConfigureAwait(false);
        return output.Text + error.Text;
    }

    private sealed class CapturedOutput : IProgress<string>
    {
        private readonly System.Text.StringBuilder _buffer = new();
        public string Text { get { lock (_buffer) return _buffer.ToString(); } }
        public void Report(string line)
        {
            lock (_buffer)
            {
                int remaining = 65536 - _buffer.Length;
                if (remaining > 0) _buffer.Append((line + Environment.NewLine).AsSpan(0, Math.Min(remaining, line.Length + Environment.NewLine.Length)));
            }
        }
    }

    public static async Task RunAsync(ProcessStartInfo startInfo, IProgress<string> progress, CancellationToken token = default)
    {
        await RunCoreAsync(startInfo, progress, progress, token).ConfigureAwait(false);
        progress.Report("任务成功完成。");
    }

    private static async Task RunCoreAsync(ProcessStartInfo startInfo, IProgress<string> output, IProgress<string> error, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, args) => { if (args.Data != null) output.Report(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data != null) error.Report(args.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{startInfo.FileName} 进程异常退出，退出代码: {process.ExitCode}。");
    }
}
