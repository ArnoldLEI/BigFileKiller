using LargeFileEditor.Core.Models;

namespace LargeFileEditor.Core.Services;

public sealed class DeleteColumnService
{
    public Task<OperationResult> DeleteColumnAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new OperationResult
        {
            Succeeded = false,
            Message = "刪除欄位功能將於第三階段實作。"
        });
    }
}
