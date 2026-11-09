using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Shell.R1;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class R1SharedCanSourceTests
{
    [Fact]
    public void NoDeviceBindingDoesNotExposePhysicalCanOrClaimUdsReadiness()
    {
        var source = new R1SharedCanSource();
        Assert.False(source.PhysicalUdsEnabled);
        Assert.False(source.TryGetDutReceiveOnly(out var hub, out var reason));
        Assert.Null(hub);
        Assert.Equal("NO_SHARED_DEVICE_HUB", reason);
    }

    [Fact]
    public async Task ExistingHubCanBeObservedButPhysicalTxIsAlwaysRejected()
    {
        var physicalHub = new FakeSharedHub();
        var source = new R1SharedCanSource();
        source.BindNonOwning(physicalHub, () => true, () => 7, () => true);

        Assert.True(source.TryGetDutReceiveOnly(out var receiver, out var status));
        Assert.NotNull(receiver);
        Assert.Equal("RECEIVE_ONLY_PHYSICAL_TX_DENIED", status);

        var frame = CanFrame.Create(BusRole.DutCan, false, 0x7E0u, [0x03, 0x22, 0xF1, 0x90]);
        var lease = new LeaseToken(Guid.NewGuid(), "fake-test", 7, DateTimeOffset.UtcNow.AddMinutes(1));
        var denied = await receiver.TransmitAsync(new CanTxRequest(frame, Guid.NewGuid()),
            lease, CancellationToken.None);

        Assert.Equal(Outcome.Rejected, denied.Outcome);
        Assert.Equal("R1_PHYSICAL_TX_DENIED", denied.FailureCode);
        Assert.Equal(0, physicalHub.TransmitCalls);

        source.Unbind(physicalHub);
        Assert.Equal(0, physicalHub.DisposeCalls); // DEVICE owns the actual Hub lifecycle.
        Assert.False(source.TryGetDutReceiveOnly(out _, out _));
    }

    [Fact]
    public async Task ReceiveFacadeRejectsKeySimAndOldGeneration()
    {
        var hub = new FakeSharedHub();
        var generation = 9L;
        var source = new R1SharedCanSource();
        source.BindNonOwning(hub, () => true, () => generation, () => true);
        Assert.True(source.TryGetDutReceiveOnly(out var receiver, out _));
        Assert.NotNull(receiver);

        var keypad = new CanSubscriptionSpec(BusRole.KeypadCan,
            ImmutableArray<uint>.Empty, CanBackpressurePolicy.DropWithLossFact, 16);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in receiver.SubscribeAsync(keypad, CancellationToken.None)) { }
        });

        generation++;
        var dut = new CanSubscriptionSpec(BusRole.DutCan,
            ImmutableArray<uint>.Empty, CanBackpressurePolicy.DropWithLossFact, 16);
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in receiver.SubscribeAsync(dut, CancellationToken.None)) { }
        });
        Assert.Equal(0, hub.TransmitCalls);
    }

    [Fact]
    public void ConnectionAndTraceFailureDenyReceiveAndDifferentHubCannotReplaceOwner()
    {
        var hub = new FakeSharedHub();
        var connected = false;
        var traceHealthy = true;
        var source = new R1SharedCanSource();
        source.BindNonOwning(hub, () => connected, () => 4, () => traceHealthy);

        Assert.False(source.TryGetDutReceiveOnly(out _, out var reason));
        Assert.Equal("SHARED_DEVICE_DISCONNECTED", reason);

        connected = true;
        traceHealthy = false;
        Assert.False(source.TryGetDutReceiveOnly(out _, out reason));
        Assert.Equal("SHARED_DEVICE_TRACE_UNHEALTHY", reason);

        traceHealthy = true;
        Assert.True(source.TryGetDutReceiveOnly(out _, out _));
        Assert.Throws<InvalidOperationException>(() =>
            source.BindNonOwning(new FakeSharedHub(), () => true, () => 8, () => true));
        Assert.Throws<InvalidOperationException>(() => source.Unbind(new FakeSharedHub()));
    }

    private sealed class FakeSharedHub : ICanFrameHub
    {
        public int TransmitCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public async IAsyncEnumerable<CanObservation> SubscribeAsync(
            CanSubscriptionSpec spec, CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask<OperationResult<CanTxReceipt>> TransmitAsync(
            CanTxRequest tx, LeaseToken lease, CancellationToken ct)
        {
            TransmitCalls++;
            return ValueTask.FromResult(OperationResult<CanTxReceipt>.Rejected(
                "FAKE_HUB_DENIED", "Underlying physical Hub should never receive TX from R1 facade."));
        }

        public void Dispose() => DisposeCalls++;
    }
}
