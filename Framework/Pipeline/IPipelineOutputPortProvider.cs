using System.Collections.Generic;

namespace CyanTools.Pipeline
{



    public sealed record PipelineOutputPort(string Key, string Title, string? RelativePath = null)
    {
        public bool IsRootOutput => string.IsNullOrWhiteSpace(RelativePath);
    }

    public interface IPipelineOutputPortProvider
    {
        IReadOnlyList<PipelineOutputPort> GetPipelineOutputPorts();
    }
}
