using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using AutoTest.Records.Operations;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Tools.Operations;

/// <summary>Read-only audit explorer. Shell owns lifecycle/registration and provides one shared store.</summary>
public sealed class OperationsViewModel : BindableBase
{
    private readonly IOperationAuditStore _store;
    private readonly IOperationTraceReader _traces;
    private readonly IOperationAuditExporter _exports;
    private readonly IOperationDiagnosticBundleExporter _bundles;
    private AuditCursor? _cursor;
    private bool _busy;
    private string _search = "";
    private string _deviceFilter = "";
    private string _runFilter = "";
    private string _operationFilter = "";
    private string _originFilter = "All";
    private bool _onlyFailures;
    private bool _onlyWithTrace;
    private string _exportPath = "";
    private string _state = "尚未查询 · 支持无设备离线历史";
    private string _error = "";
    private string _detail = "选择记录查看完整错误信息与 Trace 指针。";
    private string _traceText = "";
    private OperationAuditEvent? _selectedEvent;

    public OperationsViewModel(IOperationAuditStore store, IOperationTraceReader traces, IOperationAuditExporter exports)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _traces = traces ?? throw new ArgumentNullException(nameof(traces));
        _exports = exports ?? throw new ArgumentNullException(nameof(exports));
        _bundles = new OperationDiagnosticBundleExporter(_store, _traces);
        RefreshCommand = new DelegateCommand(() => _ = ExecuteSafelyAsync(() => RefreshAsync()));
        MoreCommand = new DelegateCommand(() => _ = ExecuteSafelyAsync(() => LoadMoreAsync()), () => !IsBusy && _cursor != null);
        TraceCommand = new DelegateCommand(() => _ = ExecuteSafelyAsync(() => ShowSelectedTraceAsync()),
            () => !IsBusy && SelectedEvent?.Trace != null);
        ReverseTraceCommand = new DelegateCommand(() => _ = ExecuteSafelyAsync(() => ShowTraceOwnersAsync()),
            () => !IsBusy && SelectedEvent?.Trace != null);
        CsvCommand = new DelegateCommand(() => _ = ExecuteSafelyAsync(() => ExportAsync(true)));
        JsonlCommand = new DelegateCommand(() => _ = ExecuteSafelyAsync(() => ExportAsync(false)));
        BundleCommand = new DelegateCommand(() => _ = ExecuteSafelyAsync(() => ExportBundleAsync()));
    }

    public ObservableCollection<OperationAuditEvent> Entries { get; } = new();
    public IReadOnlyList<string> OriginOptions { get; } = new[] { "All", "Physical", "Replay", "Mock", "Unknown" };
    public ICommand RefreshCommand { get; }
    public ICommand MoreCommand { get; }
    public ICommand TraceCommand { get; }
    public ICommand ReverseTraceCommand { get; }
    public ICommand CsvCommand { get; }
    public ICommand JsonlCommand { get; }
    public ICommand BundleCommand { get; }

    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) UpdateCommandStates(); } }
    public string Search { get => _search; set => SetProperty(ref _search, value); }
    public string DeviceFilter { get => _deviceFilter; set => SetProperty(ref _deviceFilter, value); }
    public string RunFilter { get => _runFilter; set => SetProperty(ref _runFilter, value); }
    public string OperationFilter { get => _operationFilter; set => SetProperty(ref _operationFilter, value); }
    public string OriginFilter { get => _originFilter; set => SetProperty(ref _originFilter, value); }
    public bool OnlyFailures { get => _onlyFailures; set => SetProperty(ref _onlyFailures, value); }
    public bool OnlyWithTrace { get => _onlyWithTrace; set => SetProperty(ref _onlyWithTrace, value); }
    public string ExportPath { get => _exportPath; set => SetProperty(ref _exportPath, value); }
    public string State { get => _state; private set => SetProperty(ref _state, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public string TraceText { get => _traceText; private set => SetProperty(ref _traceText, value); }

    public OperationAuditEvent? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (!SetProperty(ref _selectedEvent, value)) return;
            Detail = value == null ? "选择记录查看详情。" : JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
            TraceText = value?.Trace == null ? "无关联 Trace（并非通信成功）。" :
                $"{value.Trace.RelativeFile} @ {value.Trace.Offset}, {value.Trace.ByteLength}B, SHA256={value.Trace.Sha256}";
            UpdateCommandStates();
        }
    }

    private OperationAuditQuery BuildQuery(AuditCursor? before = null) => new()
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        DeviceId = string.IsNullOrWhiteSpace(DeviceFilter) ? null : DeviceFilter.Trim(),
        RunId = string.IsNullOrWhiteSpace(RunFilter) ? null : RunFilter.Trim(),
        OperationId = string.IsNullOrWhiteSpace(OperationFilter) ? null :
            Guid.TryParse(OperationFilter.Trim(), out var operation) ? operation :
                throw new ArgumentException("操作 ID 必须是 GUID。"),
        Origin = OriginFilter == "All" ? null : Enum.Parse<AuditOrigin>(OriginFilter),
        FailuresOnly = OnlyFailures,
        WithTraceOnly = OnlyWithTrace,
        PageSize = 100,
        Before = before
    };

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Error = "";
            var page = await _store.QueryAsync(BuildQuery(), ct);
            Entries.Clear();
            foreach (var item in page.Events) Entries.Add(item);
            _cursor = page.NextCursor;
            State = Entries.Count == 0 ? "无匹配记录 · 可以在设备断开时查询" :
                $"已载入 {Entries.Count} 条记录" + (_cursor == null ? " · 已到末页" : " · 可继续加载");
            UpdateCommandStates();
        }
        catch (Exception exception) { Error = exception.Message; State = "查询失败 · 未将故障伪装为空结果"; }
        finally { IsBusy = false; }
    }

    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (IsBusy || _cursor == null) return;
        IsBusy = true;
        try
        {
            Error = "";
            var page = await _store.QueryAsync(BuildQuery(_cursor), ct);
            foreach (var item in page.Events) Entries.Add(item);
            _cursor = page.NextCursor;
            State = $"已载入 {Entries.Count} 条记录" + (_cursor == null ? " · 已到末页" : " · 可继续加载");
            UpdateCommandStates();
        }
        catch (Exception exception) { Error = exception.Message; State = "分页加载失败"; }
        finally { IsBusy = false; }
    }

    public async Task ShowSelectedTraceAsync(CancellationToken ct = default)
    {
        var trace = SelectedEvent?.Trace;
        if (trace == null) { TraceText = "该事件没有 Trace 指针。"; return; }
        try
        {
            var entry = await _traces.ReadAsync(trace, ct);
            TraceText = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception exception) { TraceText = "Trace 不可用或校验失败: " + exception.Message; }
    }

    public async Task ShowTraceOwnersAsync(CancellationToken ct = default)
    {
        var trace = SelectedEvent?.Trace;
        if (trace == null) return;
        try
        {
            var owners = await _store.FindByTraceAsync(trace, ct);
            TraceText = "关联操作 EventId：" + Environment.NewLine +
                string.Join(Environment.NewLine, owners.Select(x => $"{x.EventId:D}  {x.Kind}  {x.Outcome}"));
        }
        catch (Exception exception) { TraceText = "Trace 反向查询失败: " + exception.Message; }
    }

    public async Task ExportAsync(bool csv, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ExportPath)) { Error = "请先输入导出文件完整路径。"; return; }
        try
        {
            Error = "";
            var count = csv ? await _exports.ExportCsvAsync(BuildQuery(), ExportPath, ct) :
                await _exports.ExportJsonlAsync(BuildQuery(), ExportPath, ct);
            State = $"已导出 {count} 条到 {ExportPath}（不覆盖已有文件）";
        }
        catch (Exception exception) { Error = "导出失败: " + exception.Message; }
    }

    public async Task ExportBundleAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ExportPath)) { Error = "请先输入 ZIP 导出文件完整路径。"; return; }
        try
        {
            Error = "";
            var result = await _bundles.ExportAsync(BuildQuery(), ExportPath, ct);
            State = $"诊断包已生成：{result.EventCount} 条事件 / {result.VerifiedTraceCount} 条已验证 Trace / " +
                $"{result.UnavailableTraceCount} 条缺失或损坏 Trace / " +
                $"{result.EventsWithoutTrace} 条无原始 Trace 事件 · " +
                (result.Complete ? "本筛选集原始证据覆盖完整（不代表实物 PASS）" :
                    "证据覆盖不完整：检查 evidence-index.json 和 manifest.json") +
                $" · {result.Destination}";
        }
        catch (Exception exception) { Error = "诊断包导出失败: " + exception.Message; }
    }

    private async Task ExecuteSafelyAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { Error = exception.Message; }
    }

    private void UpdateCommandStates()
    {
        (MoreCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (TraceCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (ReverseTraceCommand as DelegateCommand)?.RaiseCanExecuteChanged();
    }
}
