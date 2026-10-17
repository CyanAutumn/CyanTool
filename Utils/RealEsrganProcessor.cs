using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace CyanTools.Utils
{
    public class RealEsrganProcessor
    {
        private static readonly string ToolDirectory = Path.Combine(AppContext.BaseDirectory, "realesrgan-ncnn-vulkan-20220424-windows");
        private static readonly string ExecutablePath = Path.Combine(ToolDirectory, "realesrgan-ncnn-vulkan.exe");
        private static readonly string ModelsPath = Path.Combine(ToolDirectory, "models");

        public string[] GetAvailableModels()
        {
            if (!Directory.Exists(ModelsPath))
            {
                return Array.Empty<string>();
            }
            return Directory.GetFiles(ModelsPath, "*.param")
                .Select(Path.GetFileNameWithoutExtension)
                .ToArray();
        }

        public async Task RunAsync(string inputDir, string outputDir, string model, int scale, int gpuId, int tileSize, IProgress<string> progress, CancellationToken token = default)
        {
            if (!File.Exists(ExecutablePath))
            {
                throw new FileNotFoundException($"找不到超分工具，请确保 'realesrgan-ncnn-vulkan.exe' 位于以下路径: {ExecutablePath}");
            }
            if (!Directory.Exists(inputDir))
            {
                throw new DirectoryNotFoundException($"输入目录不存在: {inputDir}");
            }
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Path.Combine(inputDir, "output");
                progress.Report($"输出目录未指定，将使用默认路径: {outputDir}");
            }
            Directory.CreateDirectory(outputDir);

            string arguments = string.Format("-i \"{0}\" -o \"{1}\" -n {2} -s {3} -g {4} -t {5}", 
                inputDir, outputDir, model, scale, gpuId, tileSize);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = arguments,
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
