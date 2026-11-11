using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoTest.Desktop.Bench;
using Xunit;

namespace AutoTest.Desktop.Tests.Bench;

public sealed class BenchCanEditorTests
{
    [Fact]
    public async Task UserCanConfigureAndSaveSingleFrameWithoutPhysicalTransmissions()
    {
        var port = new TestPort();
        var vm = new BenchCanEditorViewModel(port);
        vm.AddCommand.Execute();
        Assert.Single(vm.Messages);
        var item = vm.SelectedMessage!;
        item.Name = "自定义网络帧";
        item.CanIdHex = "0x18DAF110";
        item.Extended = true;
        item.DataHex = "02 10 03";
        item.PeriodMsText = "250";
        Assert.Equal(0, port.SendCount);
        await vm.SaveAsync();
        Assert.False(vm.Dirty);
        Assert.Equal(0x18DAF110u, port.Saved!.Single().ArbitrationId);
        Assert.Equal(new byte[] { 2, 16, 3 }, port.Saved.Single().Payload());
        await vm.SendOnceAsync();
        Assert.Equal(1, port.SendCount);
        Assert.Equal("自定义网络帧", port.LastMessage!.Name);
    }

    [Fact]
    public async Task UnsavedValidCanCanTrialRunWithoutSavingAndReloadKeepsDraft()
    {
        var port = new TestPort();
        var vm = new BenchCanEditorViewModel(port);
        vm.AddCommand.Execute();
        await vm.SendOnceAsync();
        Assert.Equal(1, port.SendCount);
        Assert.Null(port.Saved);
        await vm.LoadAsync();
        Assert.Single(vm.Messages);
        Assert.True(vm.Dirty);
        await vm.SaveAsync();
        vm.SelectedMessage!.DataHex = "ABC"; // odd hexadecimal nibble count
        await vm.SaveAsync();
        Assert.True(vm.Dirty);
        Assert.Contains("保存失败", vm.Status);
        Assert.Equal("00", port.Saved!.Single().DataHex);
    }

    [Fact]
    public async Task PeriodicStopCancelsWithoutClaimingECUReceived()
    {
        var port = new TestPort { StayActiveUntilCancel = true };
        var vm = new BenchCanEditorViewModel(port);
        vm.AddCommand.Execute();
        // An unsaved checked frame may be sent as a bench experiment.
        vm.SelectedMessage!.IsChecked = true;
        var running = vm.StartPeriodicAsync();
        Assert.True(vm.IsPeriodicRunning);
        vm.StopPeriodicCommand.Execute();
        await running;
        Assert.Equal(1, port.PeriodicCount);
        Assert.False(vm.IsPeriodicRunning);
        Assert.Contains("取消", vm.Status);
    }

    [Fact]
    public async Task CheckedRowsDefineBatchAndUnselectedRowIsNotTransmitted()
    {
        var port = new TestPort();
        var vm = new BenchCanEditorViewModel(port);
        vm.AddCommand.Execute();
        vm.SelectedMessage!.CanIdHex = "0x123";
        vm.SelectedMessage.DataHex = "01 02";
        vm.SelectedMessage.PeriodMsText = "100";
        vm.SelectedMessage.IsChecked = true;
        vm.AddCommand.Execute();
        vm.SelectedMessage!.CanIdHex = "0x456";
        vm.SelectedMessage.PeriodMsText = "250";
        vm.SelectedMessage.IsChecked = true;
        vm.AddCommand.Execute();
        vm.SelectedMessage!.CanIdHex = "0x789";
        Assert.Equal(2, vm.CheckedCount);
        await vm.StartPeriodicAsync();
        Assert.Equal(1, port.PeriodicCount);
        Assert.Equal(new uint[]{0x123u, 0x456u}, port.LastBatch!.Select(x => x.ArbitrationId).ToArray());
        Assert.Equal(new int[]{100, 250}, port.LastBatch.Select(x => x.PeriodMs).ToArray());
        Assert.True(vm.Dirty); // trial did not save
        Assert.Null(port.Saved);
    }

