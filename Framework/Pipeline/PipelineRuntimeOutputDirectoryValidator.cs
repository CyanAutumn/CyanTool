using System;
using System.IO;
using System.Linq;

namespace CyanTools.Pipeline
{
    public static class PipelineRuntimeOutputDirectoryValidator
    {
        public sealed class RunOutputRootSnapshot
        {
            private string? _resolvedFullPath;

            public string ResolveOnce(Func<string> resolver)
            {
                ArgumentNullException.ThrowIfNull(resolver);
                _resolvedFullPath ??= Path.GetFullPath(resolver());
                return _resolvedFullPath;
            }
        }

        public static void EnsureDirectoryHasFiles(string path, string nodeDisplayName)
        {
            string fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"{nodeDisplayName}输出目录不存在: {fullPath}");
            }

            if (!Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).Any())
            {
                throw new InvalidOperationException($"{nodeDisplayName}下载完成但未产生任何文件。");
            }
        }

        public static string ValidateAndGetPublishPath(string path, string nodeDisplayName)
        {
            string fullPath = Path.GetFullPath(path);
            EnsureDirectoryHasFiles(fullPath, nodeDisplayName);
            return fullPath;
        }
    }
}
