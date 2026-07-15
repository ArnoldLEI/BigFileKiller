namespace LargeFileEditor.Core.Models;

public sealed class ProgressInfo
{
    public string Status { get; init; } = string.Empty;
    public long BytesRead { get; init; }
    public long TotalBytes { get; init; }
    public double Percent => TotalBytes <= 0 ? 0 : Math.Clamp(BytesRead * 100d / TotalBytes, 0, 100);
    public long LogicalRowsScanned { get; init; }
}
