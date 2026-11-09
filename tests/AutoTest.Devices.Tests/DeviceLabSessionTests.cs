using Xunit;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Devices.Usb;

namespace AutoTest.Devices.Tests;

public sealed class DeviceLabSessionTests
{
    private const string InstanceId = "USB\\VID_1183&PID_5850\\DEVICE_A";

    [Fact]
    public void LAB_DEVICE_01_CommandCatalogIsExactlyFirmwareReadOnlyWhitelist()
    {
        var actual = DeviceLabReadOnlyCommands.All.Select(c => c.Command).ToArray();
        Assert.Equal(new byte[] { 0xF0, 0xF1, 0x11, 0x13, 0x15, 0x17, 0x1F }, actual);
        Assert.Equal(actual.Length, actual.Distinct().Count());
        foreach (var command in actual)
            Assert.True(PhysicalCommandFence.IsReadOnly(command, ReadOnlySpan<byte>.Empty));
        foreach (var hazardous in new byte[] { 0x10, 0x12, 0x14, 0x16, 0x40, 0x50, 0x51, 0x52, 0x53, 0xFF })
            Assert.False(DeviceLabReadOnlyCommands.IsApproved(hazardous));
    }

    [Fact]
    public async Task LAB_DEVICE_02_ConnectWithoutSelectedDeviceOrB0NeverOpensWinUSB()
    {
        var backend = new FakeBackend();
        await using var ui = MakeSession(backend);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ui.ConnectAsync());
        ui.SetB0IsolationConfirmed(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ui.ConnectAsync());
        Assert.Equal(0, backend.ConnectCount);
    }

    [Fact]
    public async Task LAB_DEVICE_03_B0ConfirmationConsumedOnConnectAndRequiredAgainAfterDisconnect()
    {
        var backend = new FakeBackend();
        await using var ui = MakeSession(backend);
        ui.Select(backend.Candidates.Single());
        ui.SetB0IsolationConfirmed(true);
        await ui.ConnectAsync();
        Assert.Equal(1, backend.ConnectCount);
        Assert.False(ui.B0Confirmed);
        await ui.DisconnectAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ui.ConnectAsync());
        Assert.Equal(1, backend.ConnectCount);
        ui.SetB0IsolationConfirmed(true);
        await ui.ConnectAsync();
        Assert.Equal(2, backend.ConnectCount);
        Assert.Equal(2, backend.Traces.Distinct().Count());
    }

    [Fact]
    public async Task LAB_DEVICE_04_FailedNativeInitializationDoesNotAutoRetryOrReuseB0()
    {
        var backend = new FakeBackend { FailConnectOnce = true };
        await using var ui = MakeSession(backend);
        var events = new List<DeviceLabOperationEvent>();
        ui.OperationRecorded += events.Add;
        ui.Select(backend.Candidates.Single());
        ui.SetB0IsolationConfirmed(true);
        var ex = await Assert.ThrowsAsync<WinUsbStageException>(() => ui.ConnectAsync());
        Assert.Equal("WinUsb_Initialize", ex.Stage);
        Assert.Equal(6, ex.NativeErrorCode);
        Assert.Equal(1, backend.ConnectCount);
        Assert.False(ui.B0Confirmed);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ui.ConnectAsync());
        Assert.Equal(1, backend.ConnectCount);
        Assert.Contains(events, e => e.Action == "CONNECT" && e.Outcome == "FAIL" &&
            e.Detail!.Contains("Win32=6", StringComparison.Ordinal));
        ui.SetB0IsolationConfirmed(true);
        await ui.ConnectAsync();
        Assert.Equal(2, backend.ConnectCount);
    }

    [Fact]
    public async Task LAB_DEVICE_05_ChangingSelectedControllerInvalidatesB0()
    {
        var backend = new FakeBackend();
        backend.Candidates.Add(new WinUsbCandidate("path2", "USB\\VID_1183&PID_5850\\DEVICE_B", "WinUSB"));
        await using var ui = MakeSession(backend);
        ui.Select(backend.Candidates[0]);
        ui.SetB0IsolationConfirmed(true);
        ui.Select(backend.Candidates[1]);
        Assert.False(ui.B0Confirmed);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ui.ConnectAsync());
        Assert.Equal(0, backend.ConnectCount);
        Assert.Throws<UnauthorizedAccessException>(() => ui.Select(
            new WinUsbCandidate("wrong", "HID\\VID_1234&PID_4567\\SCANNER")));
    }

    [Fact]
    public async Task LAB_DEVICE_06_DangerousCommandDeniedBeforeBackendEvenWhenConnected()
    {
        var backend = new FakeBackend();
        await using var ui = MakeSession(backend);
        ui.Select(backend.Candidates[0]);
        ui.SetB0IsolationConfirmed(true);
        await ui.ConnectAsync();
        foreach (byte cmd in new byte[] { 0x10, 0x12, 0x16, 0x40, 0x50, 0xFF })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ui.ExecuteReadOnlyAsync(cmd, TimeSpan.FromSeconds(1)));
        Assert.Empty(backend.Commands);
    }

    [Fact]
    public async Task LAB_DEVICE_07_PingAndVersionProduceStructuredSequencedOperations()
    {
        var backend = new FakeBackend();
        await using var ui = MakeSession(backend);
        var events = new List<DeviceLabOperationEvent>();
        ui.OperationRecorded += events.Add;
        ui.Select(backend.Candidates.Single());
        ui.SetB0IsolationConfirmed(true);
        await ui.ConnectAsync();
        var pong = await ui.ExecuteReadOnlyAsync(0xF0, TimeSpan.FromMilliseconds(600));
        var version = await ui.ExecuteReadOnlyAsync(0xF1, TimeSpan.FromSeconds(1));
        Assert.Equal("PONG", System.Text.Encoding.ASCII.GetString(pong.Payload));
        Assert.Equal(MainControllerUsbIdentity.FirmwareVersion,
            System.Text.Encoding.ASCII.GetString(version.Payload));
        Assert.Equal(new byte[] { 0xF0, 0xF1 }, backend.Commands.ToArray());
        Assert.Contains(events, e => e.Action == "READ_0xF0" && e.Outcome == "OK" && e.Detail!.Contains("SEQ=17"));
        Assert.Contains(events, e => e.Action == "READ_0xF1" && e.Outcome == "OK");
        Assert.Contains(events, e => e.Action == "B0_ISOLATION" && e.Outcome == "CONFIRMED_BY_OPERATOR");
    }

    [Fact]
    public async Task LAB_DEVICE_08_ReadOnlyValidationRejectsBadFirmwarePongAndTimeoutRange()
    {
        var backend = new FakeBackend { WrongPong = true };
        await using var ui = MakeSession(backend);
        ui.Select(backend.Candidates.Single());
        ui.SetB0IsolationConfirmed(true);
        await ui.ConnectAsync();
        await Assert.ThrowsAsync<IOException>(() => ui.ExecuteReadOnlyAsync(0xF0, TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ui.ExecuteReadOnlyAsync(0xF1, TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public async Task LAB_DEVICE_09_BackendRawTraceNativeErrorAndCanRxAreForwardedWithoutTx()
    {
        var backend = new FakeBackend();
        await using var ui = MakeSession(backend);
        int rawCount = 0, nativeCount = 0, canCount = 0;
        ui.UsbTrace += _ => rawCount++;
        ui.NativeOperation += _ => nativeCount++;
        ui.CanTrace += _ => canCount++;
        ui.Select(backend.Candidates.Single());
        backend.PublishFakeEvidence();
        Assert.Equal(1, rawCount);
        Assert.Equal(1, nativeCount);
        Assert.Equal(1, canCount);
        Assert.Empty(backend.Commands);
    }

    [Fact]
    public async Task LAB_DEVICE_10_DisposeClosesBackendOnceAndNoReconnect()
    {
        var backend = new FakeBackend();
        var ui = MakeSession(backend);
        ui.Select(backend.Candidates.Single());
        ui.SetB0IsolationConfirmed(true);
        await ui.ConnectAsync();
        await ui.DisposeAsync();
        Assert.Equal(1, backend.DisposeCount);
        Assert.Equal(1, backend.ConnectCount);
    }

    private static DeviceLabSession MakeSession(FakeBackend backend) =>
        new(backend, () => $"/tmp/device-g4-test-{Guid.NewGuid():N}.jsonl");

    private sealed class FakeBackend : IDeviceLabBackend
    {
        public List<WinUsbCandidate> Candidates { get; } =
            new() { new WinUsbCandidate("mock-path", InstanceId, "WinUSB") };
        public int ConnectCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool FailConnectOnce { get; set; }
        public bool WrongPong { get; set; }
        public List<string> Traces { get; } = new();
        public List<byte> Commands { get; } = new();
        public UsbSessionState State { get; private set; } = UsbSessionState.Disconnected;
        public string? TraceFile { get; private set; }
        public string? TraceError => null;
        public long TraceDropped => 0;
        public long Generation { get; private set; }
        public long CanPublished => 0;
        public long CanDropped => 0;
        public long CanMalformed => 0;
        public long HubDropped => 0;
        public event Action<UsbTraceRecord>? UsbTrace;
        public event Action<WinUsbOperationEvent>? NativeOperation;
        public event Action<HardwareCanTrace>? CanTrace;
        public event Action<UsbSessionState>? ConnectionChanged;
        public IReadOnlyList<WinUsbCandidate> Enumerate() => Candidates;
        public Task ConnectAsync(string exactInstanceId, string traceFile, CancellationToken ct = default)
        {
            ConnectCount++;
            Assert.Equal(InstanceId, exactInstanceId);
            Traces.Add(traceFile);
            TraceFile = traceFile;
            if (FailConnectOnce)
            {
                FailConnectOnce = false;
                throw new WinUsbStageException("WinUsb_Initialize", 6, "synthetic test error");
            }
            Generation++;
            State = UsbSessionState.Connected;
            ConnectionChanged?.Invoke(State);
            return Task.CompletedTask;
        }
        public Task DisconnectAsync()
        {
            State = UsbSessionState.Disconnected;
            Generation++;
            ConnectionChanged?.Invoke(State);
            return Task.CompletedTask;
        }
        public Task<UsbResponse> ReadOnlyCommandAsync(byte command, TimeSpan timeout, CancellationToken ct = default)
        {
            Commands.Add(command);
            var data = command switch
            {
                0xF0 when WrongPong => "FAIL"u8.ToArray(),
                0xF0 => "PONG"u8.ToArray(),
                0xF1 => System.Text.Encoding.ASCII.GetBytes(MainControllerUsbIdentity.FirmwareVersion),
                _ => new byte[] { 0 }
            };
            return Task.FromResult(new UsbResponse(17, command, 0, data));
        }
        public void PublishFakeEvidence()
        {
            UsbTrace?.Invoke(new UsbTraceRecord(DateTimeOffset.UtcNow, 1, 0,
                "RX_BYTES", "A500", 0, 0x41, 0, null));
            NativeOperation?.Invoke(new WinUsbOperationEvent(DateTimeOffset.UtcNow, 2,
                "OPEN", "WinUsb_Initialize", "FAIL", InstanceId, "mock-path", 0, 6, "0x00000006", "synthetic"));
            CanTrace?.Invoke(new HardwareCanTrace(DateTimeOffset.UtcNow, 3,
                1, "000000000000000000000000000000", 0, null, false, "malformed"));
        }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            State = UsbSessionState.Disconnected;
            return ValueTask.CompletedTask;
        }
    }
}
