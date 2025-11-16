namespace CyanTools.Navigation
{
    public sealed class NavigationMenuEntry
    {
        private NavigationMenuEntry(string displayName, ToolPageMetadata? metadata)
        {
            DisplayName = displayName;
            Metadata = metadata;
        }

        public static NavigationMenuEntry Home { get; } = new NavigationMenuEntry("主页", null);

        public static NavigationMenuEntry FromMetadata(ToolPageMetadata metadata)
        {
            return new NavigationMenuEntry(metadata.DisplayName, metadata);
        }

        public string DisplayName { get; }
        public ToolPageMetadata? Metadata { get; }
        public bool IsHome => Metadata == null;
    }
}
