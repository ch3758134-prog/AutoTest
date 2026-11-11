using AutoTest.Desktop.Bench;
using Xunit;

namespace AutoTest.Desktop.Tests.Bench;

public sealed class ManualKeySimSwitchTests
{
    [Fact]
    public async Task SelectionAloneDoesNotSendHardwareEvenWithTwentyKeys()
    {
        var port = new CountingPort();
        var vm = new BenchWorkbenchViewModel(port);
        await vm.InitializeAsync();
        Assert.Equal(20, vm.Switches.Count);
        vm.Switches[0].IsSelected = true;
        vm.Switches[9].IsSelected = true;
        vm.Switches[19].IsSelected = true;
        Assert.Equal((1u << 0) | (1u << 9) | (1u << 19), vm.SelectedKeyMask);
        Assert.Equal(3, vm.SelectedKeyCount);
        Assert.Equal(0, port.DirectCallCount);
        Assert.Equal(0, port.ReleaseCount);
        Assert.Contains("尚未", vm.DirectResult);
    }

    [Fact]
    public async Task ApplyingDirectMaskDoesNotRequireAnEcuControllerProfile()
    {
        var port = new CountingPort();
        var vm = new BenchWorkbenchViewModel(port);
        await vm.InitializeAsync();
        Assert.Equal("未选择工程", vm.ProjectName);
        vm.Switches[0].IsSelected = true;
        vm.Switches[19].IsSelected = true;
        await vm.ApplyDirectMaskAsync();
        Assert.Equal(1, port.DirectCallCount);
        Assert.Equal(0x80001u, port.LastMask);
        Assert.Contains("主控已接收", vm.DirectResult);
        Assert.Contains("未测量", vm.DirectResult);
    }

    [Fact]
    public async Task DirectMaskManualVoltageUsesLastAcknowledgedMaskAndNeverInfersVoltage()
    {
        var port = new CountingPort();
        var vm = new BenchWorkbenchViewModel(port);
        await vm.InitializeAsync();
        vm.Switches[2].IsSelected = true;
        await vm.ApplyDirectMaskAsync();
        vm.Switches[3].IsSelected = true; // next selected mask is NOT the command already sent
        vm.Voltages[0].Value = "2";
        await vm.SaveMeasurementAsync();
        Assert.NotNull(port.Saved);
        Assert.Equal("RAW_KEYSIM", port.Saved!.ProjectRevision);
        Assert.Equal("MASK_0x00004", port.Saved.StateId);
        Assert.Equal("op", port.Saved.OperationId);
        Assert.Equal(2m, port.Saved.Voltages[0].Volts);
        Assert.Null(port.Saved.Voltages[1].Volts);
    }

    [Fact]
    public async Task ClearingSelectionDoesNotReleaseHardware()
    {
        var port = new CountingPort();
        var vm = new BenchWorkbenchViewModel(port);
        vm.Switches[3].IsSelected = true;
        vm.ClearSwitchesCommand.Execute(null);
        Assert.Equal(0u, vm.SelectedKeyMask);
        Assert.Equal(0, port.DirectCallCount);
        Assert.Equal(0, port.ReleaseCount);
        Assert.Contains("未发送", vm.DirectResult);
    }

    [Fact]
    public async Task ExplicitReleaseUsesDifferentPhysicalAction()
    {
        var port = new CountingPort();
        var vm = new BenchWorkbenchViewModel(port);
        vm.Switches[14].IsSelected = true;
        await vm.ReleaseAllKeysAsync();
        Assert.Equal(1, port.ReleaseCount);
        Assert.Equal(0, port.DirectCallCount);
        Assert.Equal(0u, vm.SelectedKeyMask);
        Assert.Contains("实际", vm.DirectResult);
    }

    [Fact]
    public async Task IgnitionIsSeparateFromKeysAndInvalidatesPreviousVoltageContext()
    {
        var port = new CountingPort();
        var vm = new BenchWorkbenchViewModel(port);
        await vm.InitializeAsync();
        vm.Switches[1].IsSelected = true;
        await vm.ApplyDirectMaskAsync();
        Assert.True(vm.SaveMeasurementCommand.CanExecute(null));
        await vm.SetIgnAsync(true);
        Assert.Equal(true, port.LastIgnOn);
        Assert.Contains("实际电压", vm.IgnStatus);
        Assert.False(vm.SaveMeasurementCommand.CanExecute(null));
        Assert.Equal(0, port.ReleaseCount);
        Assert.Equal(1, port.DirectCallCount);
    }

