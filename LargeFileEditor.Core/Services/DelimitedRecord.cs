namespace LargeFileEditor.Core.Services;

public sealed class DelimitedRecord
{
    public DelimitedRecord(IReadOnlyList<string> fields, long recordNumber)
    {
        Fields = fields;
        RecordNumber = recordNumber;
    }

    public IReadOnlyList<string> Fields { get; }
    public long RecordNumber { get; }
}
