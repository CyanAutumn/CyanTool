using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public class Waifu2xProcessor
    {
        private static readonly string ToolDirectory = Path.Combine(AppContext.BaseDirectory, "waifu2x-caffe");
        private static readonly string Waifu2xCuiExecutablePath = Path.Combine(ToolDirectory, "waifu2x-caffe-cui.exe");

        public async Task RunAsync(string inputDir, string outputDir, int noiseLevel, bool usePhotoModel, int cropSize, int scale, IProgress<string> progress)
        {
            if (!File.Exists(Waifu2xCuiExecutablePath))
            {
                throw new FileNotFoundException($"找不到waifu2x-caffe-cui.exe，请确保 'waifu2x-caffe-cui.exe' 位于以下路径: {Waifu2xCuiExecutablePath}");
            }
            if (!Directory.Exists(inputDir))
            {
                throw new DirectoryNotFoundException($"输入目录不存在: {inputDir}");
            }
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Path.Combine(inputDir, "output_waifu2x");
                progress.Report($"输出目录未指定，将使用默认路径: {outputDir}");
            }
            Directory.CreateDirectory(outputDir);

            StringBuilder arguments = new StringBuilder();
            arguments.Append($"-i \"{inputDir}\" ");
            arguments.Append($"-o \"{outputDir}\" ");

            arguments.Append($"-m noise_scale ");

            arguments.Append($"-n {noiseLevel} ");
            arguments.Append($"-s {scale}.0 ");

            if (usePhotoModel)
            {
                arguments.Append($"-y photo ");
            }
            if (cropSize > 0)
            {
                arguments.Append($"-c {cropSize} ");
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = Waifu2xCuiExecutablePath,
                Arguments = arguments.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.OutputDataReceived += (sender, args) =>
                {
                    if(args.Data != null)
                    {
                        progress.Report(args.Data);
                    }
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        progress.Report($"ERROR: {args.Data}");
                    }
                };

                progress.Report($"开始执行: {processStartInfo.FileName} {processStartInfo.Arguments}");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    progress.Report("waifu2x任务成功完成。");
                }
                else
                {
                    progress.Report($"waifu2x进程异常退出，退出代码: {process.ExitCode}。");
                }
            }
        }
    }
}