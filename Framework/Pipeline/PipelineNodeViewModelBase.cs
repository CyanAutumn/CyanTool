using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Pipeline
{



    public abstract class PipelineNodeViewModelBase : INotifyPropertyChanged
    {
        private Action<string>? _pipelineLogger;
        private string? _workflowOutputPortKey;

        public Guid NodeId { get; } = Guid.NewGuid();
        public abstract string DisplayName { get; }




        public string? WorkflowOutputPortKey
        {
            get => _workflowOutputPortKey;
            set => SetField(ref _workflowOutputPortKey, value);
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




        public void AttachLogger(Action<string> logger)
        {
            _pipelineLogger = logger;
        }

        protected void LogToPipeline(string message)
        {
            _pipelineLogger?.Invoke(message);
        }

        public virtual IPipelineDirectoryNode? GetDirectoryAutomationNode()
        {
            return this as IPipelineDirectoryNode;
        }

        public virtual IPipelineRuntimeOutputNode? GetRuntimeOutputNode()
        {
            return this as IPipelineRuntimeOutputNode;
        }

        public virtual IPipelineOutputPathSelectorNode? GetOutputPathSelectorNode()
        {
            return this as IPipelineOutputPathSelectorNode;
        }

        public virtual IPipelineOutputPortProvider? GetOutputPortProvider()
        {
            return this as IPipelineOutputPortProvider;
        }

        public virtual void SetWorkflowEditorMode(bool isWorkflowMode)
        {
            var directoryNode = GetDirectoryAutomationNode();
            if (directoryNode != null)
            {
                directoryNode.IsWorkflowMode = isWorkflowMode;
            }
        }

        public abstract Task RunAsync(PipelineContext context, CancellationToken token);
        public abstract PipelineNodeViewModelBase Clone();
    }
}
