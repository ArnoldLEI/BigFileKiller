using LargeFileEditor.Core.Models;

namespace LargeFileEditor.Core.Services;

public sealed class FileReplacementService
{
    public OperationResult ReplaceOriginal(FileReplacementRequest request)
    {
        ValidateRequest(request);

        string originalPath = Path.GetFullPath(request.OriginalFilePath);
        string tempPath = Path.GetFullPath(request.CompletedTempFilePath);
        string backupPath = CreateBackupPath(originalPath);
        long originalSize = new FileInfo(originalPath).Length;
        long newSize = new FileInfo(tempPath).Length;

        try
        {
            File.Move(originalPath, backupPath);

            try
            {
                File.Move(tempPath, originalPath);
            }
            catch
            {
                if (File.Exists(originalPath))
                {
                    File.Delete(originalPath);
                }

                File.Move(backupPath, originalPath);
                throw;
            }

            if (!request.KeepBackup && File.Exists(backupPath))
            {
                File.Delete(backupPath);
                backupPath = string.Empty;
            }

            return new OperationResult
            {
                Succeeded = true,
                OutputFilePath = originalPath,
                Message = string.IsNullOrEmpty(backupPath)
                    ? "已安全取代原始檔，備份檔已刪除。"
                    : $"已安全取代原始檔，備份檔：{backupPath}",
                OriginalSizeBytes = originalSize,
                NewSizeBytes = newSize
            };
        }
        catch (Exception ex)
        {
            throw new IOException($"檔案替換失敗，已嘗試保留或恢復原始檔。原因：{ex.Message}", ex);
        }
    }

    public static void EnsureAvailableSpace(string targetPath, long requiredBytes)
    {
        string fullPath = Path.GetFullPath(targetPath);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new IOException("無法判斷目標磁碟，未開始作業。");
        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException($"磁碟空間不足。需要至少 {FileAnalysisService.FormatFileSize(requiredBytes)}，目前可用 {FileAnalysisService.FormatFileSize(drive.AvailableFreeSpace)}。原始檔未被修改。");
        }
    }

    public static string CreateTempPathBesideOriginal(string originalPath, string operationName)
    {
        string directory = Path.GetDirectoryName(originalPath) ?? Environment.CurrentDirectory;
        string name = Path.GetFileNameWithoutExtension(originalPath);
        string extension = Path.GetExtension(originalPath);
        return Path.Combine(directory, $"{name}.{operationName}.{Guid.NewGuid():N}.tmp{extension}");
    }

    private static void ValidateRequest(FileReplacementRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OriginalFilePath) || !File.Exists(request.OriginalFilePath))
        {
            throw new FileNotFoundException("原始檔不存在，無法替換。", request.OriginalFilePath);
        }

        if (string.IsNullOrWhiteSpace(request.CompletedTempFilePath) || !File.Exists(request.CompletedTempFilePath))
        {
            throw new FileNotFoundException("暫存檔不存在，原始檔未被修改。", request.CompletedTempFilePath);
        }

        if (string.Equals(Path.GetFullPath(request.OriginalFilePath), Path.GetFullPath(request.CompletedTempFilePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("暫存檔不可與原始檔相同。");
        }
    }

    private static string CreateBackupPath(string originalPath)
    {
        string directory = Path.GetDirectoryName(originalPath) ?? Environment.CurrentDirectory;
        string name = Path.GetFileNameWithoutExtension(originalPath);
        string extension = Path.GetExtension(originalPath);
        string candidate = Path.Combine(directory, $"{name}.backup{extension}");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(directory, $"{name}.backup-{stamp}{extension}");
    }
}
