namespace LargeFileEditor.Core.Models;

public sealed class ColumnInfo
{
    public int Position { get; init; }
    public int Index { get; init; }
    public string Header { get; init; } = string.Empty;
    public bool IsDuplicate { get; init; }
    public bool IsBlank { get; init; }
}
