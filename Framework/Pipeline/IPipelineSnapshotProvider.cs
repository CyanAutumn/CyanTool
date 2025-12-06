using System.Collections.Generic;

namespace CyanTools.Pipeline
{
    public interface IPipelineSnapshotProvider
    {
        Dictionary<string, object?> CaptureSnapshot();
        void ApplySnapshot(Dictionary<string, object?> snapshot);
    }
}
