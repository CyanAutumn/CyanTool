using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CyanTools.Model;
using CyanTools.Utils;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public sealed class TrainingSetManagerViewModel : INotifyPropertyChanged
    {
        private readonly TrainingSetProjectStore _store;
        private readonly TrainingSetGitService _git;
        private readonly DispatcherTimer _autoSaveTimer;
        private TrainingSetProject? _project;
        private TrainingSetVersionNode? _selectedNode;
        private TrainingSetVersionNode? _compareSourceNode;
        private TrainingSetVersionNode? _compareTargetNode;
        private TrainingSetConfigFile? _compareSourceConfig;
        private TrainingSetConfigFile? _compareTargetConfig;
        private string _projectName = "新训练集";
        private string _projectRootPath = string.Empty;
        private string _statusText = "请选择或创建训练集项目。";
        private string _gitStatusText = "Git 未连接";
        private string _noteEditorText = string.Empty;
        private bool _isBusy;
        private bool _isLoading;

        public TrainingSetManagerViewModel()
            : this(new TrainingSetProjectStore(), new TrainingSetGitService())
        {
        }

        public TrainingSetManagerViewModel(TrainingSetProjectStore store, TrainingSetGitService git)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _git = git ?? throw new ArgumentNullException(nameof(git));

            HistoryNodes = new ObservableCollection<TrainingSetCommitNodeViewModel>();
            ConfigDiffRows = new ObservableCollection<TrainingSetConfigDiffRow>();
            LogLines = new ObservableCollection<string>();

            CreateProjectCommand = new RelayCommand(_ => CreateProject(), _ => !IsBusy);
            OpenProjectCommand = new RelayCommand(_ => OpenProject(), _ => !IsBusy);
            RefreshGitStatusCommand = new RelayCommand(_ => RefreshGitStatus(), _ => HasProject && !IsBusy);
            CreateNodeCommand = new RelayCommand(_ => CreateNode(), _ => HasProject && !IsBusy);
            SaveNodeCommand = new RelayCommand(_ => SaveNodeAsCommit(), _ => HasProject && SelectedNode != null && !IsBusy);
            CheckoutNodeCommand = new RelayCommand(_ => CheckoutSelectedNode(), _ => HasProject && !string.IsNullOrWhiteSpace(SelectedNode?.GitCommit) && !IsBusy);
            CreateBranchFromNodeCommand = new RelayCommand(_ => CreateBranchFromSelectedNode(), _ => HasProject && !string.IsNullOrWhiteSpace(SelectedNode?.GitCommit) && !IsBusy);
            ImportConfigCommand = new RelayCommand(_ => ImportConfig(), _ => HasProject && SelectedNode != null && !IsBusy);
            CompareNodesCommand = new RelayCommand(_ => CompareNodes(), _ => HasProject && CompareSourceNode != null && CompareTargetNode != null && !IsBusy);
            SelectProjectCoverCommand = new RelayCommand(_ => SelectProjectCover(), _ => HasProject && !IsBusy);
            SelectNodeCoverCommand = new RelayCommand(_ => SelectNodeCover(), _ => HasProject && SelectedNode != null && !IsBusy);
            AttachOutputImageCommand = new RelayCommand(_ => AttachFileArtifact(TrainingSetArtifactKind.OutputImage, "选择输出图片", "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件|*.*", "samples"), _ => HasProject && SelectedNode != null && !IsBusy);
            AttachModelCommand = new RelayCommand(_ => AttachFileArtifact(TrainingSetArtifactKind.Model, "选择训练成品模型", "模型文件|*.safetensors;*.ckpt;*.pt;*.pth;*.bin|所有文件|*.*", "models"), _ => HasProject && SelectedNode != null && !IsBusy);
            AttachTensorBoardLogCommand = new RelayCommand(_ => AttachTensorBoardLog(), _ => HasProject && SelectedNode != null && !IsBusy);
            InsertNoteHeadingCommand = new RelayCommand(_ => InsertIntoNote("\n## 新记录\n"), _ => SelectedNode != null);
            InsertNoteTableCommand = new RelayCommand(_ => InsertIntoNote("\n| epoch | loss | sample | notes |\n| --- | --- | --- | --- |\n|  |  |  |  |\n"), _ => SelectedNode != null);
            InsertNoteImageRecordCommand = new RelayCommand(_ => InsertIntoNote("\n| 输出图片 | 说明 |\n| --- | --- |\n| ![](samples/) |  |\n"), _ => SelectedNode != null);
            InsertNoteChecklistCommand = new RelayCommand(_ => InsertIntoNote("\n- [ ] 数据清洗\n- [ ] 参数确认\n- [ ] 样张检查\n"), _ => SelectedNode != null);
            OpenProjectFolderCommand = new RelayCommand(_ => OpenProjectFolder(), _ => HasProject);

            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _autoSaveTimer.Tick += (_, _) =>
            {
                _autoSaveTimer.Stop();
                AutoSave();
            };
        }

        public ObservableCollection<TrainingSetCommitNodeViewModel> HistoryNodes { get; }
        public ObservableCollection<TrainingSetConfigDiffRow> ConfigDiffRows { get; }
        public ObservableCollection<string> LogLines { get; }

        public IReadOnlyList<TrainingSetVersionNode> ProjectNodes => Project != null
            ? Project.Nodes
            : Array.Empty<TrainingSetVersionNode>();

        public TrainingSetProject? Project
        {
            get => _project;
            private set
            {
                if (SetField(ref _project, value))
                {
                    OnPropertyChanged(nameof(HasProject));
                    OnPropertyChanged(nameof(ProjectNodes));
                }
            }
        }

        public bool HasProject => Project != null;

        public string ProjectName
        {
            get => _projectName;
            set
            {
                if (SetField(ref _projectName, value) && Project != null)
                {
                    Project.Name = value;
                    RequestAutoSave();
                }
            }
        }

        public string ProjectRootPath
        {
            get => _projectRootPath;
            set => SetField(ref _projectRootPath, value);
        }

        public TrainingSetVersionNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetField(ref _selectedNode, value))
                {
                    _isLoading = true;
                    NoteEditorText = value?.NoteMarkdown ?? string.Empty;
                    _isLoading = false;
                    OnPropertyChanged(nameof(SelectedNodeTitle));
                    OnPropertyChanged(nameof(SelectedNodeDescription));
                    if (Project != null && value != null)
                    {
                        Project.CurrentNodeId = value.NodeId;
                        CompareTargetNode ??= value;
                        RequestAutoSave();
                    }

                    RefreshCanvasSelection();
                    OnPropertyChanged(nameof(SelectedNodeCoverImage));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string SelectedNodeTitle
        {
            get => SelectedNode?.Title ?? string.Empty;
            set
            {
                if (SelectedNode == null || string.Equals(SelectedNode.Title, value, StringComparison.Ordinal))
                {
                    return;
                }

                SelectedNode.Title = value;
                SelectedNode.UpdatedAt = DateTime.Now;
                OnPropertyChanged();
                RebuildGraph();
                RequestAutoSave();
            }
        }

        public string SelectedNodeDescription
        {
            get => SelectedNode?.Description ?? string.Empty;
            set
            {
                if (SelectedNode == null || string.Equals(SelectedNode.Description, value, StringComparison.Ordinal))
                {
                    return;
                }

                SelectedNode.Description = value;
                SelectedNode.UpdatedAt = DateTime.Now;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public TrainingSetVersionNode? CompareSourceNode
        {
            get => _compareSourceNode;
            set
            {
                if (SetField(ref _compareSourceNode, value))
                {
                    CompareSourceConfig = value?.ConfigFiles.FirstOrDefault();
                    OnPropertyChanged(nameof(CompareSourceConfigs));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public TrainingSetVersionNode? CompareTargetNode
        {
            get => _compareTargetNode;
            set
            {
                if (SetField(ref _compareTargetNode, value))
                {
                    CompareTargetConfig = value?.ConfigFiles.FirstOrDefault();
                    OnPropertyChanged(nameof(CompareTargetConfigs));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public IReadOnlyList<TrainingSetConfigFile> CompareSourceConfigs => CompareSourceNode != null
            ? CompareSourceNode.ConfigFiles
            : Array.Empty<TrainingSetConfigFile>();

        public IReadOnlyList<TrainingSetConfigFile> CompareTargetConfigs => CompareTargetNode != null
            ? CompareTargetNode.ConfigFiles
            : Array.Empty<TrainingSetConfigFile>();

        public TrainingSetConfigFile? CompareSourceConfig
        {
            get => _compareSourceConfig;
            set
            {
                if (SetField(ref _compareSourceConfig, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public TrainingSetConfigFile? CompareTargetConfig
        {
            get => _compareTargetConfig;
            set
            {
                if (SetField(ref _compareTargetConfig, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value);
        }

        public string GitStatusText
        {
            get => _gitStatusText;
            private set => SetField(ref _gitStatusText, value);
        }

        public string NoteEditorText
        {
            get => _noteEditorText;
            set
            {
                if (SetField(ref _noteEditorText, value) && !_isLoading && SelectedNode != null)
                {
                    SelectedNode.NoteMarkdown = value;
                    RequestAutoSave();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetField(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public BitmapImage? SelectedNodeCoverImage => LoadBitmapForBinding(ProjectRootPath, SelectedNode?.CoverPath);
        public BitmapImage? ProjectCoverImage => LoadBitmapForBinding(ProjectRootPath, Project?.CoverPath);

        public ICommand CreateProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand RefreshGitStatusCommand { get; }
        public ICommand CreateNodeCommand { get; }
        public ICommand SaveNodeCommand { get; }
        public ICommand CheckoutNodeCommand { get; }
        public ICommand CreateBranchFromNodeCommand { get; }
        public ICommand ImportConfigCommand { get; }
        public ICommand CompareNodesCommand { get; }
        public ICommand SelectProjectCoverCommand { get; }
        public ICommand SelectNodeCoverCommand { get; }
        public ICommand AttachOutputImageCommand { get; }
        public ICommand AttachModelCommand { get; }
        public ICommand AttachTensorBoardLogCommand { get; }
        public ICommand InsertNoteHeadingCommand { get; }
        public ICommand InsertNoteTableCommand { get; }
        public ICommand InsertNoteImageRecordCommand { get; }
        public ICommand InsertNoteChecklistCommand { get; }
        public ICommand OpenProjectFolderCommand { get; }

        public void SelectCommitNode(TrainingSetCommitNodeViewModel node)
        {
            SelectedNode = node.Node;
        }

        private void CreateProject()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择或创建训练集 Git 仓库目录"
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var name = string.IsNullOrWhiteSpace(ProjectName)
                    ? Path.GetFileName(dialog.FileName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    : ProjectName;
                LoadProjectState(_store.InitializeProject(dialog.FileName, name));
                AppendLog("已初始化 Git/LFS 训练集项目。");
                StatusText = "训练集项目已创建。";
                RefreshGitStatus();
            }
            catch (Exception ex)
            {
                ShowError("创建训练集项目失败", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OpenProject()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "打开训练集 Git 仓库目录"
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            {
                return;
            }

            IsBusy = true;
            try
            {
                LoadProjectState(_store.LoadProject(dialog.FileName));
                AppendLog("已打开训练集项目。");
                StatusText = "训练集项目已打开。";
                RefreshGitStatus();
            }
            catch (Exception ex)
            {
                ShowError("打开训练集项目失败", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void LoadProjectState(TrainingSetProject project)
        {
            Project = project;
            ProjectName = project.Name;
            ProjectRootPath = project.RootPath;
            SelectedNode = project.Nodes.FirstOrDefault(node => node.NodeId == project.CurrentNodeId) ?? project.Nodes.FirstOrDefault();
            CompareSourceNode = ProjectNodes.FirstOrDefault();
            CompareTargetNode = SelectedNode ?? ProjectNodes.LastOrDefault();
            RebuildGraph();
            OnPropertyChanged(nameof(ProjectCoverImage));
            CommandManager.InvalidateRequerySuggested();
        }

        private void CreateNode()
        {
            if (Project == null)
            {
                return;
            }

            var parent = SelectedNode;
            var node = new TrainingSetVersionNode
            {
                Title = Project.Nodes.Count == 0 ? "初始训练集" : $"节点 {Project.Nodes.Count + 1}",
                Branch = _git.GetCurrentBranch(Project.RootPath),
                NotePath = $".cyantools/notes/{Guid.NewGuid():N}.md",
                X = parent == null ? 60 : parent.X + 260,
                Y = parent == null ? 80 : parent.Y + 70
            };

            Project.Nodes.Add(node);
            if (parent != null)
            {
                Project.Edges.Add(new TrainingSetVersionEdge
                {
                    SourceNodeId = parent.NodeId,
                    TargetNodeId = node.NodeId
                });
            }

            SelectedNode = node;
            CompareTargetNode = node;
            RebuildGraph();
            RequestAutoSave();
            AppendLog($"已创建节点: {node.Title}");
        }

        private void SaveNodeAsCommit()
        {
            if (Project == null || SelectedNode == null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                _store.SaveProject(Project);
                var message = $"CyanTools node {SelectedNode.NodeId}: {SelectedNode.Title}";
                var commit = _git.Commit(Project.RootPath, message);
                if (!commit.IsSuccess)
                {
                    AppendLog(string.IsNullOrWhiteSpace(commit.StandardError) ? commit.StandardOutput : commit.StandardError);
                    StatusText = "Git 提交失败，可能没有文件改动。";
                    return;
                }

                SelectedNode.GitCommit = _git.GetHeadCommit(Project.RootPath);
                SelectedNode.Branch = _git.GetCurrentBranch(Project.RootPath);
                SelectedNode.UpdatedAt = DateTime.Now;
                _store.SaveProject(Project);
                RebuildGraph();
                RefreshGitStatus();
                AppendLog($"节点已保存为 Git 提交: {SelectedNode.CommitShort}");
            }
            catch (Exception ex)
            {
                ShowError("保存节点失败", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void CheckoutSelectedNode()
        {
            if (Project == null || SelectedNode == null || string.IsNullOrWhiteSpace(SelectedNode.GitCommit))
            {
                return;
            }

            var result = MessageBox.Show(
                "回退会让 Git 工作区切换到该节点对应提交。请确认当前未保存改动已经提交或备份。",
                "回退到节点",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
            {
                return;
            }

            var checkout = _git.CheckoutCommit(Project.RootPath, SelectedNode.GitCommit);
            AppendLog(checkout.IsSuccess ? $"已回退到节点 {SelectedNode.Title}" : checkout.StandardError);
            RefreshGitStatus();
        }

        private void CreateBranchFromSelectedNode()
        {
            if (Project == null || SelectedNode == null || string.IsNullOrWhiteSpace(SelectedNode.GitCommit))
            {
                return;
            }

            var branch = $"experiment/{SanitizeBranchSegment(SelectedNode.Title)}-{DateTime.Now:yyyyMMddHHmmss}";
            var result = _git.CreateBranchFromCommit(Project.RootPath, branch, SelectedNode.GitCommit);
            AppendLog(result.IsSuccess ? $"已从节点创建分支: {branch}" : result.StandardError);
            RefreshGitStatus();
        }

        private void ImportConfig()
        {
            if (Project == null || SelectedNode == null)
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "导入节点配置文件",
                Filter = "配置文件|*.toml;*.json;*.yaml;*.yml;*.txt|所有文件|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            foreach (var file in dialog.FileNames)
            {
                var relative = CopyIntoRepository(file, "configs", SelectedNode.NodeId);
                if (SelectedNode.ConfigFiles.All(item => !string.Equals(item.RelativePath, relative, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectedNode.ConfigFiles.Add(new TrainingSetConfigFile
                    {
                        RelativePath = relative,
                        DisplayName = Path.GetFileName(file)
                    });
                }
            }

            RequestAutoSave();
            OnPropertyChanged(nameof(SelectedNode));
            OnPropertyChanged(nameof(CompareSourceConfigs));
            OnPropertyChanged(nameof(CompareTargetConfigs));
            AppendLog($"已导入 {dialog.FileNames.Length} 个配置文件。");
        }

        private void CompareNodes()
        {
            ConfigDiffRows.Clear();
            if (Project == null || CompareSourceNode == null || CompareTargetNode == null)
            {
                return;
            }

            var sourceConfig = CompareSourceConfig ?? CompareSourceNode.ConfigFiles.FirstOrDefault();
            var targetConfig = CompareTargetConfig ??
                CompareTargetNode.ConfigFiles.FirstOrDefault(config =>
                    string.Equals(config.DisplayName, sourceConfig?.DisplayName, StringComparison.OrdinalIgnoreCase)) ??
                CompareTargetNode.ConfigFiles.FirstOrDefault();
            if (sourceConfig == null || targetConfig == null)
            {
                StatusText = "两个节点都需要至少一个配置文件才能比较。";
                return;
            }

            var before = ReadConfigContent(CompareSourceNode, sourceConfig);
            var after = ReadConfigContent(CompareTargetNode, targetConfig);
            foreach (var row in TrainingSetConfigDiffService.Compare(sourceConfig.RelativePath, before, targetConfig.RelativePath, after))
            {
                ConfigDiffRows.Add(row);
            }

            StatusText = $"仅更改: {ConfigDiffRows.Count} 项";
            AppendLog($"已比较 {CompareSourceNode.Title} -> {CompareTargetNode.Title}: {ConfigDiffRows.Count} 项差异。");
        }

        private string ReadConfigContent(TrainingSetVersionNode node, TrainingSetConfigFile config)
        {
            if (Project == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(node.GitCommit))
            {
                var show = _git.ShowFile(Project.RootPath, node.GitCommit, config.RelativePath);
                if (show.IsSuccess)
                {
                    return show.StandardOutput;
                }
            }

            var path = TrainingSetProjectStore.ResolveRelativePath(Project.RootPath, config.RelativePath);
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private void SelectProjectCover()
        {
            if (Project == null)
            {
                return;
            }

            var relative = PickAndCopyImage(".cyantools/covers");
            if (string.IsNullOrWhiteSpace(relative))
            {
                return;
            }

            Project.CoverPath = relative;
            OnPropertyChanged(nameof(ProjectCoverImage));
            RequestAutoSave();
        }

        private void SelectNodeCover()
        {
            if (SelectedNode == null)
            {
                return;
            }

            var relative = PickAndCopyImage(".cyantools/covers");
            if (string.IsNullOrWhiteSpace(relative))
            {
                return;
            }

            SelectedNode.CoverPath = relative;
            OnPropertyChanged(nameof(SelectedNodeCoverImage));
            RebuildGraph();
            RequestAutoSave();
        }

        private string PickAndCopyImage(string targetFolder)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择封面图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件|*.*"
            };

            return dialog.ShowDialog() == true ? CopyIntoRepository(dialog.FileName, targetFolder, string.Empty) : string.Empty;
        }

        private void AttachFileArtifact(TrainingSetArtifactKind kind, string title, string filter, string targetRoot)
        {
            if (SelectedNode == null)
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            foreach (var file in dialog.FileNames)
            {
                var relative = CopyIntoRepository(file, targetRoot, SelectedNode.NodeId);
                SelectedNode.Artifacts.Add(new TrainingSetArtifact
                {
                    RelativePath = relative,
                    DisplayName = Path.GetFileName(file),
                    Kind = kind,
                    IsStoredInRepository = true
                });
            }

            RequestAutoSave();
            OnPropertyChanged(nameof(SelectedNode));
            AppendLog($"已添加 {dialog.FileNames.Length} 个附件。");
        }

        private void AttachTensorBoardLog()
        {
            if (Project == null || SelectedNode == null)
            {
                return;
            }

            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择 TensorBoard 日志目录"
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            {
                return;
            }

            var targetFolder = Path.Combine(Project.RootPath, "tensorboard", SelectedNode.NodeId, Path.GetFileName(dialog.FileName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            CopyDirectory(dialog.FileName, targetFolder);
            var relative = TrainingSetProjectStore.ToRepositoryRelativePath(Project.RootPath, targetFolder);
            SelectedNode.TensorBoardLogs.Add(new TrainingSetTensorBoardLog
            {
                RelativePath = relative,
                DisplayName = Path.GetFileName(targetFolder),
                IsStoredInRepository = true
            });
            SelectedNode.Artifacts.Add(new TrainingSetArtifact
            {
                RelativePath = relative,
                DisplayName = Path.GetFileName(targetFolder),
                Kind = TrainingSetArtifactKind.TensorBoardLog,
                IsStoredInRepository = true
            });
            RequestAutoSave();
            OnPropertyChanged(nameof(SelectedNode));
            AppendLog("已保存 TensorBoard 日志目录。");
        }

        private string CopyIntoRepository(string sourceFile, string targetRoot, string nodeId)
        {
            if (Project == null)
            {
                return string.Empty;
            }

            var targetFolder = string.IsNullOrWhiteSpace(nodeId)
                ? Path.Combine(Project.RootPath, targetRoot.Replace('/', Path.DirectorySeparatorChar))
                : Path.Combine(Project.RootPath, targetRoot.Replace('/', Path.DirectorySeparatorChar), nodeId);
            Directory.CreateDirectory(targetFolder);
            var targetPath = GetUniquePath(Path.Combine(targetFolder, Path.GetFileName(sourceFile)));
            File.Copy(sourceFile, targetPath);
            return TrainingSetProjectStore.ToRepositoryRelativePath(Project.RootPath, targetPath);
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return path;
            }

            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            for (var i = 2; ; i++)
            {
                var candidate = Path.Combine(directory, $"{name}-{i}{extension}");
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(directory.Replace(source, target));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var targetFile = file.Replace(source, target);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile) ?? target);
                File.Copy(file, targetFile, overwrite: true);
            }
        }

        private void InsertIntoNote(string text)
        {
            NoteEditorText += text;
        }

        private void RefreshGitStatus()
        {
            if (Project == null)
            {
                GitStatusText = "Git 未连接";
                return;
            }

            var branch = _git.GetCurrentBranch(Project.RootPath);
            var head = _git.GetHeadCommit(Project.RootPath);
            var status = _git.GetShortStatus(Project.RootPath);
            GitStatusText = $"分支: {(string.IsNullOrWhiteSpace(branch) ? "(detached)" : branch)} | HEAD: {(head.Length > 7 ? head.Substring(0, 7) : head)} | {(string.IsNullOrWhiteSpace(status) ? "工作区干净" : "有未提交改动")}";
        }

        private void RebuildGraph()
        {
            HistoryNodes.Clear();
            if (Project == null)
            {
                return;
            }

            var titleById = Project.Nodes.ToDictionary(
                node => node.NodeId,
                node => node.Title,
                StringComparer.OrdinalIgnoreCase);
            foreach (var node in Project.Nodes)
            {
                var parentTitles = Project.Edges
                    .Where(edge => string.Equals(edge.TargetNodeId, node.NodeId, StringComparison.OrdinalIgnoreCase))
                    .Select(edge => titleById.TryGetValue(edge.SourceNodeId, out var title) ? title : edge.SourceNodeId)
                    .ToList();
                HistoryNodes.Add(new TrainingSetCommitNodeViewModel(node, this)
                    {
                        ParentSummary = parentTitles.Count == 0
                            ? "根提交 / 初始快照"
                            : $"父提交: {string.Join(", ", parentTitles)}",
                        IsCurrent = string.Equals(Project.CurrentNodeId, node.NodeId, StringComparison.OrdinalIgnoreCase)
                    });
            }

            RefreshCanvasSelection();
            OnPropertyChanged(nameof(ProjectNodes));
        }

        private void RefreshCanvasSelection()
        {
            foreach (var canvasNode in HistoryNodes)
            {
                canvasNode.IsSelected = SelectedNode != null &&
                    string.Equals(canvasNode.Node.NodeId, SelectedNode.NodeId, StringComparison.OrdinalIgnoreCase);
                canvasNode.IsCurrent = Project != null &&
                    string.Equals(Project.CurrentNodeId, canvasNode.Node.NodeId, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void RequestAutoSave()
        {
            if (_isLoading || Project == null)
            {
                return;
            }

            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        private void AutoSave()
        {
            if (Project == null)
            {
                return;
            }

            try
            {
                _store.SaveProject(Project);
                RefreshGitStatus();
                StatusText = "节点元数据已实时保存。";
            }
            catch (Exception ex)
            {
                AppendLog($"自动保存失败: {ex.Message}");
            }
        }

        private void OpenProjectFolder()
        {
            if (Project == null || !Directory.Exists(Project.RootPath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = Project.RootPath,
                UseShellExecute = true
            });
        }

        private void AppendLog(string message)
        {
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
            while (LogLines.Count > 200)
            {
                LogLines.RemoveAt(LogLines.Count - 1);
            }
        }

        private static void ShowError(string title, Exception ex)
        {
            MessageBox.Show($"{title}: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static string SanitizeBranchSegment(string value)
        {
            var cleaned = new string((value ?? string.Empty)
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-')
                .ToArray())
                .Trim('-');
            return string.IsNullOrWhiteSpace(cleaned) ? "node" : cleaned.ToLowerInvariant();
        }

        public static BitmapImage? LoadBitmapForBinding(string rootPath, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            var path = TrainingSetProjectStore.ResolveRelativePath(rootPath, relativePath);
            if (!File.Exists(path))
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public sealed class TrainingSetCommitNodeViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isCurrent;
        private string _parentSummary = string.Empty;

        public TrainingSetCommitNodeViewModel(TrainingSetVersionNode node, TrainingSetManagerViewModel owner)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public TrainingSetVersionNode Node { get; }
        public TrainingSetManagerViewModel Owner { get; }
        public string Title => Node.Title;
        public string CommitShort => string.IsNullOrWhiteSpace(Node.CommitShort) ? "未提交" : Node.CommitShort;
        public string Branch => string.IsNullOrWhiteSpace(Node.Branch) ? "未记录分支" : Node.Branch;
        public BitmapImage? CoverImage => TrainingSetManagerViewModel.LoadBitmapForBinding(Owner.ProjectRootPath, Node.CoverPath);

        public string ParentSummary
        {
            get => _parentSummary;
            set => SetField(ref _parentSummary, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetField(ref _isCurrent, value);
        }

        public ICommand SelectCommand => new RelayCommand(_ => Owner.SelectCommitNode(this));

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
