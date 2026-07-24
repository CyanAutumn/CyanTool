using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CyanTools.Model
{
    public sealed class TrainingSetProject
    {
        public string ProjectId { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "未命名训练集";
        public string RootPath { get; set; } = string.Empty;
        public string CoverPath { get; set; } = string.Empty;
        public string NotePath { get; set; } = ".cyantools/project/README.md";
        public string NoteMarkdown { get; set; } = string.Empty;
        public List<TrainingSetConfigFile> ConfigFiles { get; set; } = new();
        public string CurrentNodeId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public List<TrainingSetVersionNode> Nodes { get; set; } = new();
        public List<TrainingSetVersionEdge> Edges { get; set; } = new();
    }

    public sealed class TrainingSetVersionNode
    {
        public string NodeId { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "新节点";
        public string Description { get; set; } = string.Empty;
        public string GitCommit { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string NotePath { get; set; } = string.Empty;
        public string NoteMarkdown { get; set; } = string.Empty;
        public string CoverPath { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public List<string> Tags { get; set; } = new();
        public List<TrainingSetConfigFile> ConfigFiles { get; set; } = new();
        public List<TrainingSetTensorBoardLog> TensorBoardLogs { get; set; } = new();
        public List<TrainingSetArtifact> Artifacts { get; set; } = new();

        [JsonIgnore]
        public string CommitShort => string.IsNullOrWhiteSpace(GitCommit)
            ? string.Empty
            : GitCommit.Length <= 7 ? GitCommit : GitCommit.Substring(0, 7);
    }

    public sealed class TrainingSetVersionEdge
    {
        public string EdgeId { get; set; } = Guid.NewGuid().ToString("N");
        public string SourceNodeId { get; set; } = string.Empty;
        public string TargetNodeId { get; set; } = string.Empty;
    }

    public sealed class TrainingSetConfigFile
    {
        public string RelativePath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SnapshotPath { get; set; } = string.Empty;
    }

    public sealed class TrainingSetTensorBoardLog
    {
        public string RelativePath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsStoredInRepository { get; set; }
    }

    public sealed class TrainingSetArtifact
    {
        public string RelativePath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public TrainingSetArtifactKind Kind { get; set; } = TrainingSetArtifactKind.Other;
        public bool IsStoredInRepository { get; set; }
    }

    public enum TrainingSetArtifactKind
    {
        OutputImage,
        Model,
        TensorBoardLog,
        Other
    }

    public enum TrainingSetConfigDiffKind
    {
        Added,
        Removed,
        Modified
    }

    public sealed class TrainingSetConfigDiffRow
    {
        public string Key { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
        public TrainingSetConfigDiffKind Kind { get; set; }
    }

    public sealed class TrainingSetGitStatus
    {
        public string Branch { get; set; } = string.Empty;
        public string HeadCommit { get; set; } = string.Empty;
        public string ShortStatus { get; set; } = string.Empty;
        public bool HasChanges => !string.IsNullOrWhiteSpace(ShortStatus);
    }

    public sealed class TrainingSetGitCommit
    {
        public string CommitHash { get; set; } = string.Empty;
        public List<string> ParentHashes { get; set; } = new();
        public string Decorations { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTimeOffset AuthoredAt { get; set; }
        public string Subject { get; set; } = string.Empty;

        [JsonIgnore]
        public string ShortHash => string.IsNullOrWhiteSpace(CommitHash)
            ? string.Empty
            : CommitHash.Length <= 7 ? CommitHash : CommitHash.Substring(0, 7);

        [JsonIgnore]
        public bool IsMerge => ParentHashes.Count > 1;

        [JsonIgnore]
        public string ReferenceDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Decorations))
                {
                    return string.Empty;
                }

                return Decorations
                    .Replace("HEAD -> ", string.Empty, StringComparison.Ordinal)
                    .Replace("tag: ", string.Empty, StringComparison.Ordinal);
            }
        }
    }

    public sealed class TrainingSetStorageAllocation
    {
        public int GitFileCount { get; set; }
        public long GitBytes { get; set; }
        public int LfsFileCount { get; set; }
        public long LfsBytes { get; set; }
    }
}
