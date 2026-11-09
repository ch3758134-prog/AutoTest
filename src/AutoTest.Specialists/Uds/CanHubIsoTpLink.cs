using System.Collections.Immutable;
using System.Threading.Channels;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Uds;

// Adapter for existing public contracts; read-only diagnostic requests are
// permitted at UdsClient; no programming operation can use this physical link.
public sealed class CanHubIsoTpLink : IIsoTpFrameLink, IAsyncDisposable
{
    private readonly ICanFrameHub _hub;
    private readonly UdsEndpointProfile _profile;
    private readonly LeaseToken _lease;
    private readonly Channel<byte[]> _rx = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _pump;
    private int _closed;
    public CanHubIsoTpLink(ICanFrameHub hub, UdsEndpointProfile profile, LeaseToken lease)
    {
        UdsEndpointProfileValidator.ValidateAddressing(profile);
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _profile = profile; _lease = lease;
        if (lease.Owner is null || lease.ExpiresUtc <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Expired or invalid CAN lease.",nameof(lease));
        _pump = Task.Run(PumpAsync);
    }
    private async Task PumpAsync()
    {
        try
        {
            var spec = new CanSubscriptionSpec(_profile.Role,
                ImmutableArray.Create(_profile.ResponseCanId),CanBackpressurePolicy.DropWithLossFact,256);
            await foreach (var observation in _hub.SubscribeAsync(spec,_stop.Token).ConfigureAwait(false))
            {
                if (observation.SessionGeneration != _lease.SessionGeneration)
                    throw new IOException("CAN session generation changed.");
                if (observation.LostFrames != 0)
                    throw new IOException("CAN loss observed during ISO-TP conversation.");
                if (observation.Frame.Role != _profile.Role || observation.Frame.Id != _profile.ResponseCanId
                    || observation.Frame.ExtendedId != (_profile.ResponseCanId > 0x7FF)
                    || observation.Frame.Dlc != observation.Frame.Data.Length)
                    continue;
                await _rx.Writer.WriteAsync(observation.Frame.Data.ToArray(),_stop.Token).ConfigureAwait(false);
            }
            _rx.Writer.TryComplete(new IOException("CAN subscription ended."));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { _rx.Writer.TryComplete(); }
        catch (Exception ex) { _rx.Writer.TryComplete(ex); }
    }
    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (_closed != 0) throw new ObjectDisposedException(nameof(CanHubIsoTpLink));
        var frame = CanFrame.Create(_profile.Role,_profile.RequestCanId > 0x7FF,
            _profile.RequestCanId,data.Span);
        var receipt = await _hub.TransmitAsync(new CanTxRequest(frame,Guid.NewGuid()),_lease,ct).ConfigureAwait(false);
        if (receipt.Outcome != Outcome.Accepted)
            throw new IOException($"CAN transmission rejected: {receipt.FailureCode}");
        // A transport ACK is not an ECU physical-effect confirmation.
    }
    public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct)
    {
        if (_closed != 0) throw new ObjectDisposedException(nameof(CanHubIsoTpLink));
        return await _rx.Reader.ReadAsync(ct).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed,1) != 0) return;
        _stop.Cancel();
        try { await _pump.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
