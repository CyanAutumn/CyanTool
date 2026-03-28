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
            string? inheritedInputPath = null;
            int nodeSequence = 0;

            foreach (var node in nodes.ToList())
            {
                token.ThrowIfCancellationRequested();
                nodeSequence++;

                PipelineWorkflowResolvedPaths resolvedPaths = PipelineWorkflowPathPlanner.ApplyNodePaths(node, inheritedInputPath, nodeSequence);
                context.UpdateEffectivePaths(resolvedPaths.EffectiveInputPath, resolvedPaths.EffectiveOutputPath, nodeSequence);

                if (!string.IsNullOrWhiteSpace(resolvedPaths.EffectiveOutputPath))
                {
                    inheritedInputPath = resolvedPaths.EffectiveOutputPath;
                }
                else if (!string.IsNullOrWhiteSpace(resolvedPaths.EffectiveInputPath))
                {
                    inheritedInputPath = resolvedPaths.EffectiveInputPath;
                }

                context.AppendLog($"开始执行: {node.DisplayName}");
                node.AttachLogger(context.AppendLog);

                await node.RunAsync(context, token).ConfigureAwait(false);

                context.AppendLog($"完成: {node.DisplayName}");
            }
        }
    }
}
