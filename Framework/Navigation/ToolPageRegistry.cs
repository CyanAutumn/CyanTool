using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;

namespace CyanTools.Navigation
{
    public static class ToolPageRegistry
    {
        private static IReadOnlyList<ToolPageMetadata>? _cached;

        public static IReadOnlyList<ToolPageMetadata> GetAllPages()
        {
            if (_cached != null)
            {
                return _cached;
            }

            var assembly = typeof(ToolPageRegistry).Assembly;
            _cached = assembly
                .GetTypes()
                .Where(t => typeof(UserControl).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => new { Type = t, Attribute = t.GetCustomAttribute<ToolPageAttribute>() })
                .Where(x => x.Attribute != null)
                .Select(x => new ToolPageMetadata(x.Attribute!.DisplayName, x.Type, x.Attribute.Order, x.Attribute.RequiresUnlock))
                .OrderBy(x => x.Order)
                .ToList();

            return _cached;
        }

        public static void ResetCache() => _cached = null;
    }
}
