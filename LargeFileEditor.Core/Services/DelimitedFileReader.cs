using System.Text;

namespace LargeFileEditor.Core.Services;

public sealed class DelimitedFileReader : IDisposable
{
    private readonly StreamReader _reader;
    private readonly char _delimiter;
    private readonly char[] _oneChar = new char[1];
    private long _recordNumber;

    public DelimitedFileReader(Stream stream, Encoding encoding, char delimiter, bool leaveOpen = false)
    {
        _reader = new StreamReader(
            stream,
            encoding,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024 * 1024,
            leaveOpen: leaveOpen);
        _delimiter = delimiter;
    }

    public long CrLfCount { get; private set; }
    public long LfCount { get; private set; }
    public long CrCount { get; private set; }

    public async Task<DelimitedRecord?> ReadRecordAsync(CancellationToken cancellationToken)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool sawAnyChar = false;
        bool atFieldStart = true;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await _reader.ReadAsync(_oneChar.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                if (!sawAnyChar && fields.Count == 0 && field.Length == 0)
                {
                    return null;
                }

                fields.Add(field.ToString());
                _recordNumber++;
                return new DelimitedRecord(fields, _recordNumber);
            }

            sawAnyChar = true;
            char c = _oneChar[0];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (_reader.Peek() == '"')
                    {
                        await _reader.ReadAsync(_oneChar.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (atFieldStart && c == '"')
            {
                inQuotes = true;
                atFieldStart = false;
                continue;
            }

            if (c == _delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                atFieldStart = true;
                continue;
            }

            if (c == '\r' || c == '\n')
            {
                await CountAndConsumeNewLineAsync(c, cancellationToken).ConfigureAwait(false);
                fields.Add(field.ToString());
                _recordNumber++;
                return new DelimitedRecord(fields, _recordNumber);
            }

            field.Append(c);
            atFieldStart = false;
        }
    }

    private async Task CountAndConsumeNewLineAsync(char c, CancellationToken cancellationToken)
    {
        if (c == '\r')
        {
            if (_reader.Peek() == '\n')
            {
                await _reader.ReadAsync(_oneChar.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                CrLfCount++;
            }
            else
            {
                CrCount++;
            }
        }
        else
        {
            LfCount++;
        }
    }

    public void Dispose() => _reader.Dispose();
}
