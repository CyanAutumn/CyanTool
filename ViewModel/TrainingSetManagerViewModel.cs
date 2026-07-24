using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CyanTools.Model;
using CyanTools.Utils;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;

namespace CyanTools.ViewModel
{
    public sealed class TrainingSetManagerViewModel : INotifyPropertyChanged
    {
        private readonly TrainingSetProjectStore _store;
        private readonly TrainingSetGitService _git;
        private readonly DispatcherTimer _autoSaveTimer;
        private readonly DispatcherTimer _toastTimer;
        private TrainingSetProject? _project;
        private TrainingSetVersionNode? _selectedNode;
        private TrainingSetCommitNodeViewModel? _selectedCommit;
        private TrainingSetCommitNodeViewModel? _compareSourceCommit;
        private TrainingSetCommitNodeViewModel? _compareTargetCommit;
        private TrainingSetVersionNode? _compareSourceNode;
        private TrainingSetVersionNode? _compareTargetNode;
        private TrainingSetConfigFile? _compareSourceConfig;
        private TrainingSetConfigFile? _compareTargetConfig;
        private string _projectName = "新训练集";
        private string _projectRootPath = string.Empty;
        private string _gitStatusText = "Git 未连接";
        private string _storageAllocationText = "尚未计算 Git / LFS 分配";
        private string _noteEditorText = string.Empty;
        private string _projectNoteEditorText = string.Empty;
        private string _newBranchName = string.Empty;
        private string _toastMessage = string.Empty;
        private bool _isToastVisible;
        private bool _isToastError;
        private bool _isBusy;
        private bool _isLoading;
        private bool _isDraftVersion;

        public TrainingSetManagerViewModel()
            : this(new TrainingSetProjectStore(), new TrainingSetGitService())
        {
        }

        public TrainingSetManagerViewModel(TrainingSetProjectStore store, TrainingSetGitService git)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _git = git ?? throw new ArgumentNullException(nameof(git));

            DatasetBrowser = new DatasetManagerViewModel
            {
                IsFolderPickerVisible = false
            };
            HistoryNodes = new ObservableCollection<TrainingSetCommitNodeViewModel>();
            ConfigDiffRows = new ObservableCollection<TrainingSetConfigDiffRow>();

            CreateProjectCommand = new RelayCommand(_ => CreateProject(), _ => !IsBusy);
            OpenProjectCommand = new RelayCommand(_ => OpenProject(), _ => !IsBusy);
            RefreshHistoryCommand = new RelayCommand(async _ => await RefreshRepositoryStateAsync(), _ => HasProject && !IsBusy);
            CreateVersionCommand = new RelayCommand(_ => CreateVersion(), _ => HasProject && !IsBusy);
            SaveVersionCommand = new RelayCommand(async _ => await SaveVersionAsCommitAsync(), _ => HasProject && SelectedNode != null && !IsBusy);
            CreateBranchFromCommitCommand = new RelayCommand(_ => CreateBranchFromSelectedCommit(), _ => HasProject && SelectedCommit != null && !IsBusy);
            ImportConfigCommand = new RelayCommand(_ => ImportVersionConfig(), _ => HasProject && SelectedNode != null && !IsBusy);
            ImportProjectConfigCommand = new RelayCommand(_ => ImportProjectConfig(), _ => HasProject && !IsBusy);
            CompareCommitsCommand = new RelayCommand(_ => CompareCommits(), _ => HasProject && CompareSourceCommit != null && CompareTargetCommit != null && !IsBusy);
            SelectProjectCoverCommand = new RelayCommand(_ => SelectProjectCover(), _ => HasProject && !IsBusy);
            SelectVersionCoverCommand = new RelayCommand(_ => SelectVersionCover(), _ => HasProject && SelectedNode != null && !IsBusy);
            AttachOutputImageCommand = new RelayCommand(_ => AttachFileArtifact(TrainingSetArtifactKind.OutputImage, "选择输出图片", "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件|*.*", "samples"), _ => HasProject && SelectedNode != null && !IsBusy);
            AttachModelCommand = new RelayCommand(_ => AttachFileArtifact(TrainingSetArtifactKind.Model, "选择最终训练模型", "模型文件|*.safetensors;*.ckpt;*.pt;*.pth;*.bin;*.onnx|所有文件|*.*", "models"), _ => HasProject && SelectedNode != null && !IsBusy);
            AttachTensorBoardLogCommand = new RelayCommand(_ => AttachTensorBoardLog(), _ => HasProject && SelectedNode != null && !IsBusy);
            InsertVersionNoteHeadingCommand = new RelayCommand(_ => InsertIntoVersionNote("\n## 训练记录\n"), _ => SelectedNode != null);
            InsertVersionNoteTableCommand = new RelayCommand(_ => InsertIntoVersionNote("\n| epoch | loss | sample | notes |\n| --- | --- | --- | --- |\n|  |  |  |  |\n"), _ => SelectedNode != null);
            InsertVersionNoteChecklistCommand = new RelayCommand(_ => InsertIntoVersionNote("\n- [ ] 数据确认\n- [ ] 参数确认\n- [ ] 样张检查\n"), _ => SelectedNode != null);
            InsertProjectNoteHeadingCommand = new RelayCommand(_ => InsertIntoProjectNote("\n## 数据集记录\n"), _ => HasProject);
            InsertProjectNoteTableCommand = new RelayCommand(_ => InsertIntoProjectNote("\n| 日期 | 变更 | 说明 |\n| --- | --- | --- |\n|  |  |  |\n"), _ => HasProject);
            InsertProjectNoteChecklistCommand = new RelayCommand(_ => InsertIntoProjectNote("\n- [ ] 数据清洗\n- [ ] Prompt 复核\n- [ ] 版权与来源复核\n"), _ => HasProject);
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

