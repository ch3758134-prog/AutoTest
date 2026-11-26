using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AutoTest.Core;
using AutoTest.Contracts;
using AutoTest.Desktop.Testing;
using AutoTest.Specialists.Wave;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>A single real DBC signal chosen by the operator. The displayed samples
/// are bounded previews only; immutable evidence is captured independently.</summary>
public sealed class WaveSignalTrack : BindableBase
{
    private readonly Queue<WaveChartPoint> _recent = new();
    private string _latest = "—";
    private string _state = "等待数据";
    private long _received;
    private bool _visible = true;
    private bool _stepStyle;
    private bool _hasGap;
    private bool _hasTimeError;
    private string _clockLabel = "未收到时间戳";
    public MappingChoice Choice { get; }
    public string Key => Choice.Mapping.Name;
    public string Name => Choice.Mapping.SignalName;
    public string Unit => Choice.Mapping.Unit;
    public string Subtitle => "0x" + Choice.Mapping.CanId.ToString("X") +
        (string.IsNullOrWhiteSpace(Unit) ? "" : " · " + Unit);
    public string Color { get; }
    public string Latest { get => _latest; private set => SetProperty(ref _latest, value); }
    public string State { get => _state; private set => SetProperty(ref _state, value); }
    public long Received { get => _received; private set => SetProperty(ref _received, value); }
    public bool Visible { get => _visible; set => SetProperty(ref _visible, value); }
    public DateTimeOffset? LastSampleUtc { get; private set; }
    public IReadOnlyList<WaveChartPoint> Points => _recent.ToArray();
    public bool StepStyle
    {
        get => _stepStyle;
        set => SetProperty(ref _stepStyle, value);
    }
    public string ClockLabel
    {
        get => _clockLabel;
        private set => SetProperty(ref _clockLabel, value);
    }

    public WaveSignalTrack(MappingChoice choice, string color)
    {
        Choice = choice;
        Color = color;
        _stepStyle = choice.ValueLabels is { Count: > 0 };
    }

    public void Reset()
    {
        _recent.Clear();
        _hasGap = false;
        _hasTimeError = false;
        ClockLabel = "未收到时间戳";
        LastSampleUtc = null;
        Latest = "—";
        State = "等待 CAN 帧";
        Received = 0;
    }

    public void Add(double seconds, double value, DateTimeOffset utc, bool gap,
        TimeQuality quality)
    {
        if (!double.IsFinite(seconds) || !double.IsFinite(value)) { State = "无效数据"; return; }
        if (_recent.TryPeek(out _) && _recent.Count > 0 &&
            _recent.Last().Seconds >= seconds)
        {
            _hasTimeError = true;
            State = "时间戳未递增";
            return;
        }
        _recent.Enqueue(new WaveChartPoint(seconds, value));
        while (_recent.Count > 6000) _recent.Dequeue();
        Latest = value.ToString("G6", CultureInfo.InvariantCulture) +
            (string.IsNullOrWhiteSpace(Unit) ? "" : " " + Unit);
        LastSampleUtc = utc;
        Received++;
        _hasGap |= gap;
        ClockLabel = quality switch
        {
            TimeQuality.HostArrivalOnly => "主机接收时间 · 非硬件同步",
            TimeQuality.SynchronizedHardware => "硬件同步时间",
            _ => "时间质量：" + quality
        };
        State = _hasTimeError ? "时间序列异常" :
            _hasGap ? "曾出现数据缺口" : "接收正常";
    }

    public void Mark(string message) => State = message;
}

/// <summary>One displayed lane uses a shared horizontal time axis. Reference
/// overlay is only allowed for the same physical signal and unit.</summary>
public sealed record WavePlotLane(string Name, string Unit, string Color,
    IReadOnlyList<WaveChartPoint> Actual, IReadOnlyList<WaveChartPoint>? Reference,
    double? Tolerance, bool StepStyle, string Detail);

public sealed record WaveTimeChoice(int Seconds, string Label);
public sealed record WaveCompareModeChoice(WaveInterpolationMode Mode, string Label);

public sealed record WaveSessionItem(Guid Id, DateTimeOffset Started, Guid[] EvidenceIds,
    string[] Names)
{
    public string Title => Started.ToLocalTime().ToString("MM-dd HH:mm:ss") +
        " · " + EvidenceIds.Length + " 信号";
    public string Detail => string.Join("、", Names.Take(4)) +
        (Names.Length > 4 ? " …" : "");
}

