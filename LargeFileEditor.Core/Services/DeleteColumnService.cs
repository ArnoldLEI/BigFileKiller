using LargeFileEditor.Core.IO;
using LargeFileEditor.Core.Models;

namespace LargeFileEditor.Core.Services;

public sealed class DeleteColumnService
{
    public async Task<OperationResult> DeleteColumnAsync(
        DeleteColumnRequest request,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var inputInfo = new FileInfo(request.InputFilePath);
        string outputFullPath = Path.GetFullPath(request.OutputFilePath);
        string? outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            throw new DirectoryNotFoundException("輸出資料夾不存在，原始檔未被修改。");
        }

        if (string.Equals(inputInfo.FullName, outputFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("輸出路徑不可與原始檔相同。原始檔未被修改。");
        }

        FileReplacementService.EnsureAvailableSpace(outputFullPath, inputInfo.Length);

        var deleteIndexes = ResolveDeleteIndexes(request);
        var deleteIndexSet = deleteIndexes.ToHashSet();
        string deletedNames = ResolveDeletedNames(request, deleteIndexes);
        long rowsWritten = 0;

        progress?.Report(new ProgressInfo
        {
            Status = "正在準備刪除欄位",
            TotalBytes = inputInfo.Length
        });

        try
        {
            await using var inputStream = new FileStream(
                inputInfo.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                FileOptions.SequentialScan);
            using var countingStream = new CountingStream(inputStream);
            using var reader = new DelimitedFileReader(countingStream, request.Encoding, request.Delimiter, leaveOpen: true);

            await using var outputStream = new FileStream(
                outputFullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.SequentialScan);
            await using var writer = new StreamWriter(outputStream, request.Encoding, bufferSize: 1024 * 1024);

            var lastReport = DateTimeOffset.UtcNow;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = await reader.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
                if (record is null)
                {
                    break;
                }

                var fields = RemoveColumns(record.Fields, deleteIndexSet);
                await DelimitedFileWriter.WriteRecordAsync(writer, fields, request.Delimiter, cancellationToken).ConfigureAwait(false);
                rowsWritten++;

                if ((DateTimeOffset.UtcNow - lastReport).TotalMilliseconds >= 200)
                {
                    progress?.Report(new ProgressInfo
                    {
                        Status = $"正在刪除 {deleteIndexes.Count:N0} 個欄位",
                        BytesRead = Math.Min(countingStream.BytesRead, inputInfo.Length),
                        TotalBytes = inputInfo.Length,
                        LogicalRowsScanned = rowsWritten
                    });
                    lastReport = DateTimeOffset.UtcNow;
                }
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            outputStream.Flush(flushToDisk: true);
        }
        catch (OperationCanceledException)
        {
            DeleteIncompleteOutput(outputFullPath);
            return new OperationResult
            {
                Succeeded = false,
                Canceled = true,
                Message = "刪除欄位已取消，未完成輸出檔已刪除，原始檔未被修改。"
            };
        }
        catch
        {
            DeleteIncompleteOutput(outputFullPath);
            throw;
        }

        await using (var verifyStream = new FileStream(outputFullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (!verifyStream.CanRead)
            {
                throw new IOException("輸出檔無法重新開啟驗證，原始檔未被修改。");
            }
        }

        var outputInfo = new FileInfo(outputFullPath);
        progress?.Report(new ProgressInfo
        {
            Status = "刪除欄位完成",
            BytesRead = inputInfo.Length,
            TotalBytes = inputInfo.Length,
            LogicalRowsScanned = rowsWritten
        });

        return new OperationResult
        {
            Succeeded = true,
            Message = $"已刪除 {deleteIndexes.Count:N0} 個欄位，輸出至新檔案。",
            OutputFilePath = outputInfo.FullName,
            ColumnsDeleted = deleteIndexes.Count,
            DeletedColumnName = deletedNames,
            DeletedColumnIndex = deleteIndexes.Count == 1 ? deleteIndexes[0] : null,
            DeletedColumnIndexes = deleteIndexes,
            OriginalSizeBytes = inputInfo.Length,
            NewSizeBytes = outputInfo.Length
        };
    }

    private static void ValidateRequest(DeleteColumnRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputFilePath) || !File.Exists(request.InputFilePath))
        {
            throw new FileNotFoundException("原始檔不存在，未進行任何修改。", request.InputFilePath);
        }

        if (string.IsNullOrWhiteSpace(request.OutputFilePath))
        {
            throw new ArgumentException("請指定輸出檔案路徑。", nameof(request));
        }

        if (request.ColumnIndexes.Count == 0 && request.ColumnIndex is null && string.IsNullOrWhiteSpace(request.HeaderName))
        {
            throw new ArgumentException("請指定欄位標題、欄位位置，或選取至少一個欄位。", nameof(request));
        }
    }

    private static IReadOnlyList<int> ResolveDeleteIndexes(DeleteColumnRequest request)
    {
        if (request.ColumnIndexes.Count > 0)
        {
            return request.ColumnIndexes
                .Distinct()
                .Order()
                .Select(ValidateIndex)
                .ToArray();
        }

        if (request.ColumnIndex is int index)
        {
            return [ValidateIndex(index)];
        }

        string wanted = NormalizeHeader(request.HeaderName ?? string.Empty, request.TrimHeaderWhitespace);
        var comparison = request.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var matches = request.Columns
            .Where(column => string.Equals(NormalizeHeader(column.Header, request.TrimHeaderWhitespace), wanted, comparison))
            .ToArray();

        if (matches.Length == 0)
        {
            throw new InvalidOperationException("找不到指定欄位標題，原始檔未被修改。");
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException("存在多個同名欄位，請指定實際欄位位置。原始檔未被修改。");
        }

        return [matches[0].Index];
    }

    private static int ValidateIndex(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "欄位位置必須大於 0。原始檔未被修改。");
        }

        return index;
    }

    private static string ResolveDeletedNames(DeleteColumnRequest request, IReadOnlyList<int> deleteIndexes)
    {
        var names = deleteIndexes.Select(index =>
        {
            string? header = request.Columns.FirstOrDefault(column => column.Index == index)?.Header;
            return string.IsNullOrEmpty(header) ? $"Column{index + 1}" : header;
        });
        return string.Join(", ", names);
    }

    private static IReadOnlyList<string> RemoveColumns(IReadOnlyList<string> fields, HashSet<int> deleteIndexes)
    {
        if (fields.Count == 0)
        {
            return Array.Empty<string>();
        }

        var output = new List<string>(Math.Max(0, fields.Count - deleteIndexes.Count));
        for (int i = 0; i < fields.Count; i++)
        {
            if (!deleteIndexes.Contains(i))
            {
                output.Add(fields[i]);
            }
        }

        return output;
    }

    private static string NormalizeHeader(string value, bool trim)
    {
        return trim ? value.Trim() : value;
    }

    private static void DeleteIncompleteOutput(string outputFullPath)
    {
        try
        {
            if (File.Exists(outputFullPath))
            {
                File.Delete(outputFullPath);
            }
        }
        catch
        {
            // The original file is still safe even if cleanup is blocked.
        }
    }
}
