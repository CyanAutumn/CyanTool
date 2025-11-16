using System;

namespace CyanTools.Navigation
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ToolPageAttribute : Attribute
    {
        public ToolPageAttribute(string displayName, int order, bool requiresUnlock = false)
        {
            DisplayName = displayName;
            Order = order;
            RequiresUnlock = requiresUnlock;
        }

        public string DisplayName { get; }
        public int Order { get; }
        public bool RequiresUnlock { get; }
    }
}
