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
            NotePath = ".git/cyantools/project/README.md",
            NoteMarkdown = "# Style Study\n\n整个训练集的长期笔记。",
            CurrentNodeId = "node-b"
        };
        project.ConfigFiles.Add(new TrainingSetConfigFile
        {
            RelativePath = "configs/project/dataset.toml",
            DisplayName = "dataset.toml"
        });

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
        Assert.Contains("长期笔记", restored.NoteMarkdown);
        Assert.Single(restored.ConfigFiles);
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
        Assert.True(Directory.Exists(Path.Combine(_root, ".cyantools", "project")));
        Assert.True(Directory.Exists(Path.Combine(_root, ".git", "cyantools", "project")));
        Assert.True(File.Exists(Path.Combine(_root, ".git", "cyantools", "project.json")));
        Assert.True(Directory.Exists(Path.Combine(_root, "configs")));
        Assert.True(Directory.Exists(Path.Combine(_root, "samples")));
        Assert.True(Directory.Exists(Path.Combine(_root, "tensorboard")));
        Assert.True(Directory.Exists(Path.Combine(_root, "models")));

        var attributes = File.ReadAllText(Path.Combine(_root, ".gitattributes"));
        Assert.Contains("*.png filter=lfs diff=lfs merge=lfs -text", attributes);
        Assert.Contains("*.safetensors filter=lfs diff=lfs merge=lfs -text", attributes);
        Assert.Contains("*.onnx filter=lfs diff=lfs merge=lfs -text", attributes);
        Assert.Contains("*.parquet filter=lfs diff=lfs merge=lfs -text", attributes);
        Assert.Contains("events.out.tfevents.* filter=lfs diff=lfs merge=lfs -text", attributes);
        Assert.Contains(git.Commands, command => command.Arguments.SequenceEqual(["init"]));
        Assert.Contains(git.Commands, command => command.Arguments.SequenceEqual(["lfs", "install", "--local"]));
    }

    [Fact]
    public async Task Git_service_commit_async_runs_add_and_commit_in_order()
    {
        var git = new RecordingTrainingSetGitCommandRunner();
        var service = new TrainingSetGitService(git);

        var result = await service.CommitAsync(_root, "save node");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                "add -A",
                "commit -m save node"
            ],
            git.Commands.Select(command => string.Join(" ", command.Arguments)).ToArray());
    }

    [Fact]
    public async Task Git_service_checkout_uses_detached_commit_without_forcing_worktree()
    {
        var git = new RecordingTrainingSetGitCommandRunner();
        var service = new TrainingSetGitService(git);

        var result = await service.CheckoutCommitAsync(_root, "abc1234");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["checkout", "--detach", "abc1234"],
            git.Commands.Single().Arguments);
    }

    [Fact]
    public void Git_history_parser_returns_real_commit_topology_and_references()
    {
        const string output =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\u001fbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb cccccccccccccccccccccccccccccccccccccccc\u001fHEAD -> main, tag: v1\u001fAlice\u001f2026-07-24T10:20:30+08:00\u001fmerge dataset cleanup\u001e" +
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\u001f\u001forigin/main\u001fBob\u001f2026-07-23T09:00:00+08:00\u001finitial dataset\u001e";

        var commits = TrainingSetGitService.ParseHistory(output);

        Assert.Equal(2, commits.Count);
        Assert.True(commits[0].IsMerge);
        Assert.Equal(["bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "cccccccccccccccccccccccccccccccccccccccc"], commits[0].ParentHashes);
        Assert.Equal("main, v1", commits[0].ReferenceDisplay);
        Assert.Equal("aaaaaaa", commits[0].ShortHash);
        Assert.Equal("initial dataset", commits[1].Subject);
    }

    [Fact]
    public void Storage_policy_assigns_large_training_assets_to_lfs_and_text_metadata_to_git()
    {
        Assert.True(TrainingSetProjectStore.ShouldUseLfs("images/001.webp"));
        Assert.True(TrainingSetProjectStore.ShouldUseLfs("models/final.safetensors"));
        Assert.True(TrainingSetProjectStore.ShouldUseLfs("logs/events.out.tfevents.123"));
        Assert.True(TrainingSetProjectStore.ShouldUseLfs("cache/features.parquet"));
        Assert.False(TrainingSetProjectStore.ShouldUseLfs("images/001.txt"));
        Assert.False(TrainingSetProjectStore.ShouldUseLfs(".cyantools/project/README.md"));
        Assert.False(TrainingSetProjectStore.ShouldUseLfs("configs/train.toml"));
    }

    [Fact]
    public void Project_profile_remains_global_when_versioned_manager_snapshot_is_older()
    {
        Directory.CreateDirectory(_root);
        var store = new TrainingSetProjectStore(
            new TrainingSetGitService(new RecordingTrainingSetGitCommandRunner()));
        var project = store.InitializeProject(_root, "Current dataset");
        project.NoteMarkdown = "# Current global note";
        store.SaveProject(project);

        var staleSnapshot = new TrainingSetProject
        {
            ProjectId = project.ProjectId,
            Name = "Old commit name",
            RootPath = _root,
            NoteMarkdown = "# Old commit note"
        };
        File.WriteAllText(
            TrainingSetProjectStore.GetManagerPath(_root),
            JsonConvert.SerializeObject(staleSnapshot, Formatting.Indented));

        var loaded = store.LoadProject(_root);

        Assert.Equal("Current dataset", loaded.Name);
        Assert.Equal("# Current global note", loaded.NoteMarkdown);
        Assert.Equal(".git/cyantools/project/README.md", loaded.NotePath);
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

        public Task<TrainingSetGitCommandResult> RunAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Run(repositoryPath, arguments));
        }
    }

    private sealed record RecordedGitCommand(string RepositoryPath, string[] Arguments);
}
