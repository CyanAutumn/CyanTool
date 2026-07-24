using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CyanTools.Model;

namespace CyanTools.Utils
{





    public sealed class TrainingBackendRunner
    {
        private const string DefaultWslDistribution = "Ubuntu";
        private const string WslSdScriptsEnvironment = "wsl-sd-scripts-cu128";
        private const string DiffusionPipeEnvironment = "diffusion-pipe-cu128";
        private const string WslCondaSource =
            "{ cyan_conda_found=''; for cyan_conda_sh in \"$HOME/miniconda3/etc/profile.d/conda.sh\" \"$HOME/anaconda3/etc/profile.d/conda.sh\" \"/root/miniconda3/etc/profile.d/conda.sh\" \"/opt/conda/etc/profile.d/conda.sh\"; do if [ -f \"$cyan_conda_sh\" ]; then . \"$cyan_conda_sh\"; cyan_conda_found=1; break; fi; done; test -n \"$cyan_conda_found\"; }";

        private readonly string _managedRoot;
        private readonly string _runsRoot;

        public TrainingBackendRunner(string? managedRoot = null, string? runsRoot = null)
        {
            _managedRoot = managedRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CyanTools",
                "training-backends");
            _runsRoot = runsRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CyanTools",
                "training-bridge",
                "runs");
        }

        public TrainingBackendLaunchPlan Prepare(
            string configurationName,
            string backendId,
            IReadOnlyDictionary<string, string> values)
        {
            if (string.IsNullOrWhiteSpace(configurationName))
            {
                throw new InvalidOperationException("请先填写并保存配置文件名称。");
            }

            IReadOnlyDictionary<string, string> normalizedValues = TrainingBridgeConfigService.NormalizeValues(values);
            IReadOnlyList<TrainingBridgeConfigFile> files = TrainingBridgeConfigService.Export(backendId, normalizedValues);
            string runDirectory = CreateRunDirectory(configurationName, backendId);
            Directory.CreateDirectory(runDirectory);

            var generatedFiles = new List<TrainingBridgeConfigFile>(files.Count);
            foreach (TrainingBridgeConfigFile file in files)
            {
                string content = file.Content;
                if (backendId == TrainingBridgeConfigService.DiffusionPipeBackendId &&
                    string.Equals(file.FileName, "anima_train.toml", StringComparison.OrdinalIgnoreCase))
                {
                    string datasetPath = TrainingBridgeConfigService.WindowsPathToWsl(
                        Path.Combine(runDirectory, "anima_dataset.toml"));
                    content = content.Replace(
                        "dataset = \"anima_dataset.toml\"",
                        $"dataset = \"{datasetPath}\"",
                        StringComparison.Ordinal);
                }

                File.WriteAllText(
                    Path.Combine(runDirectory, file.FileName),
                    content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                generatedFiles.Add(new TrainingBridgeConfigFile(file.FileName, content));
            }

            return BuildLaunchPlan(backendId, normalizedValues, runDirectory, generatedFiles);
        }

        public Process Start(TrainingBackendLaunchPlan plan)
        {
            ValidateTrainingInputs(plan.BackendId, plan.Values);
            ValidateLauncherFiles(plan);

            var startInfo = new ProcessStartInfo
            {
                FileName = plan.FileName,
                WorkingDirectory = plan.WorkingDirectory,
                UseShellExecute = true
            };
            foreach (string argument in plan.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动训练进程。请确认训练器已在“选项”中部署完成。");
        }

        private TrainingBackendLaunchPlan BuildLaunchPlan(
            string backendId,
            IReadOnlyDictionary<string, string> values,
            string runDirectory,
            IReadOnlyList<TrainingBridgeConfigFile> generatedFiles)
        {
            return backendId switch
            {
                TrainingBridgeConfigService.SdScriptsBackendId => BuildSdScriptsPlan(
                    backendId,
                    values,
                    runDirectory,
                    generatedFiles,
                    isWsl: false),
                TrainingBridgeConfigService.WslSdScriptsBackendId => BuildSdScriptsPlan(
                    backendId,
                    values,
                    runDirectory,
                    generatedFiles,
                    isWsl: true),
                TrainingBridgeConfigService.AnimaFastLoraBackendId => BuildAnimaFastLoraPlan(
                    values,
                    runDirectory,
                    generatedFiles),
                TrainingBridgeConfigService.DiffusionPipeBackendId => BuildDiffusionPipePlan(
                    values,
                    runDirectory,
                    generatedFiles),
                _ => throw new InvalidOperationException($"不支持的训练器：{backendId}")
            };
        }

        private TrainingBackendLaunchPlan BuildSdScriptsPlan(
            string backendId,
            IReadOnlyDictionary<string, string> values,
            string runDirectory,
            IReadOnlyList<TrainingBridgeConfigFile> generatedFiles,
            bool isWsl)
        {
            TrainingBridgeConfigFile argumentsFile = generatedFiles.Single(file =>
                file.FileName.EndsWith(".args.txt", StringComparison.OrdinalIgnoreCase));
            string datasetPath = isWsl
                ? TrainingBridgeConfigService.WindowsPathToWsl(Path.Combine(runDirectory, "dataset.toml"))
                : Path.Combine(runDirectory, "dataset.toml");
            IReadOnlyList<string> trainerArguments = ReadArgumentLines(argumentsFile.Content)
                .Select(argument => string.Equals(argument, "dataset.toml", StringComparison.OrdinalIgnoreCase)
                    ? datasetPath
                    : argument)
                .ToArray();
            string trainingScript = ResolveSdScriptsTrainingScript(GetValue(values, "model_family", "anima"));

            if (!isWsl)
            {
                string repository = Path.Combine(_managedRoot, "repos", "sd-scripts");
                string python = Path.Combine(_managedRoot, "venvs", "sd-scripts", "Scripts", "python.exe");
                return new TrainingBackendLaunchPlan(
                    backendId,
                    values,
                    runDirectory,
                    generatedFiles,
                    python,
                    [
                        "-m", "accelerate.commands.accelerate_cli", "launch",
                        "--num_processes", "1", "--num_machines", "1", "--mixed_precision", "bf16",
                        "--dynamo_backend", "no", "--gpu_ids", "0", "--num_cpu_threads_per_process", "3",
                        trainingScript,
                        .. trainerArguments
                    ],
                    repository,
                    [python, Path.Combine(repository, trainingScript)]);
            }

            string command = string.Join(" ", new[]
            {
                WslCondaSource,
                $"conda activate {WslSdScriptsEnvironment}",
                "cd ~/sd-scripts",
                "PYTORCH_ALLOC_CONF=expandable_segments:True python -m accelerate.commands.accelerate_cli launch",
                "--num_processes 1 --num_machines 1 --mixed_precision bf16 --dynamo_backend no",
                "--gpu_ids 0 --num_cpu_threads_per_process 3",
                ShellQuote(trainingScript),
                string.Join(" ", trainerArguments.Select(ShellQuote))
            });
            return new TrainingBackendLaunchPlan(
                backendId,
                values,
                runDirectory,
                generatedFiles,
                "wsl.exe",
                ["-d", DefaultWslDistribution, "--", "bash", "-lc", command],
                runDirectory,
                Array.Empty<string>());
        }

        private TrainingBackendLaunchPlan BuildAnimaFastLoraPlan(
            IReadOnlyDictionary<string, string> values,
            string runDirectory,
            IReadOnlyList<TrainingBridgeConfigFile> generatedFiles)
        {
            TrainingBridgeConfigFile argumentsFile = generatedFiles.Single(file =>
                file.FileName.EndsWith(".args.txt", StringComparison.OrdinalIgnoreCase));
            IReadOnlyList<string> trainerArguments = ReadArgumentLines(argumentsFile.Content)
                .Select(argument => string.Equals(argument, "dataset.toml", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(runDirectory, "dataset.toml")
                    : argument)
                .ToArray();
            string trainingRoot = Path.Combine(_managedRoot, "repos", "anima-fast-lora", "external", "anima_lora");
            string python = Path.Combine(trainingRoot, ".venv", "Scripts", "python.exe");
            return new TrainingBackendLaunchPlan(
                TrainingBridgeConfigService.AnimaFastLoraBackendId,
                values,
                runDirectory,
                generatedFiles,
                python,
                [
                    "-m", "accelerate.commands.accelerate_cli", "launch",
                    "--num_processes", "1", "--num_machines", "1", "--mixed_precision", "bf16",
                    "--dynamo_backend", "no", "--gpu_ids", "0", "--num_cpu_threads_per_process", "3",
                    "train.py",
                    .. trainerArguments
                ],
                trainingRoot,
                [python, Path.Combine(trainingRoot, "train.py")]);
        }

        private TrainingBackendLaunchPlan BuildDiffusionPipePlan(
            IReadOnlyDictionary<string, string> values,
            string runDirectory,
            IReadOnlyList<TrainingBridgeConfigFile> generatedFiles)
        {
            string configurationPath = TrainingBridgeConfigService.WindowsPathToWsl(
                Path.Combine(runDirectory, "anima_train.toml"));
            string command = string.Join(" ", new[]
            {
                WslCondaSource,
                $"conda activate {DiffusionPipeEnvironment}",
                "cd ~/diffusion-pipe",
                "export CUDA_HOME=$CONDA_PREFIX PATH=$CUDA_HOME/bin:$PATH",
                "export PYTORCH_ALLOC_CONF=expandable_segments:True NCCL_P2P_DISABLE=1 NCCL_IB_DISABLE=1",
                "$CONDA_PREFIX/bin/deepspeed --num_gpus=1 train.py --deepspeed --config",
                ShellQuote(configurationPath),
                "--trust_cache --i_know_what_i_am_doing"
            });
            return new TrainingBackendLaunchPlan(
                TrainingBridgeConfigService.DiffusionPipeBackendId,
                values,
                runDirectory,
                generatedFiles,
                "wsl.exe",
                ["-d", DefaultWslDistribution, "--", "bash", "-lc", command],
                runDirectory,
                Array.Empty<string>());
        }

        private void ValidateLauncherFiles(TrainingBackendLaunchPlan plan)
        {
            foreach (string path in plan.RequiredWindowsFiles)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"未找到 {plan.BackendId} 的本机训练入口。请先到“选项”完成该训练器部署。",
                        path);
                }
            }
        }

        private static void ValidateTrainingInputs(string backendId, IReadOnlyDictionary<string, string> values)
        {
            string[] requiredKeys = backendId == TrainingBridgeConfigService.DiffusionPipeBackendId
                ? ["dataset_path", "output_dir", "transformer_path", "llm_path", "vae_path"]
                : ["dataset_path", "output_dir", "pretrained_model_path", "qwen3_path", "vae_path"];
            foreach (string key in requiredKeys)
            {
                string value = GetValue(values, key);
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"配置文件缺少必填项：{key}。");
                }
            }

            string datasetPath = GetValue(values, "dataset_path");
            if (!Directory.Exists(datasetPath))
            {
                throw new DirectoryNotFoundException($"训练集路径不存在：{datasetPath}");
            }

            foreach (string key in requiredKeys.Where(key => key is not "dataset_path" and not "output_dir"))
            {
                string path = GetValue(values, key);
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    throw new FileNotFoundException($"配置文件中的路径不存在：{key} = {path}", path);
                }
            }
        }

        private string CreateRunDirectory(string configurationName, string backendId)
        {
            string safeName = string.Concat(configurationName
                .Trim()
                .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            if (safeName.Length == 0)
            {
                safeName = "training";
            }
            return Path.Combine(
                _runsRoot,
                safeName,
                backendId,
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
        }

        private static IReadOnlyList<string> ReadArgumentLines(string content)
            => content
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToArray();

        private static string ResolveSdScriptsTrainingScript(string modelFamily)
            => modelFamily.Trim().ToLowerInvariant() switch
            {
                "anima" => "anima_train_network.py",
                "sdxl" => "sdxl_train_network.py",
                "sd3" => "sd3_train_network.py",
                "flux" => "flux_train_network.py",
                "lumina" => "lumina_train_network.py",
                "hunyuan_image" => "hunyuan_image_train_network.py",
                _ => "train_network.py"
            };

        private static string GetValue(IReadOnlyDictionary<string, string> values, string key, string fallback = "")
            => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : fallback;

        private static string ShellQuote(string value)
            => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    public sealed record TrainingBackendLaunchPlan(
        string BackendId,
        IReadOnlyDictionary<string, string> Values,
        string RunDirectory,
        IReadOnlyList<TrainingBridgeConfigFile> GeneratedFiles,
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        IReadOnlyList<string> RequiredWindowsFiles);
}
