using System;
using System.Collections.Generic;

namespace CyanTools.Utils
{
    public static class TaggingTextParser
    {
        public static IReadOnlyList<string> ParseTagList(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<string>();
            }

            char[] separators = { ',', ';', '\n', '\r', '\uFF1B', '\uFF0C' };
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string tag in text.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string cleaned = NormalizeInputTag(tag);
                if (string.IsNullOrEmpty(cleaned) || !seen.Add(cleaned))
                {
                    continue;
                }

                result.Add(tag.Trim());
            }

            return result;
        }

        private static string NormalizeInputTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            string normalized = tag.Replace('_', ' ').Trim().ToLowerInvariant();
            return string.Concat(normalized.Where(c => !char.IsWhiteSpace(c)));
        }
    }
}
