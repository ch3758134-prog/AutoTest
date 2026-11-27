using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Testing;
using AutoTest.Specialists.Wave;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;
using Prism.Commands;

namespace AutoTest.Desktop.Shell;

/// <summary>Independent, real USB/CAN observation and grouped wave capture.
/// Monitoring is not a TEST judgement. Actual records use the existing
/// loss-aware, immutable WaveCaptureService evidence format.</summary>
public sealed partial class WaveWorkbenchViewModel
{
    private LiveDbcSignalService _liveSignals = null!;
    private HardwareConnectionService _hardware = null!;
    private RealTestWorkbenchService _realTest = null!;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly ConcurrentQueue<(string Name, TestSignalResult Result)> _observations = new();
    private int _queuedObservations;
    private long _discardedPreviewUpdates;
    private bool _isObserving;
    private bool _isCapturing;
    private bool _previewPaused;
    private DateTimeOffset _observedFromUtc;
    private MappingChoice? _selectedSignal;
    private int _durationSeconds = 0;
    private string _referenceName = "";
    private bool _savingReference;
    private bool _toolUsbConnecting;
    public DelegateCommand ConnectToolUsbCommand { get; private set; } = null!;
    public DelegateCommand DisconnectToolUsbCommand { get; private set; } = null!;
    public string ToolUsbStatus => _toolUsbConnecting ? "USB 正在操作…" :
        _hardware.IsConnected ? "主控已连接 · " + _hardware.EcuCanBitrateStatus :
        "工具 USB 未连接（手动连接）";
    private int _signalRefreshVersion;
    private WaveCaptureService? _captureService;
    private readonly Dictionary<WaveCaptureId, string> _captureIds = new();
    private CancellationTokenSource? _captureTimer;
    private string? _captureManifest;
    private string? _captureRoot;
    private DateTimeOffset _captureStarted;

    public ObservableCollection<MappingChoice> AvailableSignals { get; } = new();
    public IReadOnlyList<WaveTimeChoice> DurationChoices { get; } =
    [new(0, "手动停止"), new(2, "2 秒"), new(5, "5 秒"),
     new(10, "10 秒"), new(20, "20 秒"), new(30, "30 秒"),
     new(60, "60 秒"), new(120, "120 秒"), new(300, "300 秒")];
    public WaveTimeChoice SelectedDuration
    {
        get => DurationChoices.First(x => x.Seconds == CaptureDurationSeconds);
        set { if (value is not null) CaptureDurationSeconds = value.Seconds; }
    }
    public DelegateCommand ReloadSignalsCommand { get; private set; } = null!;
    public DelegateCommand AddSignalCommand { get; private set; } = null!;
    public DelegateCommand StartObserveCommand { get; private set; } = null!;
    public DelegateCommand StopObserveCommand { get; private set; } = null!;
    public DelegateCommand PausePreviewCommand { get; private set; } = null!;
    public DelegateCommand StartCaptureCommand { get; private set; } = null!;
    public DelegateCommand StopCaptureCommand { get; private set; } = null!;
    public DelegateCommand SaveReferenceCommand { get; private set; } = null!;
    public DelegateCommand SaveStandardCommand { get; private set; } = null!;

