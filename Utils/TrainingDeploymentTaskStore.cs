using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CyanTools.Utils
{
    public enum TrainingDeploymentTaskState
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Cancelled
    }

    public sealed class TrainingDeploymentTaskRecord
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";
        public string BackendId { get; set; } = "";
        public TrainingDeploymentTaskState State { get; set; }
        public string Summary { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public List<string> Logs { get; set; } = new();
    }

    public sealed class TrainingDeploymentTaskStore
    {
        private static readonly object Gate = new();
        private static readonly Regex AnsiPattern = new("\\x1B\\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
        private readonly string _storePath;

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return options;
        }

        public TrainingDeploymentTaskStore(string managedRoot)
        {
            _storePath = Path.Combine(managedRoot, "state", "tasks.json");
        }

        public TrainingDeploymentTaskRecord Create(string backendId, string kind)
        {
            lock (Gate)
            {
                List<TrainingDeploymentTaskRecord> records = LoadUnsafe();
                DateTimeOffset now = DateTimeOffset.UtcNow;
                var record = new TrainingDeploymentTaskRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = kind,
                    BackendId = backendId,
                    State = TrainingDeploymentTaskState.Queued,
                    Summary = "任务已排队。",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                records.Add(record);
                SaveUnsafe(records);
                return record;
            }
        }

        public void AppendLog(string taskId, string message)
        {
            string clean = Sanitize(message);
            if (string.IsNullOrWhiteSpace(clean)) return;
            lock (Gate)
            {
                List<TrainingDeploymentTaskRecord> records = LoadUnsafe();
                TrainingDeploymentTaskRecord? record = records.FirstOrDefault(item => item.Id == taskId);
                if (record == null) return;
                record.Logs.Add(clean);
                record.UpdatedAt = DateTimeOffset.UtcNow;
                SaveUnsafe(records);
            }
        }

        public void UpdateState(string taskId, TrainingDeploymentTaskState state, string summary)
        {
            lock (Gate)
            {
                List<TrainingDeploymentTaskRecord> records = LoadUnsafe();
                TrainingDeploymentTaskRecord? record = records.FirstOrDefault(item => item.Id == taskId);
                if (record == null) return;
                record.State = state;
                record.Summary = summary;
                record.UpdatedAt = DateTimeOffset.UtcNow;
                SaveUnsafe(records);
            }
        }

        public IReadOnlyList<TrainingDeploymentTaskRecord> ListAll()
        {
            lock (Gate)
            {
                return LoadUnsafe().OrderBy(item => item.CreatedAt).ToArray();
            }
        }

        public void RecoverInterruptedTasks()
        {
            lock (Gate)
            {
                List<TrainingDeploymentTaskRecord> records = LoadUnsafe();
                bool changed = false;
                foreach (TrainingDeploymentTaskRecord record in records.Where(item => item.State == TrainingDeploymentTaskState.Running))
                {
                    record.State = TrainingDeploymentTaskState.Failed;
                    record.Summary = "上次运行被中断，任务已标记为失败。";
                    record.Logs.Add(record.Summary);
                    record.UpdatedAt = DateTimeOffset.UtcNow;
                    changed = true;
                }
                if (changed) SaveUnsafe(records);
            }
        }

        private List<TrainingDeploymentTaskRecord> LoadUnsafe()
        {
            try
            {
                if (!File.Exists(_storePath)) return new List<TrainingDeploymentTaskRecord>();
                return JsonSerializer.Deserialize<List<TrainingDeploymentTaskRecord>>(File.ReadAllText(_storePath), JsonOptions)
                    ?? new List<TrainingDeploymentTaskRecord>();
            }
            catch
            {
                return new List<TrainingDeploymentTaskRecord>();
            }
        }

        private void SaveUnsafe(List<TrainingDeploymentTaskRecord> records)
        {
            string? directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            string temporary = _storePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(records, JsonOptions));
            File.Move(temporary, _storePath, true);
        }

        private static string Sanitize(string value)
        {
            string withoutAnsi = AnsiPattern.Replace(value, "");
            return new string(withoutAnsi.Where(character => character is '\t' or '\r' or '\n' || !char.IsControl(character)).ToArray()).TrimEnd();
        }
    }
}
