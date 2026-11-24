using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using AutoTest.Records.Exports;
using AutoTest.Records.Operations;
using AutoTest.Records.Queries;
using AutoTest.Records.Storage;

namespace AutoTest.Desktop.Shell;

/// <summary>Only a localized visual projection of existing immutable RECORD facts.</summary>
public sealed record RecordHistoryDisplayRow(
    RecordHistoryToolRow Source, string ProjectName, IRecordHistoryToolSource RecordSource)
{
    public string StartedLocal => Source.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string DutId => Source.DutId;
    public string ModeLabel => Source.Mode switch
    {
        "Production" => "正式检测",
        "Development" => "开发调试",
        "Diagnostic" => "独立诊断",
        _ => Source.Mode
    };
    public string VerdictLabel => Source.Verdict switch
    {
        "Pass" => "通过",
        "Fail" => "失败",
        "Error" => "错误",
        "Inconclusive" => "待确认",
        "Skipped" => "未执行",
        _ => "未知"
    };
    public string PersistenceLabel => Source.PersistenceState switch
    {
        "Committed" => "已归档",
        "Interrupted" => "执行中断",
        "Writing" => "尚未结案",
        _ => "状态未知"
    };
    public long StepFactCount => Source.StepFactCount;
    public string? FailureReason => Source.FailureReason;
}

