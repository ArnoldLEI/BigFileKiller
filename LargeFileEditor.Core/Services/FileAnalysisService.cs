using System.Collections.ObjectModel;
using System.Diagnostics;
using LargeFileEditor.Core.IO;
using LargeFileEditor.Core.Models;

namespace LargeFileEditor.Core.Services;

public sealed class FileAnalysisService
{
    private readonly EncodingDetectionService _encodingDetectionService;

    public FileAnalysisService(EncodingDetectionService? encodingDetectionService = null)
    {
        _encodingDetectionService = encodingDetectionService ?? new EncodingDetectionService();
    }

    public async Task<FileAnalysisResult> AnalyzeAsync(
        string filePath,
        char delimiter,
        bool hasHeader,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("檔案不存在，原始檔未被修改。", filePath);
        }

        var fileInfo = new FileInfo(filePath);
        progress?.Report(new ProgressInfo
        {
            Status = "正在偵測編碼",
            TotalBytes = fileInfo.Length
        });

        var encoding = await _encodingDetectionService.DetectAsync(filePath, cancellationToken).ConfigureAwait(false);

        long totalRows = 0;
        int maxFieldCount = 0;
        IReadOnlyList<string>? headerFields = null;
        var stopwatch = Stopwatch.StartNew();

        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);
        using var countingStream = new CountingStream(fileStream);
        using var reader = new DelimitedFileReader(countingStream, encoding, delimiter, leaveOpen: true);

        progress?.Report(new ProgressInfo
        {
            Status = "正在讀取標題",
            BytesRead = countingStream.BytesRead,
            TotalBytes = fileInfo.Length,
            LogicalRowsScanned = totalRows
        });

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await reader.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                break;
            }

            totalRows++;
            maxFieldCount = Math.Max(maxFieldCount, record.Fields.Count);

            if (totalRows == 1)
            {
                headerFields = record.Fields.ToArray();
            }

            if (stopwatch.ElapsedMilliseconds >= 200)
            {
                progress?.Report(new ProgressInfo
                {
                    Status = totalRows == 1 ? "正在讀取標題" : "正在統計資料列",
                    BytesRead = Math.Min(countingStream.BytesRead, fileInfo.Length),
                    TotalBytes = fileInfo.Length,
                    LogicalRowsScanned = totalRows
                });
                stopwatch.Restart();
            }
        }

        progress?.Report(new ProgressInfo
        {
            Status = "掃描完成",
            BytesRead = fileInfo.Length,
            TotalBytes = fileInfo.Length,
            LogicalRowsScanned = totalRows
        });

        var columns = BuildColumns(hasHeader, headerFields, maxFieldCount);
        return new FileAnalysisResult
        {
            FilePath = fileInfo.FullName,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            FileSizeText = FormatFileSize(fileInfo.Length),
            CreatedAt = fileInfo.CreationTime,
            ModifiedAt = fileInfo.LastWriteTime,
            Encoding = encoding,
            EncodingName = GetEncodingName(encoding),
            NewLineFormat = GetNewLineFormat(reader.CrLfCount, reader.LfCount, reader.CrCount),
            Delimiter = delimiter,
            DelimiterName = GetDelimiterName(delimiter),
            TotalRowsIncludingHeader = totalRows,
            DataRows = hasHeader && totalRows > 0 ? totalRows - 1 : totalRows,
            FieldCount = maxFieldCount,
            HasHeader = hasHeader,
            IsComplete = true,
            StatusMessage = "掃描完成",
            Columns = columns
        };
    }

    private static IReadOnlyList<ColumnInfo> BuildColumns(bool hasHeader, IReadOnlyList<string>? headerFields, int fieldCount)
    {
        if (fieldCount == 0)
        {
            return ReadOnlyCollection<ColumnInfo>.Empty;
        }

        var headers = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            headers[i] = hasHeader && headerFields is not null && i < headerFields.Count
                ? headerFields[i]
                : $"Column{i + 1}";
        }

        var counts = headers
            .GroupBy(static x => x, StringComparer.Ordinal)
            .ToDictionary(static x => x.Key, static x => x.Count(), StringComparer.Ordinal);

        return headers
            .Select((header, index) => new ColumnInfo
            {
                Position = index + 1,
                Index = index,
                Header = header,
                IsBlank = string.IsNullOrWhiteSpace(header),
                IsDuplicate = counts[header] > 1
            })
            .ToArray();
    }

    public static string FormatFileSize(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes:N0} bytes"
            : $"{bytes:N0} bytes ({value:N2} {units[unit]})";
    }

    public static string GetDelimiterName(char delimiter) => delimiter switch
    {
        ',' => "逗號 (,)",
        '\t' => "Tab",
        ';' => "分號 (;)",
        _ => $"自訂 ({delimiter})"
    };

    private static string GetEncodingName(System.Text.Encoding encoding)
    {
        if (encoding is System.Text.UTF8Encoding utf8)
        {
            return utf8.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8 無 BOM";
        }

        if (Equals(encoding, System.Text.Encoding.Unicode))
        {
            return "UTF-16 LE";
        }

        if (Equals(encoding, System.Text.Encoding.BigEndianUnicode))
        {
            return "UTF-16 BE";
        }

        return encoding.EncodingName;
    }

    private static string GetNewLineFormat(long crlf, long lf, long cr)
    {
        int kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        if (kinds == 0)
        {
            return "無換行或未知";
        }

        if (kinds > 1)
        {
            return $"混合格式 (CRLF: {crlf:N0}, LF: {lf:N0}, CR: {cr:N0})";
        }

        if (crlf > 0)
        {
            return "CRLF";
        }

        return lf > 0 ? "LF" : "CR";
    }
}
