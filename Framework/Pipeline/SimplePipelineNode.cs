using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Pipeline
{



    public abstract class SimplePipelineNode<TViewModel> : PipelineNodeViewModelBase, IPipelineSnapshotProvider where TViewModel : class, new()
    {
        private readonly string _displayName;

        protected SimplePipelineNode(string displayName)
        {
            _displayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Inner = new TViewModel();

            if (Inner is INotifyPropertyChanged notifyPropertyChanged)
            {
                notifyPropertyChanged.PropertyChanged += OnInnerPropertyChanged;
            }
        }

        public TViewModel Inner { get; }

        public override string DisplayName => _displayName;

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            if (Inner is IPipelineRunnableViewModel runnableViewModel)
            {
                return runnableViewModel.RunFromPipelineAsync(context, token);
            }

            LogToPipeline($"{DisplayName} 暂未接入管道执行逻辑");
            return Task.CompletedTask;
        }

        public override IPipelineDirectoryNode? GetDirectoryAutomationNode()
        {
            return Inner as IPipelineDirectoryNode;
        }

        public override IPipelineRuntimeOutputNode? GetRuntimeOutputNode()
        {
            return Inner as IPipelineRuntimeOutputNode;
        }

        public override IPipelineOutputPathSelectorNode? GetOutputPathSelectorNode()
        {
            return Inner as IPipelineOutputPathSelectorNode;
        }

        public override IPipelineOutputPortProvider? GetOutputPortProvider()
        {
            return Inner as IPipelineOutputPortProvider;
        }

        private void OnInnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e.PropertyName);
        }

        public override void SetWorkflowEditorMode(bool isWorkflowMode)
        {
            if (Inner is IPipelineDirectoryNode directoryNode)
            {
                directoryNode.IsWorkflowMode = isWorkflowMode;
            }
        }

        public override PipelineNodeViewModelBase Clone()
        {

            return (PipelineNodeViewModelBase)Activator.CreateInstance(GetType())!;
        }

        public virtual Dictionary<string, object?> CaptureSnapshot()
        {
            return PipelineSnapshotHelper.CaptureSimpleProperties(Inner);
        }

        public virtual void ApplySnapshot(Dictionary<string, object?> snapshot)
        {
            PipelineSnapshotHelper.ApplySimpleProperties(Inner, snapshot);
        }
    }
}
