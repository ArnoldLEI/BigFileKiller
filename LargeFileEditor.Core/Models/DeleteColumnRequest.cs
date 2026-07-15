using System.Text;

namespace LargeFileEditor.Core.Models;

public sealed class DeleteColumnRequest
{
    public string InputFilePath { get; init; } = string.Empty;
    public string OutputFilePath { get; init; } = string.Empty;
    public char Delimiter { get; init; }
    public Encoding Encoding { get; init; } = Encoding.UTF8;
    public bool HasHeader { get; init; }
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = Array.Empty<ColumnInfo>();
    public string? HeaderName { get; init; }
    public int? ColumnIndex { get; init; }
    public bool IgnoreCase { get; init; }
    public bool TrimHeaderWhitespace { get; init; }
}
