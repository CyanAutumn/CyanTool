using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Pipeline
{
    public sealed class PipelineRunner
    {
        public async Task RunAsync(IEnumerable<PipelineNodeViewModelBase> nodes, PipelineContext context, CancellationToken token)
        {
            var nodesToRun = nodes.ToList();
            IReadOnlyDictionary<PipelineNodeViewModelBase, string> outputPathOverrides =
                PipelineWorkflowPathPlanner.GetOutputPathOverrides(nodesToRun);
            string? inheritedInputPath = null;
            int nodeSequence = 0;

            foreach (var node in nodesToRun)
            {
                token.ThrowIfCancellationRequested();
                nodeSequence++;

                IPipelineRuntimeOutputNode? runtimeOutputNode = node.GetRuntimeOutputNode();
                runtimeOutputNode?.ResetRuntimeOutputPath();

                outputPathOverrides.TryGetValue(node, out string? outputPathOverride);
                PipelineWorkflowResolvedPaths resolvedPaths = PipelineWorkflowPathPlanner.ApplyNodePaths(
                    node,
                    inheritedInputPath,
                    nodeSequence,
                    outputPathOverride);
                context.UpdateEffectivePaths(resolvedPaths.EffectiveInputPath, resolvedPaths.EffectiveOutputPath, nodeSequence);

                context.AppendLog($"开始执行: {node.DisplayName}");
                node.AttachLogger(context.AppendLog);

                await node.RunAsync(context, token).ConfigureAwait(false);

                if (runtimeOutputNode != null && runtimeOutputNode.TryGetRuntimeOutputPath(out string runtimeOutputPath))
                {
                    inheritedInputPath = PipelineWorkflowPathPlanner.ResolveSelectedOutputPath(node, runtimeOutputPath);
                }
                else if (!string.IsNullOrWhiteSpace(resolvedPaths.EffectiveOutputPath))
                {
                    inheritedInputPath = PipelineWorkflowPathPlanner.ResolveSelectedOutputPath(node, resolvedPaths.EffectiveOutputPath);
                }
                else if (!string.IsNullOrWhiteSpace(resolvedPaths.EffectiveInputPath))
                {
                    inheritedInputPath = resolvedPaths.EffectiveInputPath;
                }

                context.AppendLog($"完成: {node.DisplayName}");
            }
        }
    }
}