    [Theory]
    [InlineData(1000L, 100L, 1100L)]
    [InlineData(1650L, 100L, 1750L)]
    [InlineData(20000L, 500L, 20500L)]
    public void HostPeriodicCanNeverCompensatesDelayedAckWithCatchUpBurst(
        long ackTicks, long periodTicks, long expected)
    {
        // A board delay, extra CAN traffic, or Windows scheduling jitter must
        // not cause the same row to be sent immediately again.
        Assert.Equal(expected, BenchCanSchedule.NextDue(ackTicks, periodTicks));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchCanSchedule.NextDue(ackTicks, 0));
    }

    [Fact]
    public async Task UnsavedCanRowsCannotSendAfterProjectSwitch()
    {
        var port = new TestPort { ActiveProjectId = "ECU-A" };
        var vm = new BenchCanEditorViewModel(port);
        await vm.LoadAsync();
        vm.AddCommand.Execute();
        vm.SelectedMessage!.IsChecked = true;
        port.ActiveProjectId = "ECU-B";
        await vm.SendOnceAsync();
        await vm.StartPeriodicAsync();
        await vm.SaveAsync();
        Assert.Equal(0, port.SendCount);
        Assert.Equal(0, port.PeriodicCount);
        Assert.Null(port.Saved);
        port.ActiveProjectId = "ECU-A";
        await vm.StartPeriodicAsync();
        Assert.Equal(1, port.PeriodicCount);
    }

    [Fact]
    public async Task SavedCanRecordsRejectCrossProjectLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "autotest-can-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "sample.autotest-project.json");
        await File.WriteAllTextAsync(path, "{}");
        string activeProject = Guid.NewGuid().ToString("D");
        try
        {
            var store = new ProjectBenchCanStore(() => path, () => activeProject);
            await store.SaveAsync(new[] { new BenchCanMessage(
                Guid.NewGuid().ToString("D"), "user message", 0x123, false, "00 FF", 100) },
                CancellationToken.None);
            Assert.Single(await store.LoadAsync(CancellationToken.None));
            activeProject = Guid.NewGuid().ToString("D");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.LoadAsync(CancellationToken.None));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void FrameIdPayloadAndPeriodAreValidated()
    {
        var id = Guid.NewGuid().ToString("D");
        Assert.Throws<ArgumentException>(() =>
            new BenchCanMessage(id, "bad", 0x800, false, "00", 100).Validate());
        Assert.Throws<ArgumentException>(() =>
            new BenchCanMessage(id, "bad", 0x123, false, "001", 100).Validate());
        Assert.Throws<ArgumentException>(() =>
            new BenchCanMessage(id, "bad", 0x123, false, "00", 10).Validate());
        new BenchCanMessage(id, "valid", 0x1FFFFFFF, true, "01 02", 100).Validate();
    }

    private sealed class TestPort : IBenchWorkbenchPort
    {
        public string? ActiveProjectId { get; set; }
        public IReadOnlyList<BenchCanMessage>? Saved { get; set; }
        public BenchCanMessage? LastMessage { get; private set; }
        public int SendCount { get; private set; }
        public int PeriodicCount { get; private set; }
        public bool StayActiveUntilCancel { get; set; }
        public IReadOnlyList<BenchCanMessage>? LastBatch { get; private set; }
        public Task<IReadOnlyList<BenchCanMessage>> LoadCanMessagesAsync(CancellationToken ct) =>
            Task.FromResult(Saved ?? (IReadOnlyList<BenchCanMessage>)Array.Empty<BenchCanMessage>());
        public Task SaveCanMessagesAsync(IReadOnlyList<BenchCanMessage> messages, CancellationToken ct)
        { Saved = messages; return Task.CompletedTask; }
        public Task<BenchOperationResult> SendCanOnceAsync(BenchCanMessage message, CancellationToken ct)
        {
            LastMessage = message;
            SendCount++;
            return Task.FromResult(new BenchOperationResult(true, "op", "cor", 1,
                "CAN1 0x40", "ACK", "trace", null, null, BenchEvidenceSource.Physical,
                "ACK does not prove ECU received"));
        }
        public async Task<BenchActionExecution> RunCanPeriodicBatchAsync(
            IReadOnlyList<BenchCanMessage> messages,
            IProgress<BenchCanPeriodicProgress>? progress, CancellationToken ct)
        {
            PeriodicCount++;
            LastBatch = messages.ToArray();
            if (StayActiveUntilCancel) await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new BenchActionExecution(true, false, false, "test-only");
        }
        public Task<BenchConfiguration?> LoadConfigurationAsync(CancellationToken ct) =>
            Task.FromResult<BenchConfiguration?>(null);
        public Task<BenchConnectionSnapshot> GetConnectionAsync(CancellationToken ct) =>
            Task.FromResult(new BenchConnectionSnapshot(BenchConnection.Disconnected, null, null, null, null, null));
        public Task<IReadOnlyList<BenchDevice>> DiscoverCompatibleDevicesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BenchDevice>>(Array.Empty<BenchDevice>());
        public Task<BenchConnectionSnapshot> ConnectAsync(string id, CancellationToken ct) =>
            GetConnectionAsync(ct);
        public Task<BenchConnectionSnapshot> DisconnectAsync(CancellationToken ct) =>
            GetConnectionAsync(ct);
        public Task<BenchOperationResult> ApplyStateAsync(string revision, string controller,
            string state, BenchEvidenceSource source, CancellationToken ct) =>
            Task.FromResult(new BenchOperationResult(false, null, null, null,
                null, null, null, "BLOCKED", "Test-only fake has no physical hardware", source, null));
        public Task SaveManualMeasurementAsync(BenchManualMeasurement measurement, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
