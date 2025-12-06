using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private PipelineNodeDescriptor? _selectedAvailableNode;
        private PipelineNodeViewModelBase? _selectedPipelineNode;
        private bool _isRunning;
        private CancellationTokenSource? _cts;
        private readonly PipelineRunner _runner = new();

        public PipelineSchedulerViewModel()
        {
            _downloadersUnlocked = Utils.UnlockState.DownloadersUnlocked;

            AvailableNodes = new ObservableCollection<PipelineNodeDescriptor>();
            PipelineNodes = new ObservableCollection<PipelineNodeViewModelBase>();
            LogLines = new ObservableCollection<string>();

            AddNodeCommand = new RelayCommand(AddNode, () => SelectedAvailableNode != null && !IsRunning);
            RemoveNodeCommand = new RelayCommand(RemoveSelectedNode, () => SelectedPipelineNode != null && !IsRunning);
            MoveUpCommand = new RelayCommand(MoveUp, () => CanMove(-1));
            MoveDownCommand = new RelayCommand(MoveDown, () => CanMove(1));
            StartCommand = new RelayCommand(async () => await RunAsync(), () => PipelineNodes.Any() && !IsRunning);
            CancelCommand = new RelayCommand(CancelRun, () => IsRunning);
            ClearLogCommand = new RelayCommand(() => LogLines.Clear(), () => !IsRunning);

            ReloadAvailableNodes();
            Utils.UnlockState.DownloadersUnlockedChanged += OnDownloadersUnlockedChanged;
        }

        public ObservableCollection<PipelineNodeDescriptor> AvailableNodes { get; }
        public ObservableCollection<PipelineNodeViewModelBase> PipelineNodes { get; }
        public ObservableCollection<string> LogLines { get; }

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
                if (SetField(ref _selectedPipelineNode, value))
                {
                    RaiseCommandStates();
                }
            }
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

        private bool _downloadersUnlocked;

        private void AddNode()
        {
            if (SelectedAvailableNode == null)
            {
                return;
            }

            var instance = SelectedAvailableNode.Factory();
            PipelineNodes.Add(instance);
            SelectedPipelineNode = instance;
            AppendLog($"已添加节点: {instance.DisplayName}");
        }

        private void RemoveSelectedNode()
        {
            if (SelectedPipelineNode == null)
            {
                return;
            }

            var index = PipelineNodes.IndexOf(SelectedPipelineNode);
            if (index >= 0)
            {
                PipelineNodes.RemoveAt(index);
                SelectedPipelineNode = PipelineNodes.ElementAtOrDefault(Math.Max(index - 1, 0));
                AppendLog("已移除节点");
            }
        }

        private bool CanMove(int direction)
        {
            if (IsRunning || SelectedPipelineNode == null)
            {
                return false;
            }

            var index = PipelineNodes.IndexOf(SelectedPipelineNode);
            return direction < 0 ? index > 0 : index >= 0 && index < PipelineNodes.Count - 1;
        }

        private void MoveUp() => MoveNode(-1);
        private void MoveDown() => MoveNode(1);

        private void MoveNode(int direction)
        {
            if (SelectedPipelineNode == null)
            {
                return;
            }

            var index = PipelineNodes.IndexOf(SelectedPipelineNode);
            var targetIndex = index + direction;
            if (index < 0 || targetIndex < 0 || targetIndex >= PipelineNodes.Count)
            {
                return;
            }

            PipelineNodes.Move(index, targetIndex);
        }

        private async Task RunAsync()
        {
            if (IsRunning || PipelineNodes.Count == 0)
            {
                return;
            }

            IsRunning = true;
            _cts = new CancellationTokenSource();
            LogLines.Clear();

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

        private void RaiseCommandStates()
        {
            (AddNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
