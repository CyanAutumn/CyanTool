namespace CyanTools.Utils
{
    public enum FolderProcessingOperation
    {
        MoveFilesToTopLevel,
        DeleteEmptyDirectories,
        RetainDirectoriesByKeyword,
        ExtractArchives,
        ConvertDiffusionPipeOutputToSdScript
    }

    public sealed class FolderProcessingOptions
    {
        public FolderProcessingOperation Operation { get; init; } = FolderProcessingOperation.MoveFilesToTopLevel;

        public string ParameterText { get; init; } = string.Empty;

        public string OutputPath { get; init; } = string.Empty;

        public bool OverwriteExisting { get; init; }
    }
}
