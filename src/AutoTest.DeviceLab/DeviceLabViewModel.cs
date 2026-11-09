using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;

namespace AutoTest.DeviceLab;

public sealed record DeviceLabTimelineLine(string Time, string Source, string Details);

public sealed class DeviceLabViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DeviceLabSession _session;
    private readonly DeviceLabAuditJournal _audit;
    private readonly List<DeviceLabAsyncCommand> _commands = new();
    private const int UiHistoryCapacity = 1200;
    private const int UiPendingCapacity = 2048;
    private readonly ConcurrentQueue<(ObservableCollection<DeviceLabTimelineLine> Target, DeviceLabTimelineLine Line)> _pendingLines = new();
    private int _pendingCount;
    private int _drainScheduled;
    private WinUsbCandidate? _selectedCandidate;
    private DeviceLabCommand? _selectedReadOnlyCommand;
    private bool _b0Acknowledged;
    private bool _busy;
    private bool _disposed;
    private string _statusText = "未连接 · 仅允许只读诊断";
    private string _resultText = "等待人工选择主控板、完成 B0 隔离确认。";
    private string _lastNativeStage = "—";
    private string _nativeFailure = "—";
    private string _traceFile = "尚未创建";
    private string _driverService = "—";
    private int _timeoutMs = 1500;
    private long _can0Count, _can1Count, _uiLinesTrimmed;
    private long _exportTransportLost, _exportCanLost, _exportMalformed, _exportHubLost;
    private string? _exportDeviceInstanceId;
    private string? _exportDriverService;
    private long _exportGeneration;
    private string _usbFilter = "";
    private string _canFilter = "";
    private string _exportPath = NewExportPath();
    private string _exportResult = "请先断开 USB 会话，再导出原始诊断证据包。";
    private DeviceLabTimelineLine? _selectedUsbLine;
    private DeviceLabTimelineLine? _selectedCanLine;
    private string _ignReadback = "未读取 · 物理电压未知";
    private string _ecuPowerReadback = "未读取 · 物理电压未知";
    private string _keyPowerReadback = "未读取 · 物理电压未知";
    private string _solenoidReadback = "未读取 · 机械位置未知";
    private string _allPowerReadback = "未读取 · 物理电压未知";

    public DeviceLabViewModel() : this(new HardwareDeviceLabBackend()) { }
    internal DeviceLabViewModel(IDeviceLabBackend backend)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _audit = new DeviceLabAuditJournal(); // fail closed if audit storage cannot be opened
        _session = new DeviceLabSession(backend, auditHealthy: () => _audit.FailureMessage is null);
        UsbView = CollectionViewSource.GetDefaultView(Timeline);
        CanView = CollectionViewSource.GetDefaultView(CanTimeline);
        UsbView.Filter = item => item is DeviceLabTimelineLine line &&
            DeviceEvidenceTextFilter.Matches(line.Source, line.Details, UsbFilter);
        CanView.Filter = item => item is DeviceLabTimelineLine line &&
            DeviceEvidenceTextFilter.Matches(line.Source, line.Details, CanFilter);
        _session.OperationRecorded += OnOperation;
        _session.NativeOperation += OnNative;
        _session.UsbTrace += OnUsbTrace;
        _session.CanTrace += OnCanTrace;
        _session.ConnectionChanged += OnConnection;
        ReadOnlyCommands = DeviceLabReadOnlyCommands.All;
        _selectedReadOnlyCommand = ReadOnlyCommands.First();
        RefreshCommand = Command(RefreshAsync, () => !_busy && !_disposed);
        ConnectCommand = Command(ConnectAsync, () => !_busy && !_disposed &&
            _b0Acknowledged && SelectedCandidate is not null && _session.State == UsbSessionState.Disconnected);
        DisconnectCommand = Command(DisconnectAsync, () => !_busy && !_disposed &&
            _session.State != UsbSessionState.Disconnected, allowWhenAuditFailed: true);
        ReadCommand = Command(ReadAsync, () => !_busy && !_disposed &&
            _session.State == UsbSessionState.Connected && SelectedReadOnlyCommand is not null);
        ExportCommand = Command(ExportAsync, () => !_busy && !_disposed &&
            _session.State == UsbSessionState.Disconnected &&
            !string.IsNullOrWhiteSpace(_session.TraceFile) &&
            !string.IsNullOrWhiteSpace(ExportPath), allowWhenAuditFailed: true);
        CopyUsbCommand = Command(() => CopyLineAsync(SelectedUsbLine),
            () => !_busy && !_disposed && SelectedUsbLine is not null, allowWhenAuditFailed: true);
        CopyCanCommand = Command(() => CopyLineAsync(SelectedCanLine),
            () => !_busy && !_disposed && SelectedCanLine is not null, allowWhenAuditFailed: true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public ObservableCollection<WinUsbCandidate> Candidates { get; } = new();
    public ObservableCollection<DeviceLabTimelineLine> Timeline { get; } = new();
    public ObservableCollection<DeviceLabTimelineLine> CanTimeline { get; } = new();
    public ICollectionView UsbView { get; }
    public ICollectionView CanView { get; }
    public string UsbFilter
    {
        get => _usbFilter;
        set { _usbFilter = value ?? ""; Notify(); UsbView.Refresh(); }
    }
    public string CanFilter
    {
        get => _canFilter;
        set { _canFilter = value ?? ""; Notify(); CanView.Refresh(); }
    }
    public DeviceLabTimelineLine? SelectedUsbLine
    { get => _selectedUsbLine; set { _selectedUsbLine = value; Notify(); RefreshButtons(); } }
    public DeviceLabTimelineLine? SelectedCanLine
    { get => _selectedCanLine; set { _selectedCanLine = value; Notify(); RefreshButtons(); } }
    public string ExportPath
    { get => _exportPath; set { _exportPath = value ?? ""; Notify(); RefreshButtons(); } }
    public string ExportResult
    { get => _exportResult; private set { _exportResult = value; Notify(); } }
    public IReadOnlyList<DeviceLabCommand> ReadOnlyCommands { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ReadCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand CopyUsbCommand { get; }
    public ICommand CopyCanCommand { get; }

    public WinUsbCandidate? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (Equals(_selectedCandidate, value)) return;
            if (_session.State != UsbSessionState.Disconnected) return;
            try
            {
                _session.Select(value);
                _selectedCandidate = value;
                _b0Acknowledged = false;
                DriverService = value?.DriverService ?? "未读取到驱动服务名";
                Notify(); Notify(nameof(B0Acknowledged)); RefreshButtons();
            }
            catch (Exception ex) { ShowError("选择设备", ex); }
        }
    }
    public DeviceLabCommand? SelectedReadOnlyCommand
    { get => _selectedReadOnlyCommand; set { _selectedReadOnlyCommand = value; Notify(); RefreshButtons(); } }
    public bool B0Acknowledged
    {
        get => _b0Acknowledged;
        set
        {
            if (_b0Acknowledged == value) return;
            if (_session.State != UsbSessionState.Disconnected) return;
            try { _session.SetB0IsolationConfirmed(value); _b0Acknowledged = value; Notify(); RefreshButtons(); }
            catch (Exception ex) { ShowError("B0 确认", ex); }
        }
    }
    public string StatusText { get => _statusText; private set { _statusText = value; Notify(); } }
    public string ResultText { get => _resultText; private set { _resultText = value; Notify(); } }
    public string LastNativeStage { get => _lastNativeStage; private set { _lastNativeStage = value; Notify(); } }
    public string NativeFailure { get => _nativeFailure; private set { _nativeFailure = value; Notify(); } }
    public string TraceFile { get => _traceFile; private set { _traceFile = value; Notify(); } }
    public string DriverService { get => _driverService; private set { _driverService = value; Notify(); } }
    public string OperationLogFile => _audit.FilePath;
    public string IgnReadback { get => _ignReadback; private set { _ignReadback = value; Notify(); } }
    public string EcuPowerReadback { get => _ecuPowerReadback; private set { _ecuPowerReadback = value; Notify(); } }
    public string KeyPowerReadback { get => _keyPowerReadback; private set { _keyPowerReadback = value; Notify(); } }
    public string SolenoidReadback { get => _solenoidReadback; private set { _solenoidReadback = value; Notify(); } }
    public string AllPowerReadback { get => _allPowerReadback; private set { _allPowerReadback = value; Notify(); } }
    public int TimeoutMs
    { get => _timeoutMs; set { _timeoutMs = Math.Clamp(value, 100, 30000); Notify(); } }
    public long Can0Count => Interlocked.Read(ref _can0Count);
    public long Can1Count => Interlocked.Read(ref _can1Count);
    public long UiLinesTrimmed => Interlocked.Read(ref _uiLinesTrimmed);
    public string CanStats =>
        $"CAN0 按键板: {Can0Count} · CAN1 DUT: {Can1Count} · Hub Published: {_session.CanPublished} · " +
        $"DecodeErrors: {_session.CanMalformed} · QueueLost: {_session.CanDropped} · HubLost: {_session.HubDropped}";
    public string TraceStats => $"原始 JSONL 丢失: {_session.TraceDropped} · UI 仅保留最近 {UiHistoryCapacity} 行，已裁剪 {UiLinesTrimmed} 行";

    private DeviceLabAsyncCommand Command(Func<Task> work, Func<bool> canRun,
        bool allowWhenAuditFailed = false)
    {
        var cmd = new DeviceLabAsyncCommand(() => RunAsync(work, allowWhenAuditFailed), canRun);
        _commands.Add(cmd);
        return cmd;
    }
    private void RefreshButtons()
    {
        foreach (var cmd in _commands) cmd.Refresh();
    }
    private void SwitchToUi(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }
    private void AddLine(ObservableCollection<DeviceLabTimelineLine> dest, string source, string message)
    {
        if (_disposed) return;
        if (Interlocked.Increment(ref _pendingCount) > UiPendingCapacity)
        {
            Interlocked.Decrement(ref _pendingCount);
            Interlocked.Increment(ref _uiLinesTrimmed); // UI loss accounting; JSONL remains independent
            return;
        }
        _pendingLines.Enqueue((dest, new DeviceLabTimelineLine(
            DateTimeOffset.Now.ToString("HH:mm:ss.fff"), source, message)));
        ScheduleDrain();
    }
    private void ScheduleDrain()
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            _dispatcher.BeginInvoke(new Action(DrainLines), DispatcherPriority.Background);
    }
    private void DrainLines()
    {
        if (_disposed) return;
        int processed = 0;
        while (processed++ < 160 && _pendingLines.TryDequeue(out var next))
        {
            Interlocked.Decrement(ref _pendingCount);
            next.Target.Add(next.Line);
            while (next.Target.Count > UiHistoryCapacity)
            {
                next.Target.RemoveAt(0);
                Interlocked.Increment(ref _uiLinesTrimmed);
            }
        }
        Notify(nameof(UiLinesTrimmed)); Notify(nameof(TraceStats)); Notify(nameof(CanStats));
        Notify(nameof(Can0Count)); Notify(nameof(Can1Count));
        Volatile.Write(ref _drainScheduled, 0);
        if (!_pendingLines.IsEmpty) ScheduleDrain();
    }
    private void OnOperation(DeviceLabOperationEvent evt)
    {
        // All operator decisions, including pre-connect B0 and enumeration, are durable.
        try { _audit.Record(evt); }
        catch (Exception ex) { SwitchToUi(() => ShowError("结构化操作日志故障，停止新操作", ex)); }
        AddLine(Timeline, evt.Category, $"{evt.Action} {evt.Outcome} | {evt.Detail}");
    }
    private void OnNative(WinUsbOperationEvent evt)
    {
        AddLine(Timeline, "NATIVE", $"{evt.Stage} {evt.Outcome} {evt.NativeErrorHex} {evt.Detail}");
        SwitchToUi(() =>
        {
            LastNativeStage = evt.Stage;
            if (evt.NativeErrorCode is int code)
                NativeFailure = $"Win32 {code} / {evt.NativeErrorHex} · {evt.Detail}";
        });
    }
    private void OnUsbTrace(UsbTraceRecord evt)
        => AddLine(Timeline, "USB", $"{evt.Kind} GEN={evt.Generation} SEQ={evt.Sequence} " +
            $"CMD=0x{evt.Command:X2} RAW={evt.RawHex} {evt.Detail}");
    private void OnCanTrace(HardwareCanTrace evt)
    {
        AddLine(CanTimeline, "CAN", $"port={evt.PhysicalPort} role={evt.Role} " +
            $"HOST_UTC={evt.HostUtc:O} TICKS={evt.HostMonotonicTicks} " +
            $"GEN={evt.SessionGeneration} valid={evt.Valid} RAW15={evt.RawUsbPayloadHex} {evt.Detail}");
        if (evt.Valid)
        {
            if (evt.PhysicalPort == 0) Interlocked.Increment(ref _can0Count);
            if (evt.PhysicalPort == 1) Interlocked.Increment(ref _can1Count);
        }
    }
    private void OnConnection(UsbSessionState state)
        => SwitchToUi(() =>
        {
            StatusText = $"USB 状态：{state} · 世代 {_session.Generation}";
            if (state != UsbSessionState.Connected) ClearStationEvidence();
            RefreshButtons();
        });

    private async Task RunAsync(Func<Task> work, bool allowWhenAuditFailed = false)
    {
        if (_busy || _disposed || (_audit.FailureMessage is not null && !allowWhenAuditFailed)) return;
        _busy = true;
        RefreshButtons();
        try { await work(); }
        catch (Exception ex) { ShowError("操作失败", ex); }
        finally
        {
            _busy = false;
            TraceFile = _session.TraceFile ?? "尚未创建";
            Notify(nameof(TraceStats)); Notify(nameof(CanStats)); RefreshButtons();
        }
    }
    private async Task RefreshAsync()
    {
        var list = await Task.Run(() => _session.Enumerate());
        Candidates.Clear();
        foreach (var candidate in list) Candidates.Add(candidate);
        if (_session.State == UsbSessionState.Disconnected)
            SelectedCandidate = list.Count == 1 ? list[0] : null;
        ResultText = $"发现 {list.Count} 个符合固件 GUID、VID/PID 的设备；扫码枪/HID 不参与枚举。";
    }
    private async Task ConnectAsync()
    {
        var selected = SelectedCandidate ?? throw new InvalidOperationException("请先选择主控板。");
        // Preserve trace ownership even if the operator selects a different board after disconnect.
        _exportDeviceInstanceId = selected.DeviceInstanceId;
        _exportDriverService = selected.DriverService;
        StatusText = $"正在连接：{selected.DeviceInstanceId}";
        Interlocked.Exchange(ref _can0Count, 0); Interlocked.Exchange(ref _can1Count, 0);
        _exportTransportLost = _exportCanLost = _exportMalformed = _exportHubLost = 0;
        ClearStationEvidence();
        Notify(nameof(CanStats));
        try { await _session.ConnectAsync(); }
        finally
        {
            _exportGeneration = _session.Generation;
            _b0Acknowledged = false; Notify(nameof(B0Acknowledged));
        }
        ResultText = "WinUSB 会话已建立。只执行 PING/版本/只读 GPIO，CAN 仅被动接收。";
    }
    private async Task DisconnectAsync()
    {
        // Retain counters before the underlying backend clears its connection-specific bridge.
        _exportTransportLost = _session.TraceDropped;
        _exportCanLost = _session.CanDropped;
        _exportMalformed = _session.CanMalformed;
        _exportHubLost = _session.HubDropped;
        _exportGeneration = _session.Generation; // active generation, before disconnect invalidates it
        await _session.DisconnectAsync();
        StatusText = "已断开。再次连接需重新确认 B0 隔离。";
        ResultText = "断开完成；没有自动重连或 CAN 发送。";
    }
    private async Task ReadAsync()
    {
        var selected = SelectedReadOnlyCommand ?? throw new InvalidOperationException("请选择只读命令。");
        var result = await _session.ExecuteReadOnlyAsync(selected.Command, TimeSpan.FromMilliseconds(TimeoutMs));
        var explanation = FirmwareReadOnlyProtocol.Explain(result.Command, result.Payload);
        ResultText = $"{selected.Name} · SEQ={result.Sequence} · STATUS={result.Status} · " +
            $"RAW={Convert.ToHexString(result.Payload)} · {explanation}";
        switch (result.Command)
        {
            case FirmwareReadOnlyProtocol.GetIgn: IgnReadback = explanation; break;
            case FirmwareReadOnlyProtocol.GetEcuPower: EcuPowerReadback = explanation; break;
            case FirmwareReadOnlyProtocol.GetKeyPower: KeyPowerReadback = explanation; break;
            case FirmwareReadOnlyProtocol.GetSolenoid: SolenoidReadback = explanation; break;
            case FirmwareReadOnlyProtocol.GetAllPower: AllPowerReadback = explanation; break;
        }
        Notify(nameof(TraceStats));
    }
    private static string NewExportPath()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTest", "DeviceLab", "Exports");
        return Path.Combine(baseDir, $"device-r0-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.zip");
    }

    private Task CopyLineAsync(DeviceLabTimelineLine? selected)
    {
        if (selected is null) throw new InvalidOperationException("请先选择一条日志。");
        Clipboard.SetText($"{selected.Time}  {selected.Source}  {selected.Details}");
        ResultText = "已复制选中行（界面行可能被裁剪，完整原始数据请导出 JSONL 证据包）。";
        return Task.CompletedTask;
    }

    private async Task ExportAsync()
    {
        if (_session.State != UsbSessionState.Disconnected)
            throw new InvalidOperationException("先断开主控 USB 后才能导出一致的原始证据。");
        var source = _session.TraceFile ?? throw new IOException("尚未创建 USB 原始 Trace。请先进行一次连接。 ");
        var output = ExportPath;
        // The audit is application-lifetime; transport evidence is from the last selected connection.
        OnOperation(new DeviceLabOperationEvent(DateTimeOffset.UtcNow, "EVIDENCE", "EXPORT", "ATTEMPT",
            _exportDeviceInstanceId, _exportGeneration, source, output, null));
        try
        {
            var ctx = new DeviceEvidenceContext(
                "0c63b660896b1d287dfc3cfe24efa7b728e47767",
                _exportDeviceInstanceId,
                _exportDriverService,
                _exportGeneration,
                _exportTransportLost, _exportCanLost, _exportMalformed, _exportHubLost,
                UiLinesTrimmed, _session.TraceError, _audit.FailureMessage, true);
            var result = await DeviceEvidenceArchiveWriter.ExportAsync(
                source, _audit.FilePath, output, ctx);
            ExportResult = $"已导出：{result.ArchivePath}\nSHA-256: {result.Sha256}";
            ResultText = ExportResult;
            OnOperation(new DeviceLabOperationEvent(DateTimeOffset.UtcNow, "EVIDENCE", "EXPORT", "OK",
                _exportDeviceInstanceId, _exportGeneration, source,
                $"archive={result.ArchivePath} sha256={result.Sha256}", null));
            // Every export uses a new name; never silently overwrite a previous evidence pack.
            ExportPath = NewExportPath();
        }
        catch (Exception ex)
        {
            ExportResult = "导出失败：" + ex;
            OnOperation(new DeviceLabOperationEvent(DateTimeOffset.UtcNow, "EVIDENCE", "EXPORT", "FAIL",
                _exportDeviceInstanceId, _exportGeneration, source, ex.ToString(), ex.GetType().Name));
            throw;
        }
    }

    private void ClearStationEvidence()
    {
        IgnReadback = "未读取 / 旧回读失效 · 物理电压未知";
        EcuPowerReadback = "未读取 / 旧回读失效 · 物理电压未知";
        KeyPowerReadback = "未读取 / 旧回读失效 · 物理电压未知";
        SolenoidReadback = "未读取 / 旧回读失效 · 机械位置未知";
        AllPowerReadback = "未读取 / 旧回读失效 · 物理电压未知";
    }
    private void ShowError(string action, Exception ex)
    {
        StatusText = $"{action}：{ex.GetType().Name}";
        ResultText = ex.ToString(); // includes native WinUsbStageException stage and Win32 code
        AddLine(Timeline, "ERROR", ex.ToString());
        RefreshButtons();
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _session.OperationRecorded -= OnOperation;
        _session.NativeOperation -= OnNative;
        _session.UsbTrace -= OnUsbTrace;
        _session.CanTrace -= OnCanTrace;
        _session.ConnectionChanged -= OnConnection;
        try { await _session.DisposeAsync(); }
        finally { _audit.Dispose(); }
    }
}

public sealed class DeviceLabAsyncCommand : ICommand
{
    private readonly Func<Task> _work;
    private readonly Func<bool> _canExecute;
    public DeviceLabAsyncCommand(Func<Task> work, Func<bool> canExecute)
    { _work = work; _canExecute = canExecute; }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute();
    public async void Execute(object? parameter) => await _work();
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
