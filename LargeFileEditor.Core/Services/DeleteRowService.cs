using LargeFileEditor.Core.IO;
using LargeFileEditor.Core.Models;

namespace LargeFileEditor.Core.Services;

public sealed class DeleteRowService
{
    public async Task<OperationResult> DeleteSingleRowAsync(
        DeleteRowRequest request,
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
            throw new InvalidOperationException("第二階段僅支援另存新檔，輸出路徑不可與原始檔相同。原始檔未被修改。");
        }

        FileReplacementService.EnsureAvailableSpace(outputFullPath, inputInfo.Length);

        long targetLogicalRecord = request.HasHeader ? request.DataRowNumber + 1 : request.DataRowNumber;
        long currentRecord = 0;
        long deletedRows = 0;

        progress?.Report(new ProgressInfo
        {
            Status = "正在準備刪除資料列",
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

                currentRecord++;
                if (currentRecord == targetLogicalRecord)
                {
                    deletedRows++;
                }
                else
                {
                    await DelimitedFileWriter.WriteRecordAsync(writer, record.Fields, request.Delimiter, cancellationToken).ConfigureAwait(false);
                }

                if ((DateTimeOffset.UtcNow - lastReport).TotalMilliseconds >= 200)
                {
                    progress?.Report(new ProgressInfo
                    {
                        Status = $"正在刪除第 {request.DataRowNumber:N0} 筆資料列",
                        BytesRead = Math.Min(countingStream.BytesRead, inputInfo.Length),
                        TotalBytes = inputInfo.Length,
                        LogicalRowsScanned = currentRecord
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
                Message = "刪除資料列已取消，未完成輸出檔已刪除，原始檔未被修改。"
            };
        }
        catch
        {
            DeleteIncompleteOutput(outputFullPath);
            throw;
        }

        if (deletedRows != 1)
        {
            DeleteIncompleteOutput(outputFullPath);
            throw new InvalidOperationException("指定資料列不存在，原始檔未被修改。");
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
            Status = "刪除資料列完成",
            BytesRead = inputInfo.Length,
            TotalBytes = inputInfo.Length,
            LogicalRowsScanned = currentRecord
        });

        return new OperationResult
        {
            Succeeded = true,
            Message = $"已刪除第 {request.DataRowNumber:N0} 筆資料列，輸出至新檔案。",
            OutputFilePath = outputInfo.FullName,
            RowsDeleted = deletedRows,
            OriginalSizeBytes = inputInfo.Length,
            NewSizeBytes = outputInfo.Length
        };
    }

    private static void ValidateRequest(DeleteRowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputFilePath) || !File.Exists(request.InputFilePath))
        {
            throw new FileNotFoundException("原始檔不存在，未進行任何修改。", request.InputFilePath);
        }

        if (string.IsNullOrWhiteSpace(request.OutputFilePath))
        {
            throw new ArgumentException("請指定輸出檔案路徑。", nameof(request));
        }

        if (request.DataRowNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "資料列編號必須大於 0。原始檔未被修改。");
        }

        if (request.AvailableDataRows >= 0 && request.DataRowNumber > request.AvailableDataRows)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "指定列超過實際資料列數，原始檔未被修改。");
        }
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
            // The caller reports that the original file is safe; cleanup failure should not hide that.
        }
    }
}