            _toastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                IsToastVisible = false;
            };
        }

        public DatasetManagerViewModel DatasetBrowser { get; }
        public ObservableCollection<TrainingSetCommitNodeViewModel> HistoryNodes { get; }
        public ObservableCollection<TrainingSetConfigDiffRow> ConfigDiffRows { get; }

        public TrainingSetProject? Project
        {
            get => _project;
            private set
            {
                if (SetField(ref _project, value))
                {
                    OnPropertyChanged(nameof(HasProject));
                    OnPropertyChanged(nameof(ProjectConfigFiles));
                }
            }
        }

        public bool HasProject => Project != null;

        public string ProjectName
        {
            get => _projectName;
            set
            {
                if (SetField(ref _projectName, value) && Project != null && !_isLoading)
                {
                    Project.Name = value;
                    RequestAutoSave();
                }
            }
        }

        public string ProjectRootPath
        {
            get => _projectRootPath;
            private set => SetField(ref _projectRootPath, value);
        }

        public IReadOnlyList<TrainingSetConfigFile> ProjectConfigFiles => Project != null
            ? Project.ConfigFiles
            : Array.Empty<TrainingSetConfigFile>();

        public TrainingSetVersionNode? SelectedNode
        {
            get => _selectedNode;
            private set => SetSelectedNode(value, requestAutoSave: false);
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

        public TrainingSetCommitNodeViewModel? SelectedCommit
        {
            get => _selectedCommit;
            private set
            {
                if (SetField(ref _selectedCommit, value))
                {
                    OnPropertyChanged(nameof(CurrentSnapshotText));
                    OnPropertyChanged(nameof(CanCreateBranch));
                    RefreshHistorySelection();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool CanCreateBranch => SelectedCommit != null;

        public string CurrentSnapshotText
        {
            get
            {
                if (IsDraftVersion)
                {
                    return SelectedCommit == null
                        ? "尚未提交的初始版本"
                        : $"基于 {SelectedCommit.ShortHash} 的未提交版本";
                }

                return SelectedCommit == null
                    ? "未选择 Git commit"
                    : $"{SelectedCommit.ShortHash} · {SelectedCommit.Title}";
            }
        }

        public bool IsDraftVersion
        {
            get => _isDraftVersion;
            private set
            {
                if (SetField(ref _isDraftVersion, value))
                {
                    OnPropertyChanged(nameof(CurrentSnapshotText));
                }
            }
        }

        public TrainingSetCommitNodeViewModel? CompareSourceCommit
        {
            get => _compareSourceCommit;
            set
            {
                if (SetField(ref _compareSourceCommit, value))
                {
                    CompareSourceNode = LoadNodeMetadataAtCommit(value?.Commit.CommitHash);
                    CompareSourceConfig = CompareSourceNode?.ConfigFiles.FirstOrDefault();
                    OnPropertyChanged(nameof(CompareSourceConfigs));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public TrainingSetCommitNodeViewModel? CompareTargetCommit
        {
            get => _compareTargetCommit;
            set
            {
                if (SetField(ref _compareTargetCommit, value))
                {
                    CompareTargetNode = LoadNodeMetadataAtCommit(value?.Commit.CommitHash);
                    CompareTargetConfig = CompareTargetNode?.ConfigFiles.FirstOrDefault();
                    OnPropertyChanged(nameof(CompareTargetConfigs));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public TrainingSetVersionNode? CompareSourceNode
        {
            get => _compareSourceNode;
            private set => SetField(ref _compareSourceNode, value);
        }

        public TrainingSetVersionNode? CompareTargetNode
        {
            get => _compareTargetNode;
            private set => SetField(ref _compareTargetNode, value);
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
            set => SetField(ref _compareSourceConfig, value);
        }

        public TrainingSetConfigFile? CompareTargetConfig
        {
            get => _compareTargetConfig;
            set => SetField(ref _compareTargetConfig, value);
        }

        public string GitStatusText
        {
            get => _gitStatusText;
            private set => SetField(ref _gitStatusText, value);
        }

        public string StorageAllocationText
        {
            get => _storageAllocationText;
            private set => SetField(ref _storageAllocationText, value);
        }

        public string LfsPolicyText => "图片 / 模型 / TensorBoard / 大型数组与压缩包 → Git LFS；Prompt / Markdown / JSON / TOML / YAML → 普通 Git";

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

        public string ProjectNoteEditorText
        {
            get => _projectNoteEditorText;
            set
            {
                if (SetField(ref _projectNoteEditorText, value) && !_isLoading && Project != null)
                {
                    Project.NoteMarkdown = value;
                    RequestAutoSave();
                }
            }
        }

        public string NewBranchName
        {
            get => _newBranchName;
            set => SetField(ref _newBranchName, value);
        }

        public string ToastMessage
        {
            get => _toastMessage;
            private set => SetField(ref _toastMessage, value);
        }

        public bool IsToastVisible
        {
            get => _isToastVisible;
            private set => SetField(ref _isToastVisible, value);
        }

        public bool IsToastError
        {
            get => _isToastError;
            private set => SetField(ref _isToastError, value);
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
        public ICommand RefreshHistoryCommand { get; }
        public ICommand CreateVersionCommand { get; }
        public ICommand SaveVersionCommand { get; }
        public ICommand CreateBranchFromCommitCommand { get; }
        public ICommand ImportConfigCommand { get; }
        public ICommand ImportProjectConfigCommand { get; }
        public ICommand CompareCommitsCommand { get; }
        public ICommand SelectProjectCoverCommand { get; }
        public ICommand SelectVersionCoverCommand { get; }
        public ICommand AttachOutputImageCommand { get; }
        public ICommand AttachModelCommand { get; }
        public ICommand AttachTensorBoardLogCommand { get; }
        public ICommand InsertVersionNoteHeadingCommand { get; }
        public ICommand InsertVersionNoteTableCommand { get; }
        public ICommand InsertVersionNoteChecklistCommand { get; }
        public ICommand InsertProjectNoteHeadingCommand { get; }
        public ICommand InsertProjectNoteTableCommand { get; }
        public ICommand InsertProjectNoteChecklistCommand { get; }
        public ICommand OpenProjectFolderCommand { get; }

        public async Task SelectCommitAsync(TrainingSetCommitNodeViewModel commitNode)
        {
            if (Project == null || IsBusy)
            {
                return;
            }

            if (SelectedCommit != null &&
                string.Equals(SelectedCommit.Commit.CommitHash, commitNode.Commit.CommitHash, StringComparison.OrdinalIgnoreCase) &&
                !IsDraftVersion)
            {
                SelectedCommit = commitNode;
                return;
            }

            var rootPath = Project.RootPath;
            var status = await _git.GetShortStatusAsync(rootPath);
            if (!string.IsNullOrWhiteSpace(status))
            {
                var confirmation = MessageBox.Show(
                    "当前工作区有未提交改动。切换 commit 可能失败，也可能覆盖当前正在查看的状态。\n\n请先提交或备份；确认继续切换吗？",
                    "切换 Git commit",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (confirmation != MessageBoxResult.OK)
                {
                    return;
                }
            }

            IsBusy = true;
            ShowToast($"正在切换到 {commitNode.ShortHash}…");
            try
            {
                var checkout = await _git.CheckoutCommitAsync(rootPath, commitNode.Commit.CommitHash);
                if (!checkout.IsSuccess)
                {
                    ShowToast($"版本切换失败：{FirstUsefulLine(checkout.StandardError, checkout.StandardOutput)}", isError: true);
                    return;
                }

                var project = LoadProjectSnapshotOrFallback(rootPath, commitNode.Commit);
                ApplyProjectState(project, commitNode.Commit);
                SelectedCommit = commitNode;
                IsDraftVersion = false;
                await RefreshGitStatusAsync();
                ShowToast($"已切换到 {commitNode.ShortHash}，数据集快照已重新载入。");
            }
            catch (Exception ex)
            {
                ShowToast($"版本切换失败：{ex.Message}", isError: true);
            }
            finally
            {
                IsBusy = false;
            }
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
                var project = _store.InitializeProject(dialog.FileName, name);
                ApplyProjectState(project, null);
                HistoryNodes.Clear();
                SelectedCommit = null;
                CreateVersion();
                _ = RefreshRepositoryStateAsync();
                ShowToast("已创建 Git/LFS 训练集，请放入图片和 prompt 后保存首个版本。");
            }
            catch (Exception ex)
            {
                ShowToast($"创建训练集失败：{ex.Message}", isError: true);
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
                var project = _store.LoadProject(dialog.FileName);
                ApplyProjectState(project, null);
                _ = RefreshRepositoryStateAsync();
                ShowToast("训练集已打开，正在读取 Git 历史。");
            }
            catch (Exception ex)
            {
                ShowToast($"打开训练集失败：{ex.Message}", isError: true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyProjectState(TrainingSetProject project, TrainingSetGitCommit? commit)
        {
            _isLoading = true;
            try
            {
                Project = project;
                ProjectName = project.Name;
                ProjectRootPath = project.RootPath;
                ProjectNoteEditorText = project.NoteMarkdown ?? string.Empty;
                var selectedNode = project.Nodes.FirstOrDefault(node =>
                    string.Equals(node.NodeId, project.CurrentNodeId, StringComparison.OrdinalIgnoreCase))
                    ?? project.Nodes.LastOrDefault();
                SetSelectedNode(selectedNode, requestAutoSave: false);
                if (SelectedNode != null && commit != null && string.IsNullOrWhiteSpace(SelectedNode.Title))
                {
                    SelectedNode.Title = commit.Subject;
                }
            }
            finally
            {
                _isLoading = false;
            }

            DatasetBrowser.LoadFolder(project.RootPath, TrainingSetProjectStore.DatasetBrowserExcludedDirectories);
            OnPropertyChanged(nameof(ProjectCoverImage));
            OnPropertyChanged(nameof(ProjectConfigFiles));
            CommandManager.InvalidateRequerySuggested();
        }

        private TrainingSetProject LoadProjectSnapshotOrFallback(string rootPath, TrainingSetGitCommit commit)
        {
            try
            {
                return _store.LoadProject(rootPath);
            }
            catch (FileNotFoundException)
            {
                var fallback = new TrainingSetProject
                {
                    ProjectId = Project?.ProjectId ?? Guid.NewGuid().ToString("N"),
                    Name = Project?.Name ?? Path.GetFileName(rootPath),
                    RootPath = rootPath,
                    NotePath = $"{TrainingSetProjectStore.GlobalProjectDirectoryRelativePath}/README.md"
                };
                var node = new TrainingSetVersionNode
                {
                    Title = commit.Subject,
                    Branch = commit.ReferenceDisplay,
                    Description = "此 commit 早于 CyanTools 版本元数据；仍可查看其中的数据集和 prompt。"
                };
                fallback.Nodes.Add(node);
                fallback.CurrentNodeId = node.NodeId;
                return fallback;
            }
        }

        private void SetSelectedNode(TrainingSetVersionNode? node, bool requestAutoSave)
        {
            if (ReferenceEquals(_selectedNode, node))
            {
                return;
            }

            _selectedNode = node;
            _isLoading = true;
            NoteEditorText = node?.NoteMarkdown ?? string.Empty;
            _isLoading = false;
            OnPropertyChanged(nameof(SelectedNode));
            OnPropertyChanged(nameof(SelectedNodeTitle));
            OnPropertyChanged(nameof(SelectedNodeDescription));
            OnPropertyChanged(nameof(SelectedNodeCoverImage));
            if (Project != null && node != null)
            {
                Project.CurrentNodeId = node.NodeId;
                if (requestAutoSave)
                {
                    RequestAutoSave();
                }
            }

            CommandManager.InvalidateRequerySuggested();
        }

        private void CreateVersion()
        {
            if (Project == null)
            {
                return;
            }

            var parent = SelectedNode;
            var node = new TrainingSetVersionNode
            {
                Title = HistoryNodes.Count == 0 ? "初始训练集" : $"训练版本 {HistoryNodes.Count + 1}",
                Branch = _git.GetCurrentBranch(Project.RootPath),
                NotePath = $".cyantools/notes/{Guid.NewGuid():N}.md"
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

            SetSelectedNode(node, requestAutoSave: true);
            IsDraftVersion = true;
            ShowToast("已准备一个新版本；编辑数据集、配置和产物后点击“提交版本”。");
        }

        private async Task SaveVersionAsCommitAsync()
        {
            if (Project == null || SelectedNode == null)
            {
                return;
            }

            IsBusy = true;
            _autoSaveTimer.Stop();
            try
            {
                _store.SaveProject(Project);
                var message = string.IsNullOrWhiteSpace(SelectedNode.Title)
                    ? "保存训练集版本"
                    : SelectedNode.Title.Trim();
                ShowToast("正在计算文件变更并写入 Git/LFS…");
                var commit = await _git.CommitAsync(Project.RootPath, message);
                if (!commit.IsSuccess)
                {
                    ShowToast($"提交失败：{FirstUsefulLine(commit.StandardError, commit.StandardOutput)}", isError: true);
                    return;
                }

                IsDraftVersion = false;
                await RefreshRepositoryStateAsync();
                var head = await _git.GetHeadCommitAsync(Project.RootPath);
                SelectedCommit = HistoryNodes.FirstOrDefault(item =>
                    string.Equals(item.Commit.CommitHash, head, StringComparison.OrdinalIgnoreCase));
                ShowToast($"已创建 Git commit {SelectedCommit?.ShortHash ?? string.Empty}。");
            }
            catch (Exception ex)
            {
                ShowToast($"提交版本失败：{ex.Message}", isError: true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void CreateBranchFromSelectedCommit()
        {
            if (Project == null || SelectedCommit == null)
            {
                return;
            }

            var branch = string.IsNullOrWhiteSpace(NewBranchName)
                ? $"experiment/{SanitizeBranchSegment(SelectedCommit.Title)}-{DateTime.Now:yyyyMMddHHmmss}"
                : NewBranchName.Trim();
            var result = _git.CreateBranchFromCommit(Project.RootPath, branch, SelectedCommit.Commit.CommitHash);
            if (!result.IsSuccess)
            {
                ShowToast($"创建分支失败：{FirstUsefulLine(result.StandardError, result.StandardOutput)}", isError: true);
                return;
            }

            NewBranchName = string.Empty;
            IsDraftVersion = false;
            _ = RefreshRepositoryStateAsync();
            ShowToast($"已从 {SelectedCommit.ShortHash} 创建并切换到分支 {branch}。");
        }

        private async Task RefreshRepositoryStateAsync()
        {
            if (Project == null)
            {
                return;
            }

            var rootPath = Project.RootPath;
            try
            {
                var selectedHash = SelectedCommit?.Commit.CommitHash;
                var history = await _git.GetHistoryAsync(rootPath);
                var head = await _git.GetHeadCommitAsync(rootPath);
                HistoryNodes.Clear();
                foreach (var commit in history)
                {
                    HistoryNodes.Add(new TrainingSetCommitNodeViewModel(commit, this)
                    {
                        IsHead = string.Equals(commit.CommitHash, head, StringComparison.OrdinalIgnoreCase)
                    });
                }

                SelectedCommit = HistoryNodes.FirstOrDefault(item =>
                    string.Equals(item.Commit.CommitHash, selectedHash, StringComparison.OrdinalIgnoreCase))
                    ?? HistoryNodes.FirstOrDefault(item => item.IsHead);
                CompareSourceCommit ??= HistoryNodes.Skip(1).FirstOrDefault() ?? HistoryNodes.FirstOrDefault();
                CompareTargetCommit ??= HistoryNodes.FirstOrDefault();
                await RefreshGitStatusAsync();
                await RefreshStorageAllocationAsync(rootPath);
            }
            catch (Exception ex)
            {
                ShowToast($"刷新 Git 历史失败：{ex.Message}", isError: true);
            }
        }

        private async Task RefreshGitStatusAsync()
        {
            if (Project == null)
            {
                GitStatusText = "Git 未连接";
                return;
            }

            var branch = await _git.GetCurrentBranchAsync(Project.RootPath);
            var head = await _git.GetHeadCommitAsync(Project.RootPath);
            var status = await _git.GetShortStatusAsync(Project.RootPath);
            GitStatusText =
                $"{(string.IsNullOrWhiteSpace(branch) ? "detached HEAD" : branch)} · " +
                $"{ShortHash(head)} · " +
                $"{(string.IsNullOrWhiteSpace(status) ? "工作区干净" : "有未提交改动")}";
        }

        private async Task RefreshStorageAllocationAsync(string rootPath)
        {
            var allocation = await Task.Run(() => TrainingSetProjectStore.CalculateStorageAllocation(rootPath));
            StorageAllocationText =
                $"普通 Git {allocation.GitFileCount} 个 / {FormatBytes(allocation.GitBytes)} · " +
                $"Git LFS {allocation.LfsFileCount} 个 / {FormatBytes(allocation.LfsBytes)}";
        }

        private void RefreshHistorySelection()
        {
            foreach (var item in HistoryNodes)
            {
                item.IsSelected = SelectedCommit != null &&
                    string.Equals(item.Commit.CommitHash, SelectedCommit.Commit.CommitHash, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void ImportVersionConfig()
        {
            if (Project == null || SelectedNode == null)
            {
                return;
            }

            var files = PickConfigFiles("导入当前 commit 的训练配置");
            foreach (var file in files)
            {
                var relative = CopyIntoRepository(file, "configs", SelectedNode.NodeId);
                AddConfigIfMissing(SelectedNode.ConfigFiles, relative, file);
            }

            if (files.Length > 0)
            {
                RequestAutoSave();
                OnPropertyChanged(nameof(SelectedNode));
                ShowToast($"已导入 {files.Length} 个 commit 训练配置。");
            }
        }

        private void ImportProjectConfig()
        {
            if (Project == null)
            {
                return;
            }

            var files = PickConfigFiles("导入训练集级配置");
            foreach (var file in files)
            {
                var relative = CopyIntoRepository(
                    file,
                    TrainingSetProjectStore.GlobalConfigDirectoryRelativePath,
                    string.Empty);
                AddConfigIfMissing(Project.ConfigFiles, relative, file);
            }

            if (files.Length > 0)
            {
                RequestAutoSave();
                OnPropertyChanged(nameof(ProjectConfigFiles));
                ShowToast($"已导入 {files.Length} 个训练集级配置。");
            }
        }

        private static string[] PickConfigFiles(string title)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "配置文件|*.toml;*.json;*.yaml;*.yml;*.txt|所有文件|*.*",
                Multiselect = true
            };
            return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
        }

        private static void AddConfigIfMissing(List<TrainingSetConfigFile> configs, string relative, string sourceFile)
        {
            if (configs.All(item => !string.Equals(item.RelativePath, relative, StringComparison.OrdinalIgnoreCase)))
            {
                configs.Add(new TrainingSetConfigFile
                {
                    RelativePath = relative,
                    DisplayName = Path.GetFileName(sourceFile)
                });
            }
        }

        private void CompareCommits()
        {
            ConfigDiffRows.Clear();
            if (Project == null || CompareSourceCommit == null || CompareTargetCommit == null)
            {
                return;
            }

            var sourceConfig = CompareSourceConfig ?? CompareSourceNode?.ConfigFiles.FirstOrDefault();
            var targetConfig = CompareTargetConfig ??
                CompareTargetNode?.ConfigFiles.FirstOrDefault(config =>
                    string.Equals(config.DisplayName, sourceConfig?.DisplayName, StringComparison.OrdinalIgnoreCase)) ??
                CompareTargetNode?.ConfigFiles.FirstOrDefault();
            if (sourceConfig == null || targetConfig == null)
            {
                ShowToast("两个 commit 都需要训练配置才能比较。", isError: true);
                return;
            }

            var before = ReadConfigContent(CompareSourceCommit.Commit.CommitHash, sourceConfig);
            var after = ReadConfigContent(CompareTargetCommit.Commit.CommitHash, targetConfig);
            foreach (var row in TrainingSetConfigDiffService.Compare(
                sourceConfig.RelativePath,
                before,
                targetConfig.RelativePath,
                after))
            {
                ConfigDiffRows.Add(row);
            }

            ShowToast($"配置对比完成：{ConfigDiffRows.Count} 项发生变化。");
        }

        private TrainingSetVersionNode? LoadNodeMetadataAtCommit(string? commitHash)
        {
            if (Project == null || string.IsNullOrWhiteSpace(commitHash))
            {
                return null;
            }

            var show = _git.ShowFile(Project.RootPath, commitHash, ".cyantools/manager.json");
            if (!show.IsSuccess)
            {
                return null;
            }

            try
            {
                var project = JsonConvert.DeserializeObject<TrainingSetProject>(show.StandardOutput);
                return project?.Nodes.FirstOrDefault(node =>
                    string.Equals(node.NodeId, project.CurrentNodeId, StringComparison.OrdinalIgnoreCase))
                    ?? project?.Nodes.LastOrDefault();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private string ReadConfigContent(string commitHash, TrainingSetConfigFile config)
        {
            if (Project == null)
            {
                return string.Empty;
            }

            var show = _git.ShowFile(Project.RootPath, commitHash, config.RelativePath);
            return show.IsSuccess ? show.StandardOutput : string.Empty;
        }

        private void SelectProjectCover()
        {
            if (Project == null)
            {
                return;
            }

            var relative = PickAndCopyImage(TrainingSetProjectStore.GlobalProjectDirectoryRelativePath);
            if (string.IsNullOrWhiteSpace(relative))
            {
                return;
            }

            Project.CoverPath = relative;
            OnPropertyChanged(nameof(ProjectCoverImage));
            RequestAutoSave();
        }

        private void SelectVersionCover()
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
            RequestAutoSave();
        }

        private string PickAndCopyImage(string targetFolder)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择封面图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件|*.*"
            };

            return dialog.ShowDialog() == true
                ? CopyIntoRepository(dialog.FileName, targetFolder, string.Empty)
                : string.Empty;
        }

        private void AttachFileArtifact(
            TrainingSetArtifactKind kind,
            string title,
            string filter,
            string targetRoot)
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
            ShowToast($"已添加 {dialog.FileNames.Length} 个训练产物。");
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

            var sourceName = Path.GetFileName(dialog.FileName.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            var targetFolder = Path.Combine(Project.RootPath, "tensorboard", SelectedNode.NodeId, sourceName);
            CopyDirectory(dialog.FileName, targetFolder);
            var relative = TrainingSetProjectStore.ToRepositoryRelativePath(Project.RootPath, targetFolder);
            SelectedNode.TensorBoardLogs.Add(new TrainingSetTensorBoardLog
            {
                RelativePath = relative,
                DisplayName = sourceName,
                IsStoredInRepository = true
            });
            SelectedNode.Artifacts.Add(new TrainingSetArtifact
            {
                RelativePath = relative,
                DisplayName = sourceName,
                Kind = TrainingSetArtifactKind.TensorBoardLog,
                IsStoredInRepository = true
            });
            RequestAutoSave();
            OnPropertyChanged(nameof(SelectedNode));
            ShowToast("TensorBoard 日志已加入当前 commit 产物。");
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
            for (var index = 2; ; index++)
            {
                var candidate = Path.Combine(directory, $"{name}-{index}{extension}");
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
                Directory.CreateDirectory(directory.Replace(source, target, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var targetFile = file.Replace(source, target, StringComparison.OrdinalIgnoreCase);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile) ?? target);
                File.Copy(file, targetFile, overwrite: true);
            }
        }

        private void InsertIntoVersionNote(string text)
        {
            NoteEditorText += text;
        }

        private void InsertIntoProjectNote(string text)
        {
            ProjectNoteEditorText += text;
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
                _ = RefreshGitStatusAsync();
            }
            catch (Exception ex)
            {
                ShowToast($"自动保存元数据失败：{ex.Message}", isError: true);
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

        private void ShowToast(string message, bool isError = false)
        {
            ToastMessage = message;
            IsToastError = isError;
            IsToastVisible = true;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private static string FirstUsefulLine(params string[] values)
        {
            foreach (var value in values)
            {
                var line = value?
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return line.Trim();
                }
            }

            return "Git 未返回详细信息。";
        }

        private static string SanitizeBranchSegment(string value)
        {
            var cleaned = new string((value ?? string.Empty)
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-')
                .ToArray())
                .Trim('-');
            return string.IsNullOrWhiteSpace(cleaned) ? "version" : cleaned.ToLowerInvariant();
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return "无提交";
            }

            return hash.Length <= 7 ? hash : hash.Substring(0, 7);
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var value = (double)Math.Max(0, bytes);
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.##} {units[unit]}";
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
        private bool _isHead;

        public TrainingSetCommitNodeViewModel(TrainingSetGitCommit commit, TrainingSetManagerViewModel owner)
        {
            Commit = commit ?? throw new ArgumentNullException(nameof(commit));
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            SelectCommand = new RelayCommand(async _ => await Owner.SelectCommitAsync(this));
        }

        public TrainingSetGitCommit Commit { get; }
        public TrainingSetManagerViewModel Owner { get; }
        public string Title => string.IsNullOrWhiteSpace(Commit.Subject) ? "(无提交说明)" : Commit.Subject;
        public string ShortHash => Commit.ShortHash;
        public string AuthorAndDate => $"{Commit.Author} · {Commit.AuthoredAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        public string References => Commit.ReferenceDisplay;
        public string ParentSummary => Commit.ParentHashes.Count switch
        {
            0 => "根提交",
            1 => $"父级 {Short(Commit.ParentHashes[0])}",
            _ => $"合并 {string.Join(" + ", Commit.ParentHashes.Select(Short))}"
        };
        public string GraphSymbol => IsHead ? "●" : Commit.IsMerge ? "◆" : "○";
        public ICommand SelectCommand { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public bool IsHead
        {
            get => _isHead;
            set
            {
                if (SetField(ref _isHead, value))
                {
                    OnPropertyChanged(nameof(GraphSymbol));
                }
            }
        }

        private static string Short(string hash)
        {
            return hash.Length <= 7 ? hash : hash.Substring(0, 7);
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
}
