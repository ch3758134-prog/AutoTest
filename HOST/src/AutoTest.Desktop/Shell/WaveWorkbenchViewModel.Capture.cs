using System.Collections.ObjectModel;
using System.IO;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Testing;
using AutoTest.Specialists.Wave;
using AutoTest.Testing.Authoring;
using Prism.Commands;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Actual USB/CAN -> shared decoded DBC signal -> immutable wave evidence.
/// No synthetic points, no separate CAN driver, and no automatic reference approval.
/// Capture is a wave TOOL operation, not a production or test verdict.
/// </summary>
public sealed partial class WaveWorkbenchViewModel
{
    private LiveDbcSignalService _liveSignals = null!;
    private HardwareConnectionService _hardware = null!;
    private RealTestWorkbenchService _realTest = null!;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private WaveCaptureService? _captureService;
    private WaveCaptureId? _activeCapture;
    private CancellationTokenSource? _captureTimer;
    private string? _captureManifest;
    private MappingChoice? _selectedSignal;
    private int _durationSeconds = 5;
    private string _referenceName = "";
    private bool _isCapturing;
    private int _signalRefreshVersion;

    public ObservableCollection<MappingChoice> AvailableSignals { get; } = new();
    public IReadOnlyList<int> CaptureDurations { get; } = new[] { 2, 5, 10, 20, 30, 60 };
    public DelegateCommand StartCaptureCommand { get; private set; } = null!;
    public DelegateCommand StopCaptureCommand { get; private set; } = null!;
    public DelegateCommand ReloadSignalsCommand { get; private set; } = null!;
    public DelegateCommand SaveReferenceCommand { get; private set; } = null!;

    public MappingChoice? SelectedSignal
    {
        get => _selectedSignal;
        set
        {
            if (SetProperty(ref _selectedSignal, value)) RefreshCaptureCommands();
        }
    }
    public int CaptureDurationSeconds
    {
        get => _durationSeconds;
        set
        {
            if (CaptureDurations.Contains(value)) SetProperty(ref _durationSeconds, value);
        }
    }
    public string ReferenceName
    {
        get => _referenceName;
        set
        {
            if (SetProperty(ref _referenceName, value)) RefreshCaptureCommands();
        }
    }
    public bool IsCapturing
    {
        get => _isCapturing;
        private set
        {
            if (SetProperty(ref _isCapturing, value)) RefreshCaptureCommands();
        }
    }
    public string CaptureState => IsCapturing
        ? "真实 CAN/DBC 正在采样；尚未形成判定"
        : "等待真实设备采集 · 不模拟数据";

    private void InitializeCapture(LiveDbcSignalService signals,
        HardwareConnectionService hardware, RealTestWorkbenchService realTest)
    {
        _liveSignals = signals;
        _hardware = hardware;
        _realTest = realTest;
        _hardware.ConnectionChanged += CaptureStationChanged;
        StartCaptureCommand = new DelegateCommand(
            async () => await StartCaptureAsync(),
            () => !_closed && !IsCapturing && SelectedSignal is not null &&
                  !_realTest.IsRunning);
        StopCaptureCommand = new DelegateCommand(
            async () => await StopCaptureAsync(),
            () => !_closed && IsCapturing && _activeCapture.HasValue);
        ReloadSignalsCommand = new DelegateCommand(ReloadAvailableSignals);
        SaveReferenceCommand = new DelegateCommand(
            SaveReference, () => !_closed && !IsCapturing && _series is not null &&
                !string.IsNullOrWhiteSpace(ReferenceName) && Selected is { IsReference: false });
        ReloadAvailableSignals();
    }

