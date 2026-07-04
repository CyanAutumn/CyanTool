using CyanTools.Model;
using System;
using System.Text.RegularExpressions;

namespace CyanTools.Utils
{
    public static class LoraLayerMapper
    {
        private static readonly Regex SdxlInputBlockRegex = new(@"(?:^|_)input_blocks_(\d+)(?:_|\.|$)", RegexOptions.Compiled);
        private static readonly Regex SdxlMiddleBlockRegex = new(@"(?:^|_)middle_block_(\d+)(?:_|\.|$)", RegexOptions.Compiled);
        private static readonly Regex SdxlOutputBlockRegex = new(@"(?:^|_)output_blocks_(\d+)(?:_|\.|$)", RegexOptions.Compiled);
        private static readonly Regex AnimaDotBlockRegex = new(@"(?:^|\.)blocks\.(\d+)(?:\.|$)", RegexOptions.Compiled);
        private static readonly Regex AnimaUnderscoreBlockRegex = new(@"(?:^|_)blocks_(\d+)(?:_|\.|$)", RegexOptions.Compiled);

        public static LoraLayerSlot MapKey(string key, LayeredLoraArchitecture architecture, int? animaBlockCount = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return LoraLayerSlot.Unknown;
            }

            var resolved = architecture == LayeredLoraArchitecture.Auto
                ? GuessArchitectureFromKey(key)
                : architecture;

            return resolved switch
            {
                LayeredLoraArchitecture.Sdxl => MapSdxlKey(key),
                LayeredLoraArchitecture.Anima => MapAnimaKey(key),
                LayeredLoraArchitecture.AnimaSdxlCompatible => MapAnimaSdxlCompatibleKey(key, animaBlockCount),
                _ => LoraLayerSlot.Unknown
            };
        }

        public static LayeredLoraArchitecture GuessArchitectureFromKey(string key)
        {
            if (key.StartsWith("lora_unet_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("lora_te", StringComparison.OrdinalIgnoreCase))
            {
                return LayeredLoraArchitecture.Sdxl;
            }

            if (key.Contains("diffusion_model.", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("llm_adapter", StringComparison.OrdinalIgnoreCase))
            {
                return LayeredLoraArchitecture.Anima;
            }

            return LayeredLoraArchitecture.Auto;
        }

        private static LoraLayerSlot MapSdxlKey(string key)
        {
            if (key.StartsWith("lora_te", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("text_encoder", StringComparison.OrdinalIgnoreCase))
            {
                return LoraLayerSlot.Base;
            }

            var inputMatch = SdxlInputBlockRegex.Match(key);
            if (inputMatch.Success && int.TryParse(inputMatch.Groups[1].Value, out int inputIndex))
            {
                return ToInputSlot(inputIndex);
            }

            if (SdxlMiddleBlockRegex.IsMatch(key))
            {
                return LoraLayerSlot.Mid00;
            }

            var outputMatch = SdxlOutputBlockRegex.Match(key);
            if (outputMatch.Success && int.TryParse(outputMatch.Groups[1].Value, out int outputIndex))
            {
                return ToOutputSlot(outputIndex);
            }

            return LoraLayerSlot.Unknown;
        }

        private static LoraLayerSlot MapAnimaKey(string key)
        {
            if (key.Contains("llm_adapter", StringComparison.OrdinalIgnoreCase))
            {
                return LoraLayerSlot.Base;
            }

            return LoraLayerSlot.Unknown;
        }

        private static LoraLayerSlot MapAnimaSdxlCompatibleKey(string key, int? blockCount)
        {
            if (key.Contains("llm_adapter", StringComparison.OrdinalIgnoreCase))
            {
                return LoraLayerSlot.Base;
            }

            var blockIndex = TryGetAnimaBlockIndex(key);
            if (!blockIndex.HasValue || !blockCount.HasValue || blockCount.Value <= 0)
            {
                return LoraLayerSlot.Unknown;
            }

            int layerIndex = Math.Clamp(
                blockIndex.Value * LayeredLoraWeights.ReportLayerCount / blockCount.Value,
                0,
                LayeredLoraWeights.ReportLayerCount - 1);
            return LayeredLoraWeights.ReportLayerSlots[layerIndex];
        }

        public static int? TryGetAnimaBlockIndex(string key)
        {
            var match = AnimaDotBlockRegex.Match(key);
            if (!match.Success)
            {
                match = AnimaUnderscoreBlockRegex.Match(key);
            }

            if (match.Success && int.TryParse(match.Groups[1].Value, out int index))
            {
                return index;
            }

            return null;
        }

        private static LoraLayerSlot ToInputSlot(int index)
        {
            return Math.Clamp(index, 0, 11) switch
            {
                0 => LoraLayerSlot.In00,
                1 => LoraLayerSlot.In01,
                2 => LoraLayerSlot.In02,
                3 => LoraLayerSlot.In03,
                4 => LoraLayerSlot.In04,
                5 => LoraLayerSlot.In05,
                6 => LoraLayerSlot.In06,
                7 => LoraLayerSlot.In07,
                8 => LoraLayerSlot.In08,
                9 => LoraLayerSlot.In09,
                10 => LoraLayerSlot.In10,
                _ => LoraLayerSlot.In11
            };
        }

        private static LoraLayerSlot ToOutputSlot(int index)
        {
            return Math.Clamp(index, 0, 11) switch
            {
                0 => LoraLayerSlot.Out00,
                1 => LoraLayerSlot.Out01,
                2 => LoraLayerSlot.Out02,
                3 => LoraLayerSlot.Out03,
                4 => LoraLayerSlot.Out04,
                5 => LoraLayerSlot.Out05,
                6 => LoraLayerSlot.Out06,
                7 => LoraLayerSlot.Out07,
                8 => LoraLayerSlot.Out08,
                9 => LoraLayerSlot.Out09,
                10 => LoraLayerSlot.Out10,
                _ => LoraLayerSlot.Out11
            };
        }
    }
}
