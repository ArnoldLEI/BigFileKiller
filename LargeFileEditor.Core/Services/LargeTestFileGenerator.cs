using System.Text;

namespace LargeFileEditor.Core.Services;

public sealed class LargeTestFileGenerator
{
    public async Task GenerateCsvAsync(
        string filePath,
        long dataRows,
        int columns,
        long? approximateBytes,
        Encoding encoding,
        IProgress<long>? bytesWritten,
        CancellationToken cancellationToken)
    {
        if (dataRows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataRows));
        }

        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);
        await using var writer = new StreamWriter(stream, encoding, bufferSize: 1024 * 1024);

        for (int column = 1; column <= columns; column++)
        {
            if (column > 1)
            {
                await writer.WriteAsync(',').ConfigureAwait(false);
            }

            await writer.WriteAsync($"Column{column}").ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);

        long row = 1;
        while (row <= dataRows || (approximateBytes.HasValue && stream.Position < approximateBytes.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (int column = 1; column <= columns; column++)
            {
                if (column > 1)
                {
                    await writer.WriteAsync(',').ConfigureAwait(false);
                }

                await writer.WriteAsync($"R{row}C{column}").ConfigureAwait(false);
            }

            await writer.WriteLineAsync().ConfigureAwait(false);

            if (row % 1000 == 0)
            {
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                bytesWritten?.Report(stream.Position);
            }

            row++;
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        bytesWritten?.Report(stream.Position);
    }
}
