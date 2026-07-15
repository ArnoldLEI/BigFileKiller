using System.Text;

namespace LargeFileEditor.Core.Services;

public sealed class EncodingDetectionService
{
    public async Task<Encoding> DetectAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4,
            FileOptions.SequentialScan);

        var bom = new byte[4];
        int read = await stream.ReadAsync(bom.AsMemory(0, bom.Length), cancellationToken).ConfigureAwait(false);

        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        }

        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
    }
}
