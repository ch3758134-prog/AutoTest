using System.Text;
using System.Threading.Channels;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Station;
using AutoTest.Devices.Usb;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace AutoTest.Devices.Tests;

/// <summary>No real USB device, PING, CAN TX or output commands are used by these tests.</summary>
public sealed class WinUsbConnectFailureRecoveryTests
{
    private const string DeviceId = "USB\\VID_1183&PID_5850\\BENCH_EXAMPLE";
    private static readonly WinUsbCandidate Controller = new("fake-device-interface-path", DeviceId, "WinUSB");

    [Fact]
    public void DEVICE_REC_01_CreateFileFlagsContainOverlappedAndNormal()
    {
        Assert.Equal(0x40000080u, WinUsbByteLink.OpenFileFlags);
        Assert.NotEqual(0u, WinUsbByteLink.OpenFileFlags & WinUsbNative.FileFlagOverlapped);
        Assert.NotEqual(0u, WinUsbByteLink.OpenFileFlags & WinUsbNative.FileAttributeNormal);
    }

    [Fact]
    public async Task DEVICE_REC_02_InitializeErrorPreservesCodeStageAndClosesHandle()
    {
        var api = new FakeWinUsbApi { FailStage = "WinUsb_Initialize", FailureCode = 6 };
        await using var link = NewLink(api);
        var events = new List<WinUsbOperationEvent>();
        link.OperationRecorded += events.Add;
        var error = await Assert.ThrowsAsync<WinUsbStageException>(async () => await link.OpenAsync(default));
        Assert.Equal("WinUsb_Initialize", error.Stage);
        Assert.Equal(6, error.NativeErrorCode);
        Assert.Equal("0x00000006", error.NativeErrorHex);
        Assert.Contains("Win32=6", error.Message);
        Assert.Equal(WinUsbLinkState.Faulted, link.State);
        Assert.Equal(0, api.FreeCount); // Initialize never returned an interface handle
        Assert.Equal(0, api.WriteCount);
        Assert.Equal(0x40000080u, api.CapturedFlags);
        Assert.Contains(events, e => e.Stage == "WinUsb_Initialize" && e.Outcome == "FAILED"
            && e.NativeErrorCode == 6 && e.NativeErrorHex == "0x00000006");
        await link.CloseAsync(default);
        Assert.Equal(WinUsbLinkState.Disconnected, link.State);
    }

    [Fact]
    public async Task DEVICE_REC_03_DescriptorErrorPreservesExactStageAndFreeThenReconnects()
    {
        var api = new FakeWinUsbApi { FailStage = "WinUsb_QueryPipe[1]", FailureCode = 87 };
        await using var link = NewLink(api);
        var events = new List<WinUsbOperationEvent>();
        link.OperationRecorded += events.Add;
        var error = await Assert.ThrowsAsync<WinUsbStageException>(async () => await link.OpenAsync(default));
        Assert.Equal("WinUsb_QueryPipe[1]", error.Stage);
        Assert.Equal(87, error.NativeErrorCode);
        Assert.Equal(1, api.FreeCount); // after Initialize a WinUSB interface must be released
        await link.CloseAsync(default);
        api.FailStage = null;
        await link.OpenAsync(default);
        Assert.Equal(WinUsbLinkState.Connected, link.State);
        Assert.Equal(1, link.ConnectionGeneration);
        Assert.Contains(events, e => e.Stage == "ValidateEndpoints" && e.Outcome == "OK");
        await link.CloseAsync(default);
        Assert.Equal(2, api.FreeCount);
        Assert.Equal(WinUsbLinkState.Disconnected, link.State);
    }

