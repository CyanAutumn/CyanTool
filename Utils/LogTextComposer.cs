using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public static class LogTextComposer
    {
        public static string Compose(IEnumerable<string>? lines)
        {
            return string.Join(Environment.NewLine, lines ?? Enumerable.Empty<string>());
        }
    }
}
