using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AutoTest.Records.Exports;
using AutoTest.Records.Operations;
using AutoTest.Records.Queries;
using AutoTest.Records.Storage;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Read-only browser over the existing append-only RECORD database. No second
/// record model, fake history, production state changes or implicit commits.
/// </summary>
public partial class RecordHistoryWindow : Window
{
    private IRecordHistoryToolSource? _source;
    private string? _databasePath;
    private bool _busy;

    public RecordHistoryWindow(string? projectRoot)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var candidate = Path.Combine(projectRoot, "test-records", "r1-records.sqlite");
            if (File.Exists(candidate)) SetSource(candidate);
            else StatusText.Text = "当前工程尚无持久化记录。可以选择已有的记录库，或在完成真实检测后重新查询。";
        }
        Loaded += async (_, _) => await RefreshAsync();
    }

    private void SetSource(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("记录库文件不存在。", path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var database = new RecordDatabase(path);
        _source = new RecordHistoryToolSource(
            new RunHistoryQuery(database),
            new ProductionStatistics(database),
            new RunReportExporter(database,
                new ArtifactRepository(Path.Combine(directory, "artifacts"))));
        _databasePath = database.DatabasePath;
        SourceLabel.Text = "记录库：" + _databasePath;
        HistoryGrid.ItemsSource = null;
        SelectionSummary.Text = "选择一条记录查看详情";
        FailureDetail.Text = "";
        ViewReport.IsEnabled = false;
        ExportReport.IsEnabled = false;
    }

    private async void ChooseDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var picker = new OpenFileDialog
        {
            Title = "选择已有的 ECU 检测记录库",
            Filter = "AutoTest SQLite 记录库 (*.sqlite)|*.sqlite|全部文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            SetSource(picker.FileName);
            await RefreshAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException)
        {
            StatusText.Text = "记录库无法打开：" + error.Message;
        }
    }

    private async void Query_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy || _source is null) return;
        _busy = true;
        try
        {
            StatusText.Text = "正在读取真实检测记录…";
            var mode = (ModeFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
            var rows = await _source.QueryAsync(DutSearch.Text, mode, 500);
            HistoryGrid.ItemsSource = rows;
            SelectionSummary.Text = rows.Count == 0
                ? "未找到符合条件的归档记录" : "选择一条记录查看详情";
            FailureDetail.Text = "";
            ViewReport.IsEnabled = false;
            ExportReport.IsEnabled = false;
            StatusText.Text = "查询完成：" + rows.Count + " 次检测记录（最多显示最近 500 条）。" +
                " 记录仅来自已有持久化事实；未提交和中断记录不视为通过。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or
                                       InvalidOperationException)
        {
            StatusText.Text = "查询失败，未修改原始记录：" + error.Message;
            HistoryGrid.ItemsSource = null;
            ViewReport.IsEnabled = false;
            ExportReport.IsEnabled = false;
        }
        finally { _busy = false; }
    }

    private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = HistoryGrid.SelectedItem as RecordHistoryToolRow;
        ViewReport.IsEnabled = ExportReport.IsEnabled = row is not null && !_busy;
        if (row is null)
        {
            SelectionSummary.Text = "选择一条记录查看详情";
            FailureDetail.Text = "";
            return;
        }
        SelectionSummary.Text = row.DutId + " · " +
            row.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
            " · " + row.Verdict + " · " + row.PersistenceState +
            " · 步骤事实 " + row.StepFactCount;
        FailureDetail.Text = row.FailureReason ?? "";
    }

    private async void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _source is null || HistoryGrid.SelectedItem is not RecordHistoryToolRow row) return;
        _busy = true;
        try
        {
            StatusText.Text = "正在核对证据并生成报告…";
            var html = await _source.ExportFullReportHtmlAsync(row.RunId);
            var folder = Path.Combine(Path.GetTempPath(), "AutoTest", "Reports");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, row.RunId + ".html");
            await File.WriteAllTextAsync(path, html);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusText.Text = "已在默认浏览器打开所选检测报告；报告以真实归档事实生成。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or
                                       InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "报告无法打开：" + error.Message;
        }
        finally { _busy = false; }
    }

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _source is null || HistoryGrid.SelectedItem is not RecordHistoryToolRow row) return;
        var picker = new SaveFileDialog
        {
            Title = "导出所选 ECU 检测报告",
            FileName = "ECU_" + string.Concat(row.DutId.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) +
                "_" + row.StartedUtc.ToLocalTime().ToString("yyyyMMdd_HHmmss"),
            Filter = "HTML 完整报告 (*.html)|*.html|JSON 证据报告 (*.json)|*.json",
            AddExtension = true,
            OverwritePrompt = false
        };
        if (picker.ShowDialog(this) != true) return;
        if (File.Exists(picker.FileName))
        {
            StatusText.Text = "同名报告已经存在。请选择其他文件名，原报告不会被覆盖。";
            return;
        }
        _busy = true;
        try
        {
            await _source.ExportReportAsync(row.RunId, picker.FileName, picker.FilterIndex == 1);
            StatusText.Text = "报告已导出：" + picker.FileName;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or
                                       InvalidOperationException)
        {
            StatusText.Text = "报告导出失败，原始记录未改动：" + error.Message;
        }
        finally { _busy = false; }
    }
}
