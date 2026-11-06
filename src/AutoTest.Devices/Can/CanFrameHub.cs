using System.Collections.Immutable;
using System.Threading.Channels;
using AutoTest.Contracts;
using AutoTest.Core;
namespace AutoTest.Devices.Can;
// Each listener owns a bounded channel. Producers never await slow UI/UDS/Wave consumers.
public sealed class CanFrameHub : ICanFrameHub, IDisposable
{
    private sealed class Subscriber
    {
        public required CanSubscriptionSpec Spec;
        public required Channel<CanObservation> Queue;
        public long Dropped;
    }
    private readonly object _sync=new();
    private readonly HashSet<Subscriber> _subscribers=new();
    private readonly Func<CanTxRequest,LeaseToken,CancellationToken,ValueTask<OperationResult<CanTxReceipt>>>? _transmit;
    private bool _disposed;
    public CanFrameHub(Func<CanTxRequest,LeaseToken,CancellationToken,ValueTask<OperationResult<CanTxReceipt>>>? transmit=null) => _transmit=transmit;
    public int SubscriberCount { get { lock(_sync) return _subscribers.Count; } }
    public long TotalDropped {get;private set;}
    public void Publish(CanObservation observation)
    {
        lock(_sync)
        {
            if(_disposed) return;
            foreach(var listener in _subscribers)
            {
                if(listener.Spec.Role!=observation.Frame.Role ||
                   (!listener.Spec.AllowedIds.IsDefaultOrEmpty && !listener.Spec.AllowedIds.Contains(observation.Frame.Id))) continue;
                // Explicitly account for overflow before a replacement (no silent DropOldest).
                if(!listener.Queue.Writer.TryWrite(observation with { LostFrames=listener.Dropped }))
                { listener.Dropped++; TotalDropped++; }
            }
        }
    }
    public async IAsyncEnumerable<CanObservation> SubscribeAsync(CanSubscriptionSpec spec,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct=default)
    {
        if(spec.Capacity<=0) throw new ArgumentOutOfRangeException(nameof(spec));
        // BlockProducer is deliberately not implemented: never block a USB receiver.
        if(spec.Backpressure!=CanBackpressurePolicy.DropWithLossFact)
            throw new NotSupportedException("BlockProducer is unsafe on the shared receive path.");
        var listener=new Subscriber { Spec=spec, Queue=Channel.CreateBounded<CanObservation>(new BoundedChannelOptions(spec.Capacity)
            { SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait }) };
        lock(_sync) { if(_disposed) throw new ObjectDisposedException(nameof(CanFrameHub)); _subscribers.Add(listener); }
        try { await foreach(var item in listener.Queue.Reader.ReadAllAsync(ct)) yield return item; }
        finally { lock(_sync) { _subscribers.Remove(listener); listener.Queue.Writer.TryComplete(); } }
    }
    public ValueTask<OperationResult<CanTxReceipt>> TransmitAsync(CanTxRequest request,LeaseToken lease,CancellationToken ct)
    {
        if(_transmit is null) return ValueTask.FromResult(OperationResult<CanTxReceipt>.Rejected("NO_TRANSMITTER","No physical transmitter is configured."));
        return _transmit(request,lease,ct);
    }
    public void Disconnect()
    {
        lock(_sync) { foreach(var s in _subscribers) s.Queue.Writer.TryComplete(); _subscribers.Clear(); }
    }
    public void Dispose() { lock(_sync) _disposed=true; Disconnect(); }
}