    public MappingChoice? SelectedSignal
    {
        get => _selectedSignal;
        set { if (SetProperty(ref _selectedSignal, value)) RefreshCaptureCommands(); }
    }
    public int CaptureDurationSeconds
    {
        get => _durationSeconds;
        set
        {
            if (!DurationChoices.Any(x => x.Seconds == value) ||
                !SetProperty(ref _durationSeconds, value)) return;
            RaisePropertyChanged(nameof(SelectedDuration));
            RaisePropertyChanged(nameof(CaptureDurationLabel));
        }
    }
    public string CaptureDurationLabel => CaptureDurationSeconds == 0
        ? "手动停止（最长 60 分钟）" : CaptureDurationSeconds + " 秒";
    public string ReferenceName
    {
        get => _referenceName;
        set { if (SetProperty(ref _referenceName, value)) RefreshCaptureCommands(); }
    }
    public bool IsObserving
    {
        get => _isObserving;
        private set { if (SetProperty(ref _isObserving, value)) RefreshCaptureCommands(); }
    }
    public bool IsCapturing
    {
        get => _isCapturing;
        private set { if (SetProperty(ref _isCapturing, value)) RefreshCaptureCommands(); }
    }
    public bool IsPreviewPaused
    {
        get => _previewPaused;
        private set { if (SetProperty(ref _previewPaused, value)) RefreshCaptureCommands(); }
    }
    public string PauseLabel => IsPreviewPaused ? "继续画面" : "暂停画面";
    public string CaptureState => IsCapturing
        ? "正在记录真实数据 · " + _captureIds.Count + " 信号"
        : IsObserving
            ? IsPreviewPaused ? "画面已暂停 · 不影响后台原始记录" :
                "正在观察 · " + Tracks.Count + " 信号"
            : "未开始观察 · 不使用模拟数据";
    public string LiveSourceStatus => !_hardware.IsConnected
        ? "USB 未连接" : IsObserving ? "共享 CAN / DBC 真实接收" : "USB 已连接 · 待开始观察";

