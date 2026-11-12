using Microsoft.Win32.SafeHandles;
using System.Diagnostics;

namespace AutoTest.Devices.Usb;

public enum WinUsbLinkState { Disconnected, Opening, Connected, Closing, Faulted }

/// <summary>Windows WinUSB Bulk transport of the existing UsbSessionService.
/// Native lifecycle events are separate from the session's full raw TX/RX/SEQ trace.
/// PhysicalCommandFence is deliberately unchanged and checked before every OUT write.</summary>
public sealed class WinUsbByteLink : IUsbByteLink
{
    private readonly IWinUsbDiscovery _discovery;
    private readonly IWinUsbTransportNative _native;
    private readonly string _chosenInstanceId;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _readGate = new(1, 1), _writeGate = new(1, 1);
    private readonly PhysicalCommandFence _fence = new();
    private SafeFileHandle? _device;
    private IntPtr _interface;
    private WinUsbLinkState _state;
    private bool _disposed;
    private long _epoch;
    private string? _path;

    // FILE_FLAG_OVERLAPPED is REQUIRED by WinUsb_Initialize, even though
    // WinUsb_ReadPipe/WritePipe below deliberately pass NULL OVERLAPPED.
    internal const uint OpenFileFlags = WinUsbNative.FileAttributeNormal | WinUsbNative.FileFlagOverlapped;

    public WinUsbByteLink(string selectedDeviceInstanceId, IWinUsbDiscovery? discovery = null)
        : this(selectedDeviceInstanceId, discovery ?? new WinUsbDiscovery(), SystemWinUsbTransportNative.Instance) { }

