using System;
using System.IO;

namespace CyanTools.Utils
{
    public static class ToolOutputDirectoryDefaults
    {
        public static string BuildDefaultOutputDirectory(string? inputPath, string? toolName)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(toolName))
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

                return Path.Combine(parentDirectory, toolName.Trim());
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string FillWhenEmpty(string? currentOutputPath, string? inputPath, string? toolName)
        {
            if (!string.IsNullOrWhiteSpace(currentOutputPath))
            {
                return currentOutputPath;
            }

            return BuildDefaultOutputDirectory(inputPath, toolName);
        }

        public static string RefreshForInputChange(string? currentOutputPath, string? previousInputPath, string? newInputPath, string? toolName)
        {
            string oldDefault = BuildDefaultOutputDirectory(previousInputPath, toolName);
            if (!string.IsNullOrWhiteSpace(currentOutputPath)
                && !string.IsNullOrWhiteSpace(oldDefault)
                && !PathEquals(currentOutputPath, oldDefault))
            {
                return currentOutputPath;
            }

            return BuildDefaultOutputDirectory(newInputPath, toolName);
        }

        private static bool PathEquals(string left, string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left.Trim()),
                    Path.GetFullPath(right.Trim()),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
