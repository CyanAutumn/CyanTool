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
            bool overwrite = true,
            bool repairAnimaFullModelKeys = false)
        {
            ModelAPath = modelAPath;
            ModelBPath = modelBPath;
            OutputPath = outputPath;
            Architecture = architecture;
            Weights = weights;
            Overwrite = overwrite;
            RepairAnimaFullModelKeys = repairAnimaFullModelKeys;
        }

        public string ModelAPath { get; }
        public string ModelBPath { get; }
        public string OutputPath { get; }
        public LayeredLoraArchitecture Architecture { get; }
        public LayeredLoraWeights Weights { get; }
        public bool Overwrite { get; }
        public bool RepairAnimaFullModelKeys { get; }
    }

    public sealed class LayeredLoraMergeReport
    {
        public LayeredLoraMergeReport(
            LayeredLoraArchitecture architecture,
            int mergedTensorCount,
            int copiedTensorCount,
            IReadOnlyDictionary<LoraLayerSlot, int> layerTensorCounts,
            int animaBlockTensorCount,
            int animaFullModelKeyRepairTensorCount,
            IReadOnlyList<string> warnings)
        {
            Architecture = architecture;
            MergedTensorCount = mergedTensorCount;
            CopiedTensorCount = copiedTensorCount;
            LayerTensorCounts = layerTensorCounts;
            AnimaBlockTensorCount = animaBlockTensorCount;
            AnimaFullModelKeyRepairTensorCount = animaFullModelKeyRepairTensorCount;
            Warnings = warnings;
        }

        public LayeredLoraArchitecture Architecture { get; }
        public int MergedTensorCount { get; }
        public int CopiedTensorCount { get; }
        public IReadOnlyDictionary<LoraLayerSlot, int> LayerTensorCounts { get; }
        public int AnimaBlockTensorCount { get; }
        public int AnimaFullModelKeyRepairTensorCount { get; }
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
            var warnings = new List<string>();
            int animaFullModelKeyRepairTensorCount = 0;
            if (options.RepairAnimaFullModelKeys && architecture == LayeredLoraArchitecture.AnimaFullModel)
            {
                modelB = RepairAnimaFullModelKeysByShape(modelA, modelB, warnings, out animaFullModelKeyRepairTensorCount);
            }

            int? animaBlockCount = architecture == LayeredLoraArchitecture.AnimaSdxlCompatible
                ? DetectAnimaBlockCount(modelA, modelB)
                : null;

            var outputTensors = new List<SafetensorsTensor>();
            var layerCounts = new Dictionary<LoraLayerSlot, int>();
            int mergedCount = 0;
            int copiedCount = 0;
            int animaBlockTensorCount = 0;

            var keys = new SortedSet<string>(modelA.Tensors.Keys, StringComparer.Ordinal);
            keys.UnionWith(modelB.Tensors.Keys);

            foreach (var key in keys)
            {
                modelA.Tensors.TryGetValue(key, out var tensorA);
                modelB.Tensors.TryGetValue(key, out var tensorB);
                var slot = LoraLayerMapper.MapKey(key, architecture, animaBlockCount);
                IncrementLayerCount(layerCounts, slot);
                if (UsesDirectAnimaBlockWeight(key, architecture))
                {
                    animaBlockTensorCount++;
                }

                double weightB = ResolveWeight(key, architecture, slot, options.Weights);
                var merged = MergeTensor(key, tensorA, tensorB, weightB, warnings, ref mergedCount, ref copiedCount);
                outputTensors.Add(merged);
            }

            var metadata = MergeMetadata(modelA.Metadata, modelB.Metadata, architecture, options.Weights);
            if (animaFullModelKeyRepairTensorCount > 0)
            {
                metadata["cyan_merge_anima_key_repair"] = "model_b_by_model_a_shape";
                metadata["cyan_merge_anima_key_repair_count"] = animaFullModelKeyRepairTensorCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            SafetensorsFile.Save(options.OutputPath, outputTensors, metadata);

            return new LayeredLoraMergeReport(
                architecture,
                mergedCount,
                copiedCount,
                layerCounts,
                animaBlockTensorCount,
                animaFullModelKeyRepairTensorCount,
                warnings);
        }

        private static SafetensorsFile RepairAnimaFullModelKeysByShape(
            SafetensorsFile reference,
            SafetensorsFile candidate,
            List<string> warnings,
            out int repairedCount)
        {
            var candidateByShape = new Dictionary<string, Queue<SafetensorsTensor>>(StringComparer.Ordinal);
            foreach (var tensor in candidate.Tensors.Values)
            {
                var shapeKey = BuildShapeKey(tensor.Shape);
                if (!candidateByShape.TryGetValue(shapeKey, out var queue))
                {
                    queue = new Queue<SafetensorsTensor>();
                    candidateByShape[shapeKey] = queue;
                }

                queue.Enqueue(tensor);
            }

            var repairedTensors = new List<SafetensorsTensor>();
            var unmatchedReferenceKeys = new List<string>();
            foreach (var referenceTensor in reference.Tensors.Values)
            {
                var shapeKey = BuildShapeKey(referenceTensor.Shape);
                if (!candidateByShape.TryGetValue(shapeKey, out var queue) || queue.Count == 0)
                {
                    unmatchedReferenceKeys.Add(referenceTensor.Name);
                    continue;
                }

                var candidateTensor = queue.Dequeue();
                repairedTensors.Add(new SafetensorsTensor(
                    referenceTensor.Name,
                    candidateTensor.DType,
                    referenceTensor.Shape,
                    candidateTensor.Data.ToArray()));
            }

            if (unmatchedReferenceKeys.Count > 0)
            {
                var preview = string.Join(", ", unmatchedReferenceKeys.Take(5));
                throw new InvalidDataException(
                    $"Anima 大模型 Key 修复失败：模型 B 无法按 shape 匹配模型 A 的 {unmatchedReferenceKeys.Count} 个 key，例如 {preview}。");
            }

            int unusedCandidateCount = candidateByShape.Values.Sum(queue => queue.Count);
            if (unusedCandidateCount > 0)
            {
                warnings.Add($"Anima 大模型 Key 修复: 模型 B 有 {unusedCandidateCount} 个额外 tensor 未写入输出。");
            }

            repairedCount = repairedTensors.Count;
            return new SafetensorsFile(repairedTensors, candidate.Metadata);
        }

        private static string BuildShapeKey(IReadOnlyList<long> shape)
        {
            return string.Join("x", shape);
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
            int animaFullModelScore = 0;
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
                else if (guess == LayeredLoraArchitecture.AnimaFullModel)
                {
                    animaFullModelScore++;
                }
            }

            if (animaFullModelScore > 0 && animaFullModelScore >= animaScore && animaFullModelScore >= sdxlScore)
            {
                return LayeredLoraArchitecture.AnimaFullModel;
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
            if (architecture == LayeredLoraArchitecture.Anima ||
                architecture == LayeredLoraArchitecture.AnimaFullModel)
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
            if (architecture == LayeredLoraArchitecture.Anima ||
                architecture == LayeredLoraArchitecture.AnimaFullModel)
            {
                var blockIndex = LoraLayerMapper.TryGetAnimaBlockIndex(key);
                if (blockIndex.HasValue)
                {
                    return weights.GetAnimaBlockWeight(blockIndex.Value);
                }
            }

            return weights.GetLayerWeight(slot);
        }

        private static bool UsesDirectAnimaBlockWeight(string key, LayeredLoraArchitecture architecture)
        {
            return (architecture == LayeredLoraArchitecture.Anima ||
                    architecture == LayeredLoraArchitecture.AnimaFullModel) &&
                LoraLayerMapper.TryGetAnimaBlockIndex(key).HasValue;
        }

        private static void IncrementLayerCount(Dictionary<LoraLayerSlot, int> counts, LoraLayerSlot slot)
        {
            counts.TryGetValue(slot, out int current);
            counts[slot] = current + 1;
        }
    }
}
