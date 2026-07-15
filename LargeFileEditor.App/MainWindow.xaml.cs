using System.Windows;
using System.IO;
using LargeFileEditor.Core.Models;
using LargeFileEditor.Core.Services;
using Microsoft.Win32;

namespace LargeFileEditor.App;

public partial class MainWindow : Window
{
    private readonly FileAnalysisService _fileAnalysisService = new();
    private readonly DeleteRowService _deleteRowService = new();
    private CancellationTokenSource? _currentOperation;
    private FileAnalysisResult? _currentResult;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "選擇大型資料檔案",
            Filter = "文字資料檔案|*.csv;*.tsv;*.txt;*.dat|CSV|*.csv|TSV|*.tsv|所有檔案|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            FilePathTextBox.Text = dialog.FileName;
            OutputPathTextBox.Text = GenerateDefaultOutputPath(dialog.FileName);
            await StartScanAsync(dialog.FileName);
        }
    }

    private async void RescanButton_Click(object sender, RoutedEventArgs e)
    {
        await StartScanAsync(FilePathTextBox.Text);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _currentOperation?.Cancel();
    }

    private void DelimiterComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CustomDelimiterTextBox is not null)
        {
            CustomDelimiterTextBox.IsEnabled = GetSelectedDelimiterTag() == "custom";
        }
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "選擇輸出檔案",
            Filter = "CSV|*.csv|TSV|*.tsv|文字檔|*.txt|所有檔案|*.*",
            FileName = Path.GetFileName(OutputPathTextBox.Text)
        };

        string? directory = Path.GetDirectoryName(OutputPathTextBox.Text);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog(this) == true)
        {
            OutputPathTextBox.Text = dialog.FileName;
        }
    }

    private async void DeleteRowButton_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSingleRowAsync();
    }

    private async Task StartScanAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            MessageBox.Show(this, "請先選擇檔案。", "無法掃描", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryGetDelimiter(out char delimiter))
        {
            MessageBox.Show(this, "請輸入單一字元分隔符號。", "分隔符號錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _currentOperation?.Dispose();
        _currentOperation = new CancellationTokenSource();
        SetBusy(true);
        AddLog("開始掃描檔案");

        var progress = new Progress<ProgressInfo>(UpdateProgress);

        try
        {
            var result = await _fileAnalysisService.AnalyzeAsync(
                filePath,
                delimiter,
                HasHeaderCheckBox.IsChecked == true,
                progress,
                _currentOperation.Token);

            _currentResult = result;
            UpdateResult(result);
            if (string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
            {
                OutputPathTextBox.Text = GenerateDefaultOutputPath(result.FilePath);
            }

            AddLog($"掃描完成，共 {result.TotalRowsIncludingHeader:N0} 筆邏輯資料列、{result.FieldCount:N0} 個欄位");
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "狀態：掃描未完成";
            AddLog("掃描已取消，未使用不完整統計結果");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "狀態：掃描失敗";
            AddLog($"掃描失敗：{ex.Message}");
            MessageBox.Show(this, $"掃描失敗：{ex.Message}\n\n原始檔未被修改。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteSingleRowAsync()
    {
        if (_currentResult is null)
        {
            MessageBox.Show(this, "請先完成檔案掃描。", "無法刪除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!long.TryParse(DeleteRowNumberTextBox.Text, out long rowNumber) || rowNumber <= 0)
        {
            MessageBox.Show(this, "資料列編號必須是大於 0 的整數。", "列號錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (rowNumber > _currentResult.DataRows)
        {
            MessageBox.Show(this, "指定列超過實際資料列數，原始檔不會被修改。", "列號錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
        {
            OutputPathTextBox.Text = GenerateDefaultOutputPath(_currentResult.FilePath);
        }

        var request = new DeleteRowRequest
        {
            InputFilePath = _currentResult.FilePath,
            OutputFilePath = OutputPathTextBox.Text,
            Delimiter = _currentResult.Delimiter,
            Encoding = _currentResult.Encoding,
            HasHeader = _currentResult.HasHeader,
            DataRowNumber = rowNumber,
            AvailableDataRows = _currentResult.DataRows
        };

        _currentOperation?.Dispose();
        _currentOperation = new CancellationTokenSource();
        SetBusy(true);
        AddLog($"開始刪除第 {rowNumber:N0} 筆資料列");

        try
        {
            var progress = new Progress<ProgressInfo>(UpdateProgress);
            OperationResult operation = await _deleteRowService.DeleteSingleRowAsync(request, progress, _currentOperation.Token);

            if (operation.Canceled)
            {
                StatusTextBlock.Text = "狀態：刪除已取消";
                AddLog(operation.Message);
                return;
            }

            AddLog($"刪除完成，輸出檔案：{operation.OutputFilePath}");
            AddLog($"檔案大小變化：{FileAnalysisService.FormatFileSize(operation.OriginalSizeBytes)} → {FileAnalysisService.FormatFileSize(operation.NewSizeBytes)}");

            OutputPathTextBox.Text = GenerateDefaultOutputPath(operation.OutputFilePath);
            await StartScanAsync(operation.OutputFilePath);
            AddLog($"重新掃描完成，目前共 {_currentResult?.DataRows:N0} 筆資料");
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "狀態：刪除已取消";
            AddLog("刪除資料列已取消，原始檔未被修改");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "狀態：刪除失敗";
            AddLog($"刪除失敗：{ex.Message}");
            MessageBox.Show(this, $"刪除失敗：{ex.Message}\n\n原始檔未被修改。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        bool hasFile = !string.IsNullOrWhiteSpace(FilePathTextBox.Text);
        bool hasResult = _currentResult is not null;

        SelectFileButton.IsEnabled = !isBusy;
        RescanButton.IsEnabled = !isBusy && hasFile;
        DelimiterComboBox.IsEnabled = !isBusy;
        CustomDelimiterTextBox.IsEnabled = !isBusy && GetSelectedDelimiterTag() == "custom";
        HasHeaderCheckBox.IsEnabled = !isBusy;
        CancelButton.IsEnabled = isBusy;
        BrowseOutputButton.IsEnabled = !isBusy && hasResult;
        DeleteRowButton.IsEnabled = !isBusy && hasResult;
        DeleteRowNumberTextBox.IsEnabled = !isBusy && hasResult;
        OutputPathTextBox.IsEnabled = !isBusy && hasResult;
    }

    private void UpdateProgress(ProgressInfo progress)
    {
        ProgressBar.Value = progress.Percent;
        ProgressTextBlock.Text = $"{progress.BytesRead:N0} / {progress.TotalBytes:N0} bytes ({progress.Percent:N2}%)，邏輯列 {progress.LogicalRowsScanned:N0}";
        StatusTextBlock.Text = $"狀態：{progress.Status}";
    }

    private void UpdateResult(FileAnalysisResult result)
    {
        FilePathTextBox.Text = result.FilePath;
        FileNameTextBlock.Text = $"檔案名稱：{result.FileName}";
        FileSizeTextBlock.Text = $"檔案大小：{result.FileSizeText}";
        CreatedAtTextBlock.Text = $"建立時間：{result.CreatedAt:yyyy-MM-dd HH:mm:ss}";
        ModifiedAtTextBlock.Text = $"最後修改：{result.ModifiedAt:yyyy-MM-dd HH:mm:ss}";
        EncodingTextBlock.Text = $"文字編碼：{result.EncodingName}";
        NewLineTextBlock.Text = $"換行格式：{result.NewLineFormat}";
        DelimiterTextBlock.Text = $"分隔符號：{result.DelimiterName}";
        HeaderTextBlock.Text = $"標題列：{(result.HasHeader ? "第一列是欄位標題" : "無標題列，使用暫時欄名")}";
        RowsIncludingHeaderTextBlock.Text = $"資料列數（含標題）：{result.TotalRowsIncludingHeader:N0}";
        DataRowsTextBlock.Text = result.HasHeader
            ? $"資料列數（不含標題）：{result.DataRows:N0}"
            : "資料列數（不含標題）：不適用";
        FieldCountTextBlock.Text = $"欄位數：{result.FieldCount:N0}";
        StatusTextBlock.Text = $"狀態：{result.StatusMessage}";
        ColumnsListView.ItemsSource = result.Columns;
    }

    private bool TryGetDelimiter(out char delimiter)
    {
        string tag = GetSelectedDelimiterTag();
        if (tag == "custom")
        {
            string text = CustomDelimiterTextBox.Text;
            if (text.Length == 1)
            {
                delimiter = text[0];
                return true;
            }

            delimiter = default;
            return false;
        }

        delimiter = tag.Length == 1 ? tag[0] : '\t';
        return true;
    }

    private string GetSelectedDelimiterTag()
    {
        if (DelimiterComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
        {
            return tag;
        }

        return ",";
    }

    private static string GenerateDefaultOutputPath(string inputPath)
    {
        string directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string extension = Path.GetExtension(inputPath);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(directory, $"{name}.delete-row-{stamp}{extension}");
    }

    private void AddLog(string message)
    {
        LogListBox.Items.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
    }
}
