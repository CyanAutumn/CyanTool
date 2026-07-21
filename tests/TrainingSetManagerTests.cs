using CyanTools.Model;
using CyanTools.Utils;
using Newtonsoft.Json;
using Xunit;

namespace CyanTools.PathDefaults.Tests;

public sealed class TrainingSetManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyanTools_TrainingSetManagerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TrainingSetProject_round_trips_visual_node_metadata()
    {
        var project = new TrainingSetProject
        {
            ProjectId = "project-1",
            Name = "Style Study",
            RootPath = @"D:\datasets\style-study",
            CoverPath = ".cyantools/covers/project.png",
            CurrentNodeId = "node-b"
        };

        project.Nodes.Add(new TrainingSetVersionNode
        {
            NodeId = "node-a",
            Title = "baseline",
            GitCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Branch = "main",
            NotePath = ".cyantools/notes/node-a.md",
            CoverPath = ".cyantools/covers/node-a.png",
            X = 20,
            Y = 40
        });
        project.Nodes.Add(new TrainingSetVersionNode
        {
            NodeId = "node-b",
            Title = "style cleanup",
            GitCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            Branch = "experiment/style-cleanup",
            NotePath = ".cyantools/notes/node-b.md",
            CoverPath = ".cyantools/covers/node-b.png",
            X = 280,
            Y = 80,
            NoteMarkdown = "| epoch | image |\n| --- | --- |\n| 10 | samples/epoch10.png |"
        });
        project.Edges.Add(new TrainingSetVersionEdge
        {
            EdgeId = "edge-1",
            SourceNodeId = "node-a",
            TargetNodeId = "node-b"
        });
        project.Nodes[1].ConfigFiles.Add(new TrainingSetConfigFile
        {
            RelativePath = "configs/train.toml",
            DisplayName = "train.toml"
        });
        project.Nodes[1].TensorBoardLogs.Add(new TrainingSetTensorBoardLog
        {
            RelativePath = "tensorboard/node-b",
            DisplayName = "node-b run"
        });
        project.Nodes[1].Artifacts.Add(new TrainingSetArtifact
        {
            RelativePath = "models/node-b/output.safetensors",
            Kind = TrainingSetArtifactKind.Model,
            DisplayName = "output.safetensors",
            IsStoredInRepository = true
        });

        var json = JsonConvert.SerializeObject(project, Formatting.Indented);
        var restored = JsonConvert.DeserializeObject<TrainingSetProject>(json);

        Assert.NotNull(restored);
        Assert.Equal("project-1", restored!.ProjectId);
        Assert.Equal("node-b", restored.CurrentNodeId);
        Assert.Equal(2, restored.Nodes.Count);
        Assert.Single(restored.Edges);
        Assert.Equal("bbbbbbb", restored.Nodes[1].CommitShort);
        Assert.Single(restored.Nodes[1].ConfigFiles);
        Assert.Single(restored.Nodes[1].TensorBoardLogs);
        Assert.Single(restored.Nodes[1].Artifacts);
        Assert.Contains("epoch", restored.Nodes[1].NoteMarkdown);
    }

    [Fact]
    public void Project_store_initializes_git_lfs_training_set_layout()
    {
        Directory.CreateDirectory(_root);
        var git = new RecordingTrainingSetGitCommandRunner();
        var store = new TrainingSetProjectStore(new TrainingSetGitService(git));

        var project = store.InitializeProject(_root, "Dataset Project");

        Assert.Equal("Dataset Project", project.Name);
        Assert.Equal(_root, project.RootPath);
        Assert.True(File.Exists(Path.Combine(_root, ".cyantools", "manager.json")));
        Assert.True(Directory.Exists(Path.Combine(_root, ".cyantools", "nodes")));
        Assert.True(Directory.Exists(Path.Combine(_root, ".cyantools", "notes")));
        Assert.True(Directory.Exists(Path.Combine(_root, ".cyantools", "covers")));
        Assert.True(Directory.Exists(Path.Combine(_root, "configs")));
        Assert.True(Directory.Exists(Path.Combine(_root, "samples")));
        Assert.True(Directory.Exists(Path.Combine(_root, "tensorboard")));
        Assert.True(Directory.Exists(Path.Combine(_root, "models")));

        var attributes = File.ReadAllText(Path.Combine(_root, ".gitattributes"));
        Assert.Contains("*.png filter=lfs diff=lfs merge=lfs -text", attributes);
        Assert.Contains("*.safetensors filter=lfs diff=lfs merge=lfs -text", attributes);
        Assert.Contains("events.out.tfevents.* filter=lfs diff=lfs merge=lfs -text", attributes);
        Assert.Contains(git.Commands, command => command.Arguments.SequenceEqual(["init"]));
        Assert.Contains(git.Commands, command => command.Arguments.SequenceEqual(["lfs", "install", "--local"]));
    }

    [Fact]
    public void Config_diff_only_returns_changed_json_values()
    {
        const string before = """
        {
          "dataset": {
            "train_data_dir": "images",
            "resolution": "768,768"
          },
          "optimizer": {
            "unet_lr": "1e-4",
            "scheduler": "cosine"
          }
        }
        """;
        const string after = """
        {
          "dataset": {
            "train_data_dir": "images",
            "resolution": "1024,1024"
          },
          "optimizer": {
            "unet_lr": "8e-5"
          },
          "training": {
            "seed": 1337
          }
        }
        """;

        var rows = TrainingSetConfigDiffService.Compare("before.json", before, "after.json", after);

        Assert.Equal(
            ["dataset.resolution", "optimizer.scheduler", "optimizer.unet_lr", "training.seed"],
            rows.Select(row => row.Key).ToArray());
        Assert.Equal(TrainingSetConfigDiffKind.Modified, rows[0].Kind);
        Assert.Equal("768,768", rows[0].OldValue);
        Assert.Equal("1024,1024", rows[0].NewValue);
        Assert.Equal(TrainingSetConfigDiffKind.Removed, rows[1].Kind);
        Assert.Equal(TrainingSetConfigDiffKind.Modified, rows[2].Kind);
        Assert.Equal(TrainingSetConfigDiffKind.Added, rows[3].Kind);
    }

    [Fact]
    public void Config_diff_only_returns_changed_toml_values()
    {
        const string before = """
        [dataset]
        train_data_dir = "images"
        resolution = "768,768"

        [saving]
        output_dir = "output"
        logging_dir = "logs"
        """;
        const string after = """
        [dataset]
        train_data_dir = "images-clean"
        resolution = "768,768"

        [saving]
        output_dir = "output"
        save_every_n_epochs = 2
        """;

        var rows = TrainingSetConfigDiffService.Compare("before.toml", before, "after.toml", after);

        Assert.Equal(
            ["dataset.train_data_dir", "saving.logging_dir", "saving.save_every_n_epochs"],
            rows.Select(row => row.Key).ToArray());
        Assert.Equal(TrainingSetConfigDiffKind.Modified, rows[0].Kind);
        Assert.Equal(TrainingSetConfigDiffKind.Removed, rows[1].Kind);
        Assert.Equal(TrainingSetConfigDiffKind.Added, rows[2].Kind);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class RecordingTrainingSetGitCommandRunner : ITrainingSetGitCommandRunner
    {
        public List<RecordedGitCommand> Commands { get; } = new();

        public TrainingSetGitCommandResult Run(string repositoryPath, IReadOnlyList<string> arguments)
        {
            Commands.Add(new RecordedGitCommand(repositoryPath, arguments.ToArray()));
            return new TrainingSetGitCommandResult(0, string.Empty, string.Empty);
        }
    }

    private sealed record RecordedGitCommand(string RepositoryPath, string[] Arguments);
}
