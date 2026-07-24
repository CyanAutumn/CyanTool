using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CyanTools.Model;
using Newtonsoft.Json;

namespace CyanTools.Utils
{
    public sealed class TrainingSetProjectStore
    {
        public const string MetadataDirectoryName = ".cyantools";
        public const string ManagerFileName = "manager.json";

        private static readonly IReadOnlyList<string> LfsPatterns = new[]
        {
            "*.png filter=lfs diff=lfs merge=lfs -text",
            "*.jpg filter=lfs diff=lfs merge=lfs -text",
            "*.jpeg filter=lfs diff=lfs merge=lfs -text",
            "*.webp filter=lfs diff=lfs merge=lfs -text",
            "*.bmp filter=lfs diff=lfs merge=lfs -text",
            "*.gif filter=lfs diff=lfs merge=lfs -text",
            "*.tif filter=lfs diff=lfs merge=lfs -text",
            "*.tiff filter=lfs diff=lfs merge=lfs -text",
            "*.avif filter=lfs diff=lfs merge=lfs -text",
            "*.jxl filter=lfs diff=lfs merge=lfs -text",
            "*.safetensors filter=lfs diff=lfs merge=lfs -text",
            "*.ckpt filter=lfs diff=lfs merge=lfs -text",
            "*.pt filter=lfs diff=lfs merge=lfs -text",
            "*.pth filter=lfs diff=lfs merge=lfs -text",
            "*.bin filter=lfs diff=lfs merge=lfs -text",
            "*.onnx filter=lfs diff=lfs merge=lfs -text",
            "*.gguf filter=lfs diff=lfs merge=lfs -text",
            "*.npy filter=lfs diff=lfs merge=lfs -text",
            "*.npz filter=lfs diff=lfs merge=lfs -text",
            "*.parquet filter=lfs diff=lfs merge=lfs -text",
            "*.arrow filter=lfs diff=lfs merge=lfs -text",
            "*.zip filter=lfs diff=lfs merge=lfs -text",
            "*.7z filter=lfs diff=lfs merge=lfs -text",
            "*.tar filter=lfs diff=lfs merge=lfs -text",
            "*.gz filter=lfs diff=lfs merge=lfs -text",
            "*.mp4 filter=lfs diff=lfs merge=lfs -text",
            "*.mov filter=lfs diff=lfs merge=lfs -text",
            "*.webm filter=lfs diff=lfs merge=lfs -text",
            "events.out.tfevents.* filter=lfs diff=lfs merge=lfs -text"
        };

        private static readonly HashSet<string> LfsExtensions = new(
            LfsPatterns
                .Where(pattern => pattern.StartsWith("*.", StringComparison.Ordinal))
                .Select(pattern => "." + pattern.Substring(2, pattern.IndexOf(' ') - 2)),
            StringComparer.OrdinalIgnoreCase);

        public static readonly IReadOnlyList<string> DatasetBrowserExcludedDirectories = new[]
        {
            ".git",
            ".cyantools",
            "configs",
            "models",
            "samples",
            "tensorboard"
        };

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented
        };

        private readonly TrainingSetGitService _gitService;

        public TrainingSetProjectStore()
            : this(new TrainingSetGitService())
        {
        }

        public TrainingSetProjectStore(TrainingSetGitService gitService)
        {
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        }

