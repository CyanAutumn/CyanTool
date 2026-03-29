using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public enum ExistingTxtHandlingMode
    {
        Skip,
        MergeUnique,
        Overwrite
    }

    public static class TagTextMergeHelper
    {
        public static bool ShouldSkipExistingTxt(bool existingTxtExists, ExistingTxtHandlingMode mode)
        {
            return existingTxtExists && mode == ExistingTxtHandlingMode.Skip;
        }

        public static List<string> ResolveTags(string? existingText, IEnumerable<string>? newTags, ExistingTxtHandlingMode mode)
        {
            var incomingTags = newTags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .ToList() ?? new List<string>();

            if (mode == ExistingTxtHandlingMode.Overwrite)
            {
                return DeduplicatePreservingOrder(incomingTags);
            }

            var merged = SplitTags(existingText);
            merged.AddRange(incomingTags);
            return DeduplicatePreservingOrder(merged);
        }

        public static List<string> SplitTags(string? tagText)
        {
            if (string.IsNullOrWhiteSpace(tagText))
            {
                return new List<string>();
            }

            return tagText
                .Split(new[] { ',', ';', '\n', '\r', '\uFF0C', '\uFF1B' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .ToList();
        }

        private static List<string> DeduplicatePreservingOrder(IEnumerable<string> tags)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                var normalized = NormalizeTag(tag);
                if (string.IsNullOrEmpty(normalized) || !seen.Add(normalized))
                {
                    continue;
                }

                result.Add(tag.Replace('_', ' ').Trim());
            }

            return result;
        }

        private static string NormalizeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            var normalized = tag.Replace('_', ' ').Trim().ToLowerInvariant();
            return string.Concat(normalized.Where(c => !char.IsWhiteSpace(c)));
        }
    }
}
