using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Diagnostics;

/// <summary>
/// Narrow, read-only GUI boundary. Implementations must use the existing UsbSessionService,
/// never a second USB parser or a direct WinUSB write path.
/// </summary>
public interface IDeviceDiagnosticBackend : IAsyncDisposable
{
    IReadOnlyList<WinUsbCandidate> Enumerate();
    UsbSessionState State { get; }
    string? TraceFile { get; }
    string? TraceError { get; }
    long TraceDropped { get; }
    long Generation { get; }
    long CanPublished { get; }
    long CanDropped { get; }
    long CanMalformed { get; }
    long HubDropped { get; }
    event Action<UsbTraceRecord>? UsbTrace;
    event Action<WinUsbOperationEvent>? NativeOperation;
    event Action<HardwareCanTrace>? CanTrace;
    event Action<UsbSessionState>? ConnectionChanged;
    Task ConnectAsync(string exactInstanceId, string traceFile, CancellationToken ct = default);
    Task DisconnectAsync();
    Task<UsbResponse> ReadOnlyCommandAsync(byte command, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>Production adapter to the already-integrated, firmware-validated hardware controller.</summary>
public sealed class HardwareDiagnosticBackend : IDeviceDiagnosticBackend
{
    private readonly HardwareBenchController _controller = new();
    public IReadOnlyList<WinUsbCandidate> Enumerate() => _controller.Enumerate();
    public UsbSessionState State => _controller.State;
    public string? TraceFile => _controller.TraceFile;
    public string? TraceError => _controller.TraceError;
    public long TraceDropped => _controller.TraceDropped;
    public long Generation => _controller.Generation;
    public long CanPublished => _controller.CanPublished;
    public long CanDropped => _controller.CanDropped;
    public long CanMalformed => _controller.CanMalformed;
    public long HubDropped => _controller.HubDropped;
    public event Action<UsbTraceRecord>? UsbTrace
    { add => _controller.UsbTrace += value; remove => _controller.UsbTrace -= value; }
    public event Action<WinUsbOperationEvent>? NativeOperation
    { add => _controller.NativeOperation += value; remove => _controller.NativeOperation -= value; }
    public event Action<HardwareCanTrace>? CanTrace
    { add => _controller.CanTrace += value; remove => _controller.CanTrace -= value; }
    public event Action<UsbSessionState>? ConnectionChanged
    { add => _controller.ConnectionChanged += value; remove => _controller.ConnectionChanged -= value; }
    public Task ConnectAsync(string exactInstanceId, string traceFile, CancellationToken ct = default)
        => _controller.ConnectAsync(exactInstanceId, traceFile, ct);
    public Task DisconnectAsync() => _controller.DisconnectAsync();
    public Task<UsbResponse> ReadOnlyCommandAsync(byte command, TimeSpan timeout, CancellationToken ct = default)
        => _controller.ReadOnlyCommandAsync(command, timeout, ct);
    public ValueTask DisposeAsync() => _controller.DisposeAsync();
}

public sealed record DeviceDiagnosticCommand(string Name, byte Command)
{
    public override string ToString() => $"{Name} (0x{Command:X2})";
}

/// <summary>No arbitrary CMD or payload editor is exposed; no CAN transmit entry point.</summary>
public static class DeviceDiagnosticReadOnlyCommands
{
    public static IReadOnlyList<DeviceDiagnosticCommand> All { get; } = Array.AsReadOnly(new[]
    {
        new DeviceDiagnosticCommand("PING", FirmwareReadOnlyProtocol.Ping),
        new DeviceDiagnosticCommand("固件版本", FirmwareReadOnlyProtocol.GetVersion),
        new DeviceDiagnosticCommand("ECU 电源 GPIO 回读", FirmwareReadOnlyProtocol.GetEcuPower),
        new DeviceDiagnosticCommand("IGN GPIO 回读", FirmwareReadOnlyProtocol.GetIgn),
        new DeviceDiagnosticCommand("按键板电源 GPIO 回读", FirmwareReadOnlyProtocol.GetKeyPower),
        new DeviceDiagnosticCommand("夹具控制 GPIO 回读", FirmwareReadOnlyProtocol.GetSolenoid),
        new DeviceDiagnosticCommand("电源 GPIO 汇总", FirmwareReadOnlyProtocol.GetAllPower)
    });

    public static bool IsApproved(byte command)
        => All.Any(c => c.Command == command) &&
           PhysicalCommandFence.IsReadOnly(command, ReadOnlySpan<byte>.Empty);
}

/// <summary>Structured, correlation-ready UI operation event. This is not evidence of physical actuation.</summary>
public sealed record DeviceDiagnosticOperationEvent(DateTimeOffset Utc, string Category, string Action,
    string Outcome, string? DeviceInstanceId, long Generation, string? TraceFile,
    string? Detail, string? ExceptionType)
{
    public string Kind => "DEVICE_LAB_OPERATION";
}

/// <summary>
/// Stateful B0/B1/B2 read-only workflow for WPF. B0 confirmation is single-use for every
/// manual connect, including retry/reconnect. Disconnect never reconnects automatically.
/// </summary>
public sealed class DeviceDiagnosticSession : IAsyncDisposable
{
    private readonly IDeviceDiagnosticBackend _backend;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Func<string> _newTracePath;
    private readonly Func<bool>? _auditHealthy;
    private WinUsbCandidate? _selected;
    private bool _b0Confirmed;
    private bool _disposed;

    public event Action<DeviceDiagnosticOperationEvent>? OperationRecorded;
    public event Action<UsbTraceRecord>? UsbTrace;
    public event Action<WinUsbOperationEvent>? NativeOperation;
    public event Action<HardwareCanTrace>? CanTrace;
    public event Action<UsbSessionState>? ConnectionChanged;

    public DeviceDiagnosticSession(IDeviceDiagnosticBackend backend, Func<string>? newTracePath = null,
        Func<bool>? auditHealthy = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _newTracePath = newTracePath ?? NewTracePath;
        _auditHealthy = auditHealthy;
        _backend.UsbTrace += RelayUsb;
        _backend.NativeOperation += RelayNative;
        _backend.CanTrace += RelayCan;
        _backend.ConnectionChanged += RelayState;
    }

    public WinUsbCandidate? Selected => _selected;
    public bool B0Confirmed => _b0Confirmed;
    public UsbSessionState State => _backend.State;
    public string? TraceFile => _backend.TraceFile;
    public string? TraceError => _backend.TraceError;
    public long TraceDropped => _backend.TraceDropped;
    public long Generation => _backend.Generation;
    public long CanPublished => _backend.CanPublished;
    public long CanDropped => _backend.CanDropped;
    public long CanMalformed => _backend.CanMalformed;
    public long HubDropped => _backend.HubDropped;

    public IReadOnlyList<WinUsbCandidate> Enumerate()
    {
        ThrowIfDisposed();
        try
        {
            var candidates = _backend.Enumerate();
            Emit("USB", "ENUMERATE", "OK", $"Present eligible controllers={candidates.Count}");
            return candidates;
        }
        catch (Exception ex)
        {
            Emit("USB", "ENUMERATE", "FAIL", ex.ToString(), ex.GetType().Name);
            throw;
        }
    }

    public void Select(WinUsbCandidate? candidate)
    {
        ThrowIfDisposed();
        if (_backend.State != UsbSessionState.Disconnected)
            throw new InvalidOperationException("Disconnect before changing the selected controller.");
        if (candidate is not null && !MainControllerUsbIdentity.IsControllerInstance(candidate.DeviceInstanceId))
            throw new UnauthorizedAccessException("Non-main-controller USB instance refused.");
        _selected = candidate;
        _b0Confirmed = false; // changing devices invalidates physical B0 confirmation
        Emit("USB", "SELECT", "OK", candidate?.DeviceInstanceId ?? "<none>");
    }

    public void SetB0IsolationConfirmed(bool confirmed)
    {
        ThrowIfDisposed();
        if (_backend.State != UsbSessionState.Disconnected)
            throw new InvalidOperationException("B0 confirmation only applies before a connection.");
        _b0Confirmed = confirmed;
        Emit("SAFETY", "B0_ISOLATION", confirmed ? "CONFIRMED_BY_OPERATOR" : "CLEARED",
            "Software cannot verify IGN voltage, power rails or cable isolation.");
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_backend.State != UsbSessionState.Disconnected)
                throw new InvalidOperationException("Disconnect before manual reconnect.");
            if (!_b0Confirmed)
                throw new UnauthorizedAccessException("B0 physical isolation must be confirmed for EACH connection attempt.");
            _b0Confirmed = false; // consumed even if native connect fails
            try
            {
                var selected = _selected ?? throw new InvalidOperationException("Select an enumerated main controller first.");
                var candidate = WinUsbSelection.Select(_backend.Enumerate(), selected.DeviceInstanceId);
                // Selection is by exact Windows instance ID; never by COM name, HID, or list index.
                var path = _newTracePath();
                Emit("USB", "CONNECT", "ATTEMPT", $"{candidate.DeviceInstanceId}; trace={path}");
                EnsureAuditHealthy(); // mandatory durable event must succeed before opening WinUSB
                await _backend.ConnectAsync(candidate.DeviceInstanceId, path, ct).ConfigureAwait(false);
                Emit("USB", "CONNECT", "OK", $"trace={_backend.TraceFile}");
            }
            catch (Exception ex)
            {
                Emit("USB", "CONNECT", "FAIL", ex.ToString(), ex.GetType().Name);
                throw;
            }
        }
        finally { _operationGate.Release(); }
    }

