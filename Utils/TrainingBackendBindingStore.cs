using CyanTools.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CyanTools.Utils
{




    public sealed class TrainingBackendBindingStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _bindingsPath;
        private readonly string _managedRoot;

        public TrainingBackendBindingStore(string? bindingsPath = null, string? managedRoot = null)
        {
            _managedRoot = string.IsNullOrWhiteSpace(managedRoot)
                ? GetDefaultManagedRoot()
                : Path.GetFullPath(managedRoot);
            _bindingsPath = bindingsPath ?? Path.Combine(_managedRoot, "state", "trainer-bindings.json");
        }

        public IReadOnlyList<TrainingBackendBinding> ListAll()
        {
            TrainingBackendBinding[] managed = CreateManagedBindings().ToArray();
            TrainingBackendBinding[] custom = ReadCustomBindings()
                .Where(binding => managed.All(managedBinding =>
                    !string.Equals(managedBinding.Id, binding.Id, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            return managed.Concat(custom)
                .OrderBy(binding => binding.IsManaged ? 0 : 1)
                .ThenBy(binding => binding.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        public void SaveCustom(TrainingBackendBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (binding.IsManaged)
            {
                throw new InvalidOperationException("CyanTools 托管的路径绑定不能覆盖；请新建一个自定义路径绑定。");
            }

            Validate(binding);
            TrainingBackendBinding[] bindings = ReadCustomBindings().ToArray();
            int existingIndex = Array.FindIndex(bindings, item =>
                string.Equals(item.Id, binding.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                bindings[existingIndex] = Clone(binding);
            }
            else
            {
                bindings = [.. bindings, Clone(binding)];
            }

            WriteCustomBindings(bindings);
        }

        public void DeleteCustom(string bindingId)
        {
            if (string.IsNullOrWhiteSpace(bindingId))
            {
                return;
            }

            TrainingBackendBinding[] remaining = ReadCustomBindings()
                .Where(binding => !string.Equals(binding.Id, bindingId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            WriteCustomBindings(remaining);
        }

        public static string GetDefaultManagedRoot()
            => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CyanTools",
                "training-backends");

        private IEnumerable<TrainingBackendBinding> CreateManagedBindings()
        {
            string windowsSdScripts = Path.Combine(_managedRoot, "repos", "sd-scripts");
            string animaFastLora = Path.Combine(_managedRoot, "repos", "anima-fast-lora", "external", "anima_lora");
            string compatibleInferenceRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AnimaTrainer",
                "repos");
            string comfyUiWindows = Path.Combine(compatibleInferenceRoot, "comfyui-windows");
            string forgeNeoWindows = Path.Combine(compatibleInferenceRoot, "forge-neo-windows");
            const string distribution = "Ubuntu";
            return
            [
                new TrainingBackendBinding
                {
                    Id = "managed-sd-scripts",
                    Name = "CyanTools · sd-scripts Windows",
                    BackendId = TrainingBridgeConfigService.SdScriptsBackendId,
                    TrainerPath = windowsSdScripts,
                    ConfigurationDirectory = Path.Combine(windowsSdScripts, "configs"),
                    IsManaged = true
                },
                new TrainingBackendBinding
                {
                    Id = "managed-anima-fast-lora",
                    Name = "CyanTools · Anima Fast LoRA Windows",
                    BackendId = TrainingBridgeConfigService.AnimaFastLoraBackendId,
                    TrainerPath = animaFastLora,
                    ConfigurationDirectory = Path.Combine(animaFastLora, "configs"),
                    IsManaged = true
                },
                new TrainingBackendBinding
                {
                    Id = "managed-wsl-sd-scripts",
                    Name = "CyanTools · wsl-sd-scripts",
                    BackendId = TrainingBridgeConfigService.WslSdScriptsBackendId,
                    TrainerPath = "~/sd-scripts",
                    ConfigurationDirectory = "~/sd-scripts/configs",
                    WslDistribution = distribution,
                    CondaEnvironment = "wsl-sd-scripts-cu128",
                    IsManaged = true
                },
                new TrainingBackendBinding
                {
                    Id = "managed-diffusion-pipe",
                    Name = "CyanTools · diffusion-pipe",
                    BackendId = TrainingBridgeConfigService.DiffusionPipeBackendId,
                    TrainerPath = "~/diffusion-pipe",
                    ConfigurationDirectory = "~/diffusion-pipe/configs",
                    WslDistribution = distribution,
                    CondaEnvironment = "diffusion-pipe-cu128",
                    IsManaged = true
                },
                new TrainingBackendBinding
                {
                    Id = "managed-comfyui-windows",
                    Name = "兼容默认路径 · ComfyUI Windows",
                    BackendId = RuntimePathBindingBackendIds.ComfyUiWindows,
                    TrainerPath = comfyUiWindows,
                    ConfigurationDirectory = Path.Combine(comfyUiWindows, "user", "default", "workflows"),
                    IsManaged = true
                },
                new TrainingBackendBinding
                {
                    Id = "managed-comfyui-wsl",
                    Name = "兼容默认路径 · ComfyUI WSL2",
                    BackendId = RuntimePathBindingBackendIds.ComfyUiWsl,
                    TrainerPath = "~/ComfyUI",
                    ConfigurationDirectory = "~/ComfyUI/user/default/workflows",
                    WslDistribution = distribution,
                    IsManaged = true
                },
                new TrainingBackendBinding
                {
                    Id = "managed-forge-neo-windows",
                    Name = "兼容默认路径 · Webui Forge Neo Windows",
                    BackendId = RuntimePathBindingBackendIds.ForgeNeoWindows,
                    TrainerPath = forgeNeoWindows,
                    ConfigurationDirectory = forgeNeoWindows,
                    IsManaged = true
                },
                new TrainingBackendBinding
                {
                    Id = "managed-forge-neo-wsl",
                    Name = "兼容默认路径 · Webui Forge Neo WSL2",
                    BackendId = RuntimePathBindingBackendIds.ForgeNeoWsl,
                    TrainerPath = "~/sd-webui-forge-neo",
                    ConfigurationDirectory = "~/sd-webui-forge-neo",
                    WslDistribution = distribution,
                    IsManaged = true
                }
            ];
        }

        private IReadOnlyList<TrainingBackendBinding> ReadCustomBindings()
        {
            if (!File.Exists(_bindingsPath))
            {
                return Array.Empty<TrainingBackendBinding>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<TrainingBackendBinding>>(
                        File.ReadAllText(_bindingsPath),
                        SerializerOptions)
                    ?.Where(IsValidCustomBinding)
                    .Select(Clone)
                    .ToArray()
                    ?? Array.Empty<TrainingBackendBinding>();
            }
            catch
            {
                return Array.Empty<TrainingBackendBinding>();
            }
        }

        private void WriteCustomBindings(IEnumerable<TrainingBackendBinding> bindings)
        {
            TrainingBackendBinding[] serializable = bindings
                .Where(IsValidCustomBinding)
                .Select(Clone)
                .ToArray();
            string? directory = Path.GetDirectoryName(_bindingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = _bindingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(serializable, SerializerOptions));
            File.Move(temporaryPath, _bindingsPath, overwrite: true);
        }

        private static void Validate(TrainingBackendBinding binding)
        {
            if (string.IsNullOrWhiteSpace(binding.Name))
            {
                throw new InvalidOperationException("请填写路径绑定名称。");
            }
            if (!IsSupportedBackend(binding.BackendId))
            {
                throw new InvalidOperationException("请选择支持的运行环境类型。");
            }
            if (string.IsNullOrWhiteSpace(binding.TrainerPath))
            {
                throw new InvalidOperationException("请填写程序或训练器根目录。");
            }
            if (string.IsNullOrWhiteSpace(binding.ConfigurationDirectory))
            {
                throw new InvalidOperationException("请填写配置文件目录。");
            }

            if (!Guid.TryParse(binding.Id, out _))
            {
                binding.Id = Guid.NewGuid().ToString("D");
            }
            binding.Name = binding.Name.Trim();
            binding.BackendId = binding.BackendId.Trim();
            binding.TrainerPath = binding.TrainerPath.Trim();
            binding.ConfigurationDirectory = binding.ConfigurationDirectory.Trim();
            binding.WslDistribution = binding.WslDistribution.Trim();
            binding.CondaEnvironment = binding.CondaEnvironment.Trim();
        }

        private static bool IsValidCustomBinding(TrainingBackendBinding binding)
            => binding != null &&
                !binding.IsManaged &&
                Guid.TryParse(binding.Id, out _) &&
                !string.IsNullOrWhiteSpace(binding.Name) &&
                !string.IsNullOrWhiteSpace(binding.TrainerPath) &&
                !string.IsNullOrWhiteSpace(binding.ConfigurationDirectory) &&
                IsSupportedBackend(binding.BackendId);

        private static bool IsSupportedBackend(string backendId)
            => backendId is TrainingBridgeConfigService.SdScriptsBackendId
                or TrainingBridgeConfigService.WslSdScriptsBackendId
                or TrainingBridgeConfigService.AnimaFastLoraBackendId
                or TrainingBridgeConfigService.DiffusionPipeBackendId
                or RuntimePathBindingBackendIds.ComfyUiWindows
                or RuntimePathBindingBackendIds.ComfyUiWsl
                or RuntimePathBindingBackendIds.ForgeNeoWindows
                or RuntimePathBindingBackendIds.ForgeNeoWsl;

        private static TrainingBackendBinding Clone(TrainingBackendBinding binding)
            => new()
            {
                Id = binding.Id,
                Name = binding.Name,
                BackendId = binding.BackendId,
                TrainerPath = binding.TrainerPath,
                ConfigurationDirectory = binding.ConfigurationDirectory,
                WslDistribution = binding.WslDistribution,
                CondaEnvironment = binding.CondaEnvironment,
                IsManaged = binding.IsManaged
            };
    }
}