/// <summary>
/// Read-only browser over the existing append-only RECORD database. No second
/// record model, fake history, production state changes or implicit commits.
/// </summary>
public partial class RecordHistoryWindow : Window
{
    // Several existing project databases may be compared without creating an
    // aggregate database, silently migrating records, or modifying run facts.
    private readonly Dictionary<string, IRecordHistoryToolSource> _sources =
        new(StringComparer.OrdinalIgnoreCase);
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
        if (_sources.Count >= 16)
            throw new InvalidOperationException("最多同时查看 16 个记录库。请清空来源后重新选择。");
        var absolute = Path.GetFullPath(path);
        if (_sources.ContainsKey(absolute)) return;
        if (!File.Exists(absolute))
            throw new FileNotFoundException("记录库文件不存在。", absolute);
        // Avoid interpreting an arbitrary file as an editable SQLite database.
        using (var stream = File.OpenRead(absolute))
        {
            Span<byte> signature = stackalloc byte[16];
            if (stream.Length < 100 || stream.Read(signature) != 16 ||
                !signature.SequenceEqual("SQLite format 3\0"u8))
                throw new InvalidDataException("所选文件不是有效的 SQLite 记录库。");
        }
        var directory = Path.GetDirectoryName(absolute)!;
        var database = new RecordDatabase(absolute);
        _sources.Add(absolute, new RecordHistoryToolSource(
            new RunHistoryQuery(database),
            new ProductionStatistics(database),
            new RunReportExporter(database,
                new ArtifactRepository(Path.Combine(directory, "artifacts")))));
        SourceLabel.Text = "已选择 " + _sources.Count + " 个真实工程记录库";
        HistoryGrid.ItemsSource = null;
        SelectionSummary.Text = "选择一条记录查看详情";
        FailureDetail.Text = "";
        ViewReport.IsEnabled = false;
        ExportReport.IsEnabled = false;
    }

    private static string ProjectLabel(string databasePath)
    {
        var folder = Path.GetDirectoryName(databasePath)!;
        if (string.Equals(Path.GetFileName(folder), "test-records", StringComparison.OrdinalIgnoreCase))
            folder = Path.GetDirectoryName(folder)!;
        return Path.GetFileName(folder);
    }

    private void ClearSources_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _sources.Clear();
        HistoryGrid.ItemsSource = null;
        SourceLabel.Text = "尚未选择记录库";
        SelectionSummary.Text = "选择一条记录查看详情";
        FailureDetail.Text = "";
        ViewReport.IsEnabled = ExportReport.IsEnabled = false;
        StatusText.Text = "已清空查询来源；原始 SQLite 文件及记录均未修改。";
    }

    private async void DutSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await RefreshAsync();
    }

    private async void ChooseDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var picker = new OpenFileDialog
        {
            Title = "选择已有的 ECU 检测记录库",
            Filter = "AutoTest SQLite 记录库 (*.sqlite)|*.sqlite|全部文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            foreach (var file in picker.FileNames)
                SetSource(file);
            await RefreshAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or InvalidOperationException or
                                       Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
        {
            StatusText.Text = "记录库无法打开：" + error.Message;
        }
    }

    private async void Query_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy || _sources.Count == 0) return;
        _busy = true;
        try
        {
            StatusText.Text = "正在读取真实检测记录…";
            var mode = (ModeFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
            var rows = new List<RecordHistoryDisplayRow>();
            var failed = new List<string>();
            foreach (var (path, source) in _sources)
            {
                try
                {
                    var found = await source.QueryAsync(DutSearch.Text, mode, 500);
                    rows.AddRange(found.Select(row =>
                        new RecordHistoryDisplayRow(row, ProjectLabel(path), source)));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                    InvalidDataException or ArgumentException or InvalidOperationException or
                    Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
                {
                    failed.Add(ProjectLabel(path) + "：" + error.Message);
                }
            }
            var visible = rows.OrderByDescending(row => row.Source.StartedUtc)
                .ThenBy(row => row.Source.RunId, StringComparer.Ordinal).Take(500).ToArray();
            HistoryGrid.ItemsSource = visible;
            SelectionSummary.Text = visible.Length == 0
                ? "未找到符合条件的归档记录" : "选择一条记录查看详情";
            FailureDetail.Text = "";
            ViewReport.IsEnabled = false;
            ExportReport.IsEnabled = false;
            StatusText.Text = "查询完成：" + visible.Length + " 条记录，来自 " +
                (_sources.Count - failed.Count) + " 个记录库（最多显示最近 500 条）。" +
                (failed.Count > 0 ? " 有 " + failed.Count + " 个来源读取失败：" +
                    string.Join("；", failed.Take(2)) : "") +
                " 未结案或中断记录不视为通过。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or
                                       InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or
                                       System.Text.Json.JsonException)
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
        var row = (HistoryGrid.SelectedItem as RecordHistoryDisplayRow)?.Source;
        ViewReport.IsEnabled = ExportReport.IsEnabled = row is not null && !_busy;
        if (row is null)
        {
            SelectionSummary.Text = "选择一条记录查看详情";
            FailureDetail.Text = "";
            return;
        }
        var display = (RecordHistoryDisplayRow)HistoryGrid.SelectedItem;
        SelectionSummary.Text = row.DutId + " · " + display.StartedLocal +
            " · " + display.VerdictLabel + " · " + display.PersistenceLabel +
            " · 步骤事实 " + row.StepFactCount;
        FailureDetail.Text = row.FailureReason ?? "";
    }

    private async void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || HistoryGrid.SelectedItem is not RecordHistoryDisplayRow selection) return;
        var row = selection.Source;
        _busy = true;
        try
        {
            StatusText.Text = "正在核对证据并生成报告…";
            var html = await selection.RecordSource.ExportFullReportHtmlAsync(row.RunId);
            var folder = Path.Combine(Path.GetTempPath(), "AutoTest", "Reports");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, row.RunId + ".html");
            await File.WriteAllTextAsync(path, html);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusText.Text = "已在默认浏览器打开所选检测报告；报告以真实归档事实生成。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or
                                       InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or
                                       System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "报告无法打开：" + error.Message;
        }
        finally { _busy = false; }
    }

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || HistoryGrid.SelectedItem is not RecordHistoryDisplayRow selection) return;
        var row = selection.Source;
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
            await selection.RecordSource.ExportReportAsync(row.RunId, picker.FileName, picker.FilterIndex == 1);
            StatusText.Text = "报告已导出：" + picker.FileName;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or
                                       InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or
                                       System.Text.Json.JsonException)
        {
            StatusText.Text = "报告导出失败，原始记录未改动：" + error.Message;
        }
        finally { _busy = false; }
    }
}
