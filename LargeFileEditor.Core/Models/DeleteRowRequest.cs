using System.Text;

namespace LargeFileEditor.Core.Models;

public sealed class DeleteRowRequest
{
    public string InputFilePath { get; init; } = string.Empty;
    public string OutputFilePath { get; init; } = string.Empty;
    public char Delimiter { get; init; }
    public Encoding Encoding { get; init; } = Encoding.UTF8;
    public bool HasHeader { get; init; }
    public long DataRowNumber { get; init; }
    public long AvailableDataRows { get; init; }
}
