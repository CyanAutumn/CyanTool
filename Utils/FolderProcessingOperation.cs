namespace CyanTools.Utils
{
    public enum FolderProcessingOperation
    {
        MoveFilesToTopLevel,
        DeleteEmptyDirectories,
        RetainDirectoriesByKeyword,
        ExtractArchives
    }

    public sealed class FolderProcessingOptions
    {
        public FolderProcessingOperation Operation { get; init; } = FolderProcessingOperation.MoveFilesToTopLevel;

        public string ParameterText { get; init; } = string.Empty;
    }
}
