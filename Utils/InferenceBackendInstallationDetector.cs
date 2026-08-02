using CyanTools.Model;
using System;
using System.IO;

namespace CyanTools.Utils
{
    public sealed record InferenceBackendInstallationStatus(
        string InstallState,
        string Summary,
        string RootPath);

    public static class InferenceBackendInstallationDetector
    {
        public static InferenceBackendInstallationStatus Inspect(
            TrainingBackendBinding binding,
            Func<TrainingBackendBinding, string, string> resolvePath,
            Func<string, bool>? directoryExists = null,
            Func<string, bool>? fileExists = null)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentNullException.ThrowIfNull(resolvePath);
            if (!RuntimePathBindingBackendIds.IsInferenceFrontend(binding.BackendId))
            {
                throw new InvalidOperationException($"运行环境 {binding.BackendId} 不是跑图后端。");
            }
            if (string.IsNullOrWhiteSpace(binding.TrainerPath))
            {
                return new InferenceBackendInstallationStatus(
                    "未安装",
                    "程序根目录为空，请先在路径管理中配置。",
                    string.Empty);
            }

            directoryExists ??= Directory.Exists;
            fileExists ??= File.Exists;
            string root = resolvePath(binding, binding.TrainerPath);
            if (!directoryExists(root))
            {
                return new InferenceBackendInstallationStatus(
                    "未安装",
                    $"未检测到程序根目录：{root}",
                    root);
            }

            string entryName = binding.BackendId is RuntimePathBindingBackendIds.ComfyUiWindows
                or RuntimePathBindingBackendIds.ComfyUiWsl
                    ? "main.py"
                    : "launch.py";
            if (!fileExists(Path.Combine(root, entryName)))
            {
                return new InferenceBackendInstallationStatus(
                    "安装异常",
                    $"已找到根目录，但缺少启动入口 {entryName}。",
                    root);
            }

            return new InferenceBackendInstallationStatus(
                "已安装",
                $"已检测到可启动环境：{root}",
                root);
        }
    }
}