    [Fact]
    public async Task DEVICE_REC_04_DisconnectReconnectResetsCanConsentAndDoesNotSendCommands()
    {
        var api = new FakeWinUsbApi();
        await using var link = NewLink(api);
        await link.OpenAsync(default);
        byte[] canPayload = { 1, 0, 0x23, 1, 0, 0, 1, 0xAA, 0, 0, 0, 0, 0, 0, 0 };
        link.AuthorizeOneCanTransmission(canPayload, "AUTHORIZE CAN TX " + Convert.ToHexString(canPayload));
        await link.CloseAsync(default);
        await link.OpenAsync(default);
        Assert.Equal(2, link.ConnectionGeneration);
        byte[] canRequest = UsbWireCodec.EncodeRequest(1, 0x40, canPayload);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await link.WriteAsync(canRequest, default));
        Assert.Equal(0, api.WriteCount);
        Assert.Equal(WinUsbLinkState.Connected, link.State);
        await link.CloseAsync(default);
        Assert.Equal(2, api.FreeCount);
    }

    [Fact]
    public async Task DEVICE_REC_05_OnlyReadOnlyUsbCommandCanReachMockNativeWrite()
    {
        var api = new FakeWinUsbApi();
        await using var link = NewLink(api);
        await link.OpenAsync(default);
        await link.WriteAsync(UsbWireCodec.EncodeRequest(1, 0xF0, Array.Empty<byte>()), default);
        Assert.Equal(1, api.WriteCount);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await link.WriteAsync(UsbWireCodec.EncodeRequest(2, 0x12, new byte[] { 1 }), default));
        Assert.Equal(1, api.WriteCount);
        await link.CloseAsync(default);
    }

    [Fact]
    public async Task DEVICE_REC_06_TraceRecorderPersistsStructuredNativeFailureAndRawFrames()
    {
        string file = Path.Combine(Path.GetTempPath(), $"autotest-device-recovery-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using (var recorder = new HardwareTraceRecorder(file))
            {
                recorder.Record(new WinUsbOperationEvent(DateTimeOffset.UtcNow, 100, "CONNECT",
                    "WinUsb_Initialize", "FAILED", DeviceId, "fake-path", 0, 6, "0x00000006",
                    "Test simulated native error"));
                recorder.Record(new UsbTraceRecord(DateTimeOffset.UtcNow, 101, 1,
                    "TX_REQUEST", "A5000001F0", 1, 0xF0, null, "pre-write intent"));
            }
            string[] lines = await File.ReadAllLinesAsync(file);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"Stage\":\"WinUsb_Initialize\"", lines[0]);
            Assert.Contains("\"NativeErrorCode\":6", lines[0]);
            Assert.Contains("\"RawHex\":\"A5000001F0\"", lines[1]);
            Assert.Contains("\"Sequence\":1", lines[1]);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public async Task DEVICE_REC_07_SessionFailureTraceIncludesNativeStageAndCode()
    {
        await using var link = new InitFailureByteLink();
        await using var session = new UsbSessionService(link);
        var trace = new List<UsbTraceRecord>();
        session.TraceRecorded += trace.Add;
        await Assert.ThrowsAsync<WinUsbStageException>(async () => await session.ConnectAsync());
        Assert.Contains(trace, x => x.Kind == "CONNECT_ERROR" && x.Detail is not null
            && x.Detail.Contains("WinUsb_Initialize") && x.Detail.Contains("Win32=6"));
        await session.DisconnectAsync();
        Assert.Equal(UsbSessionState.Disconnected, session.State);
    }

    [Fact]
    public async Task DEVICE_REC_08_MockSessionDisconnectInvalidatesOldRequestThenReconnects()
    {
        await using var link = new ScriptedByteLink();
        await using var session = new UsbSessionService(link);
        var trace = new List<UsbTraceRecord>();
        session.TraceRecorded += e => { lock (trace) trace.Add(e); };
        await session.ConnectAsync();
        Task<UsbResponse> oldRequest = session.RequestAsync(0xF0, ReadOnlyMemory<byte>.Empty,
            TimeSpan.FromSeconds(5));
        byte[] oldRaw = await link.NextWriteAsync();
        Assert.True(UsbWireCodec.TryDecodeRequest(oldRaw, out var old));
        await session.DisconnectAsync();
        await Assert.ThrowsAnyAsync<Exception>(async () => await oldRequest);
        await session.ConnectAsync();
        Task<UsbResponse> newRequest = session.RequestAsync(0xF0, ReadOnlyMemory<byte>.Empty,
            TimeSpan.FromSeconds(5));
        byte[] newRaw = await link.NextWriteAsync();
        Assert.True(UsbWireCodec.TryDecodeRequest(newRaw, out var current));
        Assert.NotEqual(old.Sequence, current.Sequence);
        link.Deliver(UsbWireCodec.EncodeResponse(old.Sequence, 0xF0, 0, "OLD"u8));
        link.Deliver(UsbWireCodec.EncodeResponse(current.Sequence, 0xF0, 0, "PONG"u8));
        var response = await newRequest.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("PONG", Encoding.ASCII.GetString(response.Payload));
        lock (trace)
        {
            Assert.Contains(trace, e => e.Kind == "UNMATCHED_RESPONSE" && e.Sequence == old.Sequence);
            Assert.Contains(trace, e => e.Kind == "RX_BYTES" && e.RawHex.Length != 0);
        }
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task DEVICE_REC_09_CreateFileFailurePreservesAccessDeniedAndNoInitialization()
    {
        var api = new FakeWinUsbApi { FailStage = "CreateFileW", FailureCode = 5 };
        await using var link = NewLink(api);
        var events = new List<WinUsbOperationEvent>();
        link.OperationRecorded += events.Add;
        var failure = await Assert.ThrowsAsync<WinUsbStageException>(async () => await link.OpenAsync(default));
        Assert.Equal("CreateFileW", failure.Stage);
        Assert.Equal(5, failure.NativeErrorCode);
        Assert.Equal("0x00000005", failure.NativeErrorHex);
        Assert.Contains(events, e => e.Stage == "CreateFileW" && e.Outcome == "FAILED"
            && e.NativeErrorCode == 5);
        await link.CloseAsync(default);
        Assert.Equal(WinUsbLinkState.Disconnected, link.State);
        Assert.Equal(0, api.FreeCount);
    }

    [Fact]
    public async Task DEVICE_REC_10_PolicyFailurePreservesEndpointAndCanRecover()
    {
        var api = new FakeWinUsbApi { FailStage = "WinUsb_SetPipePolicy[IN:0x82]", FailureCode = 31 };
        await using var link = NewLink(api);
        var failure = await Assert.ThrowsAsync<WinUsbStageException>(async () => await link.OpenAsync(default));
        Assert.Equal("WinUsb_SetPipePolicy[IN:0x82]", failure.Stage);
        Assert.Equal(31, failure.NativeErrorCode);
        Assert.Equal(1, api.FreeCount);
        await link.CloseAsync(default);
        api.FailStage = null;
        await link.OpenAsync(default);
        Assert.Equal(WinUsbLinkState.Connected, link.State);
        await link.CloseAsync(default);
    }

    [Fact]
    public async Task DEVICE_REC_09_PhysicalStationAndKeyConsentsReachOnlyOneMockNativeWrite()
    {
        var api = new FakeWinUsbApi();
        await using var link = NewLink(api);
        await link.OpenAsync(default);
        var powerOn = UsbWireCodec.EncodeRequest(31, 0x10, new byte[] { 1 });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await link.WriteAsync(powerOn, default));
        Assert.Equal(0, api.WriteCount);
        link.AuthorizeOneStationCommand(0x10, 1, "AUTHORIZE ECU POWER ON");
        await link.WriteAsync(powerOn, default);
        Assert.Equal(1, api.WriteCount);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await link.WriteAsync(powerOn, default));
        link.AuthorizeOneStationCommand(0x12, 1, "AUTHORIZE IGN ON");
        link.RevokeStationCommand();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await link.WriteAsync(UsbWireCodec.EncodeRequest(32, 0x12, new byte[] { 1 }), default));
        Assert.Equal(1, api.WriteCount);

        var keyFrame = PhysicalKeypadProtocol.EncodeUsbCanPayload(0xA5001u);
        var keyRequest = UsbWireCodec.EncodeRequest(33, 0x40, keyFrame);
        link.AuthorizeOneKeyFrame(keyFrame, PhysicalKeypadProtocol.Confirmation(0xA5001u));
        await link.WriteAsync(keyRequest, default);
        Assert.Equal(2, api.WriteCount);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await link.WriteAsync(keyRequest, default));
        await link.CloseAsync(default);
    }

    [Fact]
    public async Task DEVICE_REC_10_DisconnectAndReconnectRevokePendingStationAndKeyAuthorization()
    {
        var api = new FakeWinUsbApi();
        await using var link = NewLink(api);
        await link.OpenAsync(default);
        link.AuthorizeOneStationCommand(0x10, 1, "AUTHORIZE ECU POWER ON");
        await link.CloseAsync(default);
        await link.OpenAsync(default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await link.WriteAsync(UsbWireCodec.EncodeRequest(10, 0x10, new byte[] { 1 }), default));

        var keyFrame = PhysicalKeypadProtocol.EncodeUsbCanPayload(0x00A17u);
        link.AuthorizeOneKeyFrame(keyFrame, PhysicalKeypadProtocol.Confirmation(0x00A17u));
        await link.CloseAsync(default);
        await link.OpenAsync(default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await link.WriteAsync(UsbWireCodec.EncodeRequest(11, 0x40, keyFrame), default));
        Assert.Equal(0, api.WriteCount);
        Assert.Equal(3, link.ConnectionGeneration);
        await link.CloseAsync(default);
    }

    private static WinUsbByteLink NewLink(FakeWinUsbApi api) =>
        new(DeviceId, new SingleControllerDiscovery(), api);

    private sealed class SingleControllerDiscovery : IWinUsbDiscovery
    {
        public IReadOnlyList<WinUsbCandidate> ListCandidates() => new[] { Controller };
    }

    private sealed class FakeWinUsbApi : IWinUsbTransportNative
    {
        public bool IsSupported => true;
        public int LastError => FailureCode;
        public int FailureCode { get; set; } = 6;
        public string? FailStage { get; set; }
        public uint CapturedFlags { get; private set; }
        public int FreeCount { get; private set; }
        public int WriteCount { get; private set; }
        public SafeFileHandle Open(string path, uint flags)
        {
            CapturedFlags = flags;
            return FailStage == "CreateFileW"
                ? new SafeFileHandle(new IntPtr(-1), false)
                : new SafeFileHandle(new IntPtr(1234), false);
        }
        public bool Initialize(SafeFileHandle file, out IntPtr interfaceHandle)
        {
            interfaceHandle = FailStage == "WinUsb_Initialize" ? IntPtr.Zero : new IntPtr(4321);
            return FailStage != "WinUsb_Initialize";
        }
        public bool QueryInterfaceSettings(IntPtr handle, out WinUsbNative.USB_INTERFACE_DESCRIPTOR d)
        {
            d = new WinUsbNative.USB_INTERFACE_DESCRIPTOR
            { bInterfaceNumber = 0, bAlternateSetting = 0, bInterfaceClass = 0xFF, bNumEndpoints = 2 };
            return FailStage != "WinUsb_QueryInterfaceSettings";
        }
        public bool QueryPipe(IntPtr handle, byte index, out WinUsbNative.WINUSB_PIPE_INFORMATION info)
        {
            info = new WinUsbNative.WINUSB_PIPE_INFORMATION
            { PipeType = WinUsbNative.PipeTypeBulk, PipeId = index == 0 ? (byte)0x82 : (byte)0x03, MaximumPacketSize = 64 };
            return FailStage != $"WinUsb_QueryPipe[{index}]";
        }
        public bool SetPipeTimeout(IntPtr handle, byte endpoint, ref uint timeout) =>
            FailStage != $"WinUsb_SetPipePolicy[{(endpoint == 0x82 ? "IN:0x82" : "OUT:0x03")}]";
        public bool Read(IntPtr handle, byte[] buffer, out uint transferred) { transferred = 0; return false; }
        public bool Write(IntPtr handle, byte[] bytes, out uint transferred)
        { WriteCount++; transferred = (uint)bytes.Length; return true; }
        public bool Cancel(SafeFileHandle handle) => true;
        public void Free(IntPtr handle) => FreeCount++;
    }

    private sealed class InitFailureByteLink : IUsbByteLink
    {
        public ValueTask OpenAsync(CancellationToken ct) =>
            ValueTask.FromException(new WinUsbStageException("WinUsb_Initialize", 6));
        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct) => ValueTask.FromResult(0);
        public ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CloseAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ScriptedByteLink : IUsbByteLink
    {
        private readonly Channel<byte[]> _rx = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte[]> _tx = Channel.CreateUnbounded<byte[]>();
        private bool _open;
        public ValueTask OpenAsync(CancellationToken ct) { _open = true; return ValueTask.CompletedTask; }
        public ValueTask CloseAsync(CancellationToken ct) { _open = false; return ValueTask.CompletedTask; }
        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct)
        {
            byte[] next = await _rx.Reader.ReadAsync(ct);
            next.AsMemory().CopyTo(destination);
            return next.Length;
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken ct)
        {
            if (!_open) throw new IOException("closed");
            _tx.Writer.TryWrite(source.ToArray());
            return ValueTask.CompletedTask;
        }
        public Task<byte[]> NextWriteAsync() => _tx.Reader.ReadAsync().AsTask();
        public void Deliver(byte[] raw) => _rx.Writer.TryWrite(raw);
        public ValueTask DisposeAsync() { _rx.Writer.TryComplete(); _tx.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
}
