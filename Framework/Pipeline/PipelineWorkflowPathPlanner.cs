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

        public static PipelineWorkflowResolvedPaths ApplyNodePaths(PipelineNodeViewModelBase node, string? inheritedInputPath, int sequence)
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
            string effectiveOutputPath = ResolveEffectiveOutputPath(directoryNode, node.DisplayName, effectiveInputPath, sequence);

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

            string? inheritedInputPath = null;
            int index = 0;
            foreach (PipelineNodeViewModelBase node in nodes)
            {
                index++;
                PipelineWorkflowResolvedPaths resolvedPaths = ApplyNodePaths(node, inheritedInputPath, index);
                if (!string.IsNullOrWhiteSpace(resolvedPaths.EffectiveOutputPath))
                {
                    inheritedInputPath = resolvedPaths.EffectiveOutputPath;
                }
                else if (!string.IsNullOrWhiteSpace(resolvedPaths.EffectiveInputPath))
                {
                    inheritedInputPath = resolvedPaths.EffectiveInputPath;
                }
            }
        }

        private static string ResolveEffectiveInputPath(IPipelineDirectoryNode node, string? inheritedInputPath)
        {
            string source = string.IsNullOrWhiteSpace(inheritedInputPath)
                ? node.PipelineInputPath
                : inheritedInputPath;

            return NormalizePath(source);
        }

        private static string ResolveEffectiveOutputPath(IPipelineDirectoryNode node, string nodeName, string effectiveInputPath, int sequence)
        {
            return node.DirectoryBehavior switch
            {
                PipelineDirectoryBehavior.GeneratedOutput => BuildGeneratedOutputPath(effectiveInputPath, nodeName, sequence),
                PipelineDirectoryBehavior.InPlace => effectiveInputPath,
                _ => NormalizePath(node.PipelineOutputPath)
            };
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
