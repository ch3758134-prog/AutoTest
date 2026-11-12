using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop.Tools;

/// <summary>UI-only actions. The composition root binds these to WaveCaptureService / recorded evidence.</summary>
public interface IWaveToolActions
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task MarkReferenceAsync(CancellationToken ct);
    Task CompareAsync(CancellationToken ct);
    Task ReplayAsync(CancellationToken ct);
}

public sealed record WavePlotPoint(double Seconds, double Value);
public sealed record WaveReadingRow(double Seconds, double Actual, double? Reference,
    string Unit, string Status);

/// <summary>Interactive standalone waveform view-model; contains no CAN/hardware access.</summary>
public sealed class WaveToolViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly DelegateCommand _start;
    private readonly DelegateCommand _stop;
    private readonly DelegateCommand _mark;
    private readonly DelegateCommand _compare;
    private readonly DelegateCommand _replay;
    private IWaveToolActions? _actions;
    private bool _busy;
    private string _status = "未连接信号源";
    private string _signal = "请选择 DBC 信号";
    private string _origin = "无采样";
    private string _quality = "未知";
    private string _referenceName = "未选择参考波形";
    private string _verdict = "未判定";
    private string _coverage = "—";
    private string _offset = "—";
    private string _sampleCount = "0";
    private string _xStart = "—";
    private string _xMid = "—";
    private string _xEnd = "—";
    private string _yMin = "—";
    private string _yMax = "—";
    private PointCollection _livePoints = new();
    private PointCollection _referencePoints = new();
    private PointCollection _highPoints = new();
    private PointCollection _lowPoints = new();

    public WaveToolViewModel()
    {
        _start = new DelegateCommand(() => Execute(a => a.StartAsync), () => _actions is not null && !_busy);
        _stop = new DelegateCommand(ExecuteStop, () => _actions is not null);
        _mark = new DelegateCommand(() => Execute(a => a.MarkReferenceAsync), () => _actions is not null && !_busy);
        _compare = new DelegateCommand(() => Execute(a => a.CompareAsync), () => _actions is not null && !_busy);
        _replay = new DelegateCommand(() => Execute(a => a.ReplayAsync), () => _actions is not null && !_busy);
    }

    public ICommand StartCommand => _start;
    public ICommand StopCommand => _stop;
    public ICommand MarkReferenceCommand => _mark;
    public ICommand CompareCommand => _compare;
    public ICommand ReplayCommand => _replay;
    public ObservableCollection<WaveReadingRow> Readings { get; } = new();
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Signal { get => _signal; private set => Set(ref _signal, value); }
    public string Origin { get => _origin; private set => Set(ref _origin, value); }
    public string Quality { get => _quality; private set => Set(ref _quality, value); }
    public string ReferenceName { get => _referenceName; private set => Set(ref _referenceName, value); }
    public string Verdict { get => _verdict; private set => Set(ref _verdict, value); }
    public string Coverage { get => _coverage; private set => Set(ref _coverage, value); }
    public string Offset { get => _offset; private set => Set(ref _offset, value); }
    public string SampleCount { get => _sampleCount; private set => Set(ref _sampleCount, value); }
    public string XStart { get => _xStart; private set => Set(ref _xStart, value); }
    public string XMid { get => _xMid; private set => Set(ref _xMid, value); }
    public string XEnd { get => _xEnd; private set => Set(ref _xEnd, value); }
    public string YMin { get => _yMin; private set => Set(ref _yMin, value); }
    public string YMax { get => _yMax; private set => Set(ref _yMax, value); }
    public PointCollection LivePoints { get => _livePoints; private set => Set(ref _livePoints, value); }
    public PointCollection ReferencePoints { get => _referencePoints; private set => Set(ref _referencePoints, value); }
    public PointCollection UpperTolerancePoints { get => _highPoints; private set => Set(ref _highPoints, value); }
    public PointCollection LowerTolerancePoints { get => _lowPoints; private set => Set(ref _lowPoints, value); }

    public void BindActions(IWaveToolActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        Status = "就绪 · 等待采集";
        RaiseCommands();
    }

    public void ShowChart(string signal, string origin, string quality,
        IReadOnlyList<WavePlotPoint> actual, IReadOnlyList<WavePlotPoint>? reference,
        double absoluteTolerance, string unit, string referenceName)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (!double.IsFinite(absoluteTolerance) || absoluteTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(absoluteTolerance));
        if (actual.Any(p => !double.IsFinite(p.Seconds) || !double.IsFinite(p.Value)) ||
            (reference?.Any(p => !double.IsFinite(p.Seconds) || !double.IsFinite(p.Value)) ?? false))
            throw new ArgumentException("Wave chart requires finite coordinates.");
        Signal = signal;
        Origin = origin;
        Quality = quality;
        ReferenceName = referenceName;
        SampleCount = actual.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var all = actual.Concat(reference ?? Array.Empty<WavePlotPoint>()).ToArray();
        if (all.Length == 0)
        {
            LivePoints = new(); ReferencePoints = new();
            UpperTolerancePoints = new(); LowerTolerancePoints = new();
            Readings.Clear(); Status = "无可绘制采样"; return;
        }
        var minTime = all.Min(p => p.Seconds);
        var maxTime = all.Max(p => p.Seconds);
        if (maxTime <= minTime) maxTime = minTime + 1;
        var minValue = all.Min(p => p.Value) - absoluteTolerance;
        var maxValue = all.Max(p => p.Value) + absoluteTolerance;
        if (maxValue <= minValue) { minValue -= 1; maxValue += 1; }
        var padding = (maxValue - minValue) * 0.12;
        minValue -= padding; maxValue += padding;
        XStart = minTime.ToString("0.###");
        XMid = ((minTime + maxTime) / 2).ToString("0.###");
        XEnd = maxTime.ToString("0.###");
        YMin = minValue.ToString("0.###") + " " + unit;
        YMax = maxValue.ToString("0.###") + " " + unit;
        Point Project(double t, double v) =>
            new(44 + (t - minTime) / (maxTime - minTime) * 866,
                328 - (v - minValue) / (maxValue - minValue) * 286);
        LivePoints = new PointCollection(actual.Select(p => Project(p.Seconds, p.Value)));
        ReferencePoints = new PointCollection((reference ?? Array.Empty<WavePlotPoint>())
            .Select(p => Project(p.Seconds, p.Value)));
        UpperTolerancePoints = new PointCollection((reference ?? Array.Empty<WavePlotPoint>())
            .Select(p => Project(p.Seconds, p.Value + absoluteTolerance)));
        LowerTolerancePoints = new PointCollection((reference ?? Array.Empty<WavePlotPoint>())
            .Select(p => Project(p.Seconds, p.Value - absoluteTolerance)));
        Readings.Clear();
        // Present a bounded inspection table; full evidence remains in WaveEvidenceStore.
        var referenceSorted = (reference ?? Array.Empty<WavePlotPoint>())
            .OrderBy(x => x.Seconds).ToArray();
        var referenceTimes = referenceSorted.Select(x => x.Seconds).ToArray();
        foreach (var point in actual.Take(500))
        {
            double? nearest = null;
            if (referenceTimes.Length != 0)
            {
                var index = Array.BinarySearch(referenceTimes, point.Seconds);
                if (index < 0) index = ~index;
                if (index >= referenceTimes.Length) index = referenceTimes.Length - 1;
                if (index > 0 && Math.Abs(referenceTimes[index - 1] - point.Seconds) <
                                 Math.Abs(referenceTimes[index] - point.Seconds)) index--;
                nearest = referenceSorted[index].Value;
            }
            Readings.Add(new WaveReadingRow(point.Seconds, point.Value, nearest, unit, "采样"));
        }
        Status = "波形已更新 · 数据来源 " + origin;
    }

    public void ShowComparison(string verdict, double? coverage, double? offsetSeconds,
        string detail)
    {
        Verdict = verdict;
        Coverage = coverage is null ? "—" : (coverage.Value * 100).ToString("0.0") + "%";
        Offset = offsetSeconds is null ? "—" : (offsetSeconds.Value * 1000).ToString("0.###") + " ms";
        Status = detail;
    }

    private async void ExecuteStop()
    {
        if (_actions is null) return;
        try { await _actions.StopAsync(CancellationToken.None); }
        catch (Exception ex) { Status = "停止失败：" + ex.Message; }
    }

    private async void Execute(Func<IWaveToolActions, Func<CancellationToken, Task>> action)
    {
        if (_actions is null || _busy) return;
        _busy = true;
        RaiseCommands();
        try { await action(_actions)(CancellationToken.None); }
        catch (Exception ex) { Status = "操作失败：" + ex.Message; }
        finally { _busy = false; RaiseCommands(); }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private void RaiseCommands()
    {
        _start.Refresh(); _stop.Refresh(); _mark.Refresh(); _compare.Refresh(); _replay.Refresh();
    }

    private sealed class DelegateCommand(Action action, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute();
        public void Execute(object? parameter) => action();
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
