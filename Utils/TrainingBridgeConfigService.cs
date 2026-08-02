using CyanTools.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyanTools.Utils
{
    public static class TrainingBridgeConfigService
    {
        public const string SdScriptsBackendId = "sd-scripts";
        public const string WslSdScriptsBackendId = "wsl-sd-scripts";
        public const string AnimaFastLoraBackendId = "anima-fast-lora";
        public const string DiffusionPipeBackendId = "diffusion-pipe";

        private static readonly string[][] AliasGroups =
        [
            ["dataset_path"],
            ["output_dir"],
            ["pretrained_model_path", "transformer_path"],
            ["qwen3_path", "llm_path"],
            ["vae_path"],
            ["dataset_resolution", "resolution"],
            ["dataset_repeats", "num_repeats"],
            ["max_train_epochs", "epochs"],
            ["train_batch_size", "micro_batch_size_per_gpu"],
            ["network_dim", "rank"],
            ["network_alpha", "rank_alpha"],
            ["learning_rate"],
            ["gradient_accumulation_steps"],
            ["save_every_n_epochs"],
            ["save_every_n_steps"],
            ["optimizer_type"],
            ["lr_scheduler"],
            ["lr_warmup_steps"],
            ["caption_extension"],
            ["keep_tokens"],
            ["enable_bucket"],
            ["shuffle_caption"],
            ["dataset_config_path"]
        ];

        private static readonly HashSet<string> NumericKeys = new(StringComparer.Ordinal)
        {
            "dataset_resolution", "resolution", "dataset_repeats", "num_repeats",
            "max_train_epochs", "epochs", "max_train_steps", "train_batch_size",
            "micro_batch_size_per_gpu", "network_dim", "rank", "network_alpha",
            "rank_alpha", "learning_rate", "network_dropout", "gradient_accumulation_steps",
            "save_every_n_epochs", "save_every_n_steps", "eval_every_n_epochs",
            "blocks_to_swap", "max_sequence_length", "lr_warmup_steps", "keep_tokens",
            "preview_every_n_epochs", "preview_width", "preview_height", "preview_steps",
            "preview_cfg", "preview_seed"
        };

        private static readonly HashSet<string> PathKeys = new(StringComparer.Ordinal)
        {
            "dataset_path", "dataset_config_path", "output_dir", "pretrained_model_path",
            "transformer_path", "qwen3_path", "clip_l_path", "clip_g_path", "t5xxl_path",
            "ae_path", "gemma2_path", "text_encoder_path", "byt5_path", "llm_path",
            "vae_path", "network_weights", "conditioning_data_dir", "config_path",
            "resume_from_checkpoint"
        };

        private static readonly Dictionary<string, string> CliFlagMap = new(StringComparer.Ordinal)
        {
            ["--dataset_config"] = "dataset_config_path",
            ["--output_dir"] = "output_dir",
            ["--pretrained_model_name_or_path"] = "pretrained_model_path",
            ["--network_module"] = "network_module",
            ["--qwen3"] = "qwen3_path",
            ["--clip_l"] = "clip_l_path",
            ["--clip_g"] = "clip_g_path",
            ["--t5xxl"] = "t5xxl_path",
            ["--ae"] = "ae_path",
            ["--gemma2"] = "gemma2_path",
            ["--text_encoder"] = "text_encoder_path",
            ["--byt5"] = "byt5_path",
            ["--vae"] = "vae_path",
            ["--output_name"] = "output_name",
            ["--save_model_as"] = "save_model_as",
            ["--learning_rate"] = "learning_rate",
            ["--optimizer_type"] = "optimizer_type",
            ["--lr_scheduler"] = "lr_scheduler",
            ["--lr_warmup_steps"] = "lr_warmup_steps",
            ["--train_batch_size"] = "train_batch_size",
            ["--max_train_steps"] = "max_train_steps",
            ["--max_train_epochs"] = "max_train_epochs",
            ["--gradient_accumulation_steps"] = "gradient_accumulation_steps",
            ["--mixed_precision"] = "mixed_precision",
            ["--save_every_n_epochs"] = "save_every_n_epochs",
            ["--save_every_n_steps"] = "save_every_n_steps",
            ["--network_dim"] = "network_dim",
            ["--network_alpha"] = "network_alpha",
            ["--network_dropout"] = "network_dropout",
            ["--network_weights"] = "network_weights"
        };

        private static readonly Dictionary<string, string> BooleanCliFlags = new(StringComparer.Ordinal)
        {
            ["--gradient_checkpointing"] = "gradient_checkpointing",
            ["--cache_latents"] = "cache_latents",
            ["--cache_text_encoder_outputs"] = "cache_text_encoder_outputs",
            ["--network_train_unet_only"] = "network_train_unet_only",
            ["--vae_disable_cache"] = "vae_disable_cache",
            ["--masked_loss"] = "masked_loss"
        };

        public static IReadOnlyDictionary<string, string> Import(
            string sourceBackendId,
            IEnumerable<TrainingBridgeConfigFile> files)
        {
            EnsureSupportedBackend(sourceBackendId);
            var selectedFiles = files?.ToArray() ?? Array.Empty<TrainingBridgeConfigFile>();
            if (selectedFiles.Length == 0)
            {
                throw new InvalidOperationException("请至少选择一个配置文件。");
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in selectedFiles)
            {
                foreach (var pair in ImportSingleFile(sourceBackendId, file))
                {
                    values[pair.Key] = pair.Value;
                }
            }

            return ExpandAliases(CleanValues(values));
        }

        public static string? DetectSourceBackend(IEnumerable<TrainingBridgeConfigFile> files)
        {
            TrainingBridgeConfigFile[] selectedFiles = files?.ToArray()
                ?? Array.Empty<TrainingBridgeConfigFile>();
            if (selectedFiles.Length == 0)
            {
                return null;
            }

            if (HasFileName(selectedFiles, "anima_train.toml") ||
                HasPathHint(selectedFiles, "diffusion-pipe") ||
                HasDiffusionPipeTomlSignature(selectedFiles))
            {
                return DiffusionPipeBackendId;
            }
            if (HasFileName(selectedFiles, "anima-fast-lora.args.txt") ||
                HasPathHint(selectedFiles, "anima-fast-lora") ||
                HasContentHint(selectedFiles, "external/anima_lora") ||
                HasContentHint(selectedFiles, @"external\anima_lora"))
            {
                return AnimaFastLoraBackendId;
            }
            if (HasFileName(selectedFiles, "wsl-sd-scripts.args.txt") ||
                HasPathHint(selectedFiles, "wsl-sd-scripts"))
            {
                return WslSdScriptsBackendId;
            }
            if (HasFileName(selectedFiles, "sd-scripts.args.txt"))
            {
                return SdScriptsBackendId;
            }
            if (HasPathHint(selectedFiles, "sd-scripts"))
            {
                return HasWslPathHint(selectedFiles)
                    ? WslSdScriptsBackendId
                    : SdScriptsBackendId;
            }
            return null;
        }

        private static bool HasFileName(
            IEnumerable<TrainingBridgeConfigFile> files,
            string expectedFileName)
            => files.Any(file => string.Equals(
                Path.GetFileName(file.FileName),
                expectedFileName,
                StringComparison.OrdinalIgnoreCase));

        private static bool HasPathHint(
            IEnumerable<TrainingBridgeConfigFile> files,
            string hint)
            => files.Any(file => !string.IsNullOrWhiteSpace(file.SourcePath) &&
                file.SourcePath.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool HasContentHint(
            IEnumerable<TrainingBridgeConfigFile> files,
            string hint)
            => files.Any(file => file.Content.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool HasWslPathHint(IEnumerable<TrainingBridgeConfigFile> files)
            => files.Any(file =>
            {
                string text = (file.SourcePath ?? string.Empty) + "\n" + file.Content;
                return text.IndexOf("/mnt/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("\\\\wsl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("wsl.localhost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("~/sd-scripts", StringComparison.OrdinalIgnoreCase) >= 0;
            });

        private static bool HasDiffusionPipeTomlSignature(
            IEnumerable<TrainingBridgeConfigFile> files)
        {
            foreach (TrainingBridgeConfigFile file in files.Where(file =>
                         string.Equals(
                             Path.GetExtension(file.FileName),
                             ".toml",
                             StringComparison.OrdinalIgnoreCase)))
            {
                string content = file.Content;
                if ((content.IndexOf("[[directory]]", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     content.IndexOf("size_buckets", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (content.IndexOf("[model]", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     content.IndexOf("transformer_path", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     content.IndexOf("llm_path", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }
            return false;
        }

        public static IReadOnlyList<TrainingBridgeConfigFile> Export(
            string targetBackendId,
            IReadOnlyDictionary<string, string> sourceValues)
        {
            EnsureSupportedBackend(targetBackendId);
            var values = NormalizeValues(sourceValues);
            return targetBackendId switch
            {
                DiffusionPipeBackendId => ExportDiffusionPipe(values),
                AnimaFastLoraBackendId => ExportCliBackend(
                    values,
                    "anima-fast-lora.args.txt",
                    defaultRepeats: 1,
                    defaultOutputName: "anima_fast_lora",
                    runtimeTarget: "windows"),
                SdScriptsBackendId => ExportCliBackend(
                    values,
                    "sd-scripts.args.txt",
                    defaultRepeats: 10,
                    defaultOutputName: "anima_lora",
                    runtimeTarget: "windows"),
                WslSdScriptsBackendId => ExportCliBackend(
                    values,
                    "wsl-sd-scripts.args.txt",
                    defaultRepeats: 10,
                    defaultOutputName: "anima_lora",
                    runtimeTarget: "wsl2"),
                _ => throw new InvalidOperationException($"暂不支持导出到训练后端：{targetBackendId}")
            };
        }

        public static IReadOnlyDictionary<string, string> NormalizeValues(
            IReadOnlyDictionary<string, string> sourceValues)
            => ExpandAliases(CleanValues(sourceValues));

        public static IReadOnlyDictionary<string, string> CreateDefaultValues()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["model_family"] = "anima",
                ["dataset_path"] = string.Empty,
                ["output_dir"] = string.Empty,
                ["pretrained_model_path"] = string.Empty,
                ["qwen3_path"] = string.Empty,
                ["vae_path"] = string.Empty,
                ["dataset_resolution"] = "1024",
                ["dataset_repeats"] = "10",
                ["caption_extension"] = ".txt",
                ["keep_tokens"] = "1",
                ["enable_bucket"] = "true",
                ["shuffle_caption"] = "true",
                ["output_name"] = "anima_lora",
                ["save_model_as"] = "safetensors",
                ["learning_rate"] = "0.0001",
                ["optimizer_type"] = "AdamW8bit",
                ["lr_scheduler"] = "constant",
                ["train_batch_size"] = "1",
                ["max_train_epochs"] = "1",
                ["gradient_accumulation_steps"] = "1",
                ["save_every_n_epochs"] = "1",
                ["network_dim"] = "16",
                ["network_alpha"] = "16",
                ["network_module"] = "networks.lora_anima",
                ["gradient_checkpointing"] = "true",
                ["cache_latents"] = "true",
                ["preview_enabled"] = "true",
                ["preview_every_n_epochs"] = "1",
                ["preview_positive_prompt"] = "a golden retriever running through a grassy field",
                ["preview_negative_prompt"] = string.Empty,
                ["preview_sampler"] = "euler",
                ["preview_scheduler"] = "normal",
                ["preview_width"] = "1024",
                ["preview_height"] = "1024",
                ["preview_steps"] = "20",
                ["preview_cfg"] = "5",
                ["preview_seed"] = "42",
                ["preview_offload_pipeline_for_vae"] = "true"
            };
        }

        private static Dictionary<string, string> ImportSingleFile(
            string backendId,
            TrainingBridgeConfigFile file)
        {
            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension == ".json")
            {
                return ImportJson(file.Content);
            }

            if (extension == ".toml")
            {
                var document = MiniTomlDocument.Parse(file.Content);
                return backendId == DiffusionPipeBackendId
                    ? ImportDiffusionPipeToml(document)
                    : ImportDatasetToml(document);
            }

            if (extension is ".txt" or ".args" or ".cfg")
            {
                var values = ImportCliArguments(file.Content);
                return backendId == WslSdScriptsBackendId
                    ? NormalizeRuntimeImportPaths(values)
                    : values;
            }

            throw new InvalidOperationException($"无法识别配置文件类型：{file.FileName}");
        }

        private static Dictionary<string, string> ImportJson(string content)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("JSON 配置文件必须是对象。");
                }

                JsonElement root = document.RootElement;
                if (root.TryGetProperty("values", out JsonElement valuesElement) &&
                    valuesElement.ValueKind == JsonValueKind.Object)
                {
                    return JsonObjectToValues(valuesElement);
                }

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                if (root.TryGetProperty("dataset_path", out JsonElement datasetPath))
                {
                    values["dataset_path"] = JsonElementToText(datasetPath);
                }

                if (root.TryGetProperty("backend_config_draft", out JsonElement draft) &&
                    draft.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in JsonObjectToValues(draft))
                    {
                        values[pair.Key] = pair.Value;
                    }
                }
                else
                {
                    foreach (var pair in JsonObjectToValues(root))
                    {
                        values[pair.Key] = pair.Value;
                    }
                }

                return values;
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("JSON 配置文件格式无效。", exception);
            }
        }

        private static Dictionary<string, string> JsonObjectToValues(JsonElement element)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                values[property.Name] = JsonElementToText(property.Value);
            }
            return values;
        }

        private static string JsonElementToText(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => element.GetRawText()
            };
        }

        private static Dictionary<string, string> ImportDiffusionPipeToml(MiniTomlDocument document)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            CopyScalar(document, values, string.Empty, "output_dir");
            foreach (string key in new[]
                     {
                         "epochs", "micro_batch_size_per_gpu", "gradient_accumulation_steps",
                         "eval_every_n_epochs", "save_every_n_epochs", "blocks_to_swap"
                     })
            {
                CopyScalar(document, values, string.Empty, key);
            }

            foreach ((string source, string target) in new[]
                     {
                         ("transformer_path", "transformer_path"),
                         ("vae_path", "vae_path"),
                         ("llm_path", "llm_path"),
                         ("max_sequence_length", "max_sequence_length")
                     })
            {
                if (document.TryGetScalar("model", source, out string value))
                {
                    values[target] = RuntimePathToWindows(value);
                }
            }

            if (document.TryGetScalar("adapter", "rank", out string rank))
            {
                values["rank"] = rank;
            }
            if (document.TryGetScalar("optimizer", "lr", out string learningRate))
            {
                values["learning_rate"] = learningRate;
            }
            if (document.TryGetScalar("optimizer", "type", out string optimizerType))
            {
                values["optimizer_type"] = optimizerType;
            }

            foreach (string key in new[]
                     {
                         "enabled", "every_n_epochs", "positive_prompt", "negative_prompt",
                         "sampler", "scheduler", "width", "height", "steps", "cfg", "seed",
                         "offload_pipeline_for_vae"
                     })
            {
                if (document.TryGetScalar("preview", key, out string value))
                {
                    values[$"preview_{key}"] = value;
                }
            }

            if (document.TryGetScalar(string.Empty, "size_buckets", out string buckets))
            {
                Match resolution = Regex.Match(buckets, @"-?\d+");
                if (resolution.Success)
                {
                    values["resolution"] = resolution.Value;
                }
            }
            if (document.TryGetFirstArrayValue("directory", "path", out string datasetPath))
            {
                values["dataset_path"] = RuntimePathToWindows(datasetPath);
            }
            if (document.TryGetFirstArrayValue("directory", "num_repeats", out string repeats))
            {
                values["num_repeats"] = repeats;
            }

            return NormalizeRuntimeImportPaths(values);
        }

        private static Dictionary<string, string> ImportDatasetToml(MiniTomlDocument document)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in new[] { "caption_extension", "shuffle_caption", "keep_tokens", "enable_bucket" })
            {
                CopyScalar(document, values, "general", key);
            }

            if (document.TryGetFirstArrayValue("datasets", "resolution", out string resolution))
            {
                Match firstNumber = Regex.Match(resolution, @"-?\d+(?:\.\d+)?");
                if (firstNumber.Success)
                {
                    values["dataset_resolution"] = firstNumber.Value;
                }
            }
            if (document.TryGetFirstArrayValue("datasets", "batch_size", out string batchSize))
            {
                values["train_batch_size"] = batchSize;
            }
            if (document.TryGetFirstArrayValue("datasets", "enable_bucket", out string enableBucket))
            {
                values["enable_bucket"] = enableBucket;
            }
            if (document.TryGetFirstArrayValue("datasets.subsets", "image_dir", out string imageDirectory))
            {
                values["dataset_path"] = DatasetRootFromSubset(imageDirectory);
            }
            if (document.TryGetFirstArrayValue("datasets.subsets", "num_repeats", out string repeats))
            {
                values["dataset_repeats"] = repeats;
            }
            if (document.TryGetFirstArrayValue("datasets.subsets", "conditioning_data_dir", out string conditioning))
            {
                values["conditioning_data_dir"] = conditioning;
            }
            if (document.TryGetFirstArrayValue("datasets.subsets", "caption_extension", out string captionExtension))
            {
                values["caption_extension"] = captionExtension;
            }
            return values;
        }

        private static void CopyScalar(
            MiniTomlDocument document,
            IDictionary<string, string> values,
            string section,
            string key)
        {
            if (document.TryGetScalar(section, key, out string value))
            {
                values[key] = PathKeys.Contains(key) ? RuntimePathToWindows(value) : value;
            }
        }

        private static Dictionary<string, string> ImportCliArguments(string content)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string rawLine in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                Match family = Regex.Match(rawLine, @"^\s*#\s*(?:[^\s]+\s+)?model_family=(\S+)\s*$", RegexOptions.IgnoreCase);
                if (family.Success)
                {
                    values["model_family"] = family.Groups[1].Value;
                }
            }

            string[] tokens = ArgumentLinesToTokens(content).ToArray();
            for (int index = 0; index < tokens.Length;)
            {
                string token = tokens[index];
                if (BooleanCliFlags.TryGetValue(token, out string? booleanKey))
                {
                    values[booleanKey] = "true";
                    index++;
                    continue;
                }

                if (CliFlagMap.TryGetValue(token, out string? key) && index + 1 < tokens.Length)
                {
                    values[key] = tokens[index + 1];
                    index += 2;
                    continue;
                }
                index++;
            }
            return values;
        }

        private static IEnumerable<string> ArgumentLinesToTokens(string content)
        {
            foreach (string rawLine in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                int whitespace = line.IndexOfAny([' ', '\t']);
                if (line.StartsWith("--", StringComparison.Ordinal) && whitespace > 0)
                {
                    yield return line[..whitespace];
                    yield return CleanPathText(line[(whitespace + 1)..]);
                }
                else
                {
                    yield return CleanPathText(line);
                }
            }
        }

        private static IReadOnlyList<TrainingBridgeConfigFile> ExportDiffusionPipe(
            IReadOnlyDictionary<string, string> values)
        {
            string datasetPath = WindowsPathToWsl(GetText(values, "dataset_path"));
            string outputDirectory = WindowsPathToWsl(GetText(values, "output_dir"));
            string transformerPath = WindowsPathToWsl(GetText(values, "transformer_path"));
            string vaePath = WindowsPathToWsl(GetText(values, "vae_path"));
            string llmPath = WindowsPathToWsl(GetText(values, "llm_path"));
            int resolution = GetInt(values, "resolution", 1024);

            string datasetText = string.Join("\n",
            [
                "size_buckets = [",
                $"    [{resolution}, {resolution}, 1],",
                "]",
                string.Empty,
                "cache_shuffle_num = 1",
                "cache_shuffle_delimiter = \", \",",
                "skip_empty_caption = true",
                string.Empty,
                "[[directory]]",
                $"path = {TomlString(datasetPath)}",
                $"num_repeats = {GetInt(values, "num_repeats", 1)}",
                string.Empty
            ]);

            string trainingText = string.Join("\n",
            [
                $"output_dir = {TomlString(outputDirectory)}",
                "dataset = \"anima_dataset.toml\"",
                string.Empty,
                $"epochs = {GetInt(values, "epochs", 50)}",
                $"micro_batch_size_per_gpu = {GetInt(values, "micro_batch_size_per_gpu", 1)}",
                "pipeline_stages = 1",
                $"gradient_accumulation_steps = {GetInt(values, "gradient_accumulation_steps", 1)}",
                "gradient_clipping = 1.0",
                "warmup_steps = 0",
                "lr_scheduler = 'constant'",
                string.Empty,
                $"eval_every_n_epochs = {GetInt(values, "eval_every_n_epochs", 5)}",
                "eval_before_first_step = false",
                "eval_micro_batch_size_per_gpu = 1",
                "eval_gradient_accumulation_steps = 1",
                string.Empty,
                $"save_every_n_epochs = {GetInt(values, "save_every_n_epochs", 1)}",
                "checkpoint_every_n_minutes = 15",
                "activation_checkpointing = true",
                "partition_method = 'parameters'",
                "save_dtype = 'bfloat16'",
                "caching_batch_size = 1",
                "steps_per_print = 1",
                $"blocks_to_swap = {GetInt(values, "blocks_to_swap", 0)}",
                string.Empty,
                "[preview]",
                $"enabled = {TomlBool(GetBool(values, "preview_enabled", true))}",
                $"every_n_epochs = {Math.Max(1, GetInt(values, "preview_every_n_epochs", 1))}",
                $"positive_prompt = {TomlString(GetText(values, "preview_positive_prompt", "a golden retriever running through a grassy field"))}",
                $"negative_prompt = {TomlString(GetText(values, "preview_negative_prompt"))}",
                $"sampler = {TomlString(GetText(values, "preview_sampler", "euler").ToLowerInvariant())}",
                $"scheduler = {TomlString(GetText(values, "preview_scheduler", "normal").ToLowerInvariant())}",
                $"width = {Math.Max(64, GetInt(values, "preview_width", 1024))}",
                $"height = {Math.Max(64, GetInt(values, "preview_height", 1024))}",
                $"steps = {Math.Max(1, GetInt(values, "preview_steps", 20))}",
                $"cfg = {GetDouble(values, "preview_cfg", 5).ToString("G17", CultureInfo.InvariantCulture)}",
                $"seed = {GetInt(values, "preview_seed", 42)}",
                $"offload_pipeline_for_vae = {TomlBool(GetBool(values, "preview_offload_pipeline_for_vae", true))}",
                string.Empty,
                "[model]",
                "type = 'anima'",
                $"transformer_path = {TomlString(transformerPath)}",
                $"vae_path = {TomlString(vaePath)}",
                $"llm_path = {TomlString(llmPath)}",
                "dtype = 'bfloat16'",
                "transformer_dtype = 'float8'",
                "timestep_sample_method = 'logit_normal'",
                "llm_adapter_lr = 0",
                $"max_sequence_length = {GetInt(values, "max_sequence_length", 256)}",
                string.Empty,
                "[adapter]",
                "type = 'lora'",
                $"rank = {GetInt(values, "rank", 16)}",
                "dtype = 'bfloat16'",
                string.Empty,
                "[optimizer]",
                "type = 'Prodigy'",
                $"lr = {GetDouble(values, "learning_rate", 1).ToString("G17", CultureInfo.InvariantCulture)}",
                "betas = [0.9, 0.99]",
                "weight_decay = 0.01",
                string.Empty,
                "[monitoring]",
                "enable_wandb = false",
                string.Empty
            ]);

            return
            [
                new TrainingBridgeConfigFile("anima_train.toml", trainingText),
                new TrainingBridgeConfigFile("anima_dataset.toml", datasetText)
            ];
        }

        private static IReadOnlyList<TrainingBridgeConfigFile> ExportCliBackend(
            IReadOnlyDictionary<string, string> values,
            string argumentsFileName,
            int defaultRepeats,
            string defaultOutputName,
            string runtimeTarget)
        {
            Func<string, string> pathFormatter = runtimeTarget == "wsl2" ? WindowsPathToWsl : value => value;
            string modelFamily = GetText(values, "model_family", "anima");
            string[] supportedFamilies = ["anima", "sd15", "sd2", "sdxl", "sd3", "flux", "lumina", "hunyuan_image"];
            if (!supportedFamilies.Contains(modelFamily, StringComparer.Ordinal))
            {
                modelFamily = "anima";
            }

            string outputName = GetText(values, "output_name", modelFamily switch
            {
                "anima" => defaultOutputName,
                "sd15" => "sd15_lora",
                "sd2" => "sd2_lora",
                "sdxl" => "sdxl_lora",
                "sd3" => "sd3_lora",
                "flux" => "flux_lora",
                "lumina" => "lumina_lora",
                "hunyuan_image" => "hunyuan_image_lora",
                _ => defaultOutputName
            });

            var arguments = new List<string>
            {
                $"# training-bridge model_family={modelFamily}",
                "--dataset_config", "dataset.toml",
                "--output_dir", pathFormatter(GetText(values, "output_dir")),
                "--pretrained_model_name_or_path", pathFormatter(GetText(values, "pretrained_model_path")),
                "--output_name", outputName,
                "--save_model_as", GetText(values, "save_model_as", "safetensors"),
                "--learning_rate", GetDouble(values, "learning_rate", 0.0001).ToString("G17", CultureInfo.InvariantCulture),
                "--optimizer_type", GetText(values, "optimizer_type", "AdamW8bit"),
                "--lr_scheduler", GetText(values, "lr_scheduler", "constant"),
                "--train_batch_size", GetInt(values, "train_batch_size", 1).ToString(CultureInfo.InvariantCulture),
                "--max_train_epochs", GetInt(values, "max_train_epochs", 1).ToString(CultureInfo.InvariantCulture),
                "--gradient_accumulation_steps", GetInt(values, "gradient_accumulation_steps", 1).ToString(CultureInfo.InvariantCulture),
                "--save_every_n_epochs", GetInt(values, "save_every_n_epochs", 1).ToString(CultureInfo.InvariantCulture),
                "--network_dim", GetInt(values, "network_dim", 16).ToString(CultureInfo.InvariantCulture),
                "--network_module", GetText(values, "network_module", modelFamily switch
                {
                    "anima" => "networks.lora_anima",
                    "flux" => "networks.lora_flux",
                    "lumina" => "networks.lora_lumina",
                    "hunyuan_image" => "networks.lora_hunyuan_image",
                    _ => "networks.lora"
                })
            };

            void AddPath(string flag, string key, bool optional = false)
            {
                string value = GetText(values, key);
                if (!optional || value.Length > 0)
                {
                    arguments.Add(flag);
                    arguments.Add(pathFormatter(value));
                }
            }

            switch (modelFamily)
            {
                case "anima":
                    AddPath("--qwen3", "qwen3_path");
                    AddPath("--vae", "vae_path");
                    break;
                case "flux":
                    AddPath("--clip_l", "clip_l_path");
                    AddPath("--t5xxl", "t5xxl_path");
                    AddPath("--ae", "ae_path");
                    break;
                case "lumina":
                    AddPath("--gemma2", "gemma2_path");
                    AddPath("--ae", "ae_path");
                    break;
                case "hunyuan_image":
                    AddPath("--text_encoder", "text_encoder_path");
                    AddPath("--byt5", "byt5_path");
                    AddPath("--vae", "vae_path");
                    break;
                case "sd3":
                    AddPath("--clip_l", "clip_l_path", optional: true);
                    AddPath("--clip_g", "clip_g_path", optional: true);
                    AddPath("--t5xxl", "t5xxl_path", optional: true);
                    AddPath("--vae", "vae_path", optional: true);
                    break;
            }

            AddOptionalValue(arguments, values, "--network_alpha", "network_alpha");
            string networkWeights = GetText(values, "network_weights");
            if (networkWeights.Length > 0)
            {
                arguments.Add("--network_weights");
                arguments.Add(pathFormatter(networkWeights));
            }
            foreach (var pair in BooleanCliFlags)
            {
                if (GetBool(values, pair.Value, false))
                {
                    arguments.Add(pair.Key);
                }
            }

            string dataset = ExportDatasetToml(values, defaultRepeats, pathFormatter);
            return
            [
                new TrainingBridgeConfigFile(argumentsFileName, string.Join("\n", arguments) + "\n"),
                new TrainingBridgeConfigFile("dataset.toml", dataset)
            ];
        }

        private static string ExportDatasetToml(
            IReadOnlyDictionary<string, string> values,
            int defaultRepeats,
            Func<string, string> pathFormatter)
        {
            int resolution = GetInt(values, "dataset_resolution", 1024);
            int repeats = GetInt(values, "dataset_repeats", defaultRepeats);
            int batchSize = GetInt(values, "train_batch_size", 1);
            string captionExtension = GetText(values, "caption_extension", ".txt");
            bool enableBucket = GetBool(values, "enable_bucket", true);
            bool shuffleCaption = GetBool(values, "shuffle_caption", true);
            int keepTokens = GetInt(values, "keep_tokens", 1);
            string datasetPath = pathFormatter(GetText(values, "dataset_path"));

            var lines = new List<string>
            {
                "[general]",
                $"enable_bucket = {TomlBool(enableBucket)}",
                $"caption_extension = {TomlString(captionExtension)}",
                $"shuffle_caption = {TomlBool(shuffleCaption)}",
                $"keep_tokens = {keepTokens}",
                string.Empty,
                "[[datasets]]",
                $"resolution = [{resolution}, {resolution}]",
                $"batch_size = {batchSize}"
            };
            if (enableBucket)
            {
                lines.Add("min_bucket_reso = 256");
                lines.Add($"max_bucket_reso = {Math.Max(1024, resolution)}");
                lines.Add("bucket_reso_steps = 64");
            }
            lines.AddRange(
            [
                string.Empty,
                "  [[datasets.subsets]]",
                $"  image_dir = {TomlString(datasetPath)}",
                $"  num_repeats = {repeats}",
                $"  caption_extension = {TomlString(captionExtension)}",
                string.Empty
            ]);
            return string.Join("\n", lines);
        }

        private static void AddOptionalValue(
            ICollection<string> arguments,
            IReadOnlyDictionary<string, string> values,
            string flag,
            string key)
        {
            string value = GetText(values, key);
            if (value.Length == 0)
            {
                return;
            }
            arguments.Add(flag);
            arguments.Add(value);
        }

        private static Dictionary<string, string> NormalizeRuntimeImportPaths(
            IReadOnlyDictionary<string, string> values)
        {
            var normalized = new Dictionary<string, string>(values, StringComparer.Ordinal);
            foreach (string key in PathKeys)
            {
                if (normalized.TryGetValue(key, out string? value))
                {
                    normalized[key] = RuntimePathToWindows(value);
                }
            }
            return normalized;
        }

        private static Dictionary<string, string> ExpandAliases(
            IReadOnlyDictionary<string, string> values)
        {
            var expanded = new Dictionary<string, string>(values, StringComparer.Ordinal);
            foreach (string[] group in AliasGroups)
            {
                string? present = group
                    .Select(key => expanded.TryGetValue(key, out string? value) ? value : null)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (present == null)
                {
                    continue;
                }
                foreach (string key in group)
                {
                    if (!expanded.TryGetValue(key, out string? current) || string.IsNullOrWhiteSpace(current))
                    {
                        expanded[key] = present;
                    }
                }
            }
            return expanded;
        }

        private static Dictionary<string, string> CleanValues(
            IReadOnlyDictionary<string, string> values)
        {
            var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                string key = pair.Key?.Trim() ?? string.Empty;
                if (key.Length == 0)
                {
                    continue;
                }

                string value = PathKeys.Contains(key)
                    ? CleanPathText(pair.Value)
                    : pair.Value?.Trim() ?? string.Empty;
                if (NumericKeys.Contains(key) &&
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    value = number.ToString("G17", CultureInfo.InvariantCulture);
                }
                cleaned[key] = value;
            }
            return cleaned;
        }

        private static string DatasetRootFromSubset(string value)
        {
            string normalized = RuntimePathToWindows(value).Replace('\\', '/').TrimEnd('/');
            int separator = normalized.LastIndexOf('/');
            if (separator > 0 && Regex.IsMatch(normalized[(separator + 1)..], @"^\d+_"))
            {
                return normalized[..separator];
            }
            return normalized;
        }

        public static string WindowsPathToWsl(string value)
        {
            string cleaned = CleanPathText(value);
            Match match = Regex.Match(cleaned, @"^([A-Za-z]):[\\/](.*)$");
            if (!match.Success)
            {
                return cleaned.Replace('\\', '/');
            }
            return $"/mnt/{match.Groups[1].Value.ToLowerInvariant()}/{match.Groups[2].Value.Replace('\\', '/')}";
        }

        public static string RuntimePathToWindows(string value)
        {
            string cleaned = CleanPathText(value);
            Match match = Regex.Match(cleaned, @"^/mnt/([A-Za-z])/(.*)$");
            if (!match.Success)
            {
                return cleaned;
            }
            return $"{match.Groups[1].Value.ToUpperInvariant()}:/{match.Groups[2].Value.Replace('\\', '/')}";
        }

        private static string CleanPathText(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            string text = string.Concat(value.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format)).Trim();
            if (text.Length >= 2 && text[0] == text[^1] && text[0] is '\'' or '"')
            {
                text = text[1..^1].Trim();
            }
            return text;
        }

        private static string GetText(
            IReadOnlyDictionary<string, string> values,
            string key,
            string defaultValue = "")
        {
            return values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : defaultValue;
        }

        private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int defaultValue)
        {
            return double.TryParse(GetText(values, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? (int)value
                : defaultValue;
        }

        private static double GetDouble(IReadOnlyDictionary<string, string> values, string key, double defaultValue)
        {
            return double.TryParse(GetText(values, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : defaultValue;
        }

        private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, bool defaultValue)
        {
            string value = GetText(values, key).ToLowerInvariant();
            if (value is "true" or "1" or "yes" or "on")
            {
                return true;
            }
            if (value is "false" or "0" or "no" or "off")
            {
                return false;
            }
            return defaultValue;
        }

        private static string TomlBool(bool value) => value ? "true" : "false";

        private static string TomlString(string value) => JsonSerializer.Serialize(value);

        private static void EnsureSupportedBackend(string backendId)
        {
            if (backendId is not (SdScriptsBackendId or WslSdScriptsBackendId or AnimaFastLoraBackendId or DiffusionPipeBackendId))
            {
                throw new InvalidOperationException($"不支持的训练后端：{backendId}");
            }
        }

        private sealed class MiniTomlDocument
        {
            private readonly Dictionary<string, string> _scalars = new(StringComparer.Ordinal);
            private readonly Dictionary<string, List<Dictionary<string, string>>> _arrayTables = new(StringComparer.Ordinal);

            public static MiniTomlDocument Parse(string content)
            {
                var document = new MiniTomlDocument();
                string section = string.Empty;
                Dictionary<string, string>? currentArray = null;
                string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                for (int index = 0; index < lines.Length; index++)
                {
                    string line = StripComment(lines[index]).Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    if (line.StartsWith("[[", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
                    {
                        section = line[2..^2].Trim();
                        currentArray = new Dictionary<string, string>(StringComparer.Ordinal);
                        if (!document._arrayTables.TryGetValue(section, out var tables))
                        {
                            tables = new List<Dictionary<string, string>>();
                            document._arrayTables[section] = tables;
                        }
                        tables.Add(currentArray);
                        continue;
                    }
                    if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                    {
                        section = line[1..^1].Trim();
                        currentArray = null;
                        continue;
                    }

                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                    {
                        continue;
                    }
                    string key = line[..equalsIndex].Trim();
                    string rawValue = line[(equalsIndex + 1)..].Trim();
                    if (rawValue.StartsWith("[", StringComparison.Ordinal) && !ArrayValueIsComplete(rawValue))
                    {
                        var builder = new StringBuilder(rawValue);
                        while (++index < lines.Length)
                        {
                            string continuation = StripComment(lines[index]).Trim();
                            builder.Append(' ').Append(continuation);
                            if (ArrayValueIsComplete(builder.ToString()))
                            {
                                break;
                            }
                        }
                        rawValue = builder.ToString();
                    }

                    string value = ParseTomlValue(rawValue);
                    if (currentArray != null)
                    {
                        currentArray[key] = value;
                    }
                    else
                    {
                        document._scalars[ScalarKey(section, key)] = value;
                    }
                }
                return document;
            }

            public bool TryGetScalar(string section, string key, out string value)
                => _scalars.TryGetValue(ScalarKey(section, key), out value!);

            public bool TryGetFirstArrayValue(string table, string key, out string value)
            {
                value = string.Empty;
                return _arrayTables.TryGetValue(table, out var tables) &&
                       tables.Count > 0 &&
                       tables[0].TryGetValue(key, out value!);
            }

            private static string ScalarKey(string section, string key)
                => section.Length == 0 ? key : section + "." + key;

            private static string StripComment(string line)
            {
                bool inSingle = false;
                bool inDouble = false;
                for (int index = 0; index < line.Length; index++)
                {
                    char character = line[index];
                    if (character == '\'' && !inDouble)
                    {
                        inSingle = !inSingle;
                    }
                    else if (character == '"' && !inSingle && (index == 0 || line[index - 1] != '\\'))
                    {
                        inDouble = !inDouble;
                    }
                    else if (character == '#' && !inSingle && !inDouble)
                    {
                        return line[..index];
                    }
                }
                return line;
            }

            private static bool ArrayValueIsComplete(string value)
                => value.Count(character => character == '[') <= value.Count(character => character == ']');

            private static string ParseTomlValue(string rawValue)
            {
                string value = rawValue.Trim().TrimEnd(',').Trim();
                if (value.Length >= 2 && value[0] == value[^1] && value[0] is '\'' or '"')
                {
                    if (value[0] == '"')
                    {
                        try
                        {
                            return JsonSerializer.Deserialize<string>(value) ?? string.Empty;
                        }
                        catch (JsonException)
                        {

                        }
                    }
                    return value[1..^1];
                }
                return value;
            }
        }
    }
}