    public async Task<UsbResponse> ExecuteReadOnlyAsync(byte command, TimeSpan timeout,
        CancellationToken ct = default)
    {
        // Deny before attempting a transport operation, including while disconnected.
        if (!DeviceDiagnosticReadOnlyCommands.IsApproved(command))
            throw new UnauthorizedAccessException($"CMD 0x{command:X2} is not a read-only DEVICE UI command.");
        if (timeout < TimeSpan.FromMilliseconds(100) || timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_backend.State != UsbSessionState.Connected)
                throw new IOException("The main controller is not connected.");
            Emit("USB", $"READ_0x{command:X2}", "ATTEMPT", $"timeoutMs={timeout.TotalMilliseconds:0}");
            EnsureAuditHealthy(); // deny even harmless GET when the operator audit failed
            try
            {
                var response = await _backend.ReadOnlyCommandAsync(command, timeout, ct).ConfigureAwait(false);
                // The production controller already validates payload semantics; keep the UI guard too.
                if (response.Command != command || response.Status != 0 ||
                    !FirmwareReadOnlyProtocol.ValidReadOnlyPayload(command, response.Payload))
                    throw new IOException("Firmware read-only response failed UI validation.");
                Emit("USB", $"READ_0x{command:X2}", "OK",
                    $"SEQ={response.Sequence}; HEX={Convert.ToHexString(response.Payload)}");
                return response;
            }
            catch (Exception ex)
            {
                Emit("USB", $"READ_0x{command:X2}", "FAIL", ex.ToString(), ex.GetType().Name);
                throw;
            }
        }
        finally { _operationGate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _b0Confirmed = false;
            Emit("USB", "DISCONNECT", "ATTEMPT", "Manual disconnect; no auto reconnect.");
            try
            {
                await _backend.DisconnectAsync().ConfigureAwait(false);
                Emit("USB", "DISCONNECT", "OK", $"trace={_backend.TraceFile}");
            }
            catch (Exception ex)
            {
                Emit("USB", "DISCONNECT", "FAIL", ex.ToString(), ex.GetType().Name);
                throw;
            }
        }
        finally { _operationGate.Release(); }
    }

    private void RelayUsb(UsbTraceRecord record) => InvokeObservers(UsbTrace, record);
    private void RelayNative(WinUsbOperationEvent record) => InvokeObservers(NativeOperation, record);
    private void RelayCan(HardwareCanTrace record) => InvokeObservers(CanTrace, record);
    private void RelayState(UsbSessionState state) => InvokeObservers(ConnectionChanged, state);
    private static void InvokeObservers<T>(Action<T>? observers, T item)
    {
        if (observers is null) return;
        foreach (Action<T> observer in observers.GetInvocationList())
        { try { observer(item); } catch { /* visualization cannot interrupt USB I/O */ } }
    }
    private void Emit(string category, string action, string outcome, string? detail = null,
        string? exceptionType = null)
    {
        var evt = new DeviceDiagnosticOperationEvent(DateTimeOffset.UtcNow, category, action, outcome,
            _selected?.DeviceInstanceId, _backend.Generation, _backend.TraceFile, detail, exceptionType);
        InvokeObservers(OperationRecorded, evt);
    }
    private void EnsureAuditHealthy()
    {
        if (_auditHealthy is not null && !_auditHealthy())
            throw new IOException("DEVICE operation audit failed to persist; refusing new USB I/O.");
    }
    private static string NewTracePath()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoTest", "DeviceDiagnostics", "Traces");
        return Path.Combine(baseDir, $"device-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.jsonl");
    }
    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DeviceDiagnosticSession));
    }
    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            _backend.UsbTrace -= RelayUsb;
            _backend.NativeOperation -= RelayNative;
            _backend.CanTrace -= RelayCan;
            _backend.ConnectionChanged -= RelayState;
            await _backend.DisposeAsync().ConfigureAwait(false);
        }
        finally { _operationGate.Release(); _operationGate.Dispose(); }
    }
}
