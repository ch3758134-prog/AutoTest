using System.IO;
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
    private string _reportPath = "";
    private string _status = "等待查询 · 不将 Mock 或 Replay 视为实际出厂记录";
    private string _error = "";
    private string _summary = "尚未加载生产统计";
    private string _snapshot = "选择一次执行，查看当次冻结快照";
    private bool _busy;
    private RecordHistoryToolRow? _selected;

    public RecordsViewModel(IRecordHistoryToolSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        RefreshCommand = new DelegateCommand(() => _ = RunSafelyAsync(() => RefreshAsync()));
        ExportJsonCommand = new DelegateCommand(() => _ = RunSafelyAsync(() => ExportReportAsync(false)));
        ExportHtmlCommand = new DelegateCommand(() => _ = RunSafelyAsync(() => ExportReportAsync(true)));
    }

    public ObservableCollection<RecordHistoryToolRow> Runs { get; } = new();
    public IReadOnlyList<string> Modes { get; } = new[] { "All", "Production", "Development", "Diagnostic" };
    public ICommand RefreshCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportHtmlCommand { get; }

    public string DutId { get => _dutId; set => SetProperty(ref _dutId, value); }
    public string Mode { get => _mode; set => SetProperty(ref _mode, value); }
    public string ReportPath { get => _reportPath; set => SetProperty(ref _reportPath, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string Snapshot { get => _snapshot; private set => SetProperty(ref _snapshot, value); }
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    public RecordHistoryToolRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                Snapshot = value?.SnapshotJson ?? "选择运行记录查看当次冻结快照。";
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Error = "";
            var rows = await _source.QueryAsync(DutId, Mode, 500, ct);
            Selected = null;
            Runs.Clear();
            foreach (var row in rows) Runs.Add(row);
            // This is production-mode history; "PassedRuns" does NOT prove Physical source or shipment.
            var stats = await _source.SummaryAsync("Production", ct);
            Summary = $"Production 历史：执行 {stats.RunCount} 次 / 不同 ECU {stats.DistinctDutCount} 台 / " +
                $"已提交 {stats.CommittedRuns} / 历史 PASS {stats.PassedRuns} / FAIL {stats.FailedRuns} / " +
                $"中断 {stats.InterruptedRuns} · 不是实物出厂放行数量";
            Status = rows.Count == 0 ? "无历史记录（不是 PASS）。" : $"显示 {rows.Count} 条（上限 500）";
        }
        catch (Exception exception) { Error = "记录查询失败: " + exception.Message; Status = "读取失败"; }
        finally { IsBusy = false; }
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

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { Error = exception.Message; }
    }
}
