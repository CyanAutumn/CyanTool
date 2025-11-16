using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace CyanTools.Navigation
{
    public sealed class ToolNavigationService
    {
        private readonly Dictionary<Type, UserControl> _pageCache = new();
        private IReadOnlyList<ToolPageMetadata> _pages;

        public ToolNavigationService()
        {
            _pages = ToolPageRegistry.GetAllPages();
        }

        public IReadOnlyList<NavigationMenuEntry> GetMenuEntries(bool downloadersUnlocked)
        {
            var entries = new List<NavigationMenuEntry> { NavigationMenuEntry.Home };

            foreach (var page in _pages)
            {
                if (page.RequiresUnlock && !downloadersUnlocked)
                {
                    continue;
                }

                entries.Add(NavigationMenuEntry.FromMetadata(page));
            }

            return entries;
        }

        public UserControl? GetOrCreateControl(ToolPageMetadata metadata)
        {
            if (_pageCache.TryGetValue(metadata.ControlType, out var cached))
            {
                return cached;
            }

            if (Activator.CreateInstance(metadata.ControlType) is UserControl control)
            {
                _pageCache[metadata.ControlType] = control;
                return control;
            }

            return null;
        }

        public bool TryGetCachedControl(Type controlType, out UserControl control)
        {
            return _pageCache.TryGetValue(controlType, out control!);
        }
    }
}
