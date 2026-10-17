using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CyanTools.ViewModel;
using CyanTools.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

            var json = SerializeDefinitions(definitions);
            AtomicFile.WriteAllText(path, json);
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
            var document = JToken.Parse(json);
            JToken nodesToken;
            if (document is JArray) nodesToken = document;
            else if (document is JObject envelope && envelope.Value<int?>("Version") == 1 && envelope["Nodes"] is JArray)
                nodesToken = envelope["Nodes"]!;
            else throw new InvalidDataException("工作流格式或版本不受支持，原文件保持不变。");
            var definitions = nodesToken.ToObject<List<WorkflowNodeDefinition>>()
                ?? throw new InvalidDataException("工作流内容为空或无效。");
            var descriptors = PipelineCatalog.GetAll().ToDictionary(d => d.NodeType.FullName!);

            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.NodeType) ||
                    !descriptors.TryGetValue(definition.NodeType, out var descriptor))
                    throw new InvalidDataException($"工作流包含不可用节点：{definition?.NodeType ?? "<null>"}。未加载任何节点，请恢复对应功能后重试。");
                if (descriptor.RequiresUnlock && !downloadersUnlocked)
                    throw new InvalidDataException($"工作流节点尚未解锁：{definition.NodeType}。未加载任何节点。");
            }
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
                var descriptor = descriptors[def.NodeType];

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
                string json = SerializeDefinitions(definitions);
                AtomicFile.WriteAllText(path, json);
            }
            catch
            {


            }
        }

        private static string SerializeDefinitions(IEnumerable<WorkflowNodeDefinition> nodes)
            => JsonConvert.SerializeObject(new { Version = 1, Nodes = nodes }, Formatting.Indented);

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
