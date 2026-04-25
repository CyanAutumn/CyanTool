namespace CyanTools.Pipeline
{
    public interface IPipelineDirectoryNode
    {
        PipelineDirectoryBehavior DirectoryBehavior { get; }
        string PipelineInputPath { get; set; }
        string PipelineOutputPath { get; set; }
        bool IsWorkflowMode { get; set; }
        bool HasInheritedInputPath { get; set; }
    }
}
