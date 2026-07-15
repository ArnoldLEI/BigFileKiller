using System.Text;
using LargeFileEditor.Core.Services;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("UTF-8 no BOM, CRLF, header counts", TestUtf8NoBom),
    ("UTF-8 BOM detection", TestUtf8Bom),
    ("UTF-16 LE detection", TestUtf16Le),
    ("UTF-16 BE detection", TestUtf16Be),
    ("LF detection", TestLf),
    ("Empty file", TestEmptyFile),
    ("Header only", TestHeaderOnly),
    ("No header generates temporary names", TestNoHeader),
    ("Blank and duplicate headers", TestBlankAndDuplicateHeaders),
    ("Quoted comma quote newline and trailing empty", TestCsvRules),
    ("Inconsistent row field counts", TestInconsistentRows),
    ("Cancellation", TestCancellation),
    ("Large test file generator streams output", TestGenerator),
    ("Delete first data row", TestDeleteFirstDataRow),
    ("Delete middle data row", TestDeleteMiddleDataRow),
    ("Delete last data row", TestDeleteLastDataRow),
    ("Delete missing data row fails safely", TestDeleteMissingDataRow),
    ("Cancel row delete removes incomplete output", TestDeleteCancellation),
    ("Delete column by header", TestDeleteColumnByHeader),
    ("Delete column by position", TestDeleteColumnByPosition),
    ("Delete first column", TestDeleteFirstColumn),
    ("Delete last column", TestDeleteLastColumn),
    ("Duplicate header requires position", TestDuplicateHeaderRequiresPosition),
    ("Duplicate header can delete selected position", TestDuplicateHeaderByPosition),
    ("Delete column preserves quoted CSV output", TestDeleteColumnQuotesOutput),
    ("Cancel column delete removes incomplete output", TestDeleteColumnCancellation)
};

int failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

Console.WriteLine(failed == 0 ? "All tests passed." : $"{failed} test(s) failed.");
return failed == 0 ? 0 : 1;

static async Task TestUtf8NoBom()
{
    string path = await WriteTempAsync("A,B\r\n1,2\r\n3,4\r\n", new UTF8Encoding(false));
    var result = await AnalyzeAsync(path, ',', hasHeader: true);
    AssertEqual("UTF-8 無 BOM", result.EncodingName);
    AssertEqual("CRLF", result.NewLineFormat);
    AssertEqual(3L, result.TotalRowsIncludingHeader);
    AssertEqual(2L, result.DataRows);
    AssertEqual(2, result.FieldCount);
    AssertEqual("A", result.Columns[0].Header);
}

static async Task TestUtf8Bom()
{
    string path = await WriteTempAsync("A,B\r\n1,2\r\n", new UTF8Encoding(true));
    var result = await AnalyzeAsync(path, ',', hasHeader: true);
    AssertEqual("UTF-8 BOM", result.EncodingName);
}

static async Task TestUtf16Le()
{
    string path = await WriteTempAsync("A\tB\r\n1\t2\r\n", Encoding.Unicode);
    var result = await AnalyzeAsync(path, '\t', hasHeader: true);
    AssertEqual("UTF-16 LE", result.EncodingName);
    AssertEqual("Tab", result.DelimiterName);
}

static async Task TestUtf16Be()
{
    string path = await WriteTempAsync("A;B\r\n1;2\r\n", Encoding.BigEndianUnicode);
    var result = await AnalyzeAsync(path, ';', hasHeader: true);
    AssertEqual("UTF-16 BE", result.EncodingName);
    AssertEqual("分號 (;)", result.DelimiterName);
}

static async Task TestLf()
{
    string path = await WriteTempAsync("A,B\n1,2\n", new UTF8Encoding(false));
    var result = await AnalyzeAsync(path, ',', hasHeader: true);
    AssertEqual("LF", result.NewLineFormat);
}

static async Task TestEmptyFile()
{
    string path = await WriteTempAsync(string.Empty, new UTF8Encoding(false));
    var result = await AnalyzeAsync(path, ',', hasHeader: true);
    AssertEqual(0L, result.TotalRowsIncludingHeader);
    AssertEqual(0, result.FieldCount);
    AssertEqual(0, result.Columns.Count);
}

