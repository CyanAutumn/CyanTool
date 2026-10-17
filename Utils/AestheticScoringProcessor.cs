// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 CyanTools contributors — https://github.com/CyanAutumn/CyanTools
// Upstream portions: Copyright (c) 2023 DeepGHS, MIT; retain its accompanying license.
// Ported directly from deepghs/imgutils generic/classify.py and metrics/dbaesthetic.py.
// See ThirdParty/DeepGHSImgutils/NOTICE.md for the pinned upstream source and license.
using System.IO;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CyanTools.Utils;

public sealed record AestheticPrediction(string Label, double Score, double Percentile, double Confidence,
    IReadOnlyDictionary<string, double> Probabilities);

public sealed class AestheticScoringProcessor : IDisposable
{
    public const string UpstreamRevision = "46df848dc4d20ac93f4919a40e2636d5f7c19766";
    public static IReadOnlyList<string> Labels { get; } = Array.AsReadOnly(
        new[] { "masterpiece", "best", "great", "good", "normal", "low", "worst" });

    private readonly string _modelPath;
    private readonly string[] _modelLabels;
    private readonly int _inputSize;
    private readonly AestheticScoreCalibration _calibration;
    private readonly Action<string>? _log;
    private InferenceSession _session;
    private bool _usingGpu;
    private string _inputName = "";

    public AestheticScoringProcessor(string directory, bool useGpu, Action<string>? log = null)
    {
        _modelPath = Path.Combine(directory, "model.onnx");
        _log = log;
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "meta.json")));
        _modelLabels = metadata.RootElement.GetProperty("labels").EnumerateArray().Select(value => value.GetString()!).ToArray();
        _inputSize = metadata.RootElement.GetProperty("img_size").GetInt32();
        if (_inputSize != 448 || _modelLabels.Length != 7 || !_modelLabels.Order().SequenceEqual(Labels.Order()))
            throw new InvalidDataException("Unsupported DeepGHS aesthetic model metadata.");
        _calibration = AestheticScoreCalibration.Load(Path.Combine(directory, "samples.npz"));
        _session = OpenSession(useGpu);
        try { ValidateModel(); }
        catch { _session.Dispose(); throw; }
    }

    private InferenceSession OpenSession(bool useGpu)
    {
        using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (useGpu)
        {
            try
            {
                TaggingProcessor.TryConfigureCuda12ForCurrentProcess(_log, out _, out _, out _);
                options.AppendExecutionProvider_CUDA();
                options.AppendExecutionProvider_CPU();
                var session = new InferenceSession(_modelPath, options);
                _usingGpu = true;
                _log?.Invoke("GPU (CUDA)");
                return session;
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException)
            {
                _log?.Invoke(LocalizationManager.Translate("GPU 不可用，已切换到 CPU。"));
            }
        }
        using var cpu = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _usingGpu = false;
        var cpuSession = new InferenceSession(_modelPath, cpu);
        _log?.Invoke("CPU");
        return cpuSession;
    }

    private void ValidateModel()
    {
        var input = _session.InputMetadata.Single();
        int[] shape = input.Value.Dimensions;
        if (input.Value.ElementType != typeof(float) || shape.Length != 4
            || shape[1] != 3 || shape[2] != _inputSize || shape[3] != _inputSize
            || !_session.OutputMetadata.TryGetValue("output", out var output)
            || output.ElementType != typeof(float) || output.Dimensions.Length != 2 || output.Dimensions[1] != 7)
            throw new InvalidDataException("Expected the DeepGHS NCHW classifier with a seven-class 'output' tensor.");
        _inputName = input.Key;
    }

    public AestheticPrediction Score(string imagePath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var input = PreprocessImage(imagePath, _inputSize);
        token.ThrowIfCancellationRequested();
        try { return Predict(input, token); }
        catch (OnnxRuntimeException) when (_usingGpu && !token.IsCancellationRequested)
        {
            _log?.Invoke(LocalizationManager.Translate("GPU 推理失败，已切换到 CPU。"));
            _session.Dispose();
            _session = OpenSession(false);
            ValidateModel();
            return Predict(input, token);
        }
    }

    private AestheticPrediction Predict(DenseTensor<float> input, CancellationToken token)
    {
        using var runOptions = new RunOptions();
        using var cancellation = token.Register(() => runOptions.Terminate = true);
        try
        {
            using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, input) },
                new[] { "output" }, runOptions);
            token.ThrowIfCancellationRequested();


            return FromProbabilities(results.Single().AsEnumerable<float>().ToArray(), _modelLabels, _calibration);
        }
        catch (OnnxRuntimeException) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
    }

    public static DenseTensor<float> PreprocessImage(string path, int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        using var image = Image.Load<Rgba32>(path);

        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 pixel = row[x];
                    byte Channel(byte value) => (byte)((value * pixel.A + 255 * (255 - pixel.A) + 127) / 255);
                    row[x] = new Rgba32(Channel(pixel.R), Channel(pixel.G), Channel(pixel.B));
                }
            }
        });

        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(size, size), Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Triangle, Compand = false
        }));
        var tensor = new DenseTensor<float>(new[] { 1, 3, size, size });
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < size; y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = 0; x < size; x++)
                {
                    Rgba32 pixel = row[x];
                    tensor[0, 0, y, x] = Normalize(pixel.R);
                    tensor[0, 1, y, x] = Normalize(pixel.G);
                    tensor[0, 2, y, x] = Normalize(pixel.B);
                }
            }
        });
        return tensor;
    }

    private static float Normalize(byte value) => (float)((value / 255f - 0.5d) / 0.5d);

    public static AestheticPrediction FromProbabilities(IReadOnlyList<float> output, IReadOnlyList<string> labels,
        AestheticScoreCalibration calibration)
    {
        if (output.Count != 7 || labels.Count != 7 || !labels.Order().SequenceEqual(Labels.Order())
            || output.Any(value => !float.IsFinite(value) || value < 0 || value > 1)
            || Math.Abs(output.Sum(value => (double)value) - 1) > 1e-4)
            throw new InvalidDataException("Expected seven DeepGHS class probabilities.");
        var probabilities = labels.Select((label, index) => (label, value: (double)output[index]))
            .ToDictionary(item => item.label, item => item.value);

        double score = Labels.Reverse().Select((label, index) => probabilities[label] * index).Sum();
        double percentile = calibration.ToPercentile(score);
        return new AestheticPrediction(AestheticScoreCalibration.LabelForPercentile(percentile), score, percentile,
            probabilities.Values.Max(), probabilities);
    }

    public void Dispose() => _session.Dispose();
}
