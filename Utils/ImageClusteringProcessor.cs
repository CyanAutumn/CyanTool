using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyanTools.Utils
{
    public class ImageClusteringProcessor
    {
        private const int ProcessorResize = 336;
        private const int ModelInputSize = 224;
        private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
        private static readonly float[] StdDev = { 0.26862954f, 0.26130258f, 0.27577711f };

        private readonly InferenceSession _session;
        private readonly Action<string> _log;
        private readonly string _inputName;
        private readonly string _styleOutputName;
        private readonly string _contentOutputName;

        public ImageClusteringProcessor(string modelPath, bool useGpu, Action<string>? logAction = null)
        {
            _log = logAction ?? (_ => { });
            _session = CreateSession(modelPath, useGpu);
            _inputName = _session.InputMetadata.Keys.First();

            _styleOutputName = "style_output";
            _contentOutputName = "content_output";
        }

        private InferenceSession CreateSession(string modelPath, bool useGpu) {

            if (useGpu) {
                var gpuOptions = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                _log("请求使用 GPU (CUDA) 执行提供器");
                try {
                    TaggingProcessor.TryConfigureCuda12ForCurrentProcess(_log, out _, out _, out _);
                    gpuOptions.AppendExecutionProvider_CUDA();
                    gpuOptions.AppendExecutionProvider_CPU();
                    _log("开始创建 GPU 会话");
                    var gpuSession = new InferenceSession(modelPath, gpuOptions);
                    _log("GPU 会话创建成功");
                    return gpuSession;
                }
                catch (EntryPointNotFoundException ex) {
                    _log($"当前 onnxruntime.dll 不支持 CUDA 执行提供器，回退到 CPU。原因: {ex.Message}");
                }
                catch (DllNotFoundException ex) {
                    _log($"CUDA 依赖缺失，回退到 CPU。原因: {ex.Message}");
                }
                catch (OnnxRuntimeException ex) {
                    _log($"CUDA 执行提供器不可用，回退到 CPU。原因: {ex.Message}");
                }
                catch (Exception ex) {
                    _log($"GPU 会话创建失败，回退到 CPU。原因: {ex.Message}");
                }
            }

            var cpuOptions = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            cpuOptions.AppendExecutionProvider_CPU();
            _log("创建 CPU 会话");
            var cpuSession = new InferenceSession(modelPath, cpuOptions);
            _log("CPU 会话创建成功");
            return cpuSession;
        }

        public List<ImageStyleResult> ProcessFolder(
            string folderPath,
            Action<double> onProgress,
            Action<string> onError,
            IEnumerable<string>? excludedFolders = null)
        {
            try
            {
                var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg",
                    ".jpeg",
                    ".png",
                    ".bmp"
                };

                var excludedSet = BuildExcludedDirectorySet(excludedFolders);


                var imageFiles = EnumerateFilesOsWalkOrder(folderPath, excludedSet)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                    .ToList();

                if (!imageFiles.Any())
                {
                    return new List<ImageStyleResult>();
                }

                var results = new List<ImageStyleResult>();
                for (int i = 0; i < imageFiles.Count; i++)
                {
                    var imagePath = imageFiles[i];
                    try
                    {
                        var inputTensor = PreprocessImage(imagePath);
                        var (styleVec, contentVec) = ExtractEmbeddings(inputTensor);
                        results.Add(new ImageStyleResult { FilePath = imagePath, StyleVector = styleVec, ContentVector = contentVec });
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke($"处理图片失败: {imagePath}\n错误: {ex.Message}");
                    }
                    onProgress?.Invoke((double)(i + 1) / imageFiles.Count * 50);
                }
                return results;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"读取文件时发生错误: {ex.Message}");
                return new List<ImageStyleResult>();
            }
        }

        private static IEnumerable<string> EnumerateFilesOsWalkOrder(string rootPath, HashSet<string> excludedDirs)
        {
            if (IsPathExcluded(rootPath, excludedDirs))
            {
                yield break;
            }


            IEnumerable<string> files = Enumerable.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
            }

            foreach (var file in files)
            {
                yield return file;
            }


            IEnumerable<string> dirs = Enumerable.Empty<string>();
            try
            {
                dirs = Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
            }

            foreach (var dir in dirs)
            {
                if (IsPathExcluded(dir, excludedDirs))
                {
                    continue;
                }

                foreach (var subFile in EnumerateFilesOsWalkOrder(dir, excludedDirs))
                {
                    yield return subFile;
                }
            }
        }

        private static HashSet<string> BuildExcludedDirectorySet(IEnumerable<string>? excludedFolders)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excludedFolders == null)
            {
                return set;
            }

            foreach (var folder in excludedFolders)
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                try
                {
                    set.Add(NormalizeDirectoryPath(folder));
                }
                catch
                {
                }
            }

            return set;
        }

        private static bool IsPathExcluded(string path, HashSet<string> excludedDirs)
        {
            if (excludedDirs.Count == 0)
            {
                return false;
            }

            string normalizedPath;
            try
            {
                normalizedPath = NormalizeDirectoryPath(path);
            }
            catch
            {
                return false;
            }

            foreach (var excluded in excludedDirs)
            {
                if (normalizedPath.Equals(excluded, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (normalizedPath.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeDirectoryPath(string path)
        {
            var full = Path.GetFullPath(path)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var root = Path.GetPathRoot(full)?
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                ?? string.Empty;
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }

            return full.TrimEnd(Path.DirectorySeparatorChar);
        }

        private DenseTensor<Float16> PreprocessImage(string imagePath) {
            byte[] imageBytes = File.ReadAllBytes(imagePath);
            using var ms = new MemoryStream(imageBytes);
            using var original = Image.Load<Rgb24>(ms);

            int canvasSize = Math.Max(original.Width, original.Height);
            using var canvas = new Image<Rgb24>(canvasSize, canvasSize, new Rgb24(255, 255, 255));
            canvas.Mutate(ctx => ctx.DrawImage(original, new Point((canvasSize - original.Width) / 2, (canvasSize - original.Height) / 2), 1f));


            canvas.Mutate(ctx => ctx.Resize(new ResizeOptions {
                Size = new SixLabors.ImageSharp.Size(ProcessorResize, ProcessorResize),
                Sampler = KnownResamplers.Lanczos3
            }).Resize(new ResizeOptions {
                Size = new SixLabors.ImageSharp.Size(ModelInputSize, ModelInputSize),
                Sampler = KnownResamplers.Bicubic
            }));

            var tensor = new DenseTensor<Float16>(new[] { 1, 3, ModelInputSize, ModelInputSize });
            canvas.ProcessPixelRows(accessor => {
                for (int y = 0; y < accessor.Height; y++) {
                    Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++) {
                        Rgb24 pixel = pixelRow[x];
                        tensor[0, 0, y, x] = (Float16)((pixel.R / 255f - Mean[0]) / StdDev[0]);
                        tensor[0, 1, y, x] = (Float16)((pixel.G / 255f - Mean[1]) / StdDev[1]);
                        tensor[0, 2, y, x] = (Float16)((pixel.B / 255f - Mean[2]) / StdDev[2]);
                    }
                }
            });
            return tensor;
        }

        private (float[] styleVector, float[] contentVector) ExtractEmbeddings(DenseTensor<Float16> inputTensor)
        {
            var inputs = new NamedOnnxValue[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) };
            using var results = _session.Run(inputs);

            float[] styleVector = null;
            float[] contentVector = null;

            foreach (var r in results)
            {
                if (r.Name == _styleOutputName)
                {
                    styleVector = r.AsTensor<Float16>().ToArray().Select(v => (float)v).ToArray();
                }
                else if (r.Name == _contentOutputName)
                {
                    contentVector = r.AsTensor<Float16>().ToArray().Select(v => (float)v).ToArray();
                }
            }
            return (styleVector, contentVector);
        }
    }

    public class ImageStyleResult
    {
        public string FilePath { get; set; } = string.Empty;
        public float[] StyleVector { get; set; } = Array.Empty<float>();
        public float[] ContentVector { get; set; } = Array.Empty<float>();
    }
}