static async Task TestHeaderOnly()
{
    string path = await WriteTempAsync("A,B,C", new UTF8Encoding(false));
    var result = await AnalyzeAsync(path, ',', hasHeader: true);
    AssertEqual(1L, result.TotalRowsIncludingHeader);
    AssertEqual(0L, result.DataRows);
    AssertEqual(3, result.FieldCount);
}

static async Task TestNoHeader()
{
    string path = await WriteTempAsync("1,2,3\n4,5,6\n", new UTF8Encoding(false));
    var result = await AnalyzeAsync(path, ',', hasHeader: false);
    AssertEqual(2L, result.DataRows);
    AssertEqual("Column1", result.Columns[0].Header);
    AssertEqual("Column3", result.Columns[2].Header);
}

static async Task TestBlankAndDuplicateHeaders()
{
    string path = await WriteTempAsync("A,,A\n1,2,3\n", new UTF8Encoding(false));
    var result = await AnalyzeAsync(path, ',', hasHeader: true);
    Assert(result.Columns[0].IsDuplicate, "First A should be duplicate.");
    Assert(result.Columns[2].IsDuplicate, "Second A should be duplicate.");
    Assert(result.Columns[1].IsBlank, "Middle header should be blank.");
}

static async Task TestCsvRules()
{
    string text = "A,B,C,D\r\n\"hello,world\",\"say \"\"hi\"\"\",\"line1\r\nline2\",\r\n1,2,3,\r\n";
    string path = await WriteTempAsync(text, new UTF8Encoding(false));
    var result = await AnalyzeAsync(path, ',', hasHeader: true);
    AssertEqual(3L, result.TotalRowsIncludingHeader);
    AssertEqual(4, result.FieldCount);
}

static async Task TestInconsistentRows()
{
    string path = await WriteTempAsync("A,B\n1\n2,3,4\n", new UTF8Encoding(false));
    var result = await AnalyzeAsync(path, ',', hasHeader: true);
    AssertEqual(3, result.FieldCount);
}

static async Task TestCancellation()
{
    string path = await WriteTempAsync("A,B\n1,2\n", new UTF8Encoding(false));
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    try
    {
        _ = await AnalyzeAsync(path, ',', hasHeader: true, cts.Token);
        throw new InvalidOperationException("Expected cancellation.");
    }
    catch (OperationCanceledException)
    {
    }
}

static async Task TestGenerator()
{
    string path = Path.Combine(GetTempRoot(), $"{Guid.NewGuid():N}.csv");
    var generator = new LargeTestFileGenerator();
    await generator.GenerateCsvAsync(path, dataRows: 10, columns: 4, approximateBytes: null, encoding: new UTF8Encoding(false), bytesWritten: null, CancellationToken.None);
    var result = await AnalyzeAsync(path, ',', hasHeader: true);
    AssertEqual(11L, result.TotalRowsIncludingHeader);
    AssertEqual(4, result.FieldCount);
}

static async Task TestDeleteFirstDataRow()
{
    string input = await WriteTempAsync("Id,Name\r\n1,A\r\n2,B\r\n3,C\r\n", new UTF8Encoding(false));
    string output = NextTempPath();
    await DeleteRowAsync(input, output, rowNumber: 1, availableRows: 3);
    var result = await AnalyzeAsync(output, ',', hasHeader: true);
    AssertEqual(3L, result.TotalRowsIncludingHeader);
    string content = await File.ReadAllTextAsync(output);
    Assert(!content.Contains("1,A"), "First data row should be removed.");
    Assert(content.Contains("2,B"), "Remaining rows should be written.");
}

static async Task TestDeleteMiddleDataRow()
{
    string input = await WriteTempAsync("Id,Name\r\n1,A\r\n2,B\r\n3,C\r\n", new UTF8Encoding(false));
    string output = NextTempPath();
    var operation = await DeleteRowAsync(input, output, rowNumber: 2, availableRows: 3);
    Assert(operation.Succeeded, "Delete should succeed.");
    AssertEqual(1L, operation.RowsDeleted);
    string content = await File.ReadAllTextAsync(output);
    Assert(!content.Contains("2,B"), "Middle data row should be removed.");
}

