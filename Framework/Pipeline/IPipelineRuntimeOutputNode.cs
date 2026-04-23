namespace CyanTools.Pipeline
{
    public interface IPipelineRuntimeOutputNode
    {
        void ResetRuntimeOutputPath();
        bool TryGetRuntimeOutputPath(out string path);
    }
}
