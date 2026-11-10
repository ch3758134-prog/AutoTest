using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Tools.Wave;

public sealed class WaveSignalRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool _selected;
    public WaveSignalRow(WaveSignalChoice choice) => Choice = choice;
    public WaveSignalChoice Choice { get; }
    public string Name => Choice.DisplayName;
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed record WaveDrawTrace(string Name, Brush Color, PointCollection Points,
    string ScaleLabel, bool Reference);
public sealed record WaveResultRow(string Signal, string Verdict, string Detail,
    string Coverage, string EvidenceId);

/// <summary>
/// The professional WAVE UI model. No USB/CAN receiver, no fake PONG and no production PASS.
/// The Replay path operates only through the supplied G4 CAN Hub/DBC backend.
/// </summary>
public sealed class WaveWorkbenchViewModel : INotifyPropertyChanged
{
    private static readonly Brush[] Colors =
        [Brushes.DeepSkyBlue, Brushes.LawnGreen, Brushes.Orange, Brushes.MediumOrchid,
         Brushes.Coral, Brushes.LightSeaGreen, Brushes.Gold, Brushes.LightPink];
    private readonly Dictionary<SignalRef, WaveWorkbenchCapture> _captures = new();
    private readonly Dictionary<SignalRef, WaveReference> _references = new();
    private IWaveWorkbenchBackend? _backend;
    private CancellationTokenSource? _pending;
    private bool _busy;
    private bool _refreshAfterCurrentOperation;
    private string _status = "未接入波形服务 · 请从正式工作台打开";
    private string _quality = "Unknown";
    private string _origin = "Unknown";
    private string _referenceStatus = "没有参考波形";
    private string _cursorInfo = "单击设置游标 A；Shift+单击设置游标 B";
    private string _viewRange = "—";
    private string _toleranceText = "0.1";
    private string _archivePath = string.Empty;
    private double _cursorA = double.NaN, _cursorB = double.NaN;
    private double _start, _end = 1;
    private PointCollection _cursorLineA = new(), _cursorLineB = new();
    private readonly WaveCommand _reload, _replay, _mock, _openArchive, _live, _stop, _save, _load, _compare;
    private readonly WaveCommand _zoomIn, _zoomOut, _reset;