static async Task TestDeleteLastDataRow()
{
    string input = await WriteTempAsync("Id,Name\r\n1,A\r\n2,B\r\n3,C", new UTF8Encoding(false));
    string output = NextTempPath();
    await DeleteRowAsync(input, output, rowNumber: 3, availableRows: 3);
    string content = await File.ReadAllTextAsync(output);
    Assert(!content.Contains("3,C"), "Last data row should be removed.");
}

static async Task TestDeleteMissingDataRow()
{
    string input = await WriteTempAsync("Id,Name\r\n1,A\r\n", new UTF8Encoding(false));
    string output = NextTempPath();

    try
    {
        await DeleteRowAsync(input, output, rowNumber: 2, availableRows: 1);
        throw new InvalidOperationException("Expected missing row failure.");
    }
    catch (ArgumentOutOfRangeException)
    {
        Assert(!File.Exists(output), "Output should not be created for invalid row.");
    }
}

static async Task TestDeleteCancellation()
{
    string input = await WriteTempAsync("Id,Name\r\n1,A\r\n2,B\r\n", new UTF8Encoding(false));
    string before = await File.ReadAllTextAsync(input);
    string output = NextTempPath();
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var request = new LargeFileEditor.Core.Models.DeleteRowRequest
    {
        InputFilePath = input,
        OutputFilePath = output,
        Delimiter = ',',
        Encoding = new UTF8Encoding(false),
        HasHeader = true,
        DataRowNumber = 1,
        AvailableDataRows = 2
    };

    var operation = await new DeleteRowService().DeleteSingleRowAsync(request, null, cts.Token);
    Assert(operation.Canceled, "Operation should report cancellation.");
    Assert(!File.Exists(output), "Incomplete output should be removed.");
    AssertEqual(before, await File.ReadAllTextAsync(input));
}

static async Task TestDeleteColumnByHeader()
{
    string input = await WriteTempAsync("Id,Name,Dept\r\n1,A,IT\r\n2,B,Sales\r\n", new UTF8Encoding(false));
    string output = NextTempPath();
    var analysis = await AnalyzeAsync(input, ',', hasHeader: true);
    await DeleteColumnAsync(input, output, analysis.Columns, header: "Id", columnIndex: null);
    AssertEqual("Name,Dept\r\nA,IT\r\nB,Sales\r\n", await File.ReadAllTextAsync(output));
}

static async Task TestDeleteColumnByPosition()
{
    string input = await WriteTempAsync("Id,Name,Dept\r\n1,A,IT\r\n", new UTF8Encoding(false));
    string output = NextTempPath();
    var analysis = await AnalyzeAsync(input, ',', hasHeader: true);
    await DeleteColumnAsync(input, output, analysis.Columns, header: null, columnIndex: 1);
    AssertEqual("Id,Dept\r\n1,IT\r\n", await File.ReadAllTextAsync(output));
}

static async Task TestDeleteFirstColumn()
{
    string input = await WriteTempAsync("A,B,C\r\n1,2,3\r\n", new UTF8Encoding(false));
    string output = NextTempPath();
    var analysis = await AnalyzeAsync(input, ',', hasHeader: true);
    await DeleteColumnAsync(input, output, analysis.Columns, header: null, columnIndex: 0);
    AssertEqual("B,C\r\n2,3\r\n", await File.ReadAllTextAsync(output));
}

static async Task TestDeleteLastColumn()
{
    string input = await WriteTempAsync("A,B,C\r\n1,2,3\r\n", new UTF8Encoding(false));
    string output = NextTempPath();
    var analysis = await AnalyzeAsync(input, ',', hasHeader: true);
    await DeleteColumnAsync(input, output, analysis.Columns, header: null, columnIndex: 2);
    AssertEqual("A,B\r\n1,2\r\n", await File.ReadAllTextAsync(output));
}

static async Task TestDuplicateHeaderRequiresPosition()
{
    string input = await WriteTempAsync("A,B,A\r\n1,2,3\r\n", new UTF8Encoding(false));
    string output = NextTempPath();
    var analysis = await AnalyzeAsync(input, ',', hasHeader: true);

    try
    {
        await DeleteColumnAsync(input, output, analysis.Columns, header: "A", columnIndex: null);
        throw new InvalidOperationException("Expected duplicate header failure.");
    }
    catch (InvalidOperationException)
    {
        Assert(!File.Exists(output), "Output should not be created when duplicate header is ambiguous.");
    }
}

