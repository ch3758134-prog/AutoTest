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
        WaveReferenceEntry? Reference)
    {
        public string Display => Title + " · " + Detail;
    }

    private readonly ProjectWorkspaceService _project;
    private CancellationTokenSource? _refreshStop;
    private int _selectionGeneration;
    private bool _closed;
    private string? _lastManifestPath;
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
        _project.CurrentChanged += ProjectChanged;
        Refresh();
    }
    public event Action? ChartChanged;
    public WaveItem? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) LoadSelected(); }
    }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Info { get => _info; private set => SetProperty(ref _info, value); }
    public string CursorInfo { get => _cursorInfo; set => SetProperty(ref _cursorInfo, value); }
    public IReadOnlyList<WaveChartPoint> Points => _points;
    public WaveSeries? Series => _series;
    public string EmptyWaveMessage => _points.Count > 0 ? "" :
        "暂无可绘制的真实波形。\n\n连接主控 USB，选择工程 DBC 信号，\n点击“开始采集”获取真实数据。";
    public string EmptyListMessage { get => _emptyListMessage; private set => SetProperty(ref _emptyListMessage, value); }
    private void NotifyChartChanged()
    {
        RaisePropertyChanged(nameof(EmptyWaveMessage));
        SaveReferenceCommand?.RaiseCanExecuteChanged();
        ChartChanged?.Invoke();
    }

    private void ProjectChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
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
        _selected = null;
        _series = null;
        _points = Array.Empty<WaveChartPoint>();
        RaisePropertyChanged(nameof(Selected));
        NotifyChartChanged();
        Info = "";

        if (_project.Current is not { } project)
        {
            Status = "未打开工程；不会加载其他目录的波形记录。";
            return;
        }
        _lastManifestPath = project.ManifestPath;
        var root = Path.Combine(project.RootDirectory, "wave-records");
        var manifest = project.ManifestPath;
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
                                id, false, null));
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
                        reference.Reference.Asset.Id.Value, true, reference));
                }
                return items;
            }, stop.Token);
            if (_closed || stop.IsCancellationRequested || !ReferenceEquals(_refreshStop, stop) ||
                _project.Current?.ManifestPath != manifest) return;
            foreach (var item in collected) Items.Add(item);
            EmptyListMessage = Items.Count == 0
                ? "此工程尚无已归档波形。\n使用上方真实采集入口获取数据。" : "";
            Status = Items.Count == 0
                ? "本工程暂无真实波形证据；连接设备并采集后可在此查看。"
                : "已载入 " + Items.Count + " 条已验证的波形记录；请选择一条查看。";
            if (Items.Count > 0) Selected = Items[0];
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
        if (_closed || Selected is not { } selected || _project.Current is not { } project)
            return;
        var root = Path.Combine(project.RootDirectory, "wave-records");
        var manifest = project.ManifestPath;
        Status = "正在后台读取和投影真实波形…";
        try
        {
            var projected = await Task.Run(() =>
            {
                var series = selected.IsReference
                    ? new WaveReferenceCatalog(Path.Combine(root, "references"))
                        .Load(selected.Reference!.Reference).Series
                    : new WaveEvidenceStore(Path.Combine(root, "evidence"))
                        .Read(new EvidenceRef(selected.Id)).Actual;
                return (Series: series, Points: WaveChartAnalysis.FromSeries(series));
            });
            if (_closed || generation != _selectionGeneration ||
                Selected != selected || _project.Current?.ManifestPath != manifest) return;
            _series = projected.Series;
            _points = projected.Points;
            var values = _series.Samples.Select(x => x.Sample.Value).ToArray();
            var min = values.Length == 0 ? "" : values.Min().ToString("G6");
            var max = values.Length == 0 ? "" : values.Max().ToString("G6");
            Info = _series.Signal.SignalName + " · " + _series.Signal.Unit +
                " · CAN ID 0x" + _series.Signal.FrameId.ToString("X") +
                " · " + _series.Samples.Length + " 个样本" +
                " · 最小 " + min + " · 最大 " + max +
                " · 丢失标记 " + (_series.HasGap || _series.Overflow ? "存在" : "未标记");
            CursorInfo = "点击曲线两次，查看区间时间差、幅值差与极值。";
            Status = "显示真实样本；降采样仅用于绘图，不用于判定。";
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
            var measurement = WaveChartAnalysis.Measure(_points, first, second);
            CursorInfo = measurement is null ? "选定范围没有可用样本。" :
                "Δt=" + measurement.DeltaSeconds.ToString("F4") + " s   " +
                "Δy=" + measurement.DeltaValue.ToString("G6") + " " +
                (_series?.Signal.Unit ?? "") + "   区间最小=" +
                measurement.Minimum.ToString("G6") + "   最大=" +
                measurement.Maximum.ToString("G6");
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
