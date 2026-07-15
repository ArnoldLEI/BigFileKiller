using System.Text;

namespace LargeFileEditor.Core.Services;

public sealed class DelimitedFileWriter
{
    public static string FormatField(string value, char delimiter)
    {
        bool mustQuote = value.Contains(delimiter)
            || value.Contains('"')
            || value.Contains('\r')
            || value.Contains('\n');

        if (!mustQuote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static async Task WriteRecordAsync(
        StreamWriter writer,
        IReadOnlyList<string> fields,
        char delimiter,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                await writer.WriteAsync(delimiter).ConfigureAwait(false);
            }

            await writer.WriteAsync(FormatField(fields[i], delimiter).AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);
    }
}
