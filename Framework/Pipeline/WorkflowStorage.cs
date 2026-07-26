using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CyanTools.ViewModel;
using Newtonsoft.Json;

namespace CyanTools.Pipeline
{
    public sealed record WorkflowNodeLayout(
        PipelineNodeViewModelBase Node,
        double? LocationX = null,
        double? LocationY = null,
        string? OutputPortKey = null);

    public static class WorkflowStorage
    {
        private const string FolderName = "Workflow";

        public static string EnsureFolder(string? workflowFolder = null)
        {
            string folder = string.IsNullOrWhiteSpace(workflowFolder)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", FolderName)
                : Path.GetFullPath(workflowFolder.Trim());
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static IReadOnlyList<string> ListWorkflows(string? workflowFolder = null)
        {
            var folder = EnsureFolder(workflowFolder);
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(folder, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void Save(
            string name,
            IEnumerable<PipelineNodeViewModelBase> nodes,
            string? workflowFolder = null)
        {
            Save(name, nodes.Select(node => new WorkflowNodeLayout(node)), workflowFolder);
        }

        public static void Save(
            string name,
            IEnumerable<WorkflowNodeLayout> nodes,
            string? workflowFolder = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var folder = EnsureFolder(workflowFolder);
            var path = GetWorkflowPath(folder, name);

            var definitions = nodes.Select(item => new WorkflowNodeDefinition
            {
                NodeType = item.Node.GetType().FullName ?? string.Empty,
                Snapshot = Capture(item.Node),
                LocationX = item.LocationX,
                LocationY = item.LocationY,
                OutputPortKey = item.OutputPortKey ?? item.Node.WorkflowOutputPortKey
            }).ToList();

            var json = JsonConvert.SerializeObject(definitions, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        public static List<PipelineNodeViewModelBase> Load(
            string name,
            bool downloadersUnlocked,
            string? workflowFolder = null)
        {
            return LoadWithLayout(name, downloadersUnlocked, workflowFolder)
                .Select(item => item.Node)
                .ToList();
        }

        public static List<WorkflowNodeLayout> LoadWithLayout(
            string name,
            bool downloadersUnlocked,
            string? workflowFolder = null)
        {
            var folder = EnsureFolder(workflowFolder);
            var path = GetWorkflowPath(folder, name);
            if (!File.Exists(path))
            {
                return new List<WorkflowNodeLayout>();
            }

            var json = File.ReadAllText(path);
            var definitions = JsonConvert.DeserializeObject<List<WorkflowNodeDefinition>>(json) ?? new List<WorkflowNodeDefinition>();
            bool removedSensitiveValues = false;

            foreach (WorkflowNodeDefinition definition in definitions)
            {
                definition.Snapshot ??= new Dictionary<string, object?>();
                Dictionary<string, object?> safeSnapshot =
                    PipelineSnapshotHelper.RemoveSensitiveProperties(definition.Snapshot);
                if (safeSnapshot.Count != definition.Snapshot.Count)
                {
                    definition.Snapshot = safeSnapshot;
                    removedSensitiveValues = true;
                }
            }

            if (removedSensitiveValues)
            {
                TryRewriteSanitizedWorkflow(path, definitions);
            }

            var nodes = new List<WorkflowNodeLayout>();
            foreach (var def in definitions)
            {
                var descriptor = PipelineCatalog.GetAll().FirstOrDefault(d => d.NodeType.FullName == def.NodeType);
                if (descriptor == null)
                {
                    continue;
                }

                if (descriptor.RequiresUnlock && !downloadersUnlocked)
                {
                    continue;
                }

                var instance = descriptor.Factory();
                Apply(instance, def.Snapshot);
                instance.WorkflowOutputPortKey = def.OutputPortKey;
                nodes.Add(new WorkflowNodeLayout(instance, def.LocationX, def.LocationY, def.OutputPortKey));
            }

            return nodes;
        }

        private static string GetWorkflowPath(string folder, string name)
        {
            string normalizedName = name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedName) ||
                !string.Equals(Path.GetFileName(normalizedName), normalizedName, StringComparison.Ordinal) ||
                normalizedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("工作流名称无效。", nameof(name));
            }

            return Path.Combine(folder, $"{normalizedName}.json");
        }

        private static Dictionary<string, object?> Capture(PipelineNodeViewModelBase node)
        {
            Dictionary<string, object?> snapshot;
            if (node is IPipelineSnapshotProvider provider)
            {
                snapshot = provider.CaptureSnapshot();
            }
            else
            {
                snapshot = PipelineSnapshotHelper.CaptureSimpleProperties(node);
            }

            return PipelineSnapshotHelper.RemoveSensitiveProperties(snapshot);
        }

        private static void Apply(PipelineNodeViewModelBase node, Dictionary<string, object?> snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            Dictionary<string, object?> safeSnapshot =
                PipelineSnapshotHelper.RemoveSensitiveProperties(snapshot);
            if (node is IPipelineSnapshotProvider provider)
            {
                provider.ApplySnapshot(safeSnapshot);
            }
            else
            {
                PipelineSnapshotHelper.ApplySimpleProperties(node, safeSnapshot);
            }
        }

        private static void TryRewriteSanitizedWorkflow(
            string path,
            IReadOnlyCollection<WorkflowNodeDefinition> definitions)
        {
            try
            {
                string json = JsonConvert.SerializeObject(definitions, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch
            {


            }
        }

        private sealed class WorkflowNodeDefinition
        {
            public string NodeType { get; set; } = string.Empty;
            public Dictionary<string, object?> Snapshot { get; set; } = new();
            public double? LocationX { get; set; }
            public double? LocationY { get; set; }
            public string? OutputPortKey { get; set; }
        }
    }
}
