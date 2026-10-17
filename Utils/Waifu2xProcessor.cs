using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace CyanTools.Utils
{
    public class Waifu2xProcessor
    {
        private static readonly string ToolDirectory = Path.Combine(AppContext.BaseDirectory, "waifu2x-caffe");
        private static readonly string ExecutablePath = Path.Combine(ToolDirectory, "waifu2x-caffe-cui.exe");

        public async Task RunAsync(string inputDir, string outputDir, int scale, int noiseLevel, bool usePhotoModel, int cropSize, IProgress<string> progress, CancellationToken token = default)
        {
            if (!File.Exists(ExecutablePath))
            {
                throw new FileNotFoundException($"找不到超分工具，请确保 'waifu2x-caffe-cui.exe' 位于以下路径: {ExecutablePath}");
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

            var arguments = new StringBuilder();
            arguments.Append($"-i \"{inputDir}\"");
            arguments.Append($" -o \"{outputDir}\"");
            

            arguments.Append(noiseLevel > 0 ? " -m noise_scale" : " -m scale");
            
            arguments.Append($" -s {scale}.0");

            if (noiseLevel > 0)
            {
                arguments.Append($" -n {noiseLevel}");
            }
            if (usePhotoModel)
            {
                arguments.Append(" -p photo");
            }
            if (cropSize > 0)
            {
                arguments.Append($" -c {cropSize}");
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = arguments.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = ToolDirectory,
            };

            await ExternalToolProcess.RunAsync(processStartInfo, progress, token);
        }
    }
}