    private void CaptureStationChanged(long _)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) RefreshCaptureCommands();
        else dispatcher.BeginInvoke(new Action(RefreshCaptureCommands));
    }

    private void RefreshCaptureCommands()
    {
        RaisePropertyChanged(nameof(CaptureState));
        StartCaptureCommand?.RaiseCanExecuteChanged();
        StopCaptureCommand?.RaiseCanExecuteChanged();
        SaveReferenceCommand?.RaiseCanExecuteChanged();
    }

    private void ReloadAvailableSignals() => _ = LoadSignalsAsync();

    private async Task LoadSignalsAsync()
    {
        if (_closed) return;
        var version = Interlocked.Increment(ref _signalRefreshVersion);
        var project = _project.Current;
        if (project is null)
        {
            AvailableSignals.Clear();
            SelectedSignal = null;
            return;
        }
        try
        {
            var choices = await TestTargetCatalog.LoadAsync(_project);
            if (_closed || version != _signalRefreshVersion ||
                _project.Current?.ManifestPath != project.ManifestPath) return;
            var previousName = SelectedSignal?.Mapping.Name;
            AvailableSignals.Clear();
            foreach (var item in choices.Where(x => x.Mapping.Kind == TestTargetKind.Signal)
                         .GroupBy(x => x.Mapping.Name, StringComparer.OrdinalIgnoreCase)
                         .Select(x => x.First()))
                AvailableSignals.Add(item);
            SelectedSignal = AvailableSignals.FirstOrDefault(x =>
                string.Equals(x.Mapping.Name, previousName, StringComparison.OrdinalIgnoreCase));
            if (AvailableSignals.Count == 0)
                Status = "当前工程没有可供采集的 DBC 信号，请先导入并核对 ECU DBC。";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
                                     InvalidDataException or ArgumentException or
                                     UnauthorizedAccessException)
        {
            if (!_closed && version == _signalRefreshVersion)
                Status = "DBC 采集信号加载失败：" + error.Message;
        }
    }

    private async Task StartCaptureAsync()
    {
        var choice = SelectedSignal;
        var project = _project.Current;
        if (_closed || IsCapturing || choice is null || project is null) return;
        if (!_hardware.IsConnected || _realTest.IsRunning)
        {
            Status = "开始采集需要连接真实主控 USB，且不能与自动 TEST 执行争用信号映射。";
            return;
        }
        IsCapturing = true;
        var projectPath = project.ManifestPath;
        try
        {
            // Bind all current DBC signals so live CAN monitoring also continues.
            // Never rebind a running TEST's shared signal source.
            var definitions = AvailableSignals.Select(x => x.Mapping).ToArray();
            await _liveSignals.BindAsync(definitions);
            if (_closed || _realTest.IsRunning || !_hardware.IsConnected ||
                _project.Current?.ManifestPath != projectPath)
                throw new InvalidOperationException("采集准备期间工位或工程已变化。");
            var root = Path.Combine(project.RootDirectory, "wave-records");
            var capture = new WaveCaptureService(
                _liveSignals,
                new WaveEvidenceStore(Path.Combine(root, "evidence")),
                new WaveReferenceRepository(Path.Combine(root, "references", "assets")));
            var signal = _liveSignals.ResolveSignal(choice.Mapping);
            var id = await capture.StartAsync(signal, new WaveCaptureConfig(
                TimeSpan.FromSeconds(CaptureDurationSeconds), 100000), CancellationToken.None);
            _captureService = capture;
            _activeCapture = id;
            _captureManifest = projectPath;
            _captureTimer = new CancellationTokenSource();
            Status = "真实采集开始：" + choice.Label + " · " +
                CaptureDurationSeconds + " 秒；结束前不产生 PASS/FAIL。";
            RefreshCaptureCommands();
            _ = AutoStopAsync(_captureTimer, CaptureDurationSeconds);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
                                      ArgumentException or UnauthorizedAccessException)
        {
            Status = "无法采集真实波形：" + error.Message;
            _captureService = null;
            _activeCapture = null;
            _captureManifest = null;
            IsCapturing = false;
        }
    }

    private async Task AutoStopAsync(CancellationTokenSource timer, int seconds)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), timer.Token);
            if (!_closed && ReferenceEquals(timer, _captureTimer))
                await StopCaptureAsync();
        }
        catch (OperationCanceledException) when (timer.IsCancellationRequested) { }
    }

    /// <summary>Refresh only an ephemeral plot projection. Evidence becomes durable on StopAsync.</summary>
    public void PollCapturePreview()
    {
        if (_closed || !_activeCapture.HasValue || _captureService is null) return;
        try
        {
            var series = _captureService.Observe(_activeCapture.Value);
            _series = series;
            _points = series.Samples.Length == 0
                ? Array.Empty<WaveChartPoint>()
                : WaveChartAnalysis.FromSeries(series);
            Info = series.Signal.SignalName + " · 正在真实采样 · " +
                series.Samples.Length + " 点 · 结束后才归档";
            NotifyChartChanged();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
                                      ArgumentException or KeyNotFoundException)
        {
            Status = "实时波形预览暂不可用：" + error.Message;
        }
    }

    private async Task StopCaptureAsync()
    {
        if (!await _captureGate.WaitAsync(0)) return;
        try
        {
            var id = _activeCapture;
            var service = _captureService;
            if (id is null || service is null) return;
            var originalProject = _captureManifest;
            _activeCapture = null;
            _captureService = null;
            _captureManifest = null;
            var timer = _captureTimer;
            _captureTimer = null;
            timer?.Cancel();
            timer?.Dispose();
            RefreshCaptureCommands();
            try
            {
                var evidence = await service.StopAsync(id.Value, CancellationToken.None);
                if (!_closed && _project.Current?.ManifestPath == originalProject)
                {
                    Status = "采集已归档真实证据：" + evidence.Evidence +
                        "；可查看曲线并选择是否建立参考波形。";
                    Refresh();
                }
            }
            catch (WaveCaptureException error)
            {
                if (!_closed)
                    Status = "采集结束但未产生有效证据 [" + error.Code + "]：" +
                        error.Message + "。请核查 CAN 接收、DBC 绑定及采样时间。";
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or
                                          ArgumentException or UnauthorizedAccessException)
            {
                if (!_closed) Status = "无法归档真实波形：" + error.Message;
            }
        }
        finally
        {
            IsCapturing = false;
            _captureGate.Release();
        }
    }

    private void SaveReference()
    {
        if (_closed || IsCapturing || _series is null || Selected?.IsReference != false ||
            string.IsNullOrWhiteSpace(ReferenceName) || _project.Current is not { } project)
            return;
        try
        {
            var catalog = new WaveReferenceCatalog(
                Path.Combine(project.RootDirectory, "wave-records", "references"));
            // Catalog verifies time quality, completeness and exact DBC signal identity.
            var saved = catalog.Save(ReferenceName.Trim(), _series);
            Status = "已保存经质量检查的参考波形：" + saved.Name +
                "；不会自动作为生产判定标准。";
            ReferenceName = "";
            Refresh();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                      ArgumentException or InvalidOperationException)
        {
            Status = "参考波形未保存：" + error.Message;
        }
    }

    private void AbortCaptureForProjectChange() => _ = StopCaptureAsync();
    private void AbortCaptureForWindowClose()
    {
        _hardware.ConnectionChanged -= CaptureStationChanged;
        _ = StopCaptureAsync();
    }
}
