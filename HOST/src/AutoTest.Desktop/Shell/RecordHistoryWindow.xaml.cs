using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
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
    // Even a stored verdict must not appear as a FINAL PASS until the
    // append-only record is durably committed.
    public string VerdictLabel => Source.PersistenceState != "Committed"
        ? Source.PersistenceState == "Writing" ? "未结案" : "中断／未结案"
        : Source.Verdict switch
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
    private readonly ObservableCollection<ArchivedStepDisplayRow> _archivedSteps = new();
    private readonly ObservableCollection<ArchivedEventDisplayRow> _archivedEvents = new();

    public RecordHistoryWindow(string? projectRoot)
    {
        InitializeComponent();
        StepFactsGrid.ItemsSource = _archivedSteps;
        EventFactsGrid.ItemsSource = _archivedEvents;
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var candidate = Path.Combine(projectRoot, "test-records", "r1-records.sqlite");
            if (File.Exists(candidate)) SetSource(candidate);
            else StatusText.Text = "当前工程尚无持久化记录。可以选择已有的记录库，或在完成真实检测后重新查询。";
        }
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
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
        finally { _busy = false; }
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
        _archivedSteps.Clear();
        _archivedEvents.Clear();
        DetailsStatus.Text = "选择一次运行，查看真实步骤、测量、事件和证据";
        StepDetailText.Text = "选择一个步骤可查看 Occurrence、证据 ID 和失败说明";
        EvidenceIntegrity.Text = "尚未选择记录；不能从空白或未结案记录推断测试通过。";
    }

    private static string JsonField(JsonElement row, string key)
    {
        if (row.ValueKind != JsonValueKind.Object ||
            !row.TryGetProperty(key, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
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
        var incomplete = JsonField(report, "evidenceArchiveIncomplete") == "True" ||
            JsonField(report, "evidenceArchiveIncomplete") == "true";
        var unarchived = JsonList(report, "unarchivedEvidenceIds");
        var capture = report.TryGetProperty("captureHealth", out var health) &&
            health.ValueKind == JsonValueKind.Object ? health : default;
        var captureComplete = JsonField(capture, "recordingComplete");
        var dropped = JsonField(capture, "droppedEvents");
        var reason = JsonField(capture, "incompleteReason");
        var artifacts = report.TryGetProperty("artifacts", out var a) &&
            a.ValueKind == JsonValueKind.Array ? a.GetArrayLength() : 0;
        var integrity = incomplete
            ? "报告标识：证据归档不完整；不能将缺失的 CAN/DBC/波形证据当作有效测量。"
            : "报告未标识证据归档缺失；仍须按冻结判据和原始事实审核结果。";
        integrity += "\n采集完整标记：" + (captureComplete.Length == 0 ? "未提供" : captureComplete) +
            "；丢失事件：" + (dropped.Length == 0 ? "未知" : dropped) +
            "；归档附件：" + artifacts + " 项。";
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
        if (HistoryGrid.SelectedItem is not RecordHistoryDisplayRow display)
        {
            ViewReport.IsEnabled = ExportReport.IsEnabled = false;
            SelectionSummary.Text = "选择一条记录查看详情";
            FailureDetail.Text = "";
            return;
        }
        var row = display.Source;
        ViewReport.IsEnabled = ExportReport.IsEnabled = !_busy;
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
            var json = await display.RecordSource.ExportFullReportJsonAsync(
                row.RunId, cancellation.Token);
            var parsed = await Task.Run(() => ParseRealReport(json, cancellation.Token),
                cancellation.Token);
            if (version != _detailRequestRevision || cancellation.IsCancellationRequested ||
                !ReferenceEquals(HistoryGrid.SelectedItem, display)) return;
            foreach (var step in parsed.Steps) _archivedSteps.Add(step);
            foreach (var entry in parsed.Events) _archivedEvents.Add(entry);
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

    private void StepFactsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        StepDetailText.Text = StepFactsGrid.SelectedItem is not ArchivedStepDisplayRow row
            ? "选择一个步骤可查看 Occurrence、证据 ID 和失败说明"
            : "步骤 " + row.Number + " · Occurrence：" +
              (string.IsNullOrEmpty(row.Occurrence) ? "未记录" : row.Occurrence) +
              " · 证据：" + (string.IsNullOrWhiteSpace(row.Evidence) ? "未引用" : row.Evidence) +
              (string.IsNullOrWhiteSpace(row.Detail) ? "" : " · " + row.Detail);
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
