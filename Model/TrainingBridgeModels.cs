using System;
using System.Collections.Generic;

namespace CyanTools.Model
{
    public sealed record TrainingBridgeBackendOption(string Id, string Label);

    public sealed record TrainingBridgeConfigFile(string FileName, string Content);

    public sealed class TrainingBridgeProjectDraft
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; } = string.Empty;
        public string SourceBackendId { get; set; } = "sd-scripts";
        public string TargetBackendId { get; set; } = "sd-scripts";
        public string Notes { get; set; } = string.Empty;
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public override string ToString() => Name;
    }
}
