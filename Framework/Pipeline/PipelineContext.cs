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
        public string CurrentInputPath { get; private set; } = string.Empty;
        public string CurrentOutputPath { get; private set; } = string.Empty;
        public int CurrentNodeSequence { get; private set; }

        public void UpdateEffectivePaths(string? inputPath, string? outputPath, int currentNodeSequence)
        {
            CurrentInputPath = inputPath ?? string.Empty;
            CurrentOutputPath = outputPath ?? string.Empty;
            CurrentNodeSequence = currentNodeSequence;
        }
    }
}
