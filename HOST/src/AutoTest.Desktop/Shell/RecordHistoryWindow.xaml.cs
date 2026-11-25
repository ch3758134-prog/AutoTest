using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
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
    // Even a stored verdict must not appear as a FINAL PASS until the
    // append-only record is durably committed.
    public string VerdictLabel => Source.PersistenceState != "Committed"
        ? Source.PersistenceState == "Writing" ? "未结案" : "中断／未结案"
        : Source.Verdict switch
        {
            "Pass" => Source.Mode switch
            {
                "Production" => "判定通过",
                "Development" => "调试通过",
                "Diagnostic" => "诊断通过",
                _ => "已通过"
            },
            "Fail" => Source.Mode switch
            {
                "Production" => "判定失败",
                "Development" => "调试失败",
                "Diagnostic" => "诊断失败",
                _ => "失败"
            },
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
/// A row is an immutable view of a real exported StepOccurrence, never a
/// second TEST result or local re-interpretation of the recorded verdict.
/// </summary>
public sealed record ArchivedStepDisplayRow(
    int Number, string Path, string State, string Actual, string Expected,
    string Quality, string When, string Occurrence, string Evidence, string Detail)
{
    public string StepLabel => string.IsNullOrWhiteSpace(Path) ? "步骤 " + Number : Path;
    public string FailureHint => string.IsNullOrWhiteSpace(Detail) ? "" : Detail;
}
public sealed record ArchivedEventDisplayRow(
    int Number, string When, string Kind, string Description, string Quality);

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
    private int _detailRequestRevision;
    private CancellationTokenSource? _detailCancellation;
    private CancellationTokenSource? _batchCancellation;
    private bool _batchRunning;
 

    public RecordHistoryWindow(string? projectRoot)
    {
        InitializeComponent();
        Loaded += (_, _) => AutoTest.Desktop.Shell.DesktopWindowSizing.FitToVisibleWorkspace(this);
        StepFactsGrid.ItemsSource = null;
        EventFactsGrid.ItemsSource = null;
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var candidate = Path.Combine(projectRoot, "test-records", "r1-records.sqlite");
            if (File.Exists(candidate)) SetSource(candidate);
            else StatusText.Text = "当前工程尚无持久化记录。可以选择已有的记录库，或在完成真实检测后重新查询。";
        }
        Loaded += async (_, _) => await RefreshAsync();
        Closing += (_, e) =>
        {
            if (!_batchRunning) return;
            e.Cancel = true;
            _batchCancellation?.Cancel();
            StatusText.Text = "报告打包仍在清理；取消完成后可重新关闭窗口。";
        };
        Closed += (_, _) =>
        {
            _batchCancellation?.Cancel();
            _detailCancellation?.Cancel();
            _detailCancellation = null;
        };
    }

    private void SetSource(string path)
    {
        var absolute = Path.GetFullPath(path);
        if (_sources.ContainsKey(absolute)) return;
        if (_sources.Count >= 16)
            throw new InvalidOperationException("最多同时查看 16 个记录库。请清空来源后重新选择。");
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
        // RecordDatabase's normal writer is allowed to migrate an empty DB.
        // Never invoke that path on an arbitrary SQLite file selected by the
        // operator. Validate the *existing* RECORD schema in read-only mode
        // before attaching a query source.
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = absolute,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly
            }.ToString()))
        {
            connection.Open();
            using var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            var currentVersion = Convert.ToInt32(version.ExecuteScalar());
            if (currentVersion != RecordDatabase.CurrentSchemaVersion)
                throw new InvalidDataException("所选 SQLite 文件不是支持的 AutoTest RECORD 版本。");
            using var tables = connection.CreateCommand();
            tables.CommandText = @"SELECT COUNT(*) FROM sqlite_master
                WHERE type='table' AND name IN
                ('run_headers','run_facts','run_step_facts','artifact_links');";
            if (Convert.ToInt32(tables.ExecuteScalar()) != 4)
                throw new InvalidDataException("所选文件缺少正式 RECORD 归档表。");
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
        ClearSelectedFacts();
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
        ClearSelectedFacts();
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
                    // The underlying SQLite reader uses short-lived connections.
                    // Keep all disk/query processing off the WPF dispatcher.
                    var dut = DutSearch.Text;
                    var found = await Task.Run(
                        async () => await source.QueryAsync(dut, mode, 500));
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
            // One DUT can own multiple committed or interrupted attempts.
            // Group the existing immutable query rows by their real DUT ID;
            // do not infer a new UUT from each scan or merge separate Runs.
            var grouped = CollectionViewSource.GetDefaultView(visible);
            grouped.GroupDescriptions?.Add(
                new PropertyGroupDescription(nameof(RecordHistoryDisplayRow.DutId)));
            HistoryGrid.ItemsSource = grouped;
            ClearSelectedFacts();
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
            ClearSelectedFacts();
            ViewReport.IsEnabled = false;
            ExportReport.IsEnabled = false;
        }
        finally
        {
            _busy = false;
            UpdateBatchExportAvailability();
        }
    }

    private sealed record RealReportPreview(
        IReadOnlyList<ArchivedStepDisplayRow> Steps,
        IReadOnlyList<ArchivedEventDisplayRow> Events,
        string IntegrityText, string Summary);

    private void ClearSelectedFacts()
    {
        _detailRequestRevision++;
        _detailCancellation?.Cancel();
        _detailCancellation = null;
        StepFactsGrid.ItemsSource = null;
        EventFactsGrid.ItemsSource = null;
        DetailsStatus.Text = "选择一次运行，查看真实步骤、测量、事件和证据";
        StepDetailText.Text = "选择一个步骤可查看 Occurrence、证据 ID 和失败说明";
        EvidenceIntegrity.Text = "尚未选择记录；不能从空白或未结案记录推断测试通过。";
    }

    private static string JsonField(JsonElement row, string key)
    {
        if (row.ValueKind != JsonValueKind.Object) return "";
        if (!row.TryGetProperty(key, out var value))
        {
            // Exporter anonymous rows use camelCase, but strong RECORD DTOs
            // like ReportCaptureHealth serialize as PascalCase by default.
            var pascal = char.ToUpperInvariant(key[0]) + key[1..];
            if (!row.TryGetProperty(pascal, out value)) return "";
        }
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static string JsonList(JsonElement row, string key)
    {
        if (row.ValueKind != JsonValueKind.Object ||
            !row.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
            return "";
        return string.Join("；", value.EnumerateArray().Select(x =>
            x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string LocalDate(string value) =>
        DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp.ToLocalTime().ToString("MM-dd HH:mm:ss.fff")
            : value;

    // Read the already-exported canonical REPORT payload. Only project a
    // bounded preview into WPF; full rows and artifact hashes remain in the
    // original immutable source and the existing HTML/JSON report.
    private static RealReportPreview ParseRealReport(string raw, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(raw);
        var report = document.RootElement;
        var steps = new List<ArchivedStepDisplayRow>();
        var events = new List<ArchivedEventDisplayRow>();
        const int maxPreview = 1200;
        var totalSteps = 0;
        var totalEvents = 0;
        if (report.TryGetProperty("stepFacts", out var stepFacts) &&
            stepFacts.ValueKind == JsonValueKind.Array)
        {
            totalSteps = stepFacts.GetArrayLength();
            foreach (var row in stepFacts.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (steps.Count == maxPreview) break;
                var path = JsonField(row, "path");
                if (string.IsNullOrWhiteSpace(path)) path = JsonField(row, "stepId");
                var failureCode = JsonField(row, "failureCode");
                var detail = JsonField(row, "detail");
                if (!string.IsNullOrWhiteSpace(failureCode))
                    detail = failureCode + (string.IsNullOrWhiteSpace(detail) ? "" : " · " + detail);
                steps.Add(new ArchivedStepDisplayRow(
                    steps.Count + 1, path,
                    JsonField(row, "disposition"),
                    JsonField(row, "actual"),
                    JsonField(row, "expected"),
                    JsonField(row, "evidenceQuality") + " / " + JsonField(row, "timeQuality"),
                    LocalDate(JsonField(row, "timestamp")),
                    JsonField(row, "occurrenceId"),
                    JsonList(row, "evidenceIds"), detail));
            }
        }
        if (report.TryGetProperty("facts", out var facts) &&
            facts.ValueKind == JsonValueKind.Array)
        {
            totalEvents = facts.GetArrayLength();
            foreach (var row in facts.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (events.Count == maxPreview) break;
                events.Add(new ArchivedEventDisplayRow(
                    events.Count + 1, LocalDate(JsonField(row, "timestamp")),
                    JsonField(row, "kind"), JsonField(row, "description"),
                    JsonField(row, "evidenceQuality")));
            }
        }
        var archiveStatus = JsonField(report, "evidenceArchiveIncomplete");
        var hasArchiveStatus = bool.TryParse(archiveStatus, out var incomplete);
        var unarchived = JsonList(report, "unarchivedEvidenceIds");
        var capture = report.TryGetProperty("captureHealth", out var health) &&
            health.ValueKind == JsonValueKind.Object ? health : default;
        var captureComplete = JsonField(capture, "recordingComplete");
        var dropped = JsonField(capture, "droppedEvents");
        var reason = JsonField(capture, "incompleteReason");
        var artifacts = report.TryGetProperty("artifacts", out var a) &&
            a.ValueKind == JsonValueKind.Array ? a.GetArrayLength() : 0;
        var integrity = !hasArchiveStatus
            ? "原始报告缺少证据完整性字段，状态未知；不得将记录判定视为实体生产合格。"
            : incomplete
                ? "报告确认：证据归档不完整。缺失的 CAN/DBC/波形事实不可用于证明 ECU 合格。"
                : "归档附件检查未发现缺项；还须确认本次采集完整、测试判据和正式运行模式。";
        integrity += "\n采集完整标记：" + (captureComplete.Length == 0 ? "未提供" : captureComplete) +
            "；丢失事件：" + (dropped.Length == 0 ? "未知" : dropped) +
            "；归档附件：" + artifacts + " 项。";
        if (!string.Equals(captureComplete, "True", StringComparison.OrdinalIgnoreCase))
            integrity += "\n采集未证明完整：即使原始 Verdict=Pass，也不得由此推断生产放行。";
        if (!string.IsNullOrWhiteSpace(reason)) integrity += "\n采集问题：" + reason;
        if (!string.IsNullOrWhiteSpace(unarchived)) integrity +=
            "\n未归档证据 ID：" + unarchived;
        integrity += "\n正式 Inspection 是否通过只能由当前记录的真实运行模式、提交状态和完整验收事实决定。";
        var summary = "真实步骤 " + totalSteps + " 条，原始事件 " + totalEvents +
            " 条；窗口预览每类最多 " + maxPreview + " 条，完整数据请打开原始报告。";
        return new RealReportPreview(steps, events, integrity, summary);
    }

    private async void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ClearSelectedFacts();
        UpdateBatchExportAvailability();
        if (HistoryGrid.SelectedItem is not RecordHistoryDisplayRow display)
        {
            ViewReport.IsEnabled = ExportReport.IsEnabled = false;
            var multiCount = HistoryGrid.SelectedItems.OfType<RecordHistoryDisplayRow>().Count();
            SelectionSummary.Text = multiCount >= 2
                ? "已多选 " + multiCount + " 次记录；可对已归档记录批量打包"
                : "选择一条记录查看详情";
            FailureDetail.Text = "";
            return;
        }
        var row = display.Source;
        ViewReport.IsEnabled = ExportReport.IsEnabled = !_busy;
        ViewReport.Content = row.PersistenceState == "Committed"
            ? "查看归档报告" : "查看未结案事实";
        ExportReport.Content = row.PersistenceState == "Committed"
            ? "导出归档报告…" : "导出未结案资料…";
        SelectionSummary.Text = row.DutId + " · " + display.StartedLocal +
            " · " + display.ModeLabel + " · " + display.VerdictLabel +
            " · " + display.PersistenceLabel + " · 步骤事实 " + row.StepFactCount;
        FailureDetail.Text = row.FailureReason ?? "";
        DetailsStatus.Text = "正在从原始归档核对逐步骤与事件数据…";
        var version = _detailRequestRevision;
        var cancellation = new CancellationTokenSource();
        _detailCancellation = cancellation;
        try
        {
            var json = await Task.Run(
                async () => await display.RecordSource.ExportFullReportJsonAsync(
                    row.RunId, cancellation.Token), cancellation.Token);
            var parsed = await Task.Run(() => ParseRealReport(json, cancellation.Token),
                cancellation.Token);
            if (version != _detailRequestRevision || cancellation.IsCancellationRequested ||
                !ReferenceEquals(HistoryGrid.SelectedItem, display)) return;
            // One immutable virtualized ItemsSource replacement rather than
            // up to 2,400 collection notifications on a selected run.
            StepFactsGrid.ItemsSource = parsed.Steps;
            EventFactsGrid.ItemsSource = parsed.Events;
            EvidenceIntegrity.Text = parsed.IntegrityText;
            DetailsStatus.Text = parsed.Summary;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or InvalidOperationException or
            Microsoft.Data.Sqlite.SqliteException or JsonException or KeyNotFoundException)
        {
            if (version == _detailRequestRevision)
            {
                DetailsStatus.Text = "本次记录明细无法核验：" + error.Message;
                EvidenceIntegrity.Text = "明细无法核验；请勿将当前信息视为完整证据。";
            }
        }
        finally
        {
            if (ReferenceEquals(_detailCancellation, cancellation))
                _detailCancellation = null;
            cancellation.Dispose();
        }
    }

    private void HistoryGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-click a concrete record, never a header, blank area, or a
        // multiple-selection batch. Explicit buttons still support keyboard use.
        if (_busy || e.ChangedButton != MouseButton.Left ||
            HistoryGrid.SelectedItems.Count != 1 ||
            e.OriginalSource is not FrameworkElement
            { DataContext: RecordHistoryDisplayRow row } ||
            !ReferenceEquals(HistoryGrid.SelectedItem, row)) return;
        e.Handled = true;
        OpenReport_Click(this, new RoutedEventArgs());
    }

    private void StepFactsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        StepDetailText.Text = StepFactsGrid.SelectedItem is not ArchivedStepDisplayRow row
            ? "选择一个步骤可查看 Occurrence、证据 ID 和失败说明"
            : "步骤 " + row.Number + " · Occurrence：" +
              (string.IsNullOrEmpty(row.Occurrence) ? "未记录" : row.Occurrence) +
              " · 证据：" + (string.IsNullOrWhiteSpace(row.Evidence) ? "未引用" : row.Evidence) +
              (string.IsNullOrWhiteSpace(row.Detail) ? "" : " · " + row.Detail);
    }

    private RecordHistoryDisplayRow[] SelectedCommittedRuns() =>
        HistoryGrid.SelectedItems.OfType<RecordHistoryDisplayRow>()
            .Where(x => x.Source.PersistenceState == "Committed").ToArray();

    private void UpdateBatchExportAvailability()
    {
        BatchExportReport.IsEnabled = !_busy && !_batchRunning &&
            SelectedCommittedRuns().Length >= 2;
    }

    private void CancelBatchExport_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning)
        {
            _batchCancellation?.Cancel();
            StatusText.Text = "正在取消当前打包并清理临时文件…";
        }
    }

    private static string SafeArchiveSegment(string raw, int maxLength = 35)
    {
        var clean = new string((raw ?? "").Where(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_').Take(maxLength).ToArray());
        return clean.Length == 0 ? "UNKNOWN" : clean;
    }

    private async void BatchExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _batchRunning) return;
        var selected = SelectedCommittedRuns();
        if (selected.Length < 2) return;
        var ignored = HistoryGrid.SelectedItems.Count - selected.Length;
        var picker = new SaveFileDialog
        {
            Title = "批量打包已归档的真实检测报告",
            FileName = "AutoTest_Reports_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            Filter = "HTML 报告 ZIP (*.zip)|*.zip|JSON 证据 ZIP (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = false
        };
        if (picker.ShowDialog(this) != true) return;
        var destination = Path.GetFullPath(picker.FileName);
        if (File.Exists(destination))
        {
            StatusText.Text = "目标 ZIP 已存在。为保护之前的报告，请选择一个新文件名。";
            return;
        }
        var isHtml = picker.FilterIndex == 1;
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        var cancellation = new CancellationTokenSource();
        _batchCancellation = cancellation;
        _batchRunning = _busy = true;
        HistoryGrid.IsEnabled = false;
        CancelBatchExport.Visibility = Visibility.Visible;
        UpdateBatchExportAvailability();
        IProgress<int> progress = new Progress<int>(done =>
        {
            if (_batchRunning && ReferenceEquals(_batchCancellation, cancellation) &&
                !cancellation.IsCancellationRequested)
                StatusText.Text = "已处理 " + done + " / " + selected.Length +
                    " 条真实归档记录…";
        });
        try
        {
            StatusText.Text = "正在批量读取真实 RECORD 并创建归档包…";
            await Task.Run(async () =>
            {
                await using var stream = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create,
                           leaveOpen: true))
                {
                    var indexedReports = new List<object>(selected.Length);
                    for (var i = 0; i < selected.Length; i++)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        var source = selected[i];
                        var run = source.Source;
                        var original = isHtml
                            ? await source.RecordSource.ExportFullReportHtmlAsync(
                                run.RunId, cancellation.Token)
                            : await source.RecordSource.ExportFullReportJsonAsync(
                                run.RunId, cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        var extension = isHtml ? ".html" : ".json";
                        var name = (i + 1).ToString("D3") + "_" +
                            SafeArchiveSegment(run.DutId) + "_" +
                            SafeArchiveSegment(run.RunId) + extension;
                        var bytes = Encoding.UTF8.GetBytes(original);
                        var item = archive.CreateEntry(name, CompressionLevel.Optimal);
                        using (var entry = item.Open())
                            await entry.WriteAsync(bytes.AsMemory(), cancellation.Token);
                        // The manifest is an INDEX of the original real reports,
                        // not a second judgement or production release document.
                        indexedReports.Add(new
                        {
                            file = name,
                            sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                            runId = run.RunId,
                            dutId = run.DutId,
                            projectSource = source.ProjectName,
                            mode = run.Mode,
                            persistence = run.PersistenceState,
                            sourceVerdict = run.Verdict,
                            startedUtc = run.StartedUtc
                        });
                        progress.Report(i + 1);
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    var manifest = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    using (var entry = manifest.Open())
                    {
                        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            schema = 1,
                            createdUtc = DateTimeOffset.UtcNow,
                            format = isHtml ? "HTML" : "JSON",
                            reportCount = indexedReports.Count,
                            note = "本文件为 RECORD 原始归档报告的可核对索引，不是额外生产放行结论。",
                            reports = indexedReports
                        }, new JsonSerializerOptions { WriteIndented = true });
                        await entry.WriteAsync(manifestBytes.AsMemory(), cancellation.Token);
                    }
                }
                await stream.FlushAsync(cancellation.Token);
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            // The completed package is published only after every original
            // report was read and the ZIP central directory was finalized.
            if (File.Exists(destination))
                throw new IOException("目标文件在打包期间已由其他进程创建，未覆盖。");
            File.Move(temporary, destination);
            StatusText.Text = "已打包 " + selected.Length + " 份已归档真实报告：" +
                destination + (ignored > 0
                    ? "；另有 " + ignored + " 条未结案记录未纳入正式打包。" : "");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText.Text = "批量打包已取消；没有发布不完整报告。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or ArgumentException or
            Microsoft.Data.Sqlite.SqliteException or JsonException or NotSupportedException)
        {
            StatusText.Text = "批量报告未完成，原始记录未修改：" + error.Message;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (IOException error)
                {
                    StatusText.Text += "；临时文件清理失败：" + error.Message;
                }
                catch (UnauthorizedAccessException error)
                {
                    StatusText.Text += "；临时文件无权限删除：" + error.Message;
                }
            }
            _batchRunning = false;
            _busy = false;
            _batchCancellation = null;
            cancellation.Dispose();
            HistoryGrid.IsEnabled = true;
            CancelBatchExport.Visibility = Visibility.Collapsed;
            UpdateBatchExportAvailability();
        }
    }

    private async void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || HistoryGrid.SelectedItem is not RecordHistoryDisplayRow selection) return;
        var row = selection.Source;
        _busy = true;
        try
        {
            StatusText.Text = "正在核对证据并生成报告…";
            var html = await Task.Run(
                async () => await selection.RecordSource.ExportFullReportHtmlAsync(row.RunId));
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
        finally
        {
            _busy = false;
            UpdateBatchExportAvailability();
        }
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
            var destination = picker.FileName;
            var isHtml = picker.FilterIndex == 1;
            await Task.Run(async () => await selection.RecordSource.ExportReportAsync(
                row.RunId, destination, isHtml));
            StatusText.Text = "报告已导出：" + picker.FileName;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or
                                       InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or
                                       System.Text.Json.JsonException)
        {
            StatusText.Text = "报告导出失败，原始记录未改动：" + error.Message;
        }
        finally
        {
            _busy = false;
            UpdateBatchExportAvailability();
        }
    }
}
