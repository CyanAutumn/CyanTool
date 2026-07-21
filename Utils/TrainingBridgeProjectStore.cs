using CyanTools.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CyanTools.Utils
{
    public sealed class TrainingBridgeProjectStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _projectsDirectory;

        public TrainingBridgeProjectStore(string? projectsDirectory = null)
        {
            _projectsDirectory = projectsDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CyanTools",
                "training-bridge",
                "projects");
        }

        public IReadOnlyList<TrainingBridgeProjectDraft> ListAll()
        {
            if (!Directory.Exists(_projectsDirectory))
            {
                return Array.Empty<TrainingBridgeProjectDraft>();
            }

            return Directory.EnumerateFiles(_projectsDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Select(TryRead)
                .Where(project => project != null)
                .Cast<TrainingBridgeProjectDraft>()
                .OrderByDescending(project => project.UpdatedAtUtc)
                .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        public TrainingBridgeProjectDraft Save(TrainingBridgeProjectDraft project)
        {
            if (string.IsNullOrWhiteSpace(project.Name))
            {
                throw new InvalidOperationException("请填写项目名称。");
            }

            if (!Guid.TryParse(project.Id, out _))
            {
                project.Id = Guid.NewGuid().ToString("D");
            }

            project.Name = project.Name.Trim();
            project.UpdatedAtUtc = DateTime.UtcNow;
            project.Values = new Dictionary<string, string>(project.Values, StringComparer.Ordinal);
            Directory.CreateDirectory(_projectsDirectory);

            string destination = GetPath(project.Id);
            string temporary = destination + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(project, SerializerOptions));
            File.Move(temporary, destination, overwrite: true);
            return project;
        }

        public void Delete(string projectId)
        {
            string path = GetPath(projectId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private TrainingBridgeProjectDraft? TryRead(string path)
        {
            try
            {
                var project = JsonSerializer.Deserialize<TrainingBridgeProjectDraft>(File.ReadAllText(path), SerializerOptions);
                if (project == null || !Guid.TryParse(project.Id, out _))
                {
                    return null;
                }

                project.Values = new Dictionary<string, string>(project.Values ?? new(), StringComparer.Ordinal);
                return project;
            }
            catch
            {
                return null;
            }
        }

        private string GetPath(string projectId)
        {
            if (!Guid.TryParse(projectId, out Guid parsed))
            {
                throw new InvalidOperationException("训练桥项目 ID 无效。");
            }

            return Path.Combine(_projectsDirectory, parsed.ToString("D") + ".json");
        }
    }
}
