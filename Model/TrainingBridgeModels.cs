using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CyanTools.Model
{
    public sealed record TrainingBridgeBackendOption(string Id, string Label);

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
        public string LocationSummary => $"训练器：{TrainerPath}\n配置：{ConfigurationDirectory}";
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
