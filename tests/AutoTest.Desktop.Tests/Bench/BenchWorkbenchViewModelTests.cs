using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoTest.Desktop.Bench;
using AutoTest.Desktop.Workbench;
using Xunit;

namespace AutoTest.Desktop.Tests.Bench;

public sealed class BenchWorkbenchViewModelTests
{
    private static BenchConfiguration Config(params BenchStateDefinition[] states) =>
        new("Demo Project", "ECU-A", "rev-7", new[]
        {
            new BenchControllerDefinition("controller-x", "任意控制器", states)
        });

    [Fact]
    public async Task ArbitraryNumberOfConfiguredStatesRendersWithoutHardcodedKeys()
    {
        var fake = new FakePort { Configuration = Config(Enumerable.Range(0, 7)
            .Select(i => new BenchStateDefinition($"id-{i}", $"状态{i}", (uint)(1 << i),
                BenchInteraction.Latching, null)).ToArray()) };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.InitializeAsync();
        Assert.Equal(7, vm.States.Count);
        Assert.Equal("rev-7", vm.Revision);
        Assert.Equal("状态6", vm.States.Last().Label);
        Assert.False(vm.IsPhysical);
    }

    [Fact]
    public async Task MissingProjectDoesNotCreateFakeStates()
    {
        var vm = new BenchWorkbenchViewModel(new FakePort());
        await vm.InitializeAsync();
        Assert.Empty(vm.States);
        Assert.Equal("未选择工程", vm.ProjectName);
    }

    [Fact]
    public async Task MultiDeviceRequiresExplicitSelectionThenUsesOneSharedPort()
    {
        var fake = new FakePort { Devices = new[] {new BenchDevice("a", "A"), new BenchDevice("b", "B")} };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.ConnectOrDisconnectAsync();
        Assert.True(vm.ShowDeviceChoice);
        Assert.Null(fake.ConnectedId);
        vm.SelectedDevice = vm.Devices[1];
        await vm.ConnectOrDisconnectAsync();
        Assert.Equal("b", fake.ConnectedId);
        Assert.Equal("已连接", vm.ConnectionStatus);
        await vm.ConnectOrDisconnectAsync();
        Assert.Equal("未连接", vm.ConnectionStatus);
    }

    [Fact]
    public async Task OneDeviceAutoConnectsWithoutSelector()
    {
        var fake = new FakePort { Devices = new[] { new BenchDevice("device-1", "Mainboard") } };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.ConnectOrDisconnectAsync();
        Assert.Equal("device-1", fake.ConnectedId);
        Assert.False(vm.ShowDeviceChoice);
    }

    [Fact]
    public async Task IllegalMaskCannotReachPort()
    {
        var fake = new FakePort { Configuration = Config(new BenchStateDefinition("bad", "bad", 0x100000,
            BenchInteraction.Latching, null)) };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.InitializeAsync();
        await vm.ApplyAsync(vm.States[0]);
        Assert.Equal(0, fake.ApplyCount);
        Assert.Contains(vm.Messages, m => m.Heading == "位掩码不合法");
    }

    [Fact]
    public async Task UnknownSourceCannotReachPort()
    {
        var fake = new FakePort { Configuration = Config(new BenchStateDefinition("s", "state", 0x10,
            BenchInteraction.Latching, null)) };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.InitializeAsync();
        vm.Source = BenchEvidenceSource.Unknown;
        await vm.ApplyAsync(vm.States[0]);
        Assert.Equal(0, fake.ApplyCount);
    }

    [Fact]
    public async Task AckDoesNotCreateFakeVoltageAndMeasurementIsExplicit()
    {
        var fake = new FakePort { Configuration = Config(new BenchStateDefinition("s", "state", 0x10,
            BenchInteraction.Momentary, null)) };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.InitializeAsync();
        await vm.ApplyAsync(vm.States[0]);
        Assert.Contains("未测量", vm.LastEvidence);
        Assert.All(vm.Voltages, v => Assert.Equal("", v.Value));
        await vm.SaveMeasurementAsync();
        Assert.Null(fake.Saved);
        vm.Voltages[0].Value = "2.5";
        await vm.SaveMeasurementAsync();
        Assert.NotNull(fake.Saved);
        Assert.Equal(BenchEvidenceSource.Mock, fake.Saved!.Source);
        Assert.Equal(2.5m, fake.Saved.Voltages[0].Volts);
        Assert.Null(fake.Saved.Voltages[1].Volts);
    }

    [Fact]
    public async Task ChangingSourceInvalidatesPriorOperationMeasurement()
    {
        var fake = new FakePort { Configuration = Config(new BenchStateDefinition("s", "state", 0x1,
            BenchInteraction.Latching, null)) };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.InitializeAsync();
        await vm.ApplyAsync(vm.States[0]);
        vm.Source = BenchEvidenceSource.Replay;
        vm.Voltages[0].Value = "1";
        await vm.SaveMeasurementAsync();
        Assert.Null(fake.Saved);
    }

