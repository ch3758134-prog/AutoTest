using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Windows.Input;
using AutoTest.Records.Operations;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Tools.Records;

public sealed class RecordsViewModel : BindableBase
{
    private readonly IRecordHistoryToolSource _source;
    private string _dutId = "";
    private string _mode = "All";
    private string _monthUtc = "";
    private string _reportPath = "";
    private string _status = "等待查询 · 不将 Mock 或 Replay 视为实际出厂记录";
    private string _error = "";
    private string _summary = "尚未加载生产统计";
    private string _snapshot = "选择一次执行，查看当次冻结快照";
    private string _stepSearch = "";
    private string _archiveIntegrity = "选择 Run 后执行归档检查；不会推定真实出厂放行。";
    private string _stepFacts = "选择一次执行后点击「查看 StepFacts」；历史事实来自该 Run 的持久化报告。";
    private bool _busy;
    private RecordHistoryToolRow? _selected;

    public RecordsViewModel(IRecordHistoryToolSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        RefreshCommand = new DelegateCommand(() => _ = RunSafelyAsync(() => RefreshAsync()));
        ExportJsonCommand = new DelegateCommand(() => _ = RunSafelyAsync(() => ExportReportAsync(false)));
        ExportHtmlCommand = new DelegateCommand(() => _ = RunSafelyAsync(() => ExportReportAsync(true)));
        InspectStepsCommand = new DelegateCommand(() => _ = RunSafelyAsync(() => InspectStepFactsAsync()));
        InspectArchiveCommand = new DelegateCommand(() => _ = RunSafelyAsync(() => InspectArchiveAsync()));
    }

    public ObservableCollection<RecordHistoryToolRow> Runs { get; } = new();
    public IReadOnlyList<string> Modes { get; } = new[] { "All", "Production", "Development", "Diagnostic" };
    public ICommand RefreshCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportHtmlCommand { get; }
    public ICommand InspectStepsCommand { get; }
    public ICommand InspectArchiveCommand { get; }

