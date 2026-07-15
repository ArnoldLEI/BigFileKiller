using LargeFileEditor.Core.Models;

namespace LargeFileEditor.Core.Services;

public sealed class FileReplacementService
{
    public Task<OperationResult> ReplaceAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new OperationResult
        {
            Succeeded = false,
            Message = "安全取代原始檔功能將於第四階段實作。"
        });
    }
}