    [Fact]
    public async Task SolenoidActionsAreExplicitAndReadbackNeverClaimsMechanicalPosition()
    {
        var port = new CountingPort();
        var vm = new BenchWorkbenchViewModel(port);
        await vm.InitializeAsync();
        await vm.SetSolenoidAsync(1);
        Assert.Equal((byte)1, port.LastSolAction);
        Assert.Contains("机械", vm.SolenoidStatus);
        await vm.ReadSolenoidAsync();
        Assert.Equal(1, port.SolReadCount);
        Assert.Contains("机械到位仍未验证", vm.SolenoidStatus);
        await vm.SetSolenoidAsync(0);
        Assert.Equal((byte)0, port.LastSolAction);
    }

    [Fact]
    public async Task EmptySelectionNeverSendsPhysicalFrame()
    {
        var port = new CountingPort();
        var vm = new BenchWorkbenchViewModel(port);
        await vm.ApplyDirectMaskAsync();
        Assert.Equal(0, port.DirectCallCount);
    }

    private sealed class CountingPort : IBenchWorkbenchPort
    {
        public int DirectCallCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public uint? LastMask { get; private set; }
        public bool? LastIgnOn { get; private set; }
        public byte? LastSolAction { get; private set; }
        public int SolReadCount { get; private set; }
        public BenchManualMeasurement? Saved { get; private set; }
        public Task<BenchConfiguration?> LoadConfigurationAsync(CancellationToken ct) =>
            Task.FromResult<BenchConfiguration?>(null);
        public Task<BenchConnectionSnapshot> GetConnectionAsync(CancellationToken ct) =>
            Task.FromResult(new BenchConnectionSnapshot(BenchConnection.Connected,
                "TEST-CONNECTION", 1, null, null, "test fixture only"));
        public Task<IReadOnlyList<BenchDevice>> DiscoverCompatibleDevicesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BenchDevice>>(Array.Empty<BenchDevice>());
        public Task<BenchConnectionSnapshot> ConnectAsync(string id, CancellationToken ct) =>
            GetConnectionAsync(ct);
        public Task<BenchConnectionSnapshot> DisconnectAsync(CancellationToken ct) =>
            Task.FromResult(new BenchConnectionSnapshot(BenchConnection.Disconnected,
                null, null, null, null, null));
        public Task<BenchOperationResult> ApplyStateAsync(string revision, string controller,
            string state, BenchEvidenceSource source, CancellationToken ct) =>
            Task.FromResult(Result(false));
        public Task<BenchOperationResult> ApplyPhysicalKeyMaskAsync(uint mask, CancellationToken ct)
        {
            DirectCallCount++;
            LastMask = mask;
            return Task.FromResult(Result(true));
        }
        public Task<BenchOperationResult> ReleaseAllPhysicalKeysAsync(CancellationToken ct)
        {
            ReleaseCount++;
            return Task.FromResult(Result(true));
        }
        public Task<BenchOperationResult> SetIgnAsync(bool on, CancellationToken ct)
        {
            LastIgnOn = on;
            return Task.FromResult(new BenchOperationResult(true, "ign-op", "ign-corr", 1,
                "SET_IGN 0x12", "MAIN_CONTROLLER_ACK", "ign-trace", null, null,
                BenchEvidenceSource.Physical,
                "主控已接受，实际电压未读取"));
        }

        public Task<BenchOperationResult> SetSolenoidAsync(byte action, CancellationToken ct)
        {
            LastSolAction = action;
            return Task.FromResult(new BenchOperationResult(true, "sol-op", "sol-cor", 1,
                "SET_SOL 0x16", "MAIN_CONTROLLER_ACK", "sol-trace", null, null,
                BenchEvidenceSource.Physical,
                "主控 ACK；没有机械到位传感器"));
        }
        public Task<BenchOperationResult> ReadSolenoidAsync(CancellationToken ct)
        {
            SolReadCount++;
            return Task.FromResult(new BenchOperationResult(true, null, null, 1,
                "GET_SOL 0x17", "GPIO_ACTION_1", "sol-trace", null, null,
                BenchEvidenceSource.Physical,
                "固件动作编码 1，不证明气缸到位"));
        }

        public Task SaveManualMeasurementAsync(BenchManualMeasurement m, CancellationToken ct)
        {
            Saved = m;
            return Task.CompletedTask;
        }
        private static BenchOperationResult Result(bool accepted) =>
            new(accepted, "op", "corr", 1, "CAN0", "USB ACK",
                "fixture-trace", null, null, BenchEvidenceSource.Physical,
                "主控 CAN_SEND 已 ACK；模拟电压未测量");
    }
}
