using AutoTest.Contracts;
using AutoTest.Core;
namespace AutoTest.Devices.Can;
// A single active lease per bus; leases never grant physical authorization by themselves.
public sealed class CanTransmitGate : ITransmitLeaseService
{
    private readonly object _sync=new();
    private readonly Dictionary<BusRole,LeaseToken> _active=new();
    private long _generation;
    public CanTransmitGate(long generation) => _generation=generation;
    public void Reset(long generation) { lock(_sync) { _active.Clear();_generation=generation; } }
    public ValueTask<OperationResult<LeaseToken>> AcquireAsync(BusRole role,string owner,TimeSpan duration,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(string.IsNullOrWhiteSpace(owner) || duration<=TimeSpan.Zero)
            return ValueTask.FromResult(OperationResult<LeaseToken>.Rejected("INVALID_LEASE","Owner and positive duration required."));
        lock(_sync)
        {
            if(_active.TryGetValue(role,out var prior) && prior.ExpiresUtc>DateTimeOffset.UtcNow)
                return ValueTask.FromResult(OperationResult<LeaseToken>.Rejected("BUS_BUSY","Another lease owns this CAN bus."));
            var lease=new LeaseToken(Guid.NewGuid(),owner,_generation,DateTimeOffset.UtcNow.Add(duration));
            _active[role]=lease;return ValueTask.FromResult(OperationResult<LeaseToken>.Accepted(lease));
        }
    }
    public bool Validate(BusRole role,LeaseToken lease)
    {
        lock(_sync) return _active.TryGetValue(role,out var active) && active==lease &&
            lease.SessionGeneration==_generation && lease.ExpiresUtc>DateTimeOffset.UtcNow;
    }
    public ValueTask<OperationResult<Unit>> ReleaseAsync(LeaseToken lease,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock(_sync)
        {
            var role=_active.FirstOrDefault(p=>p.Value==lease);
            if(role.Value!=lease) return ValueTask.FromResult(OperationResult.Rejected("STALE_LEASE","Lease not owned by this generation."));
            _active.Remove(role.Key);return ValueTask.FromResult(OperationResult.Accepted());
        }
    }
}
