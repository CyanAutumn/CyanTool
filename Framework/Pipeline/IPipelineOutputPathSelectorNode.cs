namespace CyanTools.Pipeline
{



    public interface IPipelineOutputPathSelectorNode
    {
        bool TryGetOutputFolderPath(out string path);
    }
}
