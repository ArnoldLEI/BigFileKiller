using System.Collections.ObjectModel;
using System.Text;

namespace LargeFileEditor.Core.Models;

public sealed class FileAnalysisResult
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public string FileSizeText { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public Encoding Encoding { get; init; } = Encoding.UTF8;
    public string EncodingName { get; init; } = "UTF-8";
    public string NewLineFormat { get; init; } = "未知";
    public char Delimiter { get; init; }
    public string DelimiterName { get; init; } = string.Empty;
    public long TotalRowsIncludingHeader { get; init; }
    public long DataRows { get; init; }
    public int FieldCount { get; init; }
    public bool HasHeader { get; init; }
    public bool IsComplete { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = ReadOnlyCollection<ColumnInfo>.Empty;
}
