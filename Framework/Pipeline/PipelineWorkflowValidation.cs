using System;
using System.Collections.Generic;

namespace CyanTools.Pipeline
{
    public static class PipelineWorkflowValidation
    {
        public static bool TryValidateSingletonFirstNode(
            IEnumerable<PipelineNodeViewModelBase> nodes,
            Func<PipelineNodeViewModelBase, bool> predicate,
            string displayName,
            Action<string>? log)
        {
            string? error = ValidateSingletonFirstNode(nodes, predicate, displayName);
            if (string.IsNullOrWhiteSpace(error))
            {
                return true;
            }

            log?.Invoke(error);
            return false;
        }

        public static string? ValidateSingletonFirstNode(
            IEnumerable<PipelineNodeViewModelBase> nodes,
            Func<PipelineNodeViewModelBase, bool> predicate,
            string displayName)
        {
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

            int matchCount = 0;
            int firstMatchIndex = -1;
            int index = 0;

            foreach (PipelineNodeViewModelBase node in nodes)
            {
                if (predicate(node))
                {
                    matchCount++;
                    if (firstMatchIndex < 0)
                    {
                        firstMatchIndex = index;
                    }
                }

                index++;
            }

            if (matchCount > 1)
            {
                return $"{displayName}在同一个工作流中只能出现一次。";
            }

            if (firstMatchIndex > 0)
            {
                return $"{displayName}只能位于工作流的第一个节点。";
            }

            return null;
        }
    }
}
