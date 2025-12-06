using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CyanTools.ViewModel;
using Newtonsoft.Json;

namespace CyanTools.Pipeline
{
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
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var folder = EnsureFolder();
            var path = Path.Combine(folder, $"{name}.json");

            var definitions = nodes.Select(node => new WorkflowNodeDefinition
            {
                NodeType = node.GetType().FullName ?? string.Empty,
                Snapshot = Capture(node)
            }).ToList();

            var json = JsonConvert.SerializeObject(definitions, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        public static List<PipelineNodeViewModelBase> Load(string name, bool downloadersUnlocked)
        {
            var folder = EnsureFolder();
            var path = Path.Combine(folder, $"{name}.json");
            if (!File.Exists(path))
            {
                return new List<PipelineNodeViewModelBase>();
            }

            var json = File.ReadAllText(path);
            var definitions = JsonConvert.DeserializeObject<List<WorkflowNodeDefinition>>(json) ?? new List<WorkflowNodeDefinition>();

            var nodes = new List<PipelineNodeViewModelBase>();
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
                nodes.Add(instance);
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
        }
    }
}
