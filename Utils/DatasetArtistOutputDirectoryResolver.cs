using System;
using System.IO;
using System.Linq;

namespace CyanTools.Utils
{
    public static class DatasetArtistOutputDirectoryResolver
    {
        public static string PreferExistingDirectory(string pattern, string resolvedPath, string artistId)
        {
            if (string.IsNullOrWhiteSpace(artistId) || !artistId.All(char.IsDigit))
            {
                return resolvedPath;
            }

            string patternAncestor = Path.GetFullPath(pattern);
            string resolvedAncestor = Path.GetFullPath(resolvedPath);
            while (!string.Equals(Path.GetFileName(patternAncestor), "{name}_{id}", StringComparison.OrdinalIgnoreCase))
            {
                string? nextPatternAncestor = Path.GetDirectoryName(patternAncestor);
                string? nextResolvedAncestor = Path.GetDirectoryName(resolvedAncestor);
                if (nextPatternAncestor == null || nextResolvedAncestor == null
                    || string.Equals(nextPatternAncestor, patternAncestor, StringComparison.OrdinalIgnoreCase))
                {
                    return resolvedPath;
                }

                patternAncestor = nextPatternAncestor;
                resolvedAncestor = nextResolvedAncestor;
            }

            string? parent = Path.GetDirectoryName(resolvedAncestor);
            if (parent == null || !Directory.Exists(parent))
            {
                return resolvedPath;
            }

            string suffix = Path.GetRelativePath(resolvedAncestor, Path.GetFullPath(resolvedPath));
            int bestCount = CountImages(resolvedPath);
            string bestPath = resolvedPath;
            try
            {
                foreach (string sibling in Directory.EnumerateDirectories(parent))
                {
                    if (!Path.GetFileName(sibling).EndsWith("_" + artistId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string candidate = Path.Combine(sibling, suffix);
                    int count = CountImages(candidate);
                    if (count > bestCount)
                    {
                        bestCount = count;
                        bestPath = candidate;
                    }
                }
            }
            catch (IOException)
            {
                return resolvedPath;
            }
            catch (UnauthorizedAccessException)
            {
                return resolvedPath;
            }

            return bestPath;
        }

        private static int CountImages(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            try
            {
                return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Count(DownloadedImageValidator.HasSupportedImageExtension);
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }
}