static async Task TestDuplicateHeaderByPosition()
{
    string input = await WriteTempAsync("A,B,A\r\n1,2,3\r\n", new UTF8Encoding(false));
    string output = NextTempPath();
    var analysis = await AnalyzeAsync(input, ',', hasHeader: true);
    await DeleteColumnAsync(input, output, analysis.Columns, header: null, columnIndex: 2);
    AssertEqual("A,B\r\n1,2\r\n", await File.ReadAllTextAsync(output));
}

static async Task TestDeleteColumnQuotesOutput()
{
    string input = await WriteTempAsync("Id,Note,Dept\r\n1,\"hello,world\",IT\r\n2,\"say \"\"hi\"\"\",\"line\r\nbreak\"\r\n", new UTF8Encoding(false));
    string output = NextTempPath();
    var analysis = await AnalyzeAsync(input, ',', hasHeader: true);
    await DeleteColumnAsync(input, output, analysis.Columns, header: "Id", columnIndex: null);
    string content = await File.ReadAllTextAsync(output);
    Assert(content.Contains("\"hello,world\",IT"), "Comma field should remain quoted.");
    Assert(content.Contains("\"say \"\"hi\"\"\",\"line\r\nbreak\""), "Quote and newline fields should remain valid CSV.");
}

static async Task TestDeleteColumnCancellation()
{
    string input = await WriteTempAsync("A,B\r\n1,2\r\n", new UTF8Encoding(false));
    string before = await File.ReadAllTextAsync(input);
    string output = NextTempPath();
    var analysis = await AnalyzeAsync(input, ',', hasHeader: true);
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var request = new LargeFileEditor.Core.Models.DeleteColumnRequest
    {
        InputFilePath = input,
        OutputFilePath = output,
        Delimiter = ',',
        Encoding = new UTF8Encoding(false),
        HasHeader = true,
        Columns = analysis.Columns,
        HeaderName = "A"
    };

    var operation = await new DeleteColumnService().DeleteColumnAsync(request, null, cts.Token);
    Assert(operation.Canceled, "Operation should report cancellation.");
    Assert(!File.Exists(output), "Incomplete output should be removed.");
    AssertEqual(before, await File.ReadAllTextAsync(input));
}

static Task<LargeFileEditor.Core.Models.OperationResult> DeleteRowAsync(
    string input,
    string output,
    long rowNumber,
    long availableRows)
{
    var request = new LargeFileEditor.Core.Models.DeleteRowRequest
    {
        InputFilePath = input,
        OutputFilePath = output,
        Delimiter = ',',
        Encoding = new UTF8Encoding(false),
        HasHeader = true,
        DataRowNumber = rowNumber,
        AvailableDataRows = availableRows
    };

    return new DeleteRowService().DeleteSingleRowAsync(request, null, CancellationToken.None);
}

static Task<LargeFileEditor.Core.Models.OperationResult> DeleteColumnAsync(
    string input,
    string output,
    IReadOnlyList<LargeFileEditor.Core.Models.ColumnInfo> columns,
    string? header,
    int? columnIndex)
{
    var request = new LargeFileEditor.Core.Models.DeleteColumnRequest
    {
        InputFilePath = input,
        OutputFilePath = output,
        Delimiter = ',',
        Encoding = new UTF8Encoding(false),
        HasHeader = true,
        Columns = columns,
        HeaderName = header,
        ColumnIndex = columnIndex
    };

    return new DeleteColumnService().DeleteColumnAsync(request, null, CancellationToken.None);
}

static Task<LargeFileEditor.Core.Models.FileAnalysisResult> AnalyzeAsync(
    string path,
    char delimiter,
    bool hasHeader,
    CancellationToken cancellationToken = default)
{
    return new FileAnalysisService().AnalyzeAsync(path, delimiter, hasHeader, null, cancellationToken);
}

static async Task<string> WriteTempAsync(string content, Encoding encoding)
{
    string path = NextTempPath();
    await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    await using var writer = new StreamWriter(stream, encoding);
    await writer.WriteAsync(content);
    return path;
}

static string NextTempPath() => Path.Combine(GetTempRoot(), $"{Guid.NewGuid():N}.csv");

static string GetTempRoot()
{
    string root = Path.Combine(AppContext.BaseDirectory, "TestData");
    Directory.CreateDirectory(root);
    return root;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