public sealed partial class WaveWorkbenchViewModel
{
    private static readonly string[] TraceColors =
    ["#2872BC", "#D08322", "#14816A", "#805FB5", "#B45C51",
     "#218DAD", "#746A54", "#A94D85", "#337F4D", "#BC9533",
     "#4977A8", "#A35C34"];
    private bool _isLiveView = true;
    private int _windowSeconds = 10;
    private double _viewOffsetSeconds;
    private bool _historyStepStyle;
    private WaveSignalTrack? _selectedTrack;
    private WaveSessionItem? _selectedSession;
    private readonly List<WaveSeries> _sessionSeries = new();
    private int _sessionLoadGeneration;
    private WaveReferenceEntry? _selectedReference;
    private WaveSeries? _referenceOverlay;
    private WaveComparisonDetails? _comparison;
    private string _comparisonSummary = "选择一条已保存的实际波形和对应参考，然后执行比较。";
    private string _absoluteToleranceText = "";
    private string _minimumCoverageText = "0.95";
    private WaveCompareModeChoice _selectedCompareMode =
        new(WaveInterpolationMode.Linear, "连续数值（线性插值）");

    public IReadOnlyList<WaveCompareModeChoice> CompareModes { get; } =
    [
        new(WaveInterpolationMode.Linear, "连续数值（线性插值）"),
        new(WaveInterpolationMode.StepHold, "离散状态（阶梯保持）")
    ];
    public WaveCompareModeChoice SelectedCompareMode
    {
        get => _selectedCompareMode;
        set
        {
            if (value is not null && SetProperty(ref _selectedCompareMode, value))
                CompareCommand?.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<WaveSignalTrack> Tracks { get; } = new();
    public ObservableCollection<WaveSessionItem> Sessions { get; } = new();
    public ObservableCollection<WaveItem> Comparisons { get; } = new();
    public ObservableCollection<WaveItem> Captures { get; } = new();
    public ObservableCollection<WaveReferenceEntry> References { get; } = new();
    public IReadOnlyList<WaveTimeChoice> WindowChoices { get; } =
    [new(0, "全范围"), new(5, "5 秒"), new(10, "10 秒"),
     new(30, "30 秒"), new(60, "60 秒"), new(120, "120 秒")];
    public WaveTimeChoice SelectedWindow
    {
        get => WindowChoices.First(x => x.Seconds == TimeWindowSeconds);
        set { if (value is not null) TimeWindowSeconds = value.Seconds; }
    }

    public DelegateCommand<WaveSignalTrack> RemoveTrackCommand { get; private set; } = null!;
    public DelegateCommand ShowLiveCommand { get; private set; } = null!;
    public DelegateCommand ShowArchiveCommand { get; private set; } = null!;
    public DelegateCommand CompareCommand { get; private set; } = null!;
    public DelegateCommand ClearComparisonCommand { get; private set; } = null!;
    public DelegateCommand<WaveReferenceEntry> ViewReferenceCommand { get; private set; } = null!;
    public string ComparisonTone => _comparison is null ? "Idle" :
        _comparison.Verdict switch
        {
            Verdict.Pass => "Pass",
            Verdict.Fail => "Fail",
            Verdict.Inconclusive => "Warning",
            _ => "Warning"
        };
    public DelegateCommand EarlierWindowCommand { get; private set; } = null!;
    public DelegateCommand LaterWindowCommand { get; private set; } = null!;
    public DelegateCommand LatestWindowCommand { get; private set; } = null!;

    public WaveSignalTrack? SelectedTrack
    {
        get => _selectedTrack;
        set { if (SetProperty(ref _selectedTrack, value)) NotifyChartChanged(); }
    }
    public bool IsLiveView
    {
        get => _isLiveView;
        private set
        {
            if (!SetProperty(ref _isLiveView, value)) return;
            _viewOffsetSeconds = 0;
            RaisePropertyChanged(nameof(DisplayMode));
            NotifyChartChanged();
        }
    }
    public int TimeWindowSeconds
    {
        get => _windowSeconds;
        set
        {
            if (!WindowChoices.Any(x => x.Seconds == value) ||
                !SetProperty(ref _windowSeconds, value)) return;
            _viewOffsetSeconds = 0;
            RaisePropertyChanged(nameof(SelectedWindow));
            NotifyChartChanged();
        }
    }
    public WaveSessionItem? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value) && value is not null)
            {
                Selected = null;
                IsLiveView = false;
                _ = LoadSessionAsync(value);
            }
        }
    }
    public WaveReferenceEntry? SelectedReference
    {
        get => _selectedReference;
        set
        {
            if (SetProperty(ref _selectedReference, value))
            {
                _referenceOverlay = null;
                _comparison = null;
                ComparisonSummary = "已选择参考；点击“比较”计算质量、覆盖率与偏差。";
                RaisePropertyChanged(nameof(ComparisonTone));
                CompareCommand?.RaiseCanExecuteChanged();
                NotifyChartChanged();
            }
        }
    }
    public string AbsoluteToleranceText
    {
        get => _absoluteToleranceText;
        set { if (SetProperty(ref _absoluteToleranceText, value)) CompareCommand?.RaiseCanExecuteChanged(); }
    }
    public string MinimumCoverageText
    {
        get => _minimumCoverageText;
        set { if (SetProperty(ref _minimumCoverageText, value)) CompareCommand?.RaiseCanExecuteChanged(); }
    }
    public string ComparisonSummary
    {
        get => _comparisonSummary;
        private set => SetProperty(ref _comparisonSummary, value);
    }
    public string DisplayMode => IsLiveView ? "实时观察" : "历史分析";
    public bool HistoryStepStyle
    {
        get => _historyStepStyle;
        set { if (SetProperty(ref _historyStepStyle, value)) NotifyChartChanged(); }
    }
    public string DataQuality => _series is null ? "暂无已打开的历史记录" :
        DescribeQuality(_series);
    private static string DescribeQuality(WaveSeries series)
    {
        var q = WaveTimeQuality.Describe(series);
        var time = series.Origin.Quality switch
        {
            TimeQuality.SynchronizedHardware => "硬件同步时间",
            TimeQuality.HostArrivalOnly => "主机接收时间（不保证精密时序）",
            _ => "时间质量：" + series.Origin.Quality
        };
        return (q.CanJudge ? "样本完整性通过" : "数据质量待处理：" + q.Code) +
            " · " + series.Samples.Length + " 样本 · " + time;
    }

    private void InitializeWorkspace()
    {
        EarlierWindowCommand = new DelegateCommand(() =>
        {
            if (TimeWindowSeconds <= 0) return;
            _viewOffsetSeconds += Math.Max(1, TimeWindowSeconds / 2d);
            NotifyChartChanged();
        });
        LaterWindowCommand = new DelegateCommand(() =>
        {
            _viewOffsetSeconds = Math.Max(0,
                _viewOffsetSeconds - Math.Max(1, TimeWindowSeconds / 2d));
            NotifyChartChanged();
        });
        LatestWindowCommand = new DelegateCommand(() =>
        {
            _viewOffsetSeconds = 0;
            NotifyChartChanged();
        });
        ViewReferenceCommand = new DelegateCommand<WaveReferenceEntry>(reference =>
        {
            if (reference is null) return;
            var found = Items.FirstOrDefault(item =>
                item.IsReference && item.Id == reference.Reference.Asset.Id.Value);
            if (found is not null) Selected = found;
        });
        RemoveTrackCommand = new DelegateCommand<WaveSignalTrack>(track =>
        {
            if (track is null || IsObserving || IsCapturing) return;
            Tracks.Remove(track);
            if (ReferenceEquals(SelectedTrack, track)) SelectedTrack = Tracks.FirstOrDefault();
            RefreshCaptureCommands();
            NotifyChartChanged();
        }, track => track is not null && !IsObserving && !IsCapturing);
        ShowLiveCommand = new DelegateCommand(() =>
        {
            IsLiveView = true;
            RaisePropertyChanged(nameof(DisplayMode));
        });
        ShowArchiveCommand = new DelegateCommand(() =>
        {
            IsLiveView = false;
            RaisePropertyChanged(nameof(DisplayMode));
        });
        CompareCommand = new DelegateCommand(async () => await CompareSelectedAsync(),
            () => !_closed && !IsCapturing && _series is not null &&
                Selected is { IsReference: false, Kind: "Capture" } && SelectedReference is not null &&
                !string.IsNullOrWhiteSpace(AbsoluteToleranceText));
        ClearComparisonCommand = new DelegateCommand(() =>
        {
            _referenceOverlay = null;
            _comparison = null;
            ComparisonSummary = "比较已清除；不会修改原始采集与参考文件。";
            RaisePropertyChanged(nameof(ComparisonTone));
            NotifyChartChanged();
        });
    }

    public IReadOnlyList<WavePlotLane> GetPlotLanes()
    {
        if (IsLiveView)
            return Tracks.Where(t => t.Visible).Select(t =>
                new WavePlotLane(t.Name, t.Unit, t.Color, t.Points, null, null,
                    t.StepStyle, t.State)).ToArray();
        if (_sessionSeries.Count > 0)
        {
            var start = _sessionSeries.Min(s => s.Origin.Utc);
            return _sessionSeries.Select((s, i) =>
            {
                var offset = (s.Origin.Utc - start).TotalSeconds;
                var points = WaveChartAnalysis.FromSeries(s).Select(p =>
                    new WaveChartPoint(p.Seconds + offset, p.Value)).ToArray();
                return new WavePlotLane(s.Signal.SignalName, s.Signal.Unit,
                    TraceColors[i % TraceColors.Length], points, null, null, HistoryStepStyle,
                    DescribeQuality(s));
            }).ToArray();
        }
        if (_series is null) return Array.Empty<WavePlotLane>();
        var refPoints = _referenceOverlay is null ? null :
            WaveChartAnalysis.FromSeries(_referenceOverlay).Select(p =>
                new WaveChartPoint(p.Seconds + (_comparison?.ShiftSeconds ?? 0), p.Value)).ToArray();
        return [new WavePlotLane(_series.Signal.SignalName, _series.Signal.Unit,
            TraceColors[0], _points, refPoints,
            _comparison is null ? null :
                double.TryParse(AbsoluteToleranceText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var tol) ? tol : null,
            HistoryStepStyle, DataQuality)];
    }

    public (double Min, double Max)? VisibleTimeBounds(IReadOnlyList<WavePlotLane> lanes)
    {
        var points = lanes.SelectMany(x => x.Actual.Concat(
            x.Reference ?? Array.Empty<WaveChartPoint>())).ToArray();
        if (points.Length == 0)
            return lanes.Count == 0 ? null :
                (0d, (double)(TimeWindowSeconds == 0 ? 10 : TimeWindowSeconds));
        var last = points.Max(x => x.Seconds);
        var earliest = points.Min(x => x.Seconds);
        var end = TimeWindowSeconds == 0 ? last :
            Math.Max(earliest, last - _viewOffsetSeconds);
        var start = TimeWindowSeconds == 0 ? earliest :
            Math.Max(earliest, end - TimeWindowSeconds);
        if (end - start < 0.001) end = start + 1;
        return (start, end);
    }

    private async Task LoadSessionAsync(WaveSessionItem session)
    {
        var version = ++_sessionLoadGeneration;
        var root = WaveSourceRoot;
        var sourceIdentity = WaveSourceIdentity;
        if (root is null || sourceIdentity is null) return;
        Status = "正在读取同次采集的真实信号…";
        try
        {
            var actual = await Task.Run(() =>
            {
                var store = new WaveEvidenceStore(Path.Combine(root, "evidence"));
                return session.EvidenceIds.Select(id => store.Read(new EvidenceRef(id)).Actual).ToArray();
            });
            if (_closed || version != _sessionLoadGeneration || SelectedSession != session ||
                WaveSourceIdentity != sourceIdentity) return;
            _sessionSeries.Clear();
            _sessionSeries.AddRange(actual);
            IsLiveView = false;
            _series = null;
            _points = Array.Empty<WaveChartPoint>();
            Info = session.Title + " · 共享真实时间轴 · " + actual.Length + " 条信号";
            Status = "已打开多信号历史记录。参考建立请在左侧选择其中一条原始证据。";
            NotifyChartChanged();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            if (!_closed && version == _sessionLoadGeneration)
                Status = "读取成组波形失败：" + ex.Message;
        }
    }

    private async Task CompareSelectedAsync()
    {
        if (_series is null || Selected is not { IsReference: false, Kind: "Capture" } ||
            SelectedReference is not { } reference || WaveSourceRoot is null)
            return;
        if (!double.TryParse(AbsoluteToleranceText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var tolerance) ||
            !double.TryParse(MinimumCoverageText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var coverage) ||
            !double.IsFinite(tolerance) || tolerance < 0 ||
            !double.IsFinite(coverage) || coverage <= 0 || coverage > 1)
        {
            ComparisonSummary = "请填写合法的非负绝对容差与 0～1 的最小覆盖率（使用小数点）。";
            return;
        }
        var interpolation = SelectedCompareMode.Mode;
        var actual = _series;
        var selected = Selected;
        var root = WaveSourceRoot!;
        var sourceIdentity = WaveSourceIdentity;
        Status = "正在核对参考身份与时间质量并进行分析…";
        try
        {
            var completed = await Task.Run(async () =>
            {
                var evidenceStore = new WaveEvidenceStore(Path.Combine(root, "evidence"));
                var referenceStore = new WaveReferenceRepository(Path.Combine(
                    root, "references", "assets"));
                var referenceSeries = referenceStore.LoadReference(reference.Reference).Series;
                var source = evidenceStore.Read(new EvidenceRef(selected!.Id));
                if (source.Kind != "Capture")
                    throw new InvalidDataException("只能对真实采集记录进行新的参考比较。");
                var settings = new WaveComparisonConfig(tolerance, 0, coverage,
                    1, 0.05, 1.6, 0, 0)
                {
                    InterpolationMode = interpolation
                };
                var engine = new WaveCaptureService(_liveSignals, evidenceStore, referenceStore);
                var handle = new WaveEvidenceRef(source.Id,
                    source.Actual.SourceRevision, source.Actual.Signal.Unit,
                    source.Actual.Origin.Quality);
                var result = await engine.CompareWithConfigForRunAsync(
                    handle, reference.Reference, settings);
                var comparisonId = result.Evidence.Length > 1
                    ? result.Evidence[^1] : (EvidenceRef?)null;
                var details = comparisonId.HasValue
                    ? engine.ReadEvidence(comparisonId.Value).Result
                    : WaveComparator.Compare(source.Actual, referenceSeries, settings);
                return (Reference: referenceSeries, Details: details,
                    Id: comparisonId, Result: result);
            });
            if (_closed || WaveSourceIdentity != sourceIdentity ||
                !ReferenceEquals(actual, _series) || Selected != selected) return;
            var details = completed.Details;
            if (details is null) throw new InvalidDataException("比较结果没有可校验的算法数据。");
            _comparison = details;
            RaisePropertyChanged(nameof(ComparisonTone));
            _referenceOverlay = details.Code == "SIGNAL_REVISION_MISMATCH" ? null :
                completed.Reference;
            ComparisonSummary = "分析结果 " + details.Verdict +
                " · " + (interpolation == WaveInterpolationMode.StepHold
                    ? "离散状态 · 按跳变区间计时" : "连续数值 · 线性插值") +
                " · 覆盖率 " + details.Coverage.ToString("P1") +
                " · RMSE " + details.Rmse.ToString("G4") +
                " · 最大偏差 " + details.MaximumAbsoluteError.ToString("G4") +
                " · 超差 " + details.OutsideToleranceSeconds.ToString("F4") + " s" +
                " · " + details.Code +
                (completed.Id.HasValue
                    ? " · 已归档比较证据 " + completed.Id.Value.Value.ToString("N")[..8]
                    : " · 未生成比较证据");
            IsLiveView = false;
            if (completed.Id is { } savedId)
            {
                var item = new WaveItem(actual.Signal.SignalName,
                    "比较 · " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    " · " + details.Verdict, savedId.Value, false, null, "Comparison");
                Items.Insert(0, item);
                Comparisons.Insert(0, item);
            }
            Status = "已使用真实采集与参考证据执行比较；不会自动改变 TEST 判定。";
            NotifyChartChanged();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            ComparisonSummary = "无法比较：" + ex.Message;
        }
    }

    private sealed record SessionWire(int Schema, Guid Id, DateTimeOffset StartedUtc,
        Guid[] EvidenceIds, string[] Names);

    private static void SaveSessionIndex(string root, DateTimeOffset started,
        IReadOnlyList<Guid> evidence, IReadOnlyList<string> names)
    {
        if (evidence.Count == 0) return;
        var folder = Path.Combine(root, "sessions");
        Directory.CreateDirectory(folder);
        var id = Guid.NewGuid();
        var path = Path.Combine(folder, id.ToString("N") + ".json");
        var temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(
                new SessionWire(1, id, started, evidence.ToArray(), names.ToArray())));
            File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static WaveSessionItem[] ReadSessionIndex(string root)
    {
        var folder = Path.Combine(root, "sessions");
        if (!Directory.Exists(folder)) return [];
        var result = new List<WaveSessionItem>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc).Take(200))
        {
            try
            {
                if (new FileInfo(path).Length is <= 0 or > 1_048_576) continue;
                var item = JsonSerializer.Deserialize<SessionWire>(File.ReadAllBytes(path));
                if (item is null || item.Schema != 1 || item.Id == Guid.Empty ||
                    item.EvidenceIds is null || item.Names is null ||
                    item.EvidenceIds.Length is < 1 or > 16 ||
                    item.Names.Length != item.EvidenceIds.Length ||
                    item.EvidenceIds.Any(id => id == Guid.Empty)) continue;
                result.Add(new WaveSessionItem(item.Id, item.StartedUtc,
                    item.EvidenceIds, item.Names));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result.ToArray();
    }
}
