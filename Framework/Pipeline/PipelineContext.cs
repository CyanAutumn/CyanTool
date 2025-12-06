using System;

namespace CyanTools.Pipeline
{
    public sealed class PipelineContext
    {
        public PipelineContext(Action<string> appendLog)
        {
            AppendLog = appendLog ?? throw new ArgumentNullException(nameof(appendLog));
        }

        public Action<string> AppendLog { get; }
    }
}