    private void InitializeCapture(LiveDbcSignalService signals,
        HardwareConnectionService hardware, RealTestWorkbenchService realTest)
    {
        _liveSignals = signals;
        _hardware = hardware;
        _realTest = realTest;
        _hardware.ConnectionChanged += CaptureStationChanged;
        ConnectToolUsbCommand = new DelegateCommand(
            async () => await ConnectToolUsbAsync(),
            () => !_closed && !_toolUsbConnecting && !_hardware.HasSession);
        DisconnectToolUsbCommand = new DelegateCommand(
            async () => await DisconnectToolUsbAsync(),
            () => !_closed && !_toolUsbConnecting && _hardware.HasSession &&
                !IsCapturing);

        ReloadSignalsCommand = new DelegateCommand(ReloadAvailableSignals,
            () => !_closed && !IsObserving && !IsCapturing);
        AddSignalCommand = new DelegateCommand(() =>
        {
            if (SelectedSignal is not { } choice || IsObserving || IsCapturing) return;
            if (Tracks.Any(t => t.Key == choice.Mapping.Name))
            {
                Status = "该信号已经在观察列表中。";
                return;
            }
            if (Tracks.Count >= 12)
            {
                Status = "当前工作台最多同时观察 12 路 DBC 信号。";
                return;
            }
            var track = new WaveSignalTrack(choice, TraceColors[Tracks.Count % TraceColors.Length]);
            track.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(WaveSignalTrack.Visible) or
                    nameof(WaveSignalTrack.StepStyle)) NotifyChartChanged();
            };
            Tracks.Add(track);
            SelectedTrack = track;
            Status = "已加入观察列表：" + choice.Mapping.SignalName;
            RefreshCaptureCommands();
            NotifyChartChanged();
        }, () => !_closed && !IsObserving && !IsCapturing && SelectedSignal is not null);
        StartObserveCommand = new DelegateCommand(async () => await StartObservationAsync(),
            () => !_closed && !IsIndependentArchive && !IsObserving && !_realTest.IsRunning &&
                _hardware.IsConnected && Tracks.Count > 0);
        StopObserveCommand = new DelegateCommand(() => StopObservation(),
            () => !_closed && IsObserving && !IsCapturing);
        PausePreviewCommand = new DelegateCommand(() =>
        {
            IsPreviewPaused = !IsPreviewPaused;
            if (!IsPreviewPaused) NotifyChartChanged();
        }, () => !_closed && IsObserving);
        StartCaptureCommand = new DelegateCommand(async () => await StartCaptureAsync(),
            () => !_closed && !IsIndependentArchive && !IsCapturing && !_realTest.IsRunning &&
                _hardware.IsConnected && Tracks.Count > 0);
        StopCaptureCommand = new DelegateCommand(async () => await StopCaptureAsync(),
            () => !_closed && IsCapturing);
        SaveReferenceCommand = new DelegateCommand(
            async () => await SaveReferenceAsync(),
            () => !_closed && !IsCapturing && !_savingReference && _series is not null &&
                Selected is { IsReference: false, Kind: "Capture" } &&
                !string.IsNullOrWhiteSpace(ReferenceName));
        SaveStandardCommand = new DelegateCommand(async () => await SaveStandardAsync(),
            () => !_closed && !IsCapturing && !_savingReference &&
                SelectedSession is not null && _sessionSeries.Count > 0 &&
                !string.IsNullOrWhiteSpace(ReferenceName));
        ReloadAvailableSignals();
    }

    private void RefreshCaptureCommands()
    {
        RaisePropertyChanged(nameof(CaptureState));
        RaisePropertyChanged(nameof(LiveSourceStatus));
        RaisePropertyChanged(nameof(PauseLabel));
        RaisePropertyChanged(nameof(CaptureDurationLabel));
        RaisePropertyChanged(nameof(ToolUsbStatus));
        ConnectToolUsbCommand?.RaiseCanExecuteChanged();
        DisconnectToolUsbCommand?.RaiseCanExecuteChanged();
        ReloadSignalsCommand?.RaiseCanExecuteChanged();
        AddSignalCommand?.RaiseCanExecuteChanged();
        RemoveTrackCommand?.RaiseCanExecuteChanged();
        StartObserveCommand?.RaiseCanExecuteChanged();
        StopObserveCommand?.RaiseCanExecuteChanged();
        PausePreviewCommand?.RaiseCanExecuteChanged();
        StartCaptureCommand?.RaiseCanExecuteChanged();
        StopCaptureCommand?.RaiseCanExecuteChanged();
        SaveReferenceCommand?.RaiseCanExecuteChanged();
        SaveStandardCommand?.RaiseCanExecuteChanged();
    }

    private async Task ConnectToolUsbAsync()
    {
        if (_toolUsbConnecting || _hardware.HasSession) return;
        _toolUsbConnecting = true;
        RefreshCaptureCommands();
        try
        {
            await _hardware.ConnectAsync();
            if (!_hardware.IsEcuCanBitrateApplied)
                await _hardware.ApplyRequestedEcuCanBitrateAsync();
            Status = "波形工具已手动连接真实 USB；" + _hardware.EcuCanBitrateStatus;
        }
        catch (Exception error)
        {
            Status = "波形工具连接失败：" + error.Message;
        }
        finally
        {
            _toolUsbConnecting = false;
            RefreshCaptureCommands();
        }
    }

    private async Task DisconnectToolUsbAsync()
    {
        if (_toolUsbConnecting || IsCapturing) return;
        _toolUsbConnecting = true;
        RefreshCaptureCommands();
        try
        {
            StopObservation();
            if (_hardware.HasSession) await _hardware.DisconnectAsync();
            Status = "波形工具已手动断开主控 USB。";
        }
        catch (Exception error) { Status = "波形工具断开失败：" + error.Message; }
        finally
        {
            _toolUsbConnecting = false;
            RefreshCaptureCommands();
        }
    }

    private void CaptureStationChanged(long stationEpoch)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closed) return;
            if (!_hardware.IsConnected)
            {
                if (IsCapturing) _ = StopCaptureAsync();
                StopObservation();
                Status = "USB 已断开；实时观察停止，记录将保存已有的真实样本。";
            }
            RefreshCaptureCommands();
        }));
    }

    private void ReloadAvailableSignals() => _ = LoadSignalsAsync();

    private async Task LoadSignalsAsync()
    {
        if (_closed || IsObserving || IsCapturing) return;
        var version = Interlocked.Increment(ref _signalRefreshVersion);
        var project = IsIndependentArchive ? null : _project.Current;
        if (project is null)
        {
            AvailableSignals.Clear();
            SelectedSignal = null;
            return;
        }
        try
        {
            var choices = await TestTargetCatalog.LoadAsync(_project, includeAllDbcSignals: true);
            if (_closed || version != _signalRefreshVersion ||
                _project.Current?.ManifestPath != project.ManifestPath) return;
            var previous = SelectedSignal?.Mapping.Name;
            AvailableSignals.Clear();
            foreach (var item in choices.Where(x => x.Mapping.Kind == TestTargetKind.Signal)
                         .GroupBy(x => x.Mapping.Name, StringComparer.OrdinalIgnoreCase)
                         .Select(x => x.First()))
                AvailableSignals.Add(item);
            SelectedSignal = AvailableSignals.FirstOrDefault(x =>
                string.Equals(x.Mapping.Name, previous, StringComparison.OrdinalIgnoreCase))
                ?? AvailableSignals.FirstOrDefault();
            if (AvailableSignals.Count == 0)
                Status = "当前工程没有可采集的 DBC 信号；请先配置工程 DBC。";
        }
        catch (Exception error)
        {
            if (!_closed && version == _signalRefreshVersion)
                Status = "加载工程 DBC 信号失败：" + error.Message;
        }
    }

    private void OnLiveSignal(string name, TestSignalResult result)
    {
        if (!IsObserving || _closed) return;
        _observations.Enqueue((name, result));
        if (Interlocked.Increment(ref _queuedObservations) > 15000 &&
            _observations.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _queuedObservations);
            Interlocked.Increment(ref _discardedPreviewUpdates);
        }
    }

    private async Task<bool> StartObservationAsync()
    {
        if (_closed || IsObserving) return IsObserving;
        if (_realTest.IsRunning || !_hardware.IsConnected ||
            _project.Current is null || IsIndependentArchive || Tracks.Count == 0)
        {
            Status = IsIndependentArchive
                ? "独立波形目录支持真实历史分析；实时观察需要切回工程并选择带版本指纹的 DBC 信号。"
                : "请打开工程、连接主控并选择 DBC 信号；自动 TEST 运行时不能重置信号流。";
            return false;
        }
        try
        {
            await _liveSignals.BindAsync(Tracks.Select(t => t.Choice.Mapping).ToArray());
            if (!_hardware.IsConnected || _realTest.IsRunning)
                throw new InvalidOperationException("准备期间 USB 连接或 TEST 执行状态发生变化。");
            while (_observations.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _queuedObservations, 0);
            Interlocked.Exchange(ref _discardedPreviewUpdates, 0);
            _observedFromUtc = DateTimeOffset.UtcNow;
            foreach (var track in Tracks) track.Reset();
            _liveSignals.SignalReceived += OnLiveSignal;
            IsPreviewPaused = false;
            IsObserving = true;
            IsLiveView = true;
            RaisePropertyChanged(nameof(DisplayMode));
            Status = "开始观察真实 ECU CAN 信号；可以在观察期间开始记录。";
            NotifyChartChanged();
            return true;
        }
        catch (Exception error)
        {
            Status = "不能启动真实观察：" + error.Message;
            return false;
        }
    }

    private void StopObservation()
    {
        _liveSignals.SignalReceived -= OnLiveSignal;
        if (!IsObserving) return;
        IsObserving = false;
        IsPreviewPaused = false;
        while (_observations.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _queuedObservations, 0);
        Status = "实时观察已停止。已保存的采集记录不受影响。";
        RefreshCaptureCommands();
    }

    /// <summary>Called by the window's 250ms UI timer. Does no USB I/O and
    /// never generates artificial samples between observed CAN frames.</summary>
    public void PollCapturePreview()
    {
        if (_closed || !IsObserving) return;
        if (_realTest.IsRunning)
        {
            if (IsCapturing) _ = StopCaptureAsync();
            StopObservation();
            Status = "真实 TEST 已开始；观察器停止，不重置信号流。";
            return;
        }
        if (!_hardware.IsConnected)
        {
            if (IsCapturing) _ = StopCaptureAsync();
            StopObservation();
            return;
        }
        var byName = Tracks.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
        var consumed = 0;
        while (consumed++ < 3000 && _observations.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _queuedObservations);
            if (IsPreviewPaused || !byName.TryGetValue(item.Name, out var track)) continue;
            var observation = item.Result.Observation;
            if (observation?.Value is not { } value)
            {
                track.Mark(item.Result.Status.ToString());
                continue;
            }
            track.Add((observation.Time.Utc - _observedFromUtc).TotalSeconds,
                value, observation.Time.Utc,
                observation.HasGap || item.Result.Status != TestSignalStatus.Valid,
                observation.Time.Quality);
        }
        if (!IsPreviewPaused)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var track in Tracks)
                if (track.LastSampleUtc.HasValue &&
                    now - track.LastSampleUtc.Value > TimeSpan.FromSeconds(5))
                    track.Mark("超过 5 秒无更新");
            if (consumed > 1) NotifyChartChanged();
        }
        if (Interlocked.Read(ref _discardedPreviewUpdates) > 0)
            Status = "实时绘图队列曾溢出，预览可能不完整；请查看归档的采集数据质量。";
    }

    private async Task StartCaptureAsync()
    {
        if (_closed || IsCapturing || Tracks.Count == 0) return;
        if (!IsObserving && !await StartObservationAsync()) return;
        if (IsIndependentArchive || _project.Current is not { } project || !_hardware.IsConnected) return;
        try
        {
            var root = Path.Combine(project.RootDirectory, "wave-records");
            var service = new WaveCaptureService(_liveSignals,
                new WaveEvidenceStore(Path.Combine(root, "evidence")),
                new WaveReferenceRepository(Path.Combine(root, "references", "assets")));
            var duration = CaptureDurationSeconds == 0 ? 3600 : CaptureDurationSeconds;
            var ids = new Dictionary<WaveCaptureId, string>();
            try
            {
                foreach (var track in Tracks)
                {
                    var signal = _liveSignals.ResolveSignal(track.Choice.Mapping);
                    var id = await service.StartAsync(signal,
                        new WaveCaptureConfig(TimeSpan.FromSeconds(duration), 200000),
                        CancellationToken.None);
                    ids.Add(id, track.Name);
                }
            }
            catch
            {
                foreach (var id in ids.Keys)
                {
                    try { await service.StopAsync(id, CancellationToken.None); }
                    catch (Exception) { }
                }
                throw;
            }
            _captureService = service;
            _captureIds.Clear();
            foreach (var entry in ids) _captureIds.Add(entry.Key, entry.Value);
            _captureRoot = root;
            _captureManifest = project.ManifestPath;
            _captureStarted = DateTimeOffset.UtcNow;
            _captureTimer = new CancellationTokenSource();
            IsCapturing = true;
            Status = "正在记录 " + ids.Count + " 路真实信号；不产生模拟数据或自动 PASS。";
            _ = AutoStopAsync(_captureTimer, duration);
        }
        catch (Exception error)
        {
            _captureService = null;
            _captureIds.Clear();
            IsCapturing = false;
            Status = "启动记录失败：" + error.Message;
        }
    }

    private async Task AutoStopAsync(CancellationTokenSource timer, int seconds)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), timer.Token);
            if (!_closed && ReferenceEquals(_captureTimer, timer)) await StopCaptureAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async Task StopCaptureAsync()
    {
        if (!await _captureGate.WaitAsync(0)) return;
        try
        {
            var service = _captureService;
            if (service is null) return;
            _captureService = null;
            var ids = _captureIds.ToArray();
            _captureIds.Clear();
            var root = _captureRoot;
            var manifest = _captureManifest;
            _captureManifest = null;
            _captureRoot = null;
            var timer = _captureTimer;
            _captureTimer = null;
            timer?.Cancel();
            timer?.Dispose();
            var completed = new List<Guid>();
            var names = new List<string>();
            var failures = new List<string>();
            foreach (var (id, name) in ids)
            {
                try
                {
                    var evidence = await service.StopAsync(id, CancellationToken.None);
                    completed.Add(evidence.Evidence.Value);
                    names.Add(name);
                }
                catch (Exception error)
                {
                    failures.Add(name + "：" + error.Message);
                }
            }
            if (completed.Count > 0 && root is not null)
            {
                try { SaveSessionIndex(root, _captureStarted, completed, names); }
                catch (Exception error)
                {
                    failures.Add("成组记录索引：" + error.Message);
                }
            }
            IsCapturing = false;
            if (!_closed)
            {
                Status = "本次采集已保存 " + completed.Count + " 路真实证据" +
                    (failures.Count > 0 ? "；" + string.Join("；", failures.Take(3)) : "。");
                if (_project.Current?.ManifestPath == manifest) Refresh();
            }
        }
        finally
        {
            IsCapturing = false;
            _captureGate.Release();
        }
    }

    private async Task SaveStandardAsync()
    {
        if (_closed || IsCapturing || _savingReference ||
            SelectedSession is null || _sessionSeries.Count == 0 ||
            string.IsNullOrWhiteSpace(ReferenceName) || WaveSourceRoot is null) return;
        var selected = SelectedSession;
        var sources = _sessionSeries.ToArray();
        var name = ReferenceName.Trim();
        var root = WaveSourceRoot;
        var identity = WaveSourceIdentity;
        _savingReference = true;
        SaveStandardCommand.RaiseCanExecuteChanged();
        Status = "正在检查本次采集的信号与时间质量…";
        try
        {
            var standard = await Task.Run(() =>
                new WaveReferenceCatalog(Path.Combine(root, "references"))
                    .SaveStandard(name, sources));
            if (_closed || WaveSourceIdentity != identity) return;
            Status = "已建立标准“" + standard.Name + "” · " +
                standard.Channels.Count + " 路信号，原始采集保持不变。";
            if (SelectedSession == selected && ReferenceName.Trim() == name)
                ReferenceName = "";
            Refresh();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            ArgumentException or UnauthorizedAccessException)
        {
            if (!_closed) Status = "建立标准失败：" + error.Message;
        }
        finally
        {
            _savingReference = false;
            SaveStandardCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task SaveReferenceAsync()
    {
        if (_closed || IsCapturing || _savingReference || _series is null ||
            Selected is not { IsReference: false, Kind: "Capture" } ||
            string.IsNullOrWhiteSpace(ReferenceName) || WaveSourceRoot is null) return;
        var selected = Selected;
        var series = _series;
        var name = ReferenceName.Trim();
        var root = WaveSourceRoot!;
        var sourceIdentity = WaveSourceIdentity;
        _savingReference = true;
        SaveReferenceCommand.RaiseCanExecuteChanged();
        Status = "正在检查时间质量并保存真实参考波形…";
        try
        {
            var saved = await Task.Run(() =>
                new WaveReferenceCatalog(Path.Combine(root, "references")).Save(name, series));
            if (_closed || WaveSourceIdentity != sourceIdentity) return;
            Status = "已保存真实参考波形：" + saved.Name +
                "。不会自动成为生产测试标准。";
            if (Selected == selected && ReferenceName.Trim() == name)
                ReferenceName = "";
            Refresh();
        }
        catch (Exception error)
        {
            if (!_closed) Status = "保存参考失败：" + error.Message;
        }
        finally
        {
            _savingReference = false;
            SaveReferenceCommand.RaiseCanExecuteChanged();
        }
    }

    public async Task PrepareForToolCloseAsync()
    {
        if (IsCapturing) await StopCaptureAsync();
        // Another user-triggered stop may already be finalizing the evidence.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (IsCapturing && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(60);
        if (IsCapturing)
            throw new TimeoutException("真实采集证据仍在保存，不能释放工具 USB。");
        StopObservation();
    }

    private void AbortCaptureForProjectChange()
    {
        if (IsCapturing) _ = StopCaptureAsync();
        StopObservation();
        Tracks.Clear();
        SelectedTrack = null;
        _sessionSeries.Clear();
        Sessions.Clear();
        SelectedSession = null;
        SelectedReference = null;
    }

    private void AbortCaptureForWindowClose()
    {
        _hardware.ConnectionChanged -= CaptureStationChanged;
        StopObservation();
        if (IsCapturing) _ = StopCaptureAsync();
    }
}
