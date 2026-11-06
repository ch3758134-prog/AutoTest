using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AutoTest.Devices.Usb;

public enum WinUsbLinkState { Disconnected, Opening, Connected, Closing, Faulted }

/// <summary>Real Windows WinUSB bulk byte link for the frozen controller descriptor.
/// It supplies bytes to the EXISTING UsbSessionService; does not encode another USB protocol.
/// All writes are guarded against physical state changes by default.</summary>
public sealed class WinUsbByteLink : IUsbByteLink
{
    private readonly IWinUsbDiscovery _discovery;
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

    public WinUsbByteLink(string selectedDeviceInstanceId, IWinUsbDiscovery? discovery = null)
    {
        if (!MainControllerUsbIdentity.IsControllerInstance(selectedDeviceInstanceId))
            throw new ArgumentException("Device ID must be the verified main controller VID/PID.", nameof(selectedDeviceInstanceId));
        _chosenInstanceId = selectedDeviceInstanceId;
        _discovery = discovery ?? new WinUsbDiscovery();
    }
    public string DeviceInstanceId => _chosenInstanceId;
    public string? DevicePath { get { lock (_sync) return _path; } }
    public WinUsbLinkState State { get { lock (_sync) return _state; } }
    public long ConnectionGeneration => Interlocked.Read(ref _epoch);

    /// <summary>CAN-only one-shot authorization. The debug console never calls this method;
    /// integrating a physical CAN sender also requires its existing CanTransmitGate lease.</summary>
    public void AuthorizeOneCanTransmission(ReadOnlySpan<byte> exact15Bytes, string typedConfirmation)
    {
        lock (_sync)
        {
            if (_state != WinUsbLinkState.Connected)
                throw new IOException("Connect first; authorization cannot survive reconnect.");
            _fence.AuthorizeSingleCanFrame(exact15Bytes, typedConfirmation, _epoch);
        }
    }

    public ValueTask OpenAsync(CancellationToken ct)
        => new(Task.Run(() => OpenCore(ct), ct));