    public WaveWorkbenchViewModel()
    {
        _reload = new WaveCommand(() => RunAsync(ReloadAsync), () => _backend is not null && !_busy);
        _replay = new WaveCommand(() => RunAsync(ReplayAsync), () => CanOperate &&
            (_backend is not IWaveArchiveSource archive || archive.HasRecordedArchive));
        _mock = new WaveCommand(() => RunAsync(MockAsync), () => CanOperate && _backend is IWaveMockGenerator);
        _openArchive = new WaveCommand(() => RunAsync(OpenArchiveAsync), () =>
            _backend is IWaveArchiveSource && !_busy && !string.IsNullOrWhiteSpace(ArchivePath));
        _live = new WaveCommand(() => RunAsync(LiveAsync), () => CanOperate && _backend!.CanCaptureLive);
        _stop = new WaveCommand(Stop, () => _busy);
        _save = new WaveCommand(() => RunAsync(SaveAsync), () => !_busy && HasCapture);
        _load = new WaveCommand(() => RunAsync(LoadAsync), () => CanOperate);
        _compare = new WaveCommand(() => RunAsync(CompareAsync), () => !_busy && HasCapture);
        _zoomIn = new WaveCommand(() => Zoom(0.5), () => _captures.Count != 0);
        _zoomOut = new WaveCommand(() => Zoom(2), () => _captures.Count != 0);
        _reset = new WaveCommand(ResetZoom, () => _captures.Count != 0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<WaveSignalRow> Signals { get; } = new();
    public ObservableCollection<WaveDrawTrace> Traces { get; } = new();
    public ObservableCollection<WaveResultRow> Results { get; } = new();
    public ICommand ReloadCommand => _reload;
    public ICommand ReplayCommand => _replay;
    public ICommand MockCommand => _mock;
    public ICommand OpenArchiveCommand => _openArchive;
    public ICommand LiveCommand => _live;
    public ICommand StopCommand => _stop;
    public ICommand SaveReferenceCommand => _save;
    public ICommand LoadReferenceCommand => _load;
    public ICommand CompareCommand => _compare;
    public ICommand ZoomInCommand => _zoomIn;
    public ICommand ZoomOutCommand => _zoomOut;
    public ICommand ResetZoomCommand => _reset;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Quality { get => _quality; private set => Set(ref _quality, value); }
    public string Origin { get => _origin; private set => Set(ref _origin, value); }
    public string ReferenceStatus { get => _referenceStatus; private set => Set(ref _referenceStatus, value); }
    public string CursorInfo { get => _cursorInfo; private set => Set(ref _cursorInfo, value); }
    public string ViewRange { get => _viewRange; private set => Set(ref _viewRange, value); }
    public PointCollection CursorLineA { get => _cursorLineA; private set => Set(ref _cursorLineA, value); }
    public PointCollection CursorLineB { get => _cursorLineB; private set => Set(ref _cursorLineB, value); }
    public string ToleranceText { get => _toleranceText; set => Set(ref _toleranceText, value); }
    public string ArchivePath
    {
        get => _archivePath;
        set { Set(ref _archivePath, value); RefreshCommands(); }
    }
    public bool ArchiveAvailable => _backend is IWaveArchiveSource archived && archived.HasRecordedArchive;
    public bool Busy => _busy;
    public bool LiveAvailable => _backend?.CanCaptureLive == true;
    public bool CanOperate => _backend is not null && !_busy && Signals.Any(x => x.IsSelected);
    public bool HasCapture => Signals.Any(x => x.IsSelected && _captures.ContainsKey(x.Choice.Signal));
    public int CaptureCount => _captures.Count;

    public void BindBackend(IWaveWorkbenchBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (_busy) throw new InvalidOperationException("Cannot switch WAVE backend while capturing.");
        _backend = backend;
        LoadSignals();
    }

    public void RefreshSignals()
    {
        if (_busy)
        {
            // A project DBC asset/revision change invalidates any in-flight capture.
            // Do not mutate the selected-signal collection under the capture loop.
            _refreshAfterCurrentOperation = true;
            _pending?.Cancel();
            Status = "DBC_CHANGED: cancelling current Wave operation before refreshing signals";
            return;
        }
        LoadSignals();
    }

    // Awaitable command entrypoints also make deterministic Desktop behavior tests possible.
    public Task ReplaySelectedAsync() => RunOperationAsync(ReplayAsync);
    public Task MockSelectedAsync() => RunOperationAsync(MockAsync);
    public Task OpenArchiveSelectedAsync() => RunOperationAsync(OpenArchiveAsync);
    public Task LiveSelectedAsync() => RunOperationAsync(LiveAsync);
    public Task SaveReferenceSelectedAsync() => RunOperationAsync(SaveAsync);
    public Task LoadReferenceSelectedAsync() => RunOperationAsync(LoadAsync);
    public Task CompareSelectedAsync() => RunOperationAsync(CompareAsync);

    private void LoadSignals()
    {
        if (_backend is null) return;
        var available = _backend.ListSignals();
        var previous = Signals.Where(x => x.IsSelected).Select(x => x.Choice.Signal).ToHashSet();
        var newSet = available.Select(x => x.Signal).ToHashSet();
        var stale = _captures.Keys.Any(x => !newSet.Contains(x));
        Signals.Clear();
        foreach (var entry in available)
        {
            var row = new WaveSignalRow(entry);
            row.PropertyChanged += (_, _) => { Render(); RefreshCommands(); };
            row.IsSelected = previous.Contains(entry.Signal);
            Signals.Add(row);
        }
        if (Signals.Count > 0 && !Signals.Any(x => x.IsSelected)) Signals[0].IsSelected = true;
        foreach (var key in _captures.Keys.Where(k => !newSet.Contains(k)).ToArray()) _captures.Remove(key);
        foreach (var key in _references.Keys.Where(k => !newSet.Contains(k)).ToArray()) _references.Remove(key);
        Results.Clear();
        Status = stale ? "DBC_REVISION_STALE：旧采集已移除，请重新采集、核对参考版本"
            : Signals.Count == 0 ? "DBC_REQUIRED：请在项目中导入 DBC 后刷新" : "就绪 · 请选择信号并执行 Replay";
        Render(); RefreshCommands();
    }

    private Task ReloadAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); LoadSignals(); return Task.CompletedTask; }

