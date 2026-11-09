using AutoTest.Devices.Can;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Diagnostics;

/// <summary>Concrete read-only hardware debug facade, suitable for independent CLI and WPF UI.
/// B0/B1/B2 never expose state-changing physical operations.</summary>
public sealed partial class HardwareBenchController : IAsyncDisposable
{
    private readonly IWinUsbDiscovery _discovery;
    private UsbSessionService? _session;
    private WinUsbByteLink? _link;
    private HardwareCanReceiveBridge? _canBridge;
    private CanFrameHub? _hub;
    private HardwareTraceRecorder? _log;
    private string? _selectedInstanceId;
    private string? _lastTraceFile;
    private string? _lastTraceFailure;
    private long _lastSessionGeneration;
    private bool _versionVerified;

    public event Action<UsbTraceRecord>? UsbTrace;
    public event Action<WinUsbOperationEvent>? NativeOperation;
    public event Action<HardwareCanTrace>? CanTrace;
    public event Action<UsbSessionState>? ConnectionChanged;

    public HardwareBenchController(IWinUsbDiscovery? discovery = null)
        => _discovery = discovery ?? new WinUsbDiscovery();

    public IReadOnlyList<WinUsbCandidate> Enumerate() => _discovery.ListCandidates();
    public string? SelectedInstanceId => _selectedInstanceId;
    public string? DevicePath => _link?.DevicePath;
    public long Generation => _session?.Generation ?? _lastSessionGeneration;
    public bool ProtocolVersionVerified => _versionVerified;
    public UsbSessionState State => _session?.State ?? UsbSessionState.Disconnected;
    public string? TraceFile => _log?.FilePath ?? _lastTraceFile;
    public long TraceDropped => _log?.Dropped ?? 0;
    public string? TraceError => _log?.FailureMessage ?? _lastTraceFailure;
    public long CanPublished => _canBridge?.Published ?? 0;
    public long CanDropped => _canBridge?.DroppedBeforeDecode ?? 0;
    public long CanMalformed => _canBridge?.InvalidFrames ?? 0;
    public long HubDropped => _hub?.TotalDropped ?? 0;
    public CanFrameHub? Hub => _hub; // shared receiver, no transmitter bound

