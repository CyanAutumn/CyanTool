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
            "{ cyan_conda_found=''; for cyan_conda_sh in \"$HOME/miniconda3/etc/profile.d/conda.sh\" \"$HOME/anaconda3/etc/profile.d/conda.sh\" \"/opt/conda/etc/profile.d/conda.sh\"; do if [ -f \"$cyan_conda_sh\" ]; then . \"$cyan_conda_sh\"; cyan_conda_found=1; break; fi; done; test -n \"$cyan_conda_found\"; }";

        private readonly string _managedRoot;
        private readonly string _runsRoot;

        public TrainingBackendRunner(string? managedRoot = null, string? runsRoot = null)
        {
            _managedRoot = managedRoot ?? TrainingBackendBindingStore.GetDefaultManagedRoot();
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
            TrainingBackendBinding binding = new TrainingBackendBindingStore(managedRoot: _managedRoot)
                .ListAll()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.BackendId,
                    backendId,
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"未找到训练器绑定：{backendId}");
            return Prepare(configurationName, binding, values);
        }

        public TrainingBackendLaunchPlan Prepare(
            string configurationName,
            TrainingBackendBinding binding,
            IReadOnlyDictionary<string, string> values)
        {
            if (string.IsNullOrWhiteSpace(configurationName))
            {
                throw new InvalidOperationException("请先填写并保存配置文件名称。");
            }
            ArgumentNullException.ThrowIfNull(binding);
            if (string.IsNullOrWhiteSpace(binding.BackendId) ||
                string.IsNullOrWhiteSpace(binding.TrainerPath))
            {
                throw new InvalidOperationException("所选训练器绑定缺少训练器类型或根目录。");
            }

            IReadOnlyDictionary<string, string> normalizedValues = TrainingBridgeConfigService.NormalizeValues(values);
            IReadOnlyList<TrainingBridgeConfigFile> files = TrainingBridgeConfigService.Export(binding.BackendId, normalizedValues);
            string runDirectory = CreateRunDirectory(configurationName, binding.BackendId);
            Directory.CreateDirectory(runDirectory);

            var generatedFiles = new List<TrainingBridgeConfigFile>(files.Count);
            foreach (TrainingBridgeConfigFile file in files)
            {
                string content = file.Content;
                if (binding.BackendId == TrainingBridgeConfigService.DiffusionPipeBackendId &&
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

            return BuildLaunchPlan(binding, normalizedValues, runDirectory, generatedFiles);
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
            TrainingBackendBinding binding,
            IReadOnlyDictionary<string, string> values,
            string runDirectory,
            IReadOnlyList<TrainingBridgeConfigFile> generatedFiles)
        {
            return binding.BackendId switch
            {
                TrainingBridgeConfigService.SdScriptsBackendId => BuildSdScriptsPlan(
                    binding,
                    values,
                    runDirectory,
                    generatedFiles,
                    isWsl: false),
                TrainingBridgeConfigService.WslSdScriptsBackendId => BuildSdScriptsPlan(
                    binding,
                    values,
                    runDirectory,
                    generatedFiles,
                    isWsl: true),
                TrainingBridgeConfigService.AnimaFastLoraBackendId => BuildAnimaFastLoraPlan(
                    binding,
                    values,
                    runDirectory,
                    generatedFiles),
                TrainingBridgeConfigService.DiffusionPipeBackendId => BuildDiffusionPipePlan(
                    binding,
                    values,
                    runDirectory,
                    generatedFiles),
                _ => throw new InvalidOperationException($"不支持的训练器：{binding.BackendId}")
            };
        }

        private TrainingBackendLaunchPlan BuildSdScriptsPlan(
            TrainingBackendBinding binding,
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
                string repository = binding.TrainerPath;
                string python = ResolveSdScriptsPython(binding);
                return new TrainingBackendLaunchPlan(
                    binding.BackendId,
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

            string distribution = GetBindingValue(binding.WslDistribution, DefaultWslDistribution);
            string condaEnvironment = GetBindingValue(binding.CondaEnvironment, WslSdScriptsEnvironment);
            string trainerPath = ResolveWslTrainerPath(binding.TrainerPath);
            string command = string.Join(" ", new[]
            {
                WslCondaSource,
                $"conda activate {ShellQuote(condaEnvironment)}",
                $"cd {ShellPathQuote(trainerPath)}",
                "PYTORCH_ALLOC_CONF=expandable_segments:True python -m accelerate.commands.accelerate_cli launch",
                "--num_processes 1 --num_machines 1 --mixed_precision bf16 --dynamo_backend no",
                "--gpu_ids 0 --num_cpu_threads_per_process 3",
                ShellQuote(trainingScript),
                string.Join(" ", trainerArguments.Select(ShellQuote))
            });
            return new TrainingBackendLaunchPlan(
                binding.BackendId,
                values,
                runDirectory,
                generatedFiles,
                "wsl.exe",
                ["-d", distribution, "--", "bash", "-lc", command],
                runDirectory,
                Array.Empty<string>());
        }

        private TrainingBackendLaunchPlan BuildAnimaFastLoraPlan(
            TrainingBackendBinding binding,
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
            string trainingRoot = binding.TrainerPath;
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
            TrainingBackendBinding binding,
            IReadOnlyDictionary<string, string> values,
            string runDirectory,
            IReadOnlyList<TrainingBridgeConfigFile> generatedFiles)
        {
            string configurationPath = TrainingBridgeConfigService.WindowsPathToWsl(
                Path.Combine(runDirectory, "anima_train.toml"));
            string distribution = GetBindingValue(binding.WslDistribution, DefaultWslDistribution);
            string condaEnvironment = GetBindingValue(binding.CondaEnvironment, DiffusionPipeEnvironment);
            string trainerPath = ResolveWslTrainerPath(binding.TrainerPath);
            string command = string.Join(" ", new[]
            {
                WslCondaSource,
                $"conda activate {ShellQuote(condaEnvironment)}",
                $"cd {ShellPathQuote(trainerPath)}",
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
                ["-d", distribution, "--", "bash", "-lc", command],
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

        private string ResolveSdScriptsPython(TrainingBackendBinding binding)
        {
            string[] candidates = binding.IsManaged
                ?
                [
                    Path.Combine(_managedRoot, "venvs", "sd-scripts", "Scripts", "python.exe"),
                    Path.Combine(binding.TrainerPath, ".venv", "Scripts", "python.exe"),
                    Path.Combine(binding.TrainerPath, "venv", "Scripts", "python.exe")
                ]
                :
                [
                    Path.Combine(binding.TrainerPath, ".venv", "Scripts", "python.exe"),
                    Path.Combine(binding.TrainerPath, "venv", "Scripts", "python.exe")
                ];
            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        private static string GetValue(IReadOnlyDictionary<string, string> values, string key, string fallback = "")
            => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : fallback;

        private static string GetBindingValue(string value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string ResolveWslTrainerPath(string value)
        {
            string path = value.Trim();
            if (path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase))
            {
                string[] segments = path.TrimStart('\\')
                    .Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 3)
                {
                    return "/" + string.Join('/', segments.Skip(2));
                }
            }
            if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/'))
            {
                return TrainingBridgeConfigService.WindowsPathToWsl(path);
            }
            return path.Replace('\\', '/');
        }

        private static string ShellPathQuote(string value)
        {
            string path = value.Trim();
            if (path == "~")
            {
                return "\"$HOME\"";
            }
            if (path.StartsWith("~/", StringComparison.Ordinal))
            {
                return "\"$HOME\"/" + ShellQuote(path[2..]);
            }
            return ShellQuote(path);
        }

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
