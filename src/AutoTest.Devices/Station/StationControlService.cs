using AutoTest.Contracts;
using AutoTest.Core;
namespace AutoTest.Devices.Station;
// V1 safety boundary: no real output driver, even when caller presents a nominal authorization.
public sealed class StationControlService
{
    public ValueTask<OperationResult<CommandReceipt>> SetIgnAsync(bool on,Authorization authorization,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResult<CommandReceipt>.Rejected("BENCH_NOT_AUTHORIZED","IGN electrical output is disabled in V1."));
    }
    public ValueTask<OperationResult<CommandReceipt>> OperateFixtureAsync(bool requestedEnergized,Authorization authorization,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResult<CommandReceipt>.Rejected("BENCH_NOT_AUTHORIZED","Fixture electrical output is disabled in V1; mechanical off-state is unspecified."));
    }
}
