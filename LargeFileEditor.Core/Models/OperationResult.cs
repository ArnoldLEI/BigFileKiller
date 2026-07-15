namespace LargeFileEditor.Core.Models;

public sealed class OperationResult
{
    public bool Succeeded { get; init; }
    public bool Canceled { get; init; }
    public string Message { get; init; } = string.Empty;
    public string OutputFilePath { get; init; } = string.Empty;
    public long RowsDeleted { get; init; }
    public int ColumnsDeleted { get; init; }
    public string DeletedColumnName { get; init; } = string.Empty;
    public int? DeletedColumnIndex { get; init; }
    public long OriginalSizeBytes { get; init; }
    public long NewSizeBytes { get; init; }
}
