using System;
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
        }

        public TViewModel Inner { get; }

        public override string DisplayName => _displayName;

        public override Task RunAsync(PipelineContext context, CancellationToken token)
        {
            LogToPipeline($"{DisplayName} 暂未接入管道执行逻辑");
            return Task.CompletedTask;
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