    private void OpenCore(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WinUSB requires Windows.");
        lock (_sync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WinUsbByteLink));
            if (_state != WinUsbLinkState.Disconnected)
                throw new InvalidOperationException("Link must be closed before opening.");
            _state = WinUsbLinkState.Opening;
        }
        SafeFileHandle? openedFile = null;
        IntPtr openedInterface = IntPtr.Zero;
        try
        {
            var selected = WinUsbSelection.Select(_discovery.ListCandidates(), _chosenInstanceId);
            ct.ThrowIfCancellationRequested();
            openedFile = WinUsbNative.CreateFileW(selected.DevicePath,
                WinUsbNative.GenericRead | WinUsbNative.GenericWrite,
                WinUsbNative.FileShareRead | WinUsbNative.FileShareWrite,
                IntPtr.Zero, WinUsbNative.OpenExisting, WinUsbNative.FileAttributeNormal, IntPtr.Zero);
            if (openedFile.IsInvalid)
                throw Error("Open USB interface (check WinUSB driver and device permissions)");
            if (!WinUsbNative.WinUsb_Initialize(openedFile, out openedInterface))
                throw Error("Initialize WinUSB interface");
            if (!WinUsbNative.WinUsb_QueryInterfaceSettings(openedInterface, 0, out var iface))
                throw Error("Query USB interface descriptor");
            if (iface.bInterfaceNumber != 0 || iface.bInterfaceClass != 0xFF || iface.bNumEndpoints != 2)
                throw new IOException("USB descriptor mismatch (expected vendor interface 0 and exactly 2 pipes).");
            bool readFound = false, writeFound = false;
            for (byte i = 0; i < iface.bNumEndpoints; i++)
            {
                if (!WinUsbNative.WinUsb_QueryPipe(openedInterface, 0, i, out var pipe))
                    throw Error("Query USB bulk pipe");
                if (pipe.PipeType != WinUsbNative.PipeTypeBulk || pipe.MaximumPacketSize != 64)
                    throw new IOException("Controller USB interface has an unexpected pipe type or size.");
                if (pipe.PipeId == MainControllerUsbIdentity.BulkIn) readFound = true;
                else if (pipe.PipeId == MainControllerUsbIdentity.BulkOut) writeFound = true;
                else throw new IOException($"Unexpected USB endpoint 0x{pipe.PipeId:X2}.");
            }
            if (!readFound || !writeFound)
                throw new IOException("Expected USB endpoints 0x82/0x03 not found.");
            uint readTimeout = 250, writeTimeout = 1000;
            if (!WinUsbNative.WinUsb_SetPipePolicy(openedInterface, MainControllerUsbIdentity.BulkIn,
                    WinUsbNative.PipeTransferTimeout, sizeof(uint), ref readTimeout) ||
                !WinUsbNative.WinUsb_SetPipePolicy(openedInterface, MainControllerUsbIdentity.BulkOut,
                    WinUsbNative.PipeTransferTimeout, sizeof(uint), ref writeTimeout))
                throw Error("Set finite WinUSB pipe timeouts");
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_state != WinUsbLinkState.Opening) throw new IOException("Open interrupted by close.");
                _device = openedFile;
                _interface = openedInterface;
                _path = selected.DevicePath;
                _state = WinUsbLinkState.Connected;
                _fence.Reset();
                Interlocked.Increment(ref _epoch);
                openedFile = null;
                openedInterface = IntPtr.Zero;
            }
        }
        catch
        {
            if (openedInterface != IntPtr.Zero) WinUsbNative.WinUsb_Free(openedInterface);
            openedFile?.Dispose();
            lock (_sync)
            {
                if (_state == WinUsbLinkState.Opening) _state = WinUsbLinkState.Faulted;
            }
            throw;
        }
    }

    public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct)
    {
        if (destination.Length < 64) throw new ArgumentException("USB buffer must be >= one 64-byte bulk packet.", nameof(destination));
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
                if (WinUsbNative.WinUsb_ReadPipe(iface, MainControllerUsbIdentity.BulkIn,
                        bytes, (uint)bytes.Length, out uint n, IntPtr.Zero))
                {
                    if (n == 0) continue; // zero-length transfer != disconnected link
                    if (n > bytes.Length) throw new IOException("WinUSB returned an invalid read length.");
                    bytes.AsSpan(0, (int)n).CopyTo(destination.Span);
                    return (int)n;
                }
                int error = Marshal.GetLastWin32Error();
                if (error is 121 or 1460) continue; // bounded idle timeout; never report EOF
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                throw NativeFailure("WinUSB IN read", error);
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
                _fence.AssertAllowedAndConsume(bytes, _epoch);
            }
            if (!WinUsbNative.WinUsb_WritePipe(iface, MainControllerUsbIdentity.BulkOut,
                    bytes, (uint)bytes.Length, out uint transferred, IntPtr.Zero))
                throw Error("WinUSB OUT write");
            if (transferred != bytes.Length)
                throw new IOException($"Short USB write {transferred}/{bytes.Length}; effect UNKNOWN.");
            ct.ThrowIfCancellationRequested();
        }
        finally { _writeGate.Release(); }
    }

    public ValueTask CloseAsync(CancellationToken ct)
        => new(Task.Run(CloseCore)); // Never abandon cleanup after disconnect cancellation.

    private void CloseCore()
    {
        SafeFileHandle? file;
        lock (_sync)
        {
            if (_state == WinUsbLinkState.Disconnected) return;
            _state = WinUsbLinkState.Closing;
            file = _device;
        }
        if (file is { IsInvalid: false, IsClosed: false })
        {
            try { WinUsbNative.CancelIoEx(file, IntPtr.Zero); } catch { /* finite per-pipe timeout */ }
        }
        // Each native call is bounded (250/1000ms); drain in-flight ops before WinUsb_Free.
        _readGate.Wait();
        _writeGate.Wait();
        try
        {
            lock (_sync)
            {
                if (_interface != IntPtr.Zero) WinUsbNative.WinUsb_Free(_interface);
                _interface = IntPtr.Zero;
                _device?.Dispose();
                _device = null;
                _path = null;
                _fence.Reset();
                _state = WinUsbLinkState.Disconnected;
            }
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
    private static Win32Exception Error(string action) => new(Marshal.GetLastWin32Error(), action);
    private static IOException NativeFailure(string action, int code) =>
        new($"{action} failed (Win32 {code}); disconnect/reconnect required. Physical state UNKNOWN.",
            new Win32Exception(code));

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_sync) _disposed = true;
        _readGate.Dispose(); _writeGate.Dispose();
    }
}
