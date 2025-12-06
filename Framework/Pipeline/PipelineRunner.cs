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
            foreach (var node in nodes.ToList())
            {
                token.ThrowIfCancellationRequested();

                context.AppendLog($"开始执行: {node.DisplayName}");
                node.AttachLogger(context.AppendLog);

                await node.RunAsync(context, token).ConfigureAwait(false);

                context.AppendLog($"完成: {node.DisplayName}");
            }
        }
    }
}