    public async Task ConnectAsync(string exactInstanceId, string traceFile, CancellationToken ct = default)
    {
        if (State != UsbSessionState.Disconnected || _session is not null)
            throw new InvalidOperationException("Disconnect before connecting another controller.");
        var selected = WinUsbSelection.Select(Enumerate(), exactInstanceId);
        _selectedInstanceId = selected.DeviceInstanceId;
        try
        {
            _log = new HardwareTraceRecorder(traceFile);
            _lastTraceFile = _log.FilePath;
            _lastTraceFailure = null;
            _link = new WinUsbByteLink(selected.DeviceInstanceId, _discovery);
            _link.OperationRecorded += RelayNative;
            _session = new UsbSessionService(_link, _lastSessionGeneration);
            _versionVerified = false;
            _physicalUdsLeases = new CanTransmitGate(_session.Generation);
            _hub = new CanFrameHub(TransmitPhysicalReadOnlyUdsAsync); // guarded; default DENY
            _session.TraceRecorded += RelayUsb;
            _session.StateChanged += RelayState;
            _canBridge = new HardwareCanReceiveBridge(_session, _hub);
            _canBridge.Trace += RelayCan;
            _canBridge.ObservationPublished += ObservePhysicalReadOnlyUdsRx;
            _log.RecordMessage("CONNECTION_ATTEMPT", selected.DisplayIdentity);
            await _session.ConnectAsync(ct).ConfigureAwait(false);
            _physicalUdsLeases.Reset(_session.Generation);
        }
        catch (Exception original)
        {
            // Preserve the native WinUSB stage/error even if later trace flush or cleanup fails.
            try { await DisconnectAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { original.Data["DeviceCleanupError"] = cleanup.ToString(); }
            throw;
        }
    }
    public async Task ReconnectAsync(string traceFile, CancellationToken ct = default)
    {
        string identity = _selectedInstanceId ?? throw new IOException("No previously selected controller.");
        await DisconnectAsync().ConfigureAwait(false);
        await ConnectAsync(identity, traceFile, ct).ConfigureAwait(false);
    }
    public async Task<UsbResponse> ReadOnlyCommandAsync(byte command, TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (!PhysicalCommandFence.IsReadOnly(command, ReadOnlySpan<byte>.Empty))
            throw new UnauthorizedAccessException("Only PING, GET_VERSION and GET_* commands are allowed.");
        if (_session?.State != UsbSessionState.Connected)
            throw new IOException("Controller not connected.");
        if (_log is null || _log.FailureMessage is not null || _log.Dropped != 0)
            throw new IOException("Hardware trace is unavailable or has lost records; stop and inspect logs.");
        var response = await _session.RequestAsync(command, ReadOnlyMemory<byte>.Empty, timeout, ct)
            .ConfigureAwait(false);
        if (!FirmwareReadOnlyProtocol.ValidReadOnlyPayload(command, response.Payload))
        {
            _versionVerified = false;
            throw new IOException($"Unexpected read-only response for CMD 0x{command:X2}. Stop bench verification.");
        }
        if (command == FirmwareReadOnlyProtocol.GetVersion) _versionVerified = true;
        return response;
    }

    private void RelayNative(WinUsbOperationEvent record)
    {
        _log?.Record(record);
        foreach (Action<WinUsbOperationEvent> handler in NativeOperation?.GetInvocationList()
                     .Cast<Action<WinUsbOperationEvent>>() ?? Enumerable.Empty<Action<WinUsbOperationEvent>>())
        { try { handler(record); } catch { } }
    }
    private void RelayUsb(UsbTraceRecord record)
    {
        _log?.Record(record);
        UsbTrace?.Invoke(record);
    }
    private void RelayCan(HardwareCanTrace record)
    {
        _log?.Record(record);
        CanTrace?.Invoke(record);
    }
    private void RelayState(UsbSessionState state)
    {
        if (state is UsbSessionState.Disconnected or UsbSessionState.Faulted)
        {
            _physicalUdsBoundary.Revoke();
            _physicalUdsLeases?.Reset(0);
        }
        _log?.RecordMessage("STATE", state.ToString());
        ConnectionChanged?.Invoke(state);
    }
    public async Task DisconnectAsync()
    {
        _physicalUdsBoundary.Revoke();
        _physicalUdsLeases?.Reset(0);
        var canPublished = CanPublished;
        var canDropped = CanDropped;
        var canMalformed = CanMalformed;
        var hubDropped = HubDropped;
        if (_session is not null)
        {
            var session = _session;
            _session = null;
            try { await session.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                _lastSessionGeneration = session.Generation;
                session.TraceRecorded -= RelayUsb;
                session.StateChanged -= RelayState;
                if (_link is not null) _link.OperationRecorded -= RelayNative;
                _link = null; // owned and disposed by UsbSessionService
            }
        }
        if (_canBridge is not null)
        {
            _canBridge.Trace -= RelayCan;
            _canBridge.ObservationPublished -= ObservePhysicalReadOnlyUdsRx;
            await _canBridge.DisposeAsync().ConfigureAwait(false);
            _canBridge = null;
        }
        _hub?.Dispose(); _hub = null;
        _physicalUdsLeases = null;
        _versionVerified = false;
        if (_log is not null)
        {
            _log.RecordMessage("TRACE_FINAL", $"CAN published={canPublished}, dropped={canDropped}, malformed={canMalformed}, hubLost={hubDropped}, traceLost={TraceDropped}");
            try { await _log.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                _lastTraceFailure = _log.FailureMessage;
                _lastTraceFile = _log.FilePath;
                _log = null;
            }
        }
    }
    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
