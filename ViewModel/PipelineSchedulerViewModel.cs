using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public class PipelineSchedulerViewModel : INotifyPropertyChanged
    {
        private const double NodeHorizontalSpacing = 560;
        private const double NodeVerticalSpacing = 120;

        private PipelineNodeDescriptor? _selectedAvailableNode;
        private PipelineNodeViewModelBase? _selectedPipelineNode;
        private PipelineCanvasNodeViewModel? _selectedCanvasNode;
        private bool _isRunning;
        private CancellationTokenSource? _cts;
        private readonly PipelineRunner _runner = new();
        private bool _downloadersUnlocked;
        private bool _isLoadingWorkflow;
        private string? _selectedWorkflowName;
        private bool _isRefreshingWorkflowPaths;
        private bool _isRebuildingPipelineNodes;
        private bool _isUpdatingSelection;

        public PipelineSchedulerViewModel()
        {
            _downloadersUnlocked = Utils.UnlockState.DownloadersUnlocked;

            AvailableNodes = new ObservableCollection<PipelineNodeDescriptor>();
            PipelineNodes = new ObservableCollection<PipelineNodeViewModelBase>();
            CanvasNodes = new ObservableCollection<PipelineCanvasNodeViewModel>();
            CanvasConnections = new ObservableCollection<PipelineCanvasConnectionViewModel>();
            PendingConnection = new PipelineCanvasPendingConnectionViewModel();
            LogLines = new ObservableCollection<string>();
            Workflows = new ObservableCollection<string>(WorkflowStorage.ListWorkflows());

            PipelineNodes.CollectionChanged += OnPipelineNodesCollectionChanged;
            CanvasNodes.CollectionChanged += OnCanvasNodesCollectionChanged;
            CanvasConnections.CollectionChanged += OnCanvasConnectionsCollectionChanged;

            AddNodeCommand = new RelayCommand(AddNode, () => SelectedAvailableNode != null && !IsRunning);
            RemoveNodeCommand = new RelayCommand(RemoveSelectedNode, () => SelectedCanvasNode != null && !IsRunning);
            MoveUpCommand = new RelayCommand(MoveUp, () => CanMove(-1));
            MoveDownCommand = new RelayCommand(MoveDown, () => CanMove(1));
            StartCommand = new RelayCommand(async () => await RunAsync(), () => CanvasNodes.Any() && !IsRunning);
            CancelCommand = new RelayCommand(CancelRun, () => IsRunning);
            ClearLogCommand = new RelayCommand(() => LogLines.Clear(), () => !IsRunning);
            SaveWorkflowCommand = new RelayCommand(SaveWorkflow, () => !IsRunning && !string.IsNullOrWhiteSpace(SelectedWorkflowName));
            DeleteWorkflowCommand = new RelayCommand(DeleteWorkflow, () => !IsRunning && !string.IsNullOrWhiteSpace(SelectedWorkflowName));
            StartConnectionCommand = new RelayCommand(StartConnection, CanStartConnection);
            CreateConnectionCommand = new RelayCommand(CompleteConnection, _ => !IsRunning);
            DisconnectConnectorCommand = new RelayCommand(DisconnectConnector, p => p is PipelineCanvasConnectorViewModel && !IsRunning);

            ReloadAvailableNodes();
            Utils.UnlockState.DownloadersUnlockedChanged += OnDownloadersUnlockedChanged;
        }

        public ObservableCollection<PipelineNodeDescriptor> AvailableNodes { get; }
        public ObservableCollection<PipelineNodeViewModelBase> PipelineNodes { get; }
        public ObservableCollection<PipelineCanvasNodeViewModel> CanvasNodes { get; }
        public ObservableCollection<PipelineCanvasConnectionViewModel> CanvasConnections { get; }
        public PipelineCanvasPendingConnectionViewModel PendingConnection { get; }
        public ObservableCollection<string> LogLines { get; }
        public ObservableCollection<string> Workflows { get; }

        public PipelineNodeDescriptor? SelectedAvailableNode
        {
            get => _selectedAvailableNode;
            set
            {
                if (SetField(ref _selectedAvailableNode, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public PipelineNodeViewModelBase? SelectedPipelineNode
        {
            get => _selectedPipelineNode;
            set
            {
                if (ReferenceEquals(_selectedPipelineNode, value))
                {
                    return;
                }

                _selectedPipelineNode = value;
                OnPropertyChanged();

                var matchingCanvasNode = value == null
                    ? null
                    : CanvasNodes.FirstOrDefault(n => ReferenceEquals(n.PipelineNode, value));
                SelectCanvasNode(matchingCanvasNode, updatePipelineNode: false);
                RaiseCommandStates();
            }
        }

        public PipelineCanvasNodeViewModel? SelectedCanvasNode
        {
            get => _selectedCanvasNode;
            set => SelectCanvasNode(value, updatePipelineNode: true);
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (SetField(ref _isRunning, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public ICommand AddNodeCommand { get; }
        public ICommand RemoveNodeCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand SaveWorkflowCommand { get; }
        public ICommand DeleteWorkflowCommand { get; }
        public ICommand StartConnectionCommand { get; }
        public ICommand CreateConnectionCommand { get; }
        public ICommand DisconnectConnectorCommand { get; }

        public string? SelectedWorkflowName
        {
            get => _selectedWorkflowName;
            set
            {
                if (SetField(ref _selectedWorkflowName, value))
                {
                    RaiseCommandStates();
                    TryLoadWorkflow();
                }
            }
        }

        private void AddNode()
        {
            if (SelectedAvailableNode == null)
            {
                return;
            }

            var previousTail = GetCanvasNodesInExecutionOrder().LastOrDefault(n => n.HasOutput);
            var instance = SelectedAvailableNode.Factory();
            var canvasNode = CreateCanvasNode(instance, CanvasNodes.Count);

            CanvasNodes.Add(canvasNode);
            if (previousTail != null)
            {
                TryCreateConnection(previousTail.Output.FirstOrDefault(), canvasNode.Input.FirstOrDefault(), logFailures: false);
            }

            SelectedCanvasNode = canvasNode;
            RebuildPipelineNodesFromCanvas();
            AppendLog($"已添加节点: {instance.DisplayName}");
        }

        private PipelineCanvasNodeViewModel CreateCanvasNode(PipelineNodeViewModelBase pipelineNode, int index)
        {
            var row = index / 4;
            var column = index % 4;
            var location = new Point(40 + column * NodeHorizontalSpacing, 40 + row * NodeVerticalSpacing);
            return new PipelineCanvasNodeViewModel(pipelineNode, location);
        }

        private void RemoveSelectedNode()
        {
            if (SelectedCanvasNode == null)
            {
                return;
            }

            var removedName = SelectedCanvasNode.DisplayName;
            RemoveCanvasNode(SelectedCanvasNode);
            AppendLog($"已移除节点: {removedName}");
        }

        private void RemoveCanvasNode(PipelineCanvasNodeViewModel canvasNode)
        {
            var connections = CanvasConnections
                .Where(c => ReferenceEquals(c.Input.Node, canvasNode) || ReferenceEquals(c.Output.Node, canvasNode))
                .ToList();

            foreach (var connection in connections)
            {
                CanvasConnections.Remove(connection);
            }

            var index = CanvasNodes.IndexOf(canvasNode);
            canvasNode.PropertyChanged -= OnCanvasNodePropertyChanged;
            canvasNode.Dispose();
            CanvasNodes.Remove(canvasNode);

            SelectedCanvasNode = CanvasNodes.ElementAtOrDefault(Math.Max(index - 1, 0));
            RebuildPipelineNodesFromCanvas();
        }

        private bool CanMove(int direction)
        {
            if (IsRunning || SelectedCanvasNode == null)
            {
                return false;
            }

            var order = GetCanvasNodesInExecutionOrder();
            var index = order.IndexOf(SelectedCanvasNode);
            return direction < 0 ? index > 0 : index >= 0 && index < order.Count - 1;
        }

        private void MoveUp() => MoveNode(-1);
        private void MoveDown() => MoveNode(1);

        private void MoveNode(int direction)
        {
            if (SelectedCanvasNode == null)
            {
                return;
            }

            var order = GetCanvasNodesInExecutionOrder();
            var index = order.IndexOf(SelectedCanvasNode);
            var targetIndex = index + direction;
            if (index < 0 || targetIndex < 0 || targetIndex >= order.Count)
            {
                return;
            }

            (order[index], order[targetIndex]) = (order[targetIndex], order[index]);
            ArrangeCanvasNodes(order);
            RebuildLinearConnections(order);
            SelectedCanvasNode = order[targetIndex];
        }

        private async Task RunAsync()
        {
            if (IsRunning || CanvasNodes.Count == 0)
            {
                return;
            }

            if (!TryPrepareGraphForRun())
            {
                return;
            }

            if (!PipelineWorkflowValidation.TryValidateSingletonFirstNode(
                PipelineNodes,
                node => node is DatasetDownloaderPipelineNode,
                "数据集下载器",
                AppendLog))
            {
                return;
            }

            if (!PipelineWorkflowValidation.TryValidateSingletonFirstNode(
                PipelineNodes,
                node => node is ExportVideoFramePipelineNode,
                "导出视频帧",
                AppendLog))
            {
                return;
            }

            IsRunning = true;
            _cts = new CancellationTokenSource();
            LogLines.Clear();
            RefreshWorkflowPaths();

            var context = new PipelineContext(AppendLog);
            try
            {
                await _runner.RunAsync(PipelineNodes, context, _cts.Token);
                AppendLog("管道执行完成");
            }
            catch (OperationCanceledException)
            {
                AppendLog("管道已取消");
            }
            catch (Exception ex)
            {
                AppendLog($"管道执行失败: {ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsRunning = false;
            }
        }

        private bool TryPrepareGraphForRun()
        {
            if (!TryBuildExecutableCanvasOrder(out var canvasOrder, out var error))
            {
                AppendLog(error ?? "节点图无效。");
                return false;
            }

            ReplacePipelineNodes(canvasOrder.Select(n => n.PipelineNode));
            RefreshWorkflowPaths();
            return true;
        }

        private void CancelRun()
        {
            _cts?.Cancel();
        }

        private void AppendLog(string message)
        {
            void Append() => LogLines.Add($"{DateTime.Now:HH:mm:ss} {message}");

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                Append();
            }
            else
            {
                Application.Current?.Dispatcher?.Invoke(Append);
            }
        }

        private void SaveWorkflow()
        {
            if (string.IsNullOrWhiteSpace(SelectedWorkflowName))
            {
                return;
            }

            List<PipelineCanvasNodeViewModel> order = new();
            if (CanvasNodes.Count > 1 && !TryBuildExecutableCanvasOrder(out order, out var error))
            {
                AppendLog(error ?? "节点图无效，无法保存。");
                return;
            }

            if (CanvasNodes.Count <= 1)
            {
                order = GetCanvasNodesInExecutionOrder();
            }

            ReplacePipelineNodes(order.Select(n => n.PipelineNode));
            WorkflowStorage.Save(SelectedWorkflowName.Trim(), PipelineNodes);
            RefreshWorkflowList();
            AppendLog($"已保存工作流: {SelectedWorkflowName.Trim()}");
        }

        private void DeleteWorkflow()
        {
            var name = SelectedWorkflowName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var folder = WorkflowStorage.EnsureFolder();
            var path = Path.Combine(folder, $"{name}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                AppendLog($"已删除工作流: {name}");
            }

            RefreshWorkflowList();
            SelectedWorkflowName = Workflows.FirstOrDefault();
        }

        private void RefreshWorkflowList()
        {
            var current = SelectedWorkflowName;
            Workflows.Clear();
            foreach (var wf in WorkflowStorage.ListWorkflows())
            {
                Workflows.Add(wf);
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                SelectedWorkflowName = current;
            }
        }

        private void TryLoadWorkflow()
        {
            if (_isLoadingWorkflow || string.IsNullOrWhiteSpace(SelectedWorkflowName))
            {
                return;
            }

            var name = SelectedWorkflowName.Trim();
            if (!Workflows.Contains(name))
            {
                return;
            }

            _isLoadingWorkflow = true;
            try
            {
                var nodes = WorkflowStorage.Load(name, _downloadersUnlocked);
                LoadCanvasFromPipelineNodes(nodes);
            }
            finally
            {
                _isLoadingWorkflow = false;
            }

            RefreshWorkflowPaths();
            SelectedCanvasNode = CanvasNodes.FirstOrDefault();
            AppendLog($"已载入工作流: {name}");
        }

        private void LoadCanvasFromPipelineNodes(IReadOnlyList<PipelineNodeViewModelBase> nodes)
        {
            ClearCanvas();

            for (var i = 0; i < nodes.Count; i++)
            {
                CanvasNodes.Add(CreateCanvasNode(nodes[i], i));
            }

            RebuildLinearConnections(CanvasNodes.ToList());
            RebuildPipelineNodesFromCanvas();
        }

        private void ClearCanvas()
        {
            CanvasConnections.Clear();
            foreach (var node in CanvasNodes)
            {
                node.PropertyChanged -= OnCanvasNodePropertyChanged;
                node.Dispose();
            }

            CanvasNodes.Clear();
            SelectedCanvasNode = null;
            RebuildPipelineNodesFromCanvas();
        }

        private void OnDownloadersUnlockedChanged()
        {
            _downloadersUnlocked = true;
            ReloadAvailableNodes();
        }

        private void ReloadAvailableNodes()
        {
            var selectedName = SelectedAvailableNode?.DisplayName;

            AvailableNodes.Clear();
            foreach (var descriptor in PipelineCatalog.GetAll().Where(d => !d.RequiresUnlock || _downloadersUnlocked))
            {
                AvailableNodes.Add(descriptor);
            }

            SelectedAvailableNode = AvailableNodes.FirstOrDefault(d => d.DisplayName == selectedName) ?? AvailableNodes.FirstOrDefault();
        }

        private void StartConnection(object parameter)
        {
            if (parameter is not PipelineCanvasConnectorViewModel connector)
            {
                PendingConnection.Source = null;
                PendingConnection.Target = null;
                PendingConnection.IsVisible = false;
                return;
            }

            PendingConnection.Source = connector;
            PendingConnection.Target = null;
            PendingConnection.IsVisible = true;
        }

        private bool CanStartConnection(object parameter)
        {
            return !IsRunning && parameter is PipelineCanvasConnectorViewModel;
        }

        private void CompleteConnection(object parameter)
        {
            TryCreateConnection(PendingConnection.Source, PendingConnection.Target, logFailures: true);
            PendingConnection.Source = null;
            PendingConnection.Target = null;
            PendingConnection.IsVisible = false;
        }

        private bool TryCreateConnection(
            PipelineCanvasConnectorViewModel? first,
            PipelineCanvasConnectorViewModel? second,
            bool logFailures)
        {
            if (!TryNormalizeConnection(first, second, out var output, out var input, out var error))
            {
                if (logFailures && !string.IsNullOrWhiteSpace(error))
                {
                    AppendLog(error);
                }

                return false;
            }

            var ignoredConnections = CanvasConnections
                .Where(c => ReferenceEquals(c.Input, input) || ReferenceEquals(c.Output, output))
                .ToHashSet();

            if (CreatesCycle(output.Node, input.Node, ignoredConnections))
            {
                if (logFailures)
                {
                    AppendLog("不能创建会形成循环的连线。");
                }

                return false;
            }

            RemoveConnectionsForConnector(input);
            RemoveConnectionsForConnector(output);

            CanvasConnections.Add(new PipelineCanvasConnectionViewModel
            {
                Input = input,
                Output = output
            });

            RebuildPipelineNodesFromCanvas();
            return true;
        }

        private bool TryNormalizeConnection(
            PipelineCanvasConnectorViewModel? first,
            PipelineCanvasConnectorViewModel? second,
            out PipelineCanvasConnectorViewModel output,
            out PipelineCanvasConnectorViewModel input,
            out string? error)
        {
            output = null!;
            input = null!;
            error = null;

            if (first == null || second == null)
            {
                error = "连线需要连接两个端口。";
                return false;
            }

            if (ReferenceEquals(first, second) || ReferenceEquals(first.Node, second.Node))
            {
                error = "不能把节点连接到自己。";
                return false;
            }

            if (first.IsInput == second.IsInput)
            {
                error = "请从输出端口连接到输入端口。";
                return false;
            }

            output = first.IsInput ? second : first;
            input = first.IsInput ? first : second;
            var normalizedOutput = output;
            var normalizedInput = input;

            if (CanvasConnections.Any(c => ReferenceEquals(c.Input, normalizedInput) && ReferenceEquals(c.Output, normalizedOutput)))
            {
                error = "这条连线已经存在。";
                return false;
            }

            return true;
        }

        private void DisconnectConnector(object parameter)
        {
            if (parameter is PipelineCanvasConnectorViewModel connector)
            {
                RemoveConnectionsForConnector(connector);
                RebuildPipelineNodesFromCanvas();
            }
        }

        private void RemoveConnectionsForConnector(PipelineCanvasConnectorViewModel connector)
        {
            var connections = CanvasConnections
                .Where(c => ReferenceEquals(c.Input, connector) || ReferenceEquals(c.Output, connector))
                .ToList();

            foreach (var connection in connections)
            {
                CanvasConnections.Remove(connection);
            }
        }

        private bool CreatesCycle(
            PipelineCanvasNodeViewModel source,
            PipelineCanvasNodeViewModel target,
            ISet<PipelineCanvasConnectionViewModel>? ignoredConnections = null)
        {
            var current = target;
            var visited = new HashSet<PipelineCanvasNodeViewModel>();
            while (visited.Add(current))
            {
                var nextConnection = CanvasConnections.FirstOrDefault(c =>
                    ignoredConnections?.Contains(c) != true &&
                    ReferenceEquals(c.Output.Node, current));

                if (nextConnection == null)
                {
                    return false;
                }

                current = nextConnection.Input.Node;
                if (ReferenceEquals(current, source))
                {
                    return true;
                }
            }

            return true;
        }

        private bool TryBuildExecutableCanvasOrder(
            out List<PipelineCanvasNodeViewModel> order,
            out string? error)
        {
            order = new List<PipelineCanvasNodeViewModel>();
            error = null;

            if (CanvasNodes.Count == 0)
            {
                error = "请先添加节点。";
                return false;
            }

            if (CanvasNodes.Count == 1)
            {
                order.Add(CanvasNodes[0]);
                return true;
            }

            if (CanvasConnections.Count != CanvasNodes.Count - 1)
            {
                error = "多节点工作流需要用连线串成一条单一路径。";
                return false;
            }

            var incoming = CanvasNodes.ToDictionary(n => n, _ => 0);
            var outgoing = CanvasNodes.ToDictionary(n => n, _ => 0);

            foreach (var connection in CanvasConnections)
            {
                if (!incoming.ContainsKey(connection.Input.Node) || !outgoing.ContainsKey(connection.Output.Node))
                {
                    error = "连线引用了不存在的节点。";
                    return false;
                }

                incoming[connection.Input.Node]++;
                outgoing[connection.Output.Node]++;

                if (incoming[connection.Input.Node] > 1 || outgoing[connection.Output.Node] > 1)
                {
                    error = "当前执行器只支持单一路径：每个节点最多一个输入连线和一个输出连线。";
                    return false;
                }
            }

            var starts = CanvasNodes.Where(n => incoming[n] == 0).ToList();
            var ends = CanvasNodes.Where(n => outgoing[n] == 0).ToList();
            if (starts.Count != 1 || ends.Count != 1)
            {
                error = "节点图需要有唯一的起点和终点。";
                return false;
            }

            var current = starts[0];
            var visited = new HashSet<PipelineCanvasNodeViewModel>();
            while (visited.Add(current))
            {
                order.Add(current);
                var next = CanvasConnections.FirstOrDefault(c => ReferenceEquals(c.Output.Node, current));
                if (next == null)
                {
                    break;
                }

                current = next.Input.Node;
            }

            if (order.Count != CanvasNodes.Count)
            {
                error = "节点图中存在断开的节点或循环。";
                return false;
            }

            return true;
        }

        private List<PipelineCanvasNodeViewModel> GetCanvasNodesInExecutionOrder()
        {
            if (TryBuildExecutableCanvasOrder(out var order, out _))
            {
                return order;
            }

            return CanvasNodes
                .OrderBy(n => n.Location.X)
                .ThenBy(n => n.Location.Y)
                .ToList();
        }

        private void RebuildLinearConnections(IReadOnlyList<PipelineCanvasNodeViewModel> order)
        {
            CanvasConnections.Clear();
            for (var i = 0; i < order.Count - 1; i++)
            {
                var output = order[i].Output.FirstOrDefault();
                var input = order[i + 1].Input.FirstOrDefault();
                if (output == null || input == null)
                {
                    continue;
                }

                CanvasConnections.Add(new PipelineCanvasConnectionViewModel
                {
                    Output = output,
                    Input = input
                });
            }

            UpdateConnectorStates();
            RebuildPipelineNodesFromCanvas();
        }

        private static void ArrangeCanvasNodes(IReadOnlyList<PipelineCanvasNodeViewModel> order)
        {
            for (var i = 0; i < order.Count; i++)
            {
                order[i].Location = new Point(40 + i * NodeHorizontalSpacing, 40);
            }
        }

        private void RebuildPipelineNodesFromCanvas()
        {
            ReplacePipelineNodes(GetCanvasNodesInExecutionOrder().Select(n => n.PipelineNode));
        }

        private void ReplacePipelineNodes(IEnumerable<PipelineNodeViewModelBase> nodes)
        {
            _isRebuildingPipelineNodes = true;
            try
            {
                PipelineNodes.Clear();
                foreach (var node in nodes)
                {
                    PipelineNodes.Add(node);
                }
            }
            finally
            {
                _isRebuildingPipelineNodes = false;
            }

            RefreshWorkflowPaths();
            RaiseCommandStates();
        }

        private void UpdateConnectorStates()
        {
            foreach (var connector in CanvasNodes.SelectMany(n => n.Input.Concat(n.Output)))
            {
                connector.IsConnected = false;
            }

            foreach (var connection in CanvasConnections)
            {
                connection.Input.IsConnected = true;
                connection.Output.IsConnected = true;
            }
        }

        private void SelectCanvasNode(PipelineCanvasNodeViewModel? value, bool updatePipelineNode)
        {
            if (ReferenceEquals(_selectedCanvasNode, value))
            {
                if (updatePipelineNode && !ReferenceEquals(_selectedPipelineNode, value?.PipelineNode))
                {
                    _selectedPipelineNode = value?.PipelineNode;
                    OnPropertyChanged(nameof(SelectedPipelineNode));
                }

                return;
            }

            _isUpdatingSelection = true;
            try
            {
                if (_selectedCanvasNode != null)
                {
                    _selectedCanvasNode.IsSelected = false;
                }

                _selectedCanvasNode = value;
                if (_selectedCanvasNode != null)
                {
                    _selectedCanvasNode.IsSelected = true;
                }

                OnPropertyChanged(nameof(SelectedCanvasNode));

                if (updatePipelineNode)
                {
                    _selectedPipelineNode = value?.PipelineNode;
                    OnPropertyChanged(nameof(SelectedPipelineNode));
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }

            RaiseCommandStates();
        }

        private void RaiseCommandStates()
        {
            (AddNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SaveWorkflowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteWorkflowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartConnectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CreateConnectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DisconnectConnectorCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnCanvasNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (PipelineCanvasNodeViewModel node in e.OldItems)
                {
                    node.PropertyChanged -= OnCanvasNodePropertyChanged;
                    node.Dispose();
                }
            }

            if (e.NewItems != null)
            {
                foreach (PipelineCanvasNodeViewModel node in e.NewItems)
                {
                    node.PropertyChanged -= OnCanvasNodePropertyChanged;
                    node.PropertyChanged += OnCanvasNodePropertyChanged;
                }
            }

            UpdateConnectorStates();
            if (!_isLoadingWorkflow)
            {
                RebuildPipelineNodesFromCanvas();
            }

            RaiseCommandStates();
        }

        private void OnCanvasConnectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateConnectorStates();
            if (!_isLoadingWorkflow)
            {
                RebuildPipelineNodesFromCanvas();
            }
        }

        private void OnCanvasNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isUpdatingSelection)
            {
                return;
            }

            if (sender is PipelineCanvasNodeViewModel node &&
                e.PropertyName == nameof(PipelineCanvasNodeViewModel.IsSelected) &&
                node.IsSelected)
            {
                SelectedCanvasNode = node;
            }
        }

        private void OnPipelineNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (PipelineNodeViewModelBase node in e.OldItems)
                {
                    DetachNode(node);
                }
            }

            if (e.NewItems != null)
            {
                foreach (PipelineNodeViewModelBase node in e.NewItems)
                {
                    AttachNode(node);
                }
            }

            if (!_isRebuildingPipelineNodes)
            {
                RefreshWorkflowPaths();
            }

            RaiseCommandStates();
        }

        private void AttachNode(PipelineNodeViewModelBase node)
        {
            node.PropertyChanged -= OnPipelineNodePropertyChanged;
            node.PropertyChanged += OnPipelineNodePropertyChanged;
            node.SetWorkflowEditorMode(true);
        }

        private void DetachNode(PipelineNodeViewModelBase node)
        {
            node.PropertyChanged -= OnPipelineNodePropertyChanged;
        }

        private void OnPipelineNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isLoadingWorkflow || _isRefreshingWorkflowPaths || IsRunning)
            {
                return;
            }

            RefreshWorkflowPaths();
        }

        private void RefreshWorkflowPaths()
        {
            if (_isLoadingWorkflow || _isRefreshingWorkflowPaths)
            {
                return;
            }

            _isRefreshingWorkflowPaths = true;
            try
            {
                foreach (var node in PipelineNodes)
                {
                    node.SetWorkflowEditorMode(true);
                }

                PipelineWorkflowPathPlanner.ApplyWorkflowPaths(PipelineNodes);

                foreach (var canvasNode in CanvasNodes)
                {
                    canvasNode.RefreshPathPreview();
                }
            }
            finally
            {
                _isRefreshingWorkflowPaths = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
