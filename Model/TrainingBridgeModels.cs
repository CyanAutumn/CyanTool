using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CyanTools.Model
{
    public sealed record TrainingBridgeBackendOption(string Id, string Label);

    public static class RuntimePathBindingBackendIds
    {
        public const string ComfyUiWindows = "comfyui-windows";
        public const string ComfyUiWsl = "comfyui-wsl";
        public const string ForgeNeoWindows = "forge-neo-windows";
        public const string ForgeNeoWsl = "forge-neo-wsl";

        public static bool IsWsl(string? backendId)
            => backendId is "wsl-sd-scripts" or "diffusion-pipe" or ComfyUiWsl or ForgeNeoWsl;

        public static bool SupportsTraining(string? backendId)
            => backendId is "sd-scripts" or "wsl-sd-scripts" or "anima-fast-lora" or "diffusion-pipe";

        public static bool IsInferenceFrontend(string? backendId)
            => backendId is ComfyUiWindows or ComfyUiWsl or ForgeNeoWindows or ForgeNeoWsl;
    }

    public sealed class TrainingBackendBinding
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; } = string.Empty;
        public string BackendId { get; set; } = "sd-scripts";
        public string TrainerPath { get; set; } = string.Empty;
        public string ConfigurationDirectory { get; set; } = string.Empty;
        public string WslDistribution { get; set; } = string.Empty;
        public string CondaEnvironment { get; set; } = string.Empty;
        public bool IsManaged { get; set; }

        [JsonIgnore]
        public string DisplayName => $"{Name} · {BackendId}";

        [JsonIgnore]
        public bool IsWsl => RuntimePathBindingBackendIds.IsWsl(BackendId);

        [JsonIgnore]
        public bool SupportsTraining => RuntimePathBindingBackendIds.SupportsTraining(BackendId);

        [JsonIgnore]
        public string RootDirectoryLabel => SupportsTraining ? "训练器根目录" : "程序根目录";

        [JsonIgnore]
        public string ConfigurationDirectoryLabel
            => BackendId is RuntimePathBindingBackendIds.ComfyUiWindows or RuntimePathBindingBackendIds.ComfyUiWsl
                ? "工作流目录"
                : "配置文件目录";

        [JsonIgnore]
        public string LocationSummary => $"根目录：{TrainerPath}\n{ConfigurationDirectoryLabel}：{ConfigurationDirectory}";
    }

    public sealed record TrainingBridgeConfigFile(
        string FileName,
        string Content,
        string? SourcePath = null);

    public sealed record TrainingBridgeGpuMetrics(
        string Name,
        double? LoadPercent,
        double? MemoryUsedGb,
        double? MemoryTotalGb,
        double? MemoryLoadPercent,
        double? TemperatureC);

    public sealed record TrainingBridgeMemoryMetrics(
        double? UsedGb,
        double? TotalGb,
        double? LoadPercent);

    public sealed record TrainingBridgeDiskMetrics(
        string Path,
        double? UsedGb,
        double? TotalGb,
        double? LoadPercent);

    public sealed record TrainingBridgeSystemMetrics(
        DateTimeOffset SampledAt,
        double? CpuLoadPercent,
        int LogicalProcessors,
        TrainingBridgeMemoryMetrics Memory,
        TrainingBridgeDiskMetrics Disk,
        TrainingBridgeGpuMetrics? PrimaryGpu);

    public sealed class TrainingBridgeProjectDraft
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string SourceBackendId { get; set; } = "sd-scripts";
        public string TargetBackendId { get; set; } = "sd-scripts";
        public string SourceBindingId { get; set; } = string.Empty;
        public string TargetBindingId { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public override string ToString() => Name;
    }
}