    [Fact]
    public async Task NativeFailureInformationIsVisibleWithoutInventingWin32Code()
    {
        var fake = new FakePort { Devices = new[] {new BenchDevice("dev", "Mainboard")},
            ConnectFailure = true };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.ConnectOrDisconnectAsync();
        Assert.Contains("WinUsb_Initialize", vm.ConnectionDetail);
        Assert.Contains("31", vm.ConnectionDetail);
        Assert.Contains("dev", vm.ConnectionDetail);
        Assert.Equal("未提供（由 DEVICE 返回）", WorkbenchDisplay.Win32(null));
    }

    [Fact]
    public async Task FailedConnectionForcesFreshDiscoveryInsteadOfStaleRetry()
    {
        var fake = new FakePort { Devices = new[] { new BenchDevice("old", "Old") }, ConnectFailure = true };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.ConnectOrDisconnectAsync();
        Assert.Equal(1, fake.DiscoveryCount);
        Assert.Equal("连接失败", vm.ConnectionStatus);
        fake.Devices = new[] { new BenchDevice("new", "New") };
        fake.ConnectFailure = false;
        await vm.ConnectOrDisconnectAsync();
        Assert.Equal(2, fake.DiscoveryCount);
        Assert.Equal("new", fake.ConnectedId);
    }

    [Fact]
    public async Task ExternalDisconnectInvalidatesMeasurementWithoutNewUsbSession()
    {
        var fake = new FakePort { Configuration = Config(new BenchStateDefinition("s", "State", 1,
            BenchInteraction.Latching, null)) };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.InitializeAsync();
        await vm.ApplyAsync(vm.States[0]);
        vm.AcceptConnectionSnapshot(new BenchConnectionSnapshot(BenchConnection.Disconnected,
            null, null, null, null, "USB 已拔出"));
        vm.Voltages[0].Value = "1.2";
        await vm.SaveMeasurementAsync();
        Assert.Null(fake.Saved);
        Assert.Equal("未连接", vm.ConnectionStatus);
    }

    [Fact]
    public async Task ReloadMakesPreviousStepObjectInvalid()
    {
        var fake = new FakePort { Configuration = Config(new BenchStateDefinition("s", "State", 1,
            BenchInteraction.Latching, null)) };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.InitializeAsync();
        var stale = vm.States[0];
        await vm.ReloadAsync();
        await vm.ApplyAsync(stale);
        Assert.Equal(0, fake.ApplyCount);
    }

    [Fact]
    public async Task PreviousMultiDeviceSelectionCannotSurviveNewDiscovery()
    {
        var fake = new FakePort { Devices = new[] { new BenchDevice("a", "A"), new BenchDevice("b", "B") } };
        var vm = new BenchWorkbenchViewModel(fake);
        await vm.ConnectOrDisconnectAsync();
        var old = vm.Devices[0];
        vm.AcceptConnectionSnapshot(new BenchConnectionSnapshot(BenchConnection.Disconnected,
            null, null, null, null, "USB 已变化"));
        vm.SelectedDevice = old;
        Assert.Null(vm.SelectedDevice);
    }

    private sealed class FakePort : IBenchWorkbenchPort
    {
        public BenchConfiguration? Configuration { get; set; }
        public IReadOnlyList<BenchDevice> Devices { get; set; } = Array.Empty<BenchDevice>();
        public string? ConnectedId { get; private set; }
        public bool ConnectFailure { get; set; }
        public int DiscoveryCount { get; private set; }
        public int ApplyCount { get; private set; }
        public BenchManualMeasurement? Saved { get; private set; }
        public Task<BenchConfiguration?> LoadConfigurationAsync(CancellationToken ct) => Task.FromResult(Configuration);
        public Task<BenchConnectionSnapshot> GetConnectionAsync(CancellationToken ct) =>
            Task.FromResult(new BenchConnectionSnapshot(BenchConnection.Disconnected, null, null, null, null, null));
        public Task<IReadOnlyList<BenchDevice>> DiscoverCompatibleDevicesAsync(CancellationToken ct)
        {
            DiscoveryCount++;
            return Task.FromResult(Devices);
        }
        public Task<BenchConnectionSnapshot> ConnectAsync(string identity, CancellationToken ct)
        {
            ConnectedId = ConnectFailure ? null : identity;
            return Task.FromResult(ConnectFailure
                ? new BenchConnectionSnapshot(BenchConnection.Faulted, identity, 2, "WinUsb_Initialize", 31, "原生初始化失败")
                : new BenchConnectionSnapshot(BenchConnection.Connected, identity, 2, null, null, "已连接"));
        }
        public Task<BenchConnectionSnapshot> DisconnectAsync(CancellationToken ct)
        {
            ConnectedId = null;
            return Task.FromResult(new BenchConnectionSnapshot(BenchConnection.Disconnected, null, null, null, null, "已断开"));
        }
        public Task<BenchOperationResult> ApplyStateAsync(string revision, string controllerId, string stateId,
            BenchEvidenceSource source, CancellationToken ct)
        {
            ApplyCount++;
            return Task.FromResult(new BenchOperationResult(true, "op-01", "cor-01", 2, "AA", "55", "trace-1",
                null, null, source, "Mock ACK"));
        }
        public Task SaveManualMeasurementAsync(BenchManualMeasurement value, CancellationToken ct)
        { Saved = value; return Task.CompletedTask; }
    }
}
