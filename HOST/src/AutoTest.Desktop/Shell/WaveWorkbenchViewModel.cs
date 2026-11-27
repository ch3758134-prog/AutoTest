using System.Collections.ObjectModel;
using System.IO;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Specialists.Wave;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Testing;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Read-only real waveform evidence/reference browser. Uses the project evidence
/// codec, verified sidecars and waveform chart projection; no invented samples,
/// CAN subscriptions, UI-created reference files or simulated captures.
/// </summary>
public sealed partial class WaveWorkbenchViewModel : BindableBase
{
    public sealed record WaveItem(string Title, string Detail, Guid Id, bool IsReference,
        WaveReferenceEntry? Reference, string Kind = "Capture")
    {
        public string Display => Title + " · " + Detail;
    }

    private readonly ProjectWorkspaceService _project;
    private CancellationTokenSource? _refreshStop;
    private int _selectionGeneration;
    private bool _closed;
    private string? _lastManifestPath;
    private string? _independentWaveRoot;

    // An independent wave archive is an explicit folder, never an implicit
    // fake ECU project. All evidence is read through the same SHA-verified
    // stores as the engineering workspace.
    public string? WaveSourceRoot => _independentWaveRoot ??
        (_project.Current is { } p ? Path.Combine(p.RootDirectory, "wave-records") : null);
    public string? WaveSourceIdentity => _independentWaveRoot ??
        _project.Current?.ManifestPath;
    public bool IsIndependentArchive => _independentWaveRoot is not null;
    public bool CanEditProjectWaveSettings => !IsIndependentArchive && _project.Current is not null;
    public bool HasEcuProject => _project.Current is not null;
    public string SourceDescription => IsIndependentArchive
        ? "独立波形目录 · " + _independentWaveRoot
        : _project.Current is { } p
            ? "工程波形 · " + p.Definition.Name
            : "未选择波形来源；可以打开已采集的独立波形目录";