        public TrainingSetProject InitializeProject(string rootPath, string name)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("训练集目录不能为空。", nameof(rootPath));
            }

            Directory.CreateDirectory(rootPath);
            EnsureLayout(rootPath);
            WriteGitAttributes(rootPath);
            _gitService.InitializeRepository(rootPath);

            var project = new TrainingSetProject
            {
                Name = string.IsNullOrWhiteSpace(name) ? "未命名训练集" : name.Trim(),
                RootPath = rootPath,
                CoverPath = string.Empty,
                NotePath = ".cyantools/project/README.md",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            SaveProject(project);
            return project;
        }

        public TrainingSetProject LoadProject(string rootPath)
        {
            var managerPath = GetManagerPath(rootPath);
            if (!File.Exists(managerPath))
            {
                throw new FileNotFoundException("未找到训练集管理器项目文件。", managerPath);
            }

            var json = File.ReadAllText(managerPath);
            var project = JsonConvert.DeserializeObject<TrainingSetProject>(json) ?? new TrainingSetProject();
            project.RootPath = rootPath;
            return project;
        }

        public void SaveProject(TrainingSetProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            if (string.IsNullOrWhiteSpace(project.RootPath))
            {
                throw new InvalidOperationException("训练集项目缺少根目录。");
            }

            EnsureLayout(project.RootPath);
            project.UpdatedAt = DateTime.Now;
            var managerPath = GetManagerPath(project.RootPath);
            File.WriteAllText(managerPath, JsonConvert.SerializeObject(project, JsonSettings));

            if (!string.IsNullOrWhiteSpace(project.NotePath))
            {
                var projectNotePath = ResolveRelativePath(project.RootPath, project.NotePath);
                Directory.CreateDirectory(Path.GetDirectoryName(projectNotePath) ?? project.RootPath);
                File.WriteAllText(projectNotePath, project.NoteMarkdown ?? string.Empty);
            }

            var nodeFolder = Path.Combine(project.RootPath, MetadataDirectoryName, "nodes");
            foreach (var node in project.Nodes)
            {
                node.UpdatedAt = DateTime.Now;
                var safeNodeName = SanitizeFileName(node.NodeId);
                var nodePath = Path.Combine(nodeFolder, $"{safeNodeName}.json");
                File.WriteAllText(nodePath, JsonConvert.SerializeObject(node, JsonSettings));

                if (!string.IsNullOrWhiteSpace(node.NotePath) && !string.IsNullOrEmpty(node.NoteMarkdown))
                {
                    var notePath = ResolveRelativePath(project.RootPath, node.NotePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(notePath) ?? project.RootPath);
                    File.WriteAllText(notePath, node.NoteMarkdown);
                }
            }
        }

        public static void EnsureLayout(string rootPath)
        {
            Directory.CreateDirectory(Path.Combine(rootPath, MetadataDirectoryName));
            Directory.CreateDirectory(Path.Combine(rootPath, MetadataDirectoryName, "nodes"));
            Directory.CreateDirectory(Path.Combine(rootPath, MetadataDirectoryName, "notes"));
            Directory.CreateDirectory(Path.Combine(rootPath, MetadataDirectoryName, "covers"));
            Directory.CreateDirectory(Path.Combine(rootPath, MetadataDirectoryName, "project"));
            Directory.CreateDirectory(Path.Combine(rootPath, "configs"));
            Directory.CreateDirectory(Path.Combine(rootPath, "samples"));
            Directory.CreateDirectory(Path.Combine(rootPath, "tensorboard"));
            Directory.CreateDirectory(Path.Combine(rootPath, "models"));
        }

        public static bool ShouldUseLfs(string path)
        {
            var fileName = Path.GetFileName(path);
            return fileName.StartsWith("events.out.tfevents.", StringComparison.OrdinalIgnoreCase)
                || LfsExtensions.Contains(Path.GetExtension(fileName));
        }

        public static TrainingSetStorageAllocation CalculateStorageAllocation(string rootPath)
        {
            var result = new TrainingSetStorageAllocation();
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return result;
            }

            foreach (var file in EnumerateRepositoryFiles(rootPath))
            {
                try
                {
                    var length = new FileInfo(file).Length;
                    if (ShouldUseLfs(file))
                    {
                        result.LfsFileCount++;
                        result.LfsBytes += length;
                    }
                    else
                    {
                        result.GitFileCount++;
                        result.GitBytes += length;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return result;
        }

        public static string GetManagerPath(string rootPath)
        {
            return Path.Combine(rootPath, MetadataDirectoryName, ManagerFileName);
        }

        public static string ToRepositoryRelativePath(string rootPath, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(rootPath);
            var relativePath = Path.GetRelativePath(fullRoot, fullPath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        public static string ResolveRelativePath(string rootPath, string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }

            return Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void WriteGitAttributes(string rootPath)
        {
            var path = Path.Combine(rootPath, ".gitattributes");
            var existingLines = File.Exists(path)
                ? File.ReadAllLines(path).ToList()
                : new List<string>();

            var comparer = StringComparer.OrdinalIgnoreCase;
            foreach (var pattern in LfsPatterns)
            {
                if (!existingLines.Contains(pattern, comparer))
                {
                    existingLines.Add(pattern);
                }
            }

            File.WriteAllLines(path, existingLines);
        }

        private static IEnumerable<string> EnumerateRepositoryFiles(string rootPath)
        {
            var pending = new Stack<string>();
            pending.Push(rootPath);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                IEnumerable<string> files;
                IEnumerable<string> directories;
                try
                {
                    files = Directory.EnumerateFiles(directory).ToArray();
                    directories = Directory.EnumerateDirectories(directory).ToArray();
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }

                foreach (var child in directories)
                {
                    if (!string.Equals(Path.GetFileName(child), ".git", StringComparison.OrdinalIgnoreCase))
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var source = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
            var chars = source.ToCharArray();
            return new string(chars.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }
    }
}
