using AutoTest.Application.Composition;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.TestFixtures;

// Fixture intentionally delegates to production mock so tests exercise the exact V0 semantics.
public sealed class FakeStation : IStationSession, IStationControl, IMockObservationSource
{
    private readonly MockStation _inner;
    public FakeStation(TimeProvider? clock = null) => _inner = new MockStation(clock);
    public long CurrentGeneration => _inner.CurrentGeneration;
    public StationConnectionState State => _inner.State;
    public ValueTask<OperationResult<SessionInfo>> ConnectAsync(StationId station, CancellationToken ct) =>
        _inner.ConnectAsync(station, ct);
    public ValueTask<OperationResult<Unit>> DisconnectAsync(CancellationToken ct) =>
        _inner.DisconnectAsync(ct);
    public ValueTask<OperationResult<CommandReceipt>> ExecuteAsync(StationCommand command,
        Authorization authorization, CancellationToken ct) =>
        _inner.ExecuteAsync(command, authorization, ct);
    public ValueTask<OperationResult<MockObservation>> ObserveMockAsync(Guid id, long generation,
        CancellationToken ct) => _inner.ObserveMockAsync(id, generation, ct);
}
