using System;
using System.Collections.Generic;
using System.IO;
using CyanTools.Utils;

namespace CyanTools.Pipeline
{
    public readonly record struct PipelineWorkflowResolvedPaths(string EffectiveInputPath, string EffectiveOutputPath);

    public static class PipelineWorkflowPathPlanner
    {
        public static string BuildGeneratedOutputPath(string inputPath, string nodeName, int sequence)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(nodeName))
            {
                return string.Empty;
            }

            return SiblingDirectoryPathBuilder.Build(inputPath, $"{sequence:D2}-{nodeName.Trim()}");
        }

        public static PipelineWorkflowResolvedPaths ApplyNodePaths(
            PipelineNodeViewModelBase node,
            string? inheritedInputPath,
            int sequence,
            string? outputPathOverride = null)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            IPipelineDirectoryNode? directoryNode = node.GetDirectoryAutomationNode();
            if (directoryNode == null || directoryNode.DirectoryBehavior == PipelineDirectoryBehavior.None)
            {
                return default;
            }

            string effectiveInputPath = ResolveEffectiveInputPath(directoryNode, inheritedInputPath);
            string effectiveOutputPath = ResolveEffectiveOutputPath(
                directoryNode,
                node.DisplayName,
                effectiveInputPath,
                sequence,
                outputPathOverride);

            directoryNode.PipelineInputPath = effectiveInputPath;
            directoryNode.PipelineOutputPath = effectiveOutputPath;

            return new PipelineWorkflowResolvedPaths(effectiveInputPath, effectiveOutputPath);
        }

        public static void ApplyWorkflowPaths(IEnumerable<PipelineNodeViewModelBase> nodes)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            var nodeList = nodes.ToList();
            IReadOnlyDictionary<PipelineNodeViewModelBase, string> outputPathOverrides = GetOutputPathOverrides(nodeList);

            string? inheritedInputPath = null;
            int index = 0;
            foreach (PipelineNodeViewModelBase node in nodeList)
            {
                index++;
                outputPathOverrides.TryGetValue(node, out string? outputPathOverride);
                PipelineWorkflowResolvedPaths resolvedPaths = ApplyNodePaths(
                    node,
                    inheritedInputPath,
                    index,
                    outputPathOverride);
                if (TryResolveRuntimeOutputPath(node, out string runtimeOutputPath))
                {
                    inheritedInputPath = runtimeOutputPath;
                }
                else if (!string.IsNullOrWhiteSpace(resolvedPaths.EffectiveOutputPath))
                {
                    inheritedInputPath = resolvedPaths.EffectiveOutputPath;
                }
                else if (!string.IsNullOrWhiteSpace(resolvedPaths.EffectiveInputPath))
                {
                    inheritedInputPath = resolvedPaths.EffectiveInputPath;
                }
            }
        }

        public static IReadOnlyDictionary<PipelineNodeViewModelBase, string> GetOutputPathOverrides(
            IEnumerable<PipelineNodeViewModelBase> nodes)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            var nodeList = nodes.ToList();
            var overrides = new Dictionary<PipelineNodeViewModelBase, string>();
            for (var index = 1; index < nodeList.Count; index++)
            {
                IPipelineOutputPathSelectorNode? selector = nodeList[index].GetOutputPathSelectorNode();
                IPipelineDirectoryNode? precedingDirectoryNode = nodeList[index - 1].GetDirectoryAutomationNode();
                if (selector == null ||
                    precedingDirectoryNode?.DirectoryBehavior != PipelineDirectoryBehavior.GeneratedOutput ||
                    !selector.TryGetOutputFolderPath(out string outputPath))
                {
                    continue;
                }

                try
                {
                    overrides[nodeList[index - 1]] = NormalizePath(outputPath);
                }
                catch (ArgumentException)
                {

                }
                catch (IOException)
                {

                }
                catch (NotSupportedException)
                {

                }
            }

            return overrides;
        }

        private static string ResolveEffectiveInputPath(IPipelineDirectoryNode node, string? inheritedInputPath)
        {
            node.HasInheritedInputPath = !string.IsNullOrWhiteSpace(inheritedInputPath);

            string source = string.IsNullOrWhiteSpace(inheritedInputPath)
                ? node.PipelineInputPath
                : inheritedInputPath;

            return NormalizePath(source);
        }

        private static string ResolveEffectiveOutputPath(
            IPipelineDirectoryNode node,
            string nodeName,
            string effectiveInputPath,
            int sequence,
            string? outputPathOverride)
        {
            if (node.DirectoryBehavior == PipelineDirectoryBehavior.GeneratedOutput &&
                !string.IsNullOrWhiteSpace(outputPathOverride))
            {
                return NormalizePath(outputPathOverride);
            }

            return node.DirectoryBehavior switch
            {
                PipelineDirectoryBehavior.GeneratedOutput => BuildGeneratedOutputPath(effectiveInputPath, nodeName, sequence),
                PipelineDirectoryBehavior.InPlace => effectiveInputPath,
                _ => NormalizePath(node.PipelineOutputPath)
            };
        }

        private static bool TryResolveRuntimeOutputPath(PipelineNodeViewModelBase node, out string path)
        {
            path = string.Empty;
            IPipelineRuntimeOutputNode? runtimeOutputNode = node.GetRuntimeOutputNode();
            if (runtimeOutputNode == null ||
                !runtimeOutputNode.TryGetRuntimeOutputPath(out string runtimeOutputPath) ||
                string.IsNullOrWhiteSpace(runtimeOutputPath))
            {
                return false;
            }

            try
            {
                path = NormalizePath(runtimeOutputPath);
                return !string.IsNullOrWhiteSpace(path);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Path.GetFullPath(path.Trim());
        }
    }
}
