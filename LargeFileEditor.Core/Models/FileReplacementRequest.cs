namespace LargeFileEditor.Core.Models;

public sealed class FileReplacementRequest
{
    public string OriginalFilePath { get; init; } = string.Empty;
    public string CompletedTempFilePath { get; init; } = string.Empty;
    public bool KeepBackup { get; init; } = true;
}
