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

        public Guid NodeId { get; } = Guid.NewGuid();
        public abstract string DisplayName { get; }

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

        public abstract Task RunAsync(PipelineContext context, CancellationToken token);
        public abstract PipelineNodeViewModelBase Clone();
    }
}
