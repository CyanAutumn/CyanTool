using CyanTools.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyanTools.Utils
{
    public sealed class LayeredLoraMergeOptions
    {
        public LayeredLoraMergeOptions(
            string modelAPath,
            string modelBPath,
            string outputPath,
            LayeredLoraArchitecture architecture,
            LayeredLoraWeights weights,
            bool overwrite = true)
        {
            ModelAPath = modelAPath;
            ModelBPath = modelBPath;
            OutputPath = outputPath;
            Architecture = architecture;
            Weights = weights;
            Overwrite = overwrite;
        }

        public string ModelAPath { get; }
        public string ModelBPath { get; }
        public string OutputPath { get; }
        public LayeredLoraArchitecture Architecture { get; }
        public LayeredLoraWeights Weights { get; }
        public bool Overwrite { get; }
    }

    public sealed class LayeredLoraMergeReport
    {
        public LayeredLoraMergeReport(
            LayeredLoraArchitecture architecture,
            int mergedTensorCount,
            int copiedTensorCount,
            IReadOnlyDictionary<LoraLayerSlot, int> layerTensorCounts,
            IReadOnlyList<string> warnings)
        {
            Architecture = architecture;
            MergedTensorCount = mergedTensorCount;
            CopiedTensorCount = copiedTensorCount;
            LayerTensorCounts = layerTensorCounts;
            Warnings = warnings;
        }

        public LayeredLoraArchitecture Architecture { get; }
        public int MergedTensorCount { get; }
        public int CopiedTensorCount { get; }
        public IReadOnlyDictionary<LoraLayerSlot, int> LayerTensorCounts { get; }
        public IReadOnlyList<string> Warnings { get; }
    }

    public static class LayeredLoraMerger
    {
        public static LayeredLoraMergeReport Merge(LayeredLoraMergeOptions options)
        {
            if (!File.Exists(options.ModelAPath))
            {
                throw new FileNotFoundException("找不到 LoRA A 文件。", options.ModelAPath);
            }

            if (!File.Exists(options.ModelBPath))
            {
                throw new FileNotFoundException("找不到 LoRA B 文件。", options.ModelBPath);
            }

            if (File.Exists(options.OutputPath) && !options.Overwrite)
            {
                throw new IOException($"输出文件已存在：{options.OutputPath}");
            }

            var modelA = SafetensorsFile.Load(options.ModelAPath);
            var modelB = SafetensorsFile.Load(options.ModelBPath);
            var architecture = ResolveArchitecture(options.Architecture, modelA, modelB);
            int? animaBlockCount = architecture == LayeredLoraArchitecture.AnimaSdxlCompatible
                ? DetectAnimaBlockCount(modelA, modelB)
                : null;

            var outputTensors = new List<SafetensorsTensor>();
            var warnings = new List<string>();
            var layerCounts = new Dictionary<LoraLayerSlot, int>();
            int mergedCount = 0;
            int copiedCount = 0;

            var keys = new SortedSet<string>(modelA.Tensors.Keys, StringComparer.Ordinal);
            keys.UnionWith(modelB.Tensors.Keys);

            foreach (var key in keys)
            {
                modelA.Tensors.TryGetValue(key, out var tensorA);
                modelB.Tensors.TryGetValue(key, out var tensorB);
                var slot = LoraLayerMapper.MapKey(key, architecture, animaBlockCount);
                IncrementLayerCount(layerCounts, slot);
                double weightB = ResolveWeight(key, architecture, slot, options.Weights);
                var merged = MergeTensor(key, tensorA, tensorB, weightB, warnings, ref mergedCount, ref copiedCount);
                outputTensors.Add(merged);
            }

            var metadata = MergeMetadata(modelA.Metadata, modelB.Metadata, architecture, options.Weights);
            SafetensorsFile.Save(options.OutputPath, outputTensors, metadata);

            return new LayeredLoraMergeReport(architecture, mergedCount, copiedCount, layerCounts, warnings);
        }

        private static SafetensorsTensor MergeTensor(
            string key,
            SafetensorsTensor? tensorA,
            SafetensorsTensor? tensorB,
            double weightB,
            List<string> warnings,
            ref int mergedCount,
            ref int copiedCount)
        {
            double weightA = 1d - weightB;
            if (tensorA != null && tensorB != null)
            {
                if (tensorA.HasSameShape(tensorB) && tensorA.IsFloatingPoint && tensorB.IsFloatingPoint)
                {
                    if (!string.Equals(tensorA.DType, tensorB.DType, StringComparison.Ordinal))
                    {
                        warnings.Add($"{key}: dtype 不一致，输出使用 A 的 {tensorA.DType}。");
                    }

                    mergedCount++;
                    return BlendFloatingTensors(key, tensorA, tensorB, weightA, weightB, tensorA.DType);
                }

                copiedCount++;
                warnings.Add($"{key}: 形状或 dtype 不适合插值，按权重选择其中一侧复制。");
                return weightB >= 0.5d ? CloneTensor(key, tensorB) : CloneTensor(key, tensorA);
            }

            if (tensorA != null)
            {
                if (tensorA.IsFloatingPoint)
                {
                    mergedCount++;
                    return ScaleFloatingTensor(key, tensorA, weightA);
                }

                copiedCount++;
                return CloneTensor(key, tensorA);
            }

            if (tensorB != null)
            {
                if (tensorB.IsFloatingPoint)
                {
                    mergedCount++;
                    return ScaleFloatingTensor(key, tensorB, weightB);
                }

                copiedCount++;
                return CloneTensor(key, tensorB);
            }

            throw new InvalidOperationException($"Missing tensor for key {key}.");
        }

        private static SafetensorsTensor BlendFloatingTensors(
            string key,
            SafetensorsTensor tensorA,
            SafetensorsTensor tensorB,
            double weightA,
            double weightB,
            string outputDType)
        {
            var valuesA = tensorA.ToSingleArray();
            var valuesB = tensorB.ToSingleArray();
            if (valuesA.Length != valuesB.Length)
            {
                throw new InvalidDataException($"{key}: tensor element counts do not match.");
            }

            var values = new float[valuesA.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (float)(valuesA[i] * weightA + valuesB[i] * weightB);
            }

            return SafetensorsTensor.FromSingleArray(key, tensorA.Shape, values, outputDType);
        }

        private static SafetensorsTensor ScaleFloatingTensor(string key, SafetensorsTensor tensor, double weight)
        {
            var values = tensor.ToSingleArray();
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (float)(values[i] * weight);
            }

            return SafetensorsTensor.FromSingleArray(key, tensor.Shape, values, tensor.DType);
        }

        private static SafetensorsTensor CloneTensor(string key, SafetensorsTensor tensor)
        {
            return new SafetensorsTensor(key, tensor.DType, tensor.Shape, tensor.Data.ToArray());
        }

        private static LayeredLoraArchitecture ResolveArchitecture(
            LayeredLoraArchitecture requested,
            SafetensorsFile modelA,
            SafetensorsFile modelB)
        {
            if (requested != LayeredLoraArchitecture.Auto)
            {
                return requested;
            }

            int sdxlScore = 0;
            int animaScore = 0;
            foreach (var key in modelA.Tensors.Keys.Concat(modelB.Tensors.Keys))
            {
                var guess = LoraLayerMapper.GuessArchitectureFromKey(key);
                if (guess == LayeredLoraArchitecture.Sdxl)
                {
                    sdxlScore++;
                }
                else if (guess == LayeredLoraArchitecture.Anima)
                {
                    animaScore++;
                }
            }

            if (animaScore > 0 && animaScore >= sdxlScore)
            {
                return LayeredLoraArchitecture.Anima;
            }

            return LayeredLoraArchitecture.Sdxl;
        }

        private static int DetectAnimaBlockCount(SafetensorsFile modelA, SafetensorsFile modelB)
        {
            int maxBlockIndex = -1;
            foreach (var key in modelA.Tensors.Keys.Concat(modelB.Tensors.Keys))
            {
                var index = LoraLayerMapper.TryGetAnimaBlockIndex(key);
                if (index.HasValue)
                {
                    maxBlockIndex = Math.Max(maxBlockIndex, index.Value);
                }
            }

            return maxBlockIndex >= 0 ? maxBlockIndex + 1 : 28;
        }

        private static Dictionary<string, string> MergeMetadata(
            IReadOnlyDictionary<string, string> metadataA,
            IReadOnlyDictionary<string, string> metadataB,
            LayeredLoraArchitecture architecture,
            LayeredLoraWeights weights)
        {
            var metadata = new Dictionary<string, string>(metadataA, StringComparer.Ordinal);
            foreach (var item in metadataB)
            {
                if (!metadata.ContainsKey(item.Key))
                {
                    metadata[item.Key] = item.Value;
                }
            }

            metadata["cyan_merge_type"] = "layered_lora";
            metadata["cyan_merge_architecture"] = architecture.ToString();
            metadata["cyan_merge_base_alpha"] = weights.BaseAlpha.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
            if (architecture == LayeredLoraArchitecture.Anima)
            {
                metadata["cyan_merge_anima_blocks"] = weights.ToAnimaBlockText();
            }
            else
            {
                metadata["cyan_merge_layers"] = weights.ToReportText();
            }

            return metadata;
        }

        private static double ResolveWeight(
            string key,
            LayeredLoraArchitecture architecture,
            LoraLayerSlot slot,
            LayeredLoraWeights weights)
        {
            if (architecture == LayeredLoraArchitecture.Anima)
            {
                var blockIndex = LoraLayerMapper.TryGetAnimaBlockIndex(key);
                if (blockIndex.HasValue)
                {
                    return weights.GetAnimaBlockWeight(blockIndex.Value);
                }
            }

            return weights.GetLayerWeight(slot);
        }

        private static void IncrementLayerCount(Dictionary<LoraLayerSlot, int> counts, LoraLayerSlot slot)
        {
            counts.TryGetValue(slot, out int current);
            counts[slot] = current + 1;
        }
    }
}