    public string DutId { get => _dutId; set => SetProperty(ref _dutId, value); }
    public string Mode { get => _mode; set => SetProperty(ref _mode, value); }
    public string MonthUtc { get => _monthUtc; set => SetProperty(ref _monthUtc, value); }
    public string ReportPath { get => _reportPath; set => SetProperty(ref _reportPath, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string Snapshot { get => _snapshot; private set => SetProperty(ref _snapshot, value); }
    public string StepSearch { get => _stepSearch; set => SetProperty(ref _stepSearch, value); }
    public string StepFacts { get => _stepFacts; private set => SetProperty(ref _stepFacts, value); }
    public string ArchiveIntegrity { get => _archiveIntegrity; private set => SetProperty(ref _archiveIntegrity, value); }
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    public RecordHistoryToolRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                Snapshot = value?.SnapshotJson ?? "选择运行记录查看当次冻结快照。";
                StepFacts = "选择 Run 后点击「查看 StepFacts」查询已归档事实；不根据当前工程推断。";
                ArchiveIntegrity = "尚未对选中的 Run 执行归档校验；历史 PASS 不代表实际出厂放行。";
            }
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Error = "";
            var (fromUtc, toUtc) = ParseUtcMonth(MonthUtc);
            var rows = await _source.QueryPeriodAsync(DutId, Mode, 500, fromUtc, toUtc, ct);
            // This one-statement production statistics read is not a real-world release decision.
            var stats = await _source.ProductionPeriodSummaryAsync(fromUtc, toUtc, ct);
            Selected = null;
            Runs.Clear();
            foreach (var row in rows) Runs.Add(row);
            var window = fromUtc is null ? "全部历史" : MonthUtc.Trim() + " (UTC)";
            Summary = $"Production {window}：执行 {stats.RunCount} 次 / 不同 ECU {stats.DistinctDutCount} 台 / " +
                $"首次记录 {stats.FirstSeenAttempts} / 重复检测记录 {stats.RepeatAttempts} / " +
                $"重复 ECU {stats.RepeatedDutCount} 台 / 已提交 {stats.CommittedRuns} / " +
                $"历史 PASS {stats.PassedRuns} / FAIL {stats.FailedRuns} / 中断 {stats.InterruptedRuns}。" +
                " 历史重复不等于批准复检；不是实物出厂放行数量。";
            Status = rows.Count == 0 ? "无匹配历史记录（不是 PASS）。" :
                $"显示 {rows.Count} 条（最多 500，历史模式 {Mode}，统计仅 Production）";
        }
        catch (Exception exception) { Error = "记录查询失败: " + exception.Message; Status = "读取失败"; }
        finally { IsBusy = false; }
    }

    public async Task InspectStepFactsAsync(CancellationToken ct = default)
    {
        if (Selected is null) { Error = "请先选择 RunId。"; return; }
        try
        {
            Error = "";
            var selectedRun = Selected.RunId;
            using var report = JsonDocument.Parse(await _source.ExportFullReportJsonAsync(selectedRun, ct));
            if (!report.RootElement.TryGetProperty("stepFacts", out var steps) ||
                steps.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("归档报告缺少 stepFacts 数组；不能显示模拟结果。");
            var rows = steps.EnumerateArray().Where(step =>
                string.IsNullOrWhiteSpace(StepSearch) ||
                step.TryGetProperty("stepId", out var id) &&
                id.ToString().Contains(StepSearch.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(step => step.Clone()).ToArray();
            // A stale asynchronous response must never replace a newer selected RunId.
            if (Selected?.RunId != selectedRun) return;
            StepFacts = rows.Length == 0 ? "未找到已归档 StepFact（非测试通过）。" :
                JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
            Status = $"Run {selectedRun}: 已读取 {rows.Length} 条已归档 StepFacts（预期/实际/证据质量）。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Error = "逐步事实读取失败: " + exception.Message;
            StepFacts = "证据不可用；未补造当前工程的步骤结果。";
        }
    }

    public async Task InspectArchiveAsync(CancellationToken ct = default)
    {
        if (Selected is null) { Error = "请先选择 RunId。"; return; }
        var runId = Selected.RunId;
        try
        {
            Error = "";
            var result = await _source.InspectArchiveAsync(runId, ct);
            // A selection change during disk IO must not display another Run's inspection.
            if (Selected?.RunId != runId) return;
            ArchiveIntegrity = result.ToManifestJson();
            Status = $"Run {runId}: 归档检查 {result.Status}；实体出厂放行状态未评估";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (Selected?.RunId != runId) return;
            Error = "归档完整性检查失败: " + exception.Message;
            ArchiveIntegrity = "无法证明归档完整性；不能据此认定历史结果可信。";
        }
    }

    public async Task ExportReportAsync(bool html, CancellationToken ct = default)
    {
        if (Selected == null) { Error = "请先选择 RunId。"; return; }
        if (string.IsNullOrWhiteSpace(ReportPath)) { Error = "请指定新报告文件完整路径。"; return; }
        try
        {
            Error = "";
            var path = Path.GetFullPath(ReportPath);
            await _source.ExportReportAsync(Selected.RunId, path, html, ct);
            Status = "已导出报告（原记录未修改）: " + path;
        }
        catch (Exception exception) { Error = "报告导出失败: " + exception.Message; }
    }


    private static (DateTimeOffset? FromUtc, DateTimeOffset? ToUtc) ParseUtcMonth(string? month)
    {
        if (string.IsNullOrWhiteSpace(month)) return (null, null);
        var text = month.Trim();
        if (!DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day) ||
            text != day.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            throw new FormatException("月份必须为 YYYY-MM（UTC），例如 2026-11；留空代表全部历史。");
        var first = new DateTimeOffset(day.Year, day.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var last = new DateTimeOffset(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month),
            23, 59, 59, TimeSpan.Zero).AddTicks(TimeSpan.TicksPerSecond - 1);
        return (first, last);
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { Error = exception.Message; }
    }
}
