using System;

namespace CyanTools.Pipeline
{
    public sealed class PipelineNodeDescriptor
    {
        public PipelineNodeDescriptor(string displayName, Type nodeType, Func<PipelineNodeViewModelBase> factory, bool requiresUnlock = false)
        {
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            NodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            RequiresUnlock = requiresUnlock;
        }

        public string DisplayName { get; }
        public Type NodeType { get; }
        public Func<PipelineNodeViewModelBase> Factory { get; }
        public bool RequiresUnlock { get; }
    }
}
