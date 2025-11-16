using System;
using System.Windows.Controls;

namespace CyanTools.Navigation
{
    public sealed class ToolPageMetadata
    {
        public ToolPageMetadata(string displayName, Type controlType, int order, bool requiresUnlock)
        {
            DisplayName = displayName;
            ControlType = controlType;
            Order = order;
            RequiresUnlock = requiresUnlock;
        }

        public string DisplayName { get; }
        public Type ControlType { get; }
        public int Order { get; }
        public bool RequiresUnlock { get; }
    }
}
