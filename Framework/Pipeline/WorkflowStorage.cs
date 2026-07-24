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

        public static string EnsureFolder()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var folder = Path.Combine(baseDir, "Configs", FolderName);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static IReadOnlyList<string> ListWorkflows()
        {
            var folder = EnsureFolder();
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(folder, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void Save(string name, IEnumerable<PipelineNodeViewModelBase> nodes)
        {
            Save(name, nodes.Select(node => new WorkflowNodeLayout(node)));
        }

        public static void Save(string name, IEnumerable<WorkflowNodeLayout> nodes)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var folder = EnsureFolder();
            var path = Path.Combine(folder, $"{name}.json");

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

        public static List<PipelineNodeViewModelBase> Load(string name, bool downloadersUnlocked)
        {
            return LoadWithLayout(name, downloadersUnlocked)
                .Select(item => item.Node)
                .ToList();
        }

        public static List<WorkflowNodeLayout> LoadWithLayout(string name, bool downloadersUnlocked)
        {
            var folder = EnsureFolder();
            var path = Path.Combine(folder, $"{name}.json");
            if (!File.Exists(path))
            {
                return new List<WorkflowNodeLayout>();
            }

            var json = File.ReadAllText(path);
            var definitions = JsonConvert.DeserializeObject<List<WorkflowNodeDefinition>>(json) ?? new List<WorkflowNodeDefinition>();

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

        private static Dictionary<string, object?> Capture(PipelineNodeViewModelBase node)
        {
            if (node is IPipelineSnapshotProvider provider)
            {
                return provider.CaptureSnapshot();
            }

            return PipelineSnapshotHelper.CaptureSimpleProperties(node);
        }

        private static void Apply(PipelineNodeViewModelBase node, Dictionary<string, object?> snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            if (node is IPipelineSnapshotProvider provider)
            {
                provider.ApplySnapshot(snapshot);
            }
            else
            {
                PipelineSnapshotHelper.ApplySimpleProperties(node, snapshot);
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