    private Task OpenArchiveAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_backend is not IWaveArchiveSource archive)
            throw new WaveCaptureException("CAN_REPLAY_NOT_CONFIGURED", "Shared replay publisher is missing.");
        archive.OpenArchiveFile(ArchivePath);
        Status = "已读取真实保存的 CAN RX 帧档案；Replay 输入已就绪";
        RefreshCommands();
        return Task.CompletedTask;
    }

    private async Task MockAsync(CancellationToken ct)
    {
        if (_backend is not IWaveMockGenerator mock)
            throw new WaveCaptureException("MOCK_NOT_AVAILABLE", "Mock CAN generator not configured.");
        var selected = Selected();
        foreach (var row in selected)
        {
            ct.ThrowIfCancellationRequested();
            var captured = await mock.RunMockAsync(row.Choice.Signal, 80, ct);
            _captures[row.Choice.Signal] = captured;
        }
        ResetZoom();
        Status = $"Mock 完成：{selected.Length} 路合成 CAN 帧 · 仅用于 UI 和比较算法测试";
    }

    private async Task ReplayAsync(CancellationToken ct)
    {
        var selected = Selected();
        foreach (var row in selected)
        {
            ct.ThrowIfCancellationRequested();
            var captured = await _backend!.ReplayAsync(row.Choice.Signal, 50000, ct);
            _captures[row.Choice.Signal] = captured;
        }
        ResetZoom();
        Status = $"Replay 完成：{selected.Length} 路信号，来自保存 CAN RX 帧 / DBC；非实机出厂数据";
    }

    private async Task LiveAsync(CancellationToken ct)
    {
        var selected = Selected();
        foreach (var row in selected)
        {
            ct.ThrowIfCancellationRequested();
            var captured = await _backend!.CaptureLiveAsync(row.Choice.Signal,
                TimeSpan.FromSeconds(2), 5000, ct);
            _captures[row.Choice.Signal] = captured;
        }
        ResetZoom();
        Status = "共享信号源采集完成（实际来源见上方标签）；非实机验收";
    }

    private Task SaveAsync(CancellationToken ct)
    {
        foreach (var row in Selected())
        {
            ct.ThrowIfCancellationRequested();
            if (!_captures.TryGetValue(row.Choice.Signal, out var captured)) continue;
            var reference = _backend!.SaveReference(captured.Series,
                row.Choice.Signal.SignalName + " / " + captured.Series.SourceRevision.Sha256[..8]);
            _references[row.Choice.Signal] = reference;
        }
        Render();
        Status = "参考曲线已保存（固定 DBC Revision + 内容 SHA-256）";
        return Task.CompletedTask;
    }

    private Task LoadAsync(CancellationToken ct)
    {
        foreach (var row in Selected())
        {
            ct.ThrowIfCancellationRequested();
            var reference = _backend!.ReadReference(row.Choice.Signal);
            if (reference is null) _references.Remove(row.Choice.Signal);
            else _references[row.Choice.Signal] = reference;
        }
        Render();
        Status = _references.Count == 0 ? "当前 DBC Revision 无参考曲线；请先保存" :
            "参考曲线已按固定 Revision 重新加载并验证 SHA-256";
        return Task.CompletedTask;
    }

    private async Task CompareAsync(CancellationToken ct)
    {
        if (!double.TryParse(ToleranceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var tolerance) ||
            !double.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(ToleranceText), "容差必须是非负有限数字（使用 . 小数点）");
        Results.Clear();
        var selected = Selected();
        foreach (var row in selected)
        {
            ct.ThrowIfCancellationRequested();
            if (!_captures.TryGetValue(row.Choice.Signal, out var capture)) continue;
            if (!_references.TryGetValue(row.Choice.Signal, out var reference))
            {
                Results.Add(new WaveResultRow(row.Name, "INCONCLUSIVE", "REFERENCE_MISSING", "—", "—"));
                continue;
            }
            var profile = WaveComparisonConfig.FromContract(new CompareProfile(tolerance, .9));
            var result = await _backend!.CompareAsync(capture, reference, profile, ct);
            Results.Add(new WaveResultRow(row.Name, result.Details.Verdict.ToString().ToUpperInvariant(),
                result.Details.Code + " · " + result.Details.Reason,
                (result.Details.Coverage * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",
                result.Evidence.Value.ToString("D")));
        }
        Status = Results.Count == 0 ? "未选中具有采样的信号" :
            "比较结束；结果为离线开发判定，不代表真实出厂 PASS";
    }

    private WaveSignalRow[] Selected() => Signals.Where(x => x.IsSelected).Take(8).ToArray();

    public void SetCursorFraction(double fraction, bool second)
    {
        if (!double.IsFinite(fraction) || _captures.Count == 0) return;
        var seconds = _start + Math.Clamp(fraction, 0, 1) * (_end - _start);
        if (second) _cursorB = seconds;
        else _cursorA = seconds;
        Render();
    }

    public void Zoom(double factor)
    {
        if (_captures.Count == 0 || !double.IsFinite(factor) || factor <= 0) return;
        var center = double.IsFinite(_cursorA) ? _cursorA : (_start + _end) / 2;
        var length = Math.Clamp((_end - _start) * factor, .001, 3600);
        _start = center - length / 2; _end = center + length / 2;
        Render();
    }

    public void ResetZoom()
    {
        var selected = Selected().Where(x => _captures.ContainsKey(x.Choice.Signal))
            .Select(x => _captures[x.Choice.Signal].Series).ToArray();
        if (selected.Length == 0) { _start = 0; _end = 1; Render(); return; }
        var points = selected.SelectMany(series => WaveChartAnalysis.FromSeries(series)).ToArray();
        if (points.Length == 0) { _start = 0; _end = 1; Render(); return; }
        _start = points.Min(p => p.Seconds);
        _end = Math.Max(_start + .001, points.Max(p => p.Seconds));
        Render();
    }

    private void Render()
    {
        Traces.Clear();
        var selected = Selected().Where(x => _captures.ContainsKey(x.Choice.Signal)).ToArray();
        var n = selected.Length;
        for (var i = 0; i < n; i++)
        {
            var key = selected[i].Choice.Signal;
            var captured = _captures[key];
            var data = WaveChartAnalysis.FromSeries(captured.Series);
            System.Collections.Immutable.ImmutableArray<WaveChartPoint> samples;
            try { samples = WaveChartAnalysis.InWindow(data, _start, _end, 900); }
            catch (InvalidDataException ex)
            {
                Status = "INVALID_WAVE_TIME: " + ex.Message;
                continue;
            }
            var reference = _references.TryGetValue(key, out var pinned) ? pinned : null;
            var refPoints = reference is null ? Array.Empty<WaveChartPoint>() :
                WaveChartAnalysis.FromSeries(reference.Series)
                    .Where(x => x.Seconds >= _start && x.Seconds <= _end).ToArray();
            var combined = samples.Concat(refPoints).ToArray();
            if (combined.Length == 0) continue;
            var min = combined.Min(x => x.Value);
            var max = combined.Max(x => x.Value);
            if (max <= min) { min -= 1; max += 1; }
            var span = max - min;
            var bandTop = 14 + i * 328.0 / n;
            var bandHeight = 308.0 / n;
            PointCollection Project(IEnumerable<WaveChartPoint> values) =>
                new(values.Select(x => new Point(48 + (x.Seconds - _start) / (_end - _start) * 852,
                    bandTop + 4 + (max - x.Value) / span * (bandHeight - 16))));
            var label = $"{key.SignalName} [{key.Unit}] {min:0.###}～{max:0.###}";
            Traces.Add(new WaveDrawTrace(label, Colors[i % Colors.Length], Project(samples), label, false));
            if (reference is not null)
                Traces.Add(new WaveDrawTrace(label + " / 参考", Brushes.Goldenrod,
                    Project(refPoints), label, true));
        }
        CursorLineA = CursorLine(_cursorA);
        CursorLineB = CursorLine(_cursorB);
        ViewRange = $"{_start:0.###} s ～ {_end:0.###} s";
        var first = selected.FirstOrDefault();
        if (first is not null)
        {
            var capture = _captures[first.Choice.Signal];
            var quality = WaveTimeQuality.Describe(capture.Series);
            Quality = $"{quality.Code} / {capture.Series.Origin.Quality} / {capture.Series.Samples.Length} samples";
            Origin = capture.SourceLabel + " / " + capture.Series.SourceRevision.Sha256[..8];
            ReferenceStatus = _references.TryGetValue(first.Choice.Signal, out var refer)
                ? refer.Name + " / " + refer.Reference.Asset.Fingerprint.Sha256[..8]
                : "当前信号没有固定参考";
            var points = WaveChartAnalysis.FromSeries(capture.Series);
            var measured = double.IsFinite(_cursorA) && double.IsFinite(_cursorB)
                ? WaveChartAnalysis.Measure(points, _cursorA, _cursorB) : null;
            var a = double.IsFinite(_cursorA) ? WaveChartAnalysis.Nearest(points, _cursorA) : null;
            CursorInfo = measured is not null
                ? $"游标 Δt={measured.DeltaSeconds:0.###}s, Δv={measured.DeltaValue:0.###}{capture.Series.Signal.Unit}, min/max={measured.Minimum:0.###}/{measured.Maximum:0.###}"
                : a is not null ? $"游标 A: {a.Seconds:0.###} s, {a.Value:0.###} {capture.Series.Signal.Unit}；Shift+单击设置 B"
                : "单击设置游标 A；Shift+单击设置游标 B";
        }
        else
        {
            Origin = "Unknown"; Quality = "Unknown"; ReferenceStatus = "没有参考波形";
            CursorInfo = "单击设置游标 A；Shift+单击设置游标 B";
        }
        RefreshCommands();
    }

    private PointCollection CursorLine(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < _start || seconds > _end) return new();
        var x = 48 + (seconds - _start) / (_end - _start) * 852;
        return new PointCollection([new Point(x, 12), new Point(x, 334)]);
    }

    private async void RunAsync(Func<CancellationToken, Task> action) =>
        await RunOperationAsync(action);

    private async Task RunOperationAsync(Func<CancellationToken, Task> action)
    {
        if (_busy) return;
        _busy = true;
        _pending = new CancellationTokenSource();
        Status = "正在执行 WAVE 操作… 可取消";
        OnChanged(nameof(Busy)); RefreshCommands();
        try { await action(_pending.Token); }
        catch (OperationCanceledException) { Status = "操作已取消，未宣称完成"; }
        catch (Exception ex) { Status = "WAVE_ERROR：" + ex.GetType().Name + " / " + ex.Message; }
        finally
        {
            _pending.Dispose(); _pending = null;
            _busy = false;
            if (_refreshAfterCurrentOperation)
            {
                _refreshAfterCurrentOperation = false;
                LoadSignals();
                Status = "DBC_REVISION_CHANGED: Wave signal list reloaded after cancelling the previous operation";
            }
            OnChanged(nameof(Busy)); RefreshCommands();
        }
    }

    private void Stop() => _pending?.Cancel();

    private void RefreshCommands()
    {
        OnChanged(nameof(CanOperate)); OnChanged(nameof(HasCapture));
        OnChanged(nameof(CaptureCount)); OnChanged(nameof(LiveAvailable));
        OnChanged(nameof(ArchiveAvailable));
        _reload.Raise(); _replay.Raise(); _mock.Raise(); _openArchive.Raise(); _live.Raise(); _stop.Raise(); _save.Raise();
        _load.Raise(); _compare.Raise(); _zoomIn.Raise(); _zoomOut.Raise(); _reset.Raise();
    }
    private void Set<T>(ref T field, T next, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, next)) return;
        field = next; OnChanged(name);
    }
    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class WaveCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute();
        public void Execute(object? parameter) { if (canExecute()) execute(); }
        public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