    // Injection is internal to the Devices test assembly; never a public way to bypass the safety fence.
    internal WinUsbByteLink(string selectedDeviceInstanceId, IWinUsbDiscovery discovery, IWinUsbTransportNative native)
    {
        if (!MainControllerUsbIdentity.IsControllerInstance(selectedDeviceInstanceId))
            throw new ArgumentException("Device ID must be the verified main controller VID/PID.", nameof(selectedDeviceInstanceId));
        _chosenInstanceId = selectedDeviceInstanceId;
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public event Action<WinUsbOperationEvent>? OperationRecorded;
    public string DeviceInstanceId => _chosenInstanceId;
    public string? DevicePath { get { lock (_sync) return _path; } }
    public WinUsbLinkState State { get { lock (_sync) return _state; } }
    public long ConnectionGeneration => Interlocked.Read(ref _epoch);

    private void Operation(string operation, string stage, string outcome, string? path = null,
        int? error = null, string? detail = null)
    {
        var record = new WinUsbOperationEvent(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(),
            operation, stage, outcome, _chosenInstanceId, path ?? DevicePath,
            ConnectionGeneration, error,
            error is int code ? $"0x{unchecked((uint)code):X8}" : null, detail);
        foreach (Action<WinUsbOperationEvent> handler in OperationRecorded?.GetInvocationList()
                     .Cast<Action<WinUsbOperationEvent>>() ?? Enumerable.Empty<Action<WinUsbOperationEvent>>())
        { try { handler(record); } catch { /* observers cannot change native link behavior */ } }
    }

    private WinUsbStageException NativeError(string stage) => new(stage, _native.LastError);

    /// <summary>CAN-only one-shot authorization. The B0/B1/B2 debugger never calls this.</summary>
    public void AuthorizeOneCanTransmission(ReadOnlySpan<byte> exact15Bytes, string typedConfirmation)
    {
        lock (_sync)
        {
            if (_state != WinUsbLinkState.Connected)
                throw new IOException("Connect first; authorization cannot survive reconnect.");
            _fence.AuthorizeSingleCanFrame(exact15Bytes, typedConfirmation, _epoch);
        }
    }

    public void AuthorizePhysicalTestRun(string fingerprint, string typedConfirmation,
        IEnumerable<(byte Port, bool Extended, uint Id)> allowedCan, bool allowIgn)
    {
        lock (_sync)
        {
            if (_state != WinUsbLinkState.Connected)
                throw new IOException("Physical run requires an existing connected USB session.");
            _fence.AuthorizePhysicalRun(fingerprint, typedConfirmation, allowedCan, allowIgn, _epoch);
        }
    }

    public void RevokePhysicalTestRun()
    {
        lock (_sync) _fence.RevokePhysicalRun();
    }

    public ValueTask OpenAsync(CancellationToken ct)
        => new(Task.Run(() => OpenCore(ct), ct));

    private void OpenCore(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_native.IsSupported) throw new PlatformNotSupportedException("WinUSB requires Windows.");
        lock (_sync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WinUsbByteLink));
            if (_state != WinUsbLinkState.Disconnected)
                throw new InvalidOperationException("Link must be closed before opening.");
            _state = WinUsbLinkState.Opening;
        }

        SafeFileHandle? openedFile = null;
        IntPtr openedInterface = IntPtr.Zero;
        string stage = "SelectInterface";
        string? selectedPath = null;
        try
        {
            Operation("CONNECT", stage, "BEGIN");
            var selected = WinUsbSelection.Select(_discovery.ListCandidates(), _chosenInstanceId);
            selectedPath = selected.DevicePath;
            Operation("CONNECT", stage, "OK", selectedPath);
            ct.ThrowIfCancellationRequested();

            stage = "DriverBinding";
            string binding = string.IsNullOrWhiteSpace(selected.DriverService) ? "UNKNOWN" : selected.DriverService;
            Operation("CONNECT", stage,
                string.Equals(binding, "WinUSB", StringComparison.OrdinalIgnoreCase) ? "OK" : "CHECK",
                selectedPath, detail: $"SetupAPI SPDRP_SERVICE={binding}; expected WinUSB; read-only diagnostic, not automatic driver repair.");

            stage = "CreateFileW";
            Operation("CONNECT", stage, "BEGIN", selectedPath,
                detail: $"access=GENERIC_READ|GENERIC_WRITE share=READ|WRITE disposition=OPEN_EXISTING flags=0x{OpenFileFlags:X8}");
            openedFile = _native.Open(selectedPath, OpenFileFlags);
            if (openedFile.IsInvalid) throw NativeError(stage);
            Operation("CONNECT", stage, "OK", selectedPath);

            stage = "WinUsb_Initialize";
            Operation("CONNECT", stage, "BEGIN", selectedPath);
            if (!_native.Initialize(openedFile, out openedInterface)) throw NativeError(stage);
            if (openedInterface == IntPtr.Zero)
                throw new WinUsbStageException(stage, null, "WinUsb_Initialize succeeded but returned a null interface handle.");
            Operation("CONNECT", stage, "OK", selectedPath);

            stage = "WinUsb_QueryInterfaceSettings";
            if (!_native.QueryInterfaceSettings(openedInterface, out var iface)) throw NativeError(stage);
            Operation("CONNECT", stage, "OK", selectedPath, detail:
                $"interface={iface.bInterfaceNumber} alt={iface.bAlternateSetting} class=0x{iface.bInterfaceClass:X2} endpoints={iface.bNumEndpoints}");
            stage = "ValidateInterface";
            if (iface.bInterfaceNumber != 0 || iface.bAlternateSetting != 0 ||
                iface.bInterfaceClass != 0xFF || iface.bNumEndpoints != 2)
                throw new WinUsbStageException(stage, null,
                    $"Unexpected interface={iface.bInterfaceNumber}, alt={iface.bAlternateSetting}, class=0x{iface.bInterfaceClass:X2}, endpoints={iface.bNumEndpoints}.");

            bool readFound = false, writeFound = false;
            for (byte i = 0; i < iface.bNumEndpoints; i++)
            {
                stage = $"WinUsb_QueryPipe[{i}]";
                if (!_native.QueryPipe(openedInterface, i, out var pipe)) throw NativeError(stage);
                Operation("CONNECT", stage, "OK", selectedPath,
                    detail: $"endpoint=0x{pipe.PipeId:X2} type={pipe.PipeType} packet={pipe.MaximumPacketSize}");
                stage = $"ValidatePipe[{i}]";
                if (pipe.PipeType != WinUsbNative.PipeTypeBulk || pipe.MaximumPacketSize != 64)
                    throw new WinUsbStageException(stage, null, "Unexpected pipe type or packet size; expected Bulk/64.");
                if (pipe.PipeId == MainControllerUsbIdentity.BulkIn) readFound = true;
                else if (pipe.PipeId == MainControllerUsbIdentity.BulkOut) writeFound = true;
                else throw new WinUsbStageException(stage, null, $"Unexpected endpoint 0x{pipe.PipeId:X2}.");
            }
            stage = "ValidateEndpoints";
            if (!readFound || !writeFound)
                throw new WinUsbStageException(stage, null, "Expected physical endpoints 0x82 IN and 0x03 OUT.");
            Operation("CONNECT", stage, "OK", selectedPath, detail: "0x82 IN, 0x03 OUT");

            uint readTimeout = 250;
            stage = "WinUsb_SetPipePolicy[IN:0x82]";
            if (!_native.SetPipeTimeout(openedInterface, MainControllerUsbIdentity.BulkIn, ref readTimeout))
                throw NativeError(stage);
            Operation("CONNECT", stage, "OK", selectedPath, detail: "PIPE_TRANSFER_TIMEOUT=250 ms");
            uint writeTimeout = 1000;
            stage = "WinUsb_SetPipePolicy[OUT:0x03]";
            if (!_native.SetPipeTimeout(openedInterface, MainControllerUsbIdentity.BulkOut, ref writeTimeout))
                throw NativeError(stage);
            Operation("CONNECT", stage, "OK", selectedPath, detail: "PIPE_TRANSFER_TIMEOUT=1000 ms");
            ct.ThrowIfCancellationRequested();
            stage = "CommitConnection";
            lock (_sync)
            {
                if (_state != WinUsbLinkState.Opening)
                    throw new IOException("Open interrupted by close.");
                _device = openedFile;
                _interface = openedInterface;
                _path = selectedPath;
                _state = WinUsbLinkState.Connected;
                _fence.Reset();
                Interlocked.Increment(ref _epoch);
                openedFile = null;
                openedInterface = IntPtr.Zero;
            }
            Operation("CONNECT", stage, "OK", selectedPath,
                detail: "No command sent; physical IGN/power state not inferred.");
        }
        catch (Exception ex)
        {
            // Capture stage and error BEFORE cleanup can overwrite GetLastError.
            int? code = (ex as WinUsbStageException)?.NativeErrorCode;
            Operation("CONNECT", stage, ex is OperationCanceledException ? "CANCELLED" : "FAILED",
                selectedPath, code, ex.ToString());
            if (openedInterface != IntPtr.Zero) _native.Free(openedInterface);
            openedFile?.Dispose();
            lock (_sync)
            {
                if (_state == WinUsbLinkState.Opening) _state = WinUsbLinkState.Faulted;
            }
            if (ex is OperationCanceledException or WinUsbStageException) throw;
            throw new WinUsbStageException(stage, null, ex.Message);
        }
    }

    public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct)
    {
        if (destination.Length < 64) throw new ArgumentException("USB buffer must be >= 64 bytes.", nameof(destination));
        return new(Task.Run(() => ReadCore(destination, ct), ct));
    }
    private int ReadCore(Memory<byte> destination, CancellationToken ct)
    {
        _readGate.Wait(ct);
        try
        {
            var bytes = new byte[destination.Length];
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                IntPtr iface;
                lock (_sync) iface = RequireConnected();
                if (_native.Read(iface, bytes, out uint n))
                {
                    if (n == 0) continue; // idle or zero-length transfer != disconnected link
                    if (n > bytes.Length) throw new IOException("WinUSB returned invalid read length.");
                    bytes.AsSpan(0, (int)n).CopyTo(destination.Span);
                    return (int)n;
                }
                int error = _native.LastError;
                if (error is 121 or 1460) continue; // finite idle timeout; not EOF
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                var failure = new WinUsbStageException("WinUsb_ReadPipe[IN:0x82]", error);
                Operation("READ", failure.Stage, "FAILED", error: error, detail: failure.Message);
                throw failure;
            }
        }
        finally { _readGate.Release(); }
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken ct)
        => new(Task.Run(() => WriteCore(source.ToArray(), ct), ct));
    private void WriteCore(byte[] bytes, CancellationToken ct)
    {
        _writeGate.Wait(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            IntPtr iface;
            lock (_sync)
            {
                iface = RequireConnected();
                try { _fence.AssertAllowedAndConsume(bytes, _epoch); }
                catch (UnauthorizedAccessException ex)
                {
                    Operation("WRITE", "PhysicalCommandFence", "BLOCKED", detail: ex.Message);
                    throw;
                }
            }
            if (!_native.Write(iface, bytes, out uint transferred))
            {
                int code = _native.LastError;
                var failure = new WinUsbStageException("WinUsb_WritePipe[OUT:0x03]", code);
                Operation("WRITE", failure.Stage, "FAILED", error: code, detail: failure.Message);
                throw failure;
            }
            if (transferred != bytes.Length)
            {
                Operation("WRITE", "WinUsb_WritePipe[OUT:0x03]", "SHORT", detail: $"{transferred}/{bytes.Length}; physical effect UNKNOWN");
                throw new IOException($"Short USB write {transferred}/{bytes.Length}; effect UNKNOWN.");
            }
            Operation("WRITE", "WinUsb_WritePipe[OUT:0x03]", "OK", detail: $"bytes={transferred}");
            ct.ThrowIfCancellationRequested();
        }
        finally { _writeGate.Release(); }
    }

    public ValueTask CloseAsync(CancellationToken ct)
        => new(Task.Run(CloseCore)); // canceled requests must not abandon native cleanup

    private void CloseCore()
    {
        SafeFileHandle? file;
        lock (_sync)
        {
            if (_state == WinUsbLinkState.Disconnected) return;
            _state = WinUsbLinkState.Closing;
            file = _device;
        }
        Operation("DISCONNECT", "CancelIoEx", "BEGIN");
        if (file is { IsInvalid: false, IsClosed: false })
        {
            try
            {
                if (!_native.Cancel(file))
                    Operation("DISCONNECT", "CancelIoEx", "CHECK", error: _native.LastError,
                        detail: "Cleanup continues: bounded pipe timeouts are still in effect.");
            }
            catch (Exception ex) { Operation("DISCONNECT", "CancelIoEx", "CHECK", detail: ex.Message); }
        }
        // The existing synchronous native WinUSB pipe calls are bounded by 250/1000 ms.
        _readGate.Wait();
        _writeGate.Wait();
        try
        {
            lock (_sync)
            {
                if (_interface != IntPtr.Zero) _native.Free(_interface);
                _interface = IntPtr.Zero;
                _device?.Dispose();
                _device = null;
                _path = null;
                _fence.Reset();
                _state = WinUsbLinkState.Disconnected;
            }
            Operation("DISCONNECT", "ReleaseHandles", "OK");
        }
        finally { _writeGate.Release(); _readGate.Release(); }
    }

    private IntPtr RequireConnected()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WinUsbByteLink));
        if (_state != WinUsbLinkState.Connected || _device is null || _interface == IntPtr.Zero)
            throw new IOException("USB device is disconnected, closing, or faulted.");
        return _interface;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_sync) _disposed = true;
        _readGate.Dispose(); _writeGate.Dispose();
    }
}
