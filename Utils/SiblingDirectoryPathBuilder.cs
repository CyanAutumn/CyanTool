using System;
using System.IO;

namespace CyanTools.Utils
{
    public static class SiblingDirectoryPathBuilder
    {
        public static string Build(string? inputPath, string? directoryName)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(directoryName))
            {
                return string.Empty;
            }

            try
            {
                string normalizedInput = Path.GetFullPath(inputPath.Trim());
                string? parentDirectory = Directory.GetParent(normalizedInput)?.FullName;
                if (string.IsNullOrWhiteSpace(parentDirectory))
                {
                    return string.Empty;
                }

                string safeDirectoryName = SanitizePathSegment(directoryName.Trim());
                if (string.IsNullOrWhiteSpace(safeDirectoryName))
                {
                    return string.Empty;
                }

                return Path.Combine(parentDirectory, safeDirectoryName);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SanitizePathSegment(string value)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return string.Join(string.Empty, value.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }
}