    public void OpenIndependentArchive(string folder)
    {
        if (IsCapturing || IsObserving)
            throw new InvalidOperationException("先停止真实采集或观察，再切换波形来源。");
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root) ||
            (!Directory.Exists(Path.Combine(root, "evidence")) &&
             !Directory.Exists(Path.Combine(root, "references"))))
            throw new InvalidDataException(
                "请选择包含 evidence 或 references 子目录的既有 wave-records 目录，不能把任意文件夹当作有效波形库。");
        _independentWaveRoot = root;
        AbortCaptureForProjectChange();
        ReloadAvailableSignals(); // no project-bound live choices while browsing an independent archive
        RaisePropertyChanged(nameof(SourceDescription));
        RaisePropertyChanged(nameof(CanEditProjectWaveSettings));
        RefreshCaptureCommands();
        Refresh();
    }

    public void UseProjectArchive()
    {
        if (IsCapturing || IsObserving)
            throw new InvalidOperationException("请先停止真实采集再切换波形来源。");
        if (_project.Current is null)
            throw new InvalidOperationException("当前没有 ECU 工程；请先打开工程或选择独立波形目录。");
        _independentWaveRoot = null;
        AbortCaptureForProjectChange();
        ReloadAvailableSignals(); // restore the live, revision-pinned project DBC choices
        RaisePropertyChanged(nameof(SourceDescription));
        RaisePropertyChanged(nameof(CanEditProjectWaveSettings));
        RefreshCaptureCommands();
        Refresh();
    }
    private string _emptyListMessage = "";
    private WaveItem? _selected;
    private WaveSeries? _series;
    private IReadOnlyList<WaveChartPoint> _points = Array.Empty<WaveChartPoint>();
    private string _status = "打开工程后刷新，查看已保存的真实波形证据与参考波形。";
    private string _info = "";
    private string _cursorInfo = "点击曲线两次测量时间差、数值差与区间极值。";
    public ObservableCollection<WaveItem> Items { get; } = new();
    public DelegateCommand RefreshCommand { get; }
    public WaveWorkbenchViewModel(ProjectWorkspaceService project,
        LiveDbcSignalService signals, HardwareConnectionService hardware,
        RealTestWorkbenchService realTest)
    {
        _project = project;
        RefreshCommand = new DelegateCommand(Refresh);
        InitializeCapture(signals, hardware, realTest);
        InitializeWorkspace();
        _project.CurrentChanged += ProjectChanged;
        Refresh();
    }
    public event Action? ChartChanged;
    public WaveItem? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            if (value is not null)
            {
                _selectedSession = null;
                RaisePropertyChanged(nameof(SelectedSession));
                ++_sessionLoadGeneration;
                _sessionSeries.Clear();
                IsLiveView = false;
                RaisePropertyChanged(nameof(DisplayMode));
            }
            _comparison = null;
            _referenceOverlay = null;
            ComparisonSummary = "选择一条真实采集记录和对应参考后进行比较。";
            CompareCommand?.RaiseCanExecuteChanged();
            LoadSelected();
        }
    }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Info { get => _info; private set => SetProperty(ref _info, value); }
    public string CursorInfo { get => _cursorInfo; set => SetProperty(ref _cursorInfo, value); }
    public IReadOnlyList<WaveChartPoint> Points => _points;
    public WaveSeries? Series => _series;
    public string EmptyWaveMessage => GetPlotLanes().Count > 0
        ? "" : IsLiveView
            ? "请从左侧选择要观察的 DBC 信号。"
            : "请选择一条采集记录或参考波形。";
    public string EmptyListMessage { get => _emptyListMessage; private set => SetProperty(ref _emptyListMessage, value); }
    private void NotifyChartChanged()
    {
        RaisePropertyChanged(nameof(EmptyWaveMessage));
        SaveReferenceCommand?.RaiseCanExecuteChanged();
        SaveStandardCommand?.RaiseCanExecuteChanged();
        ChartChanged?.Invoke();
    }

    private void ProjectChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsIndependentArchive) return;
            RaisePropertyChanged(nameof(SourceDescription));
            RaisePropertyChanged(nameof(CanEditProjectWaveSettings));
            RaisePropertyChanged(nameof(HasEcuProject));
            // Project autosave emits CurrentChanged: do not clear the plot on
            // a manifest update in the same opened project.
            var nextPath = _project.Current?.ManifestPath;
            if (string.Equals(nextPath, _lastManifestPath, StringComparison.OrdinalIgnoreCase))
                return;
            _lastManifestPath = nextPath;
            AbortCaptureForProjectChange();
            ReloadAvailableSignals();
            Refresh();
        }));

    public void Refresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_closed) return;
        _refreshStop?.Cancel();
        var stop = new CancellationTokenSource();
        _refreshStop = stop;
        _selectionGeneration++; // invalidate any pending selection projection
        Items.Clear();
        Captures.Clear();
        Comparisons.Clear();
        References.Clear();
        Standards.Clear();
        Sessions.Clear();
        _sessionSeries.Clear();
        _selectedSession = null;
        _selectedReference = null;
        _referenceOverlay = null;
        _comparison = null;
        RaisePropertyChanged(nameof(SelectedSession));
        RaisePropertyChanged(nameof(SelectedReference));
        _selected = null;
        _series = null;
        _points = Array.Empty<WaveChartPoint>();
        RaisePropertyChanged(nameof(Selected));
        NotifyChartChanged();
        Info = "";

        var root = WaveSourceRoot;
        var manifest = WaveSourceIdentity;
        if (root is null || manifest is null)
        {
            Status = "请选择已采集的真实波形目录；无工程也能查看和分析归档。";
            return;
        }
        _lastManifestPath = _project.Current?.ManifestPath;
        Status = "正在后台校验真实波形证据与参考资源…";
        try
        {
            // Evidence store verifies immutable files. Only pure filesystem and
            // codec work runs off the dispatcher; ObservableCollection stays UI.
            var collected = await Task.Run(() =>
            {
                var items = new List<WaveItem>();
                var evidencePath = Path.Combine(root, "evidence");
                if (Directory.Exists(evidencePath))
                {
                    var store = new WaveEvidenceStore(evidencePath);
                    foreach (var file in Directory.EnumerateFiles(evidencePath, "*.wave.json")
                                 .OrderByDescending(File.GetLastWriteTimeUtc).Take(500))
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        var prefix = Path.GetFileName(file).Split('.')[0];
                        if (!Guid.TryParseExact(prefix, "N", out var id)) continue;
                        try
                        {
                            var actual = store.Read(new EvidenceRef(id));
                            items.Add(new WaveItem(actual.Actual.Signal.SignalName,
                                actual.Kind + " · " + actual.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
                                " · " + actual.Actual.Samples.Length + " 点",
                                id, false, null, actual.Kind));
                        }
                        catch (Exception error) when (error is IOException or InvalidDataException or
                                                       ArgumentException or UnauthorizedAccessException)
                        {
                            // Damaged capture is never offered as verified evidence.
                        }
                    }
                }
                stop.Token.ThrowIfCancellationRequested();
                var catalog = new WaveReferenceCatalog(Path.Combine(root, "references"));
                foreach (var reference in catalog.List())
                {
                    stop.Token.ThrowIfCancellationRequested();
                    items.Add(new WaveItem(reference.Name,
                        "参考 · " + reference.Signal.SignalName + " · " +
                        reference.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        reference.Reference.Asset.Id.Value, true, reference, "Reference"));
                }
                return (Items: items, Sessions: ReadSessionIndex(root),
                    Standards: catalog.ListStandards());
            }, stop.Token);
            if (_closed || stop.IsCancellationRequested || !ReferenceEquals(_refreshStop, stop) ||
                WaveSourceIdentity != manifest) return;
            foreach (var item in collected.Items)
            {
                Items.Add(item);
                if (item.IsReference && item.Reference is not null) References.Add(item.Reference);
                else if (item.Kind == "Capture") Captures.Add(item);
                else if (item.Kind == "Comparison") Comparisons.Add(item);
            }
            foreach (var group in collected.Sessions) Sessions.Add(group);
            foreach (var standard in collected.Standards) Standards.Add(standard);
            EmptyListMessage = Items.Count == 0
                ? "此目录没有有效波形证据或参考记录。" : "";
            Status = Items.Count == 0
                ? "当前来源暂无已验证的波形证据。"
                : "已载入 " + Items.Count + " 条已验证的波形记录；请选择一条查看。";
            if (Items.Count > 0 && !IsLiveView) Selected = Captures.FirstOrDefault() ?? Items[0];
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_closed && ReferenceEquals(_refreshStop, stop))
                Status = "读取真实波形记录失败：" + error.Message;
        }
        finally
        {
            if (ReferenceEquals(_refreshStop, stop)) _refreshStop = null;
            stop.Dispose();
        }
    }

    private void LoadSelected() => _ = LoadSelectedAsync();

    private async Task LoadSelectedAsync()
    {
        var generation = ++_selectionGeneration;
        _series = null;
        _points = Array.Empty<WaveChartPoint>();
        Info = "";
        NotifyChartChanged();
        if (_closed || Selected is not { } selected) return;
        var root = WaveSourceRoot;
        var manifest = WaveSourceIdentity;
        if (root is null || manifest is null) return;
        Status = "正在后台读取和投影真实波形…";
        try
        {
            var projected = await Task.Run(() =>
            {
                WaveEvidenceDocument? evidence = selected.IsReference ? null :
                    new WaveEvidenceStore(Path.Combine(root, "evidence"))
                        .Read(new EvidenceRef(selected.Id));
                var series = selected.IsReference
                    ? new WaveReferenceCatalog(Path.Combine(root, "references"))
                        .Load(selected.Reference!.Reference).Series
                    : evidence!.Actual;
                return (Series: series, Points: WaveChartAnalysis.FromSeries(series),
                    Evidence: evidence);
            });
            if (_closed || generation != _selectionGeneration ||
                Selected != selected || WaveSourceIdentity != manifest) return;
            _series = projected.Series;
            _points = projected.Points;
            if (projected.Evidence is { Kind: "Comparison", Reference: not null,
                    Result: not null } record)
            {
                _referenceOverlay = record.Reference;
                _comparison = record.Result;
                if (record.Configuration is not null)
                {
                    AbsoluteToleranceText = record.Configuration.AbsoluteTolerance.ToString(
                        "G", System.Globalization.CultureInfo.InvariantCulture);
                    MinimumCoverageText = record.Configuration.MinimumCoverage.ToString(
                        "G", System.Globalization.CultureInfo.InvariantCulture);
                }
                ComparisonSummary = "已载入历史比较证据 · " + record.Result.Verdict +
                    " · 覆盖率 " + record.Result.Coverage.ToString("P1") +
                    " · " + record.Result.Code;
                RaisePropertyChanged(nameof(ComparisonTone));
            }
            var values = _series.Samples.Select(x => x.Sample.Value).ToArray();
            var min = values.Length == 0 ? "" : values.Min().ToString("G6");
            var max = values.Length == 0 ? "" : values.Max().ToString("G6");
            Info = _series.Signal.SignalName + " · " + _series.Signal.Unit +
                " · CAN ID 0x" + _series.Signal.FrameId.ToString("X") +
                " · " + _series.Samples.Length + " 个样本" +
                " · 最小 " + min + " · 最大 " + max +
                " · 丢失标记 " + (_series.HasGap || _series.Overflow ? "存在" : "未标记");
            RaisePropertyChanged(nameof(DataQuality));
            CompareCommand?.RaiseCanExecuteChanged();
            CursorInfo = "点击同一信号轨道两次，测量时间差、幅值差与区间极值。";
            Status = selected.Kind == "Comparison"
                ? "历史比较证据已核验；实际波形、参考、容差与计算结果均来自归档文件。"
                : "显示真实样本；降采样仅用于绘图，不用于判定。";
        }
        catch (Exception error)
        {
            if (_closed || generation != _selectionGeneration) return;
            _points = Array.Empty<WaveChartPoint>();
            _series = null;
            Info = "";
            Status = "波形证据无法读取：" + error.Message;
        }
        finally
        {
            if (!_closed && generation == _selectionGeneration)
                NotifyChartChanged();
        }
    }

    public void SetCursorMeasure(double first, double second)
    {
        try
        {
            var lane = GetPlotLanes().FirstOrDefault(x => x.Actual.Count > 0);
            var measurement = lane is null ? null :
                WaveChartAnalysis.Measure(lane.Actual, first, second);
            CursorInfo = measurement is null ? "当前没有可测量的真实样本。" :
                lane!.Name + " · Δt=" + measurement.DeltaSeconds.ToString("F4") + " s" +
                " · Δy=" + measurement.DeltaValue.ToString("G6") + " " + lane.Unit +
                " · 最小=" + measurement.Minimum.ToString("G6") +
                " · 最大=" + measurement.Maximum.ToString("G6");
        }
        catch (Exception ex) { CursorInfo = "无法计算游标：" + ex.Message; }
    }

    public void Close()
    {
        AbortCaptureForWindowClose();
        _closed = true;
        _selectionGeneration++;
        _refreshStop?.Cancel();
        _project.CurrentChanged -= ProjectChanged;
    }
}
