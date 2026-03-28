using System;
using System.Collections.Generic;
using System.Linq;

namespace CyanTools.Utils
{
    public static class DatasetPromptHelper
    {
        public const string BlackBoxTag = "black box";

        public static string BuildPromptContent(string? existingContent, IEnumerable<string>? extraTags = null)
        {
            var tags = SplitTags(existingContent);
            var seen = new HashSet<string>(tags.Select(NormalizeTag), StringComparer.Ordinal);

            if (extraTags != null)
            {
                foreach (var rawTag in extraTags)
                {
                    var normalized = NormalizeTag(rawTag);
                    if (string.IsNullOrEmpty(normalized) || !seen.Add(normalized))
                    {
                        continue;
                    }

                    tags.Add(rawTag.Replace('_', ' ').Trim());
                }
            }

            return string.Join(", ", tags);
        }

        public static bool AreEquivalent(string? left, string? right)
        {
            var leftTags = SplitTags(left);
            var rightTags = SplitTags(right);

            if (leftTags.Count != rightTags.Count)
            {
                return false;
            }

            for (int index = 0; index < leftTags.Count; index++)
            {
                if (!string.Equals(
                    NormalizeTag(leftTags[index]),
                    NormalizeTag(rightTags[index]),
                    StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> SplitTags(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new List<string>();
            }

            return content
                .Split(new[] { ',', '，', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Replace('_', ' ').Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToList();
        }

        private static string NormalizeTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            var normalized = tag.Replace('_', ' ').Trim().ToLowerInvariant();
            return string.Concat(normalized.Where(ch => !char.IsWhiteSpace(ch)));
        }
    }
}
