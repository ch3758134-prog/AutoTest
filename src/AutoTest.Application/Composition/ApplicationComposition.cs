using System.Collections.Immutable;
using AutoTest.Application.Commands;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Application.Composition;

public sealed record MockComposition(IApplicationCommandGateway Gateway,
    MockStation Station, InMemoryMockFactSink Facts);

// This is the ONLY production composition root in V0. No USB/CAN/flash dependencies exist.
public static class ApplicationComposition
{
    public static MockComposition RegisterMock(TimeProvider? clock = null)
    {
        var station = new MockStation(clock);
        var facts = new InMemoryMockFactSink();
        var router = new ApplicationCommandRouter(station, station, station, facts, facts,
            clock ?? TimeProvider.System);
        return new MockComposition(router, station, facts);
    }

    // Core is the already shared contract/value model; no hidden second container.
    public static void RegisterCore() { /* no stateful core services in V0 */ }
    public static void RegisterReal() => throw new NotSupportedException(
        "REAL_DEVICE_ADAPTER_NOT_AUTHORIZED_IN_WPB_G4_INIT");
}

public sealed class MockStation : IStationSession, IStationControl, IMockObservationSource
{
    private readonly object _sync = new();
    private readonly TimeProvider _clock;
    private readonly HashSet<Guid> _acknowledged = new();
    private long _generation;
    private StationId _station = StationId.Create("V0-SIM-STATION");
    private StationConnectionState _state = StationConnectionState.Disconnected;

    public MockStation(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;
    public long CurrentGeneration { get { lock (_sync) return _generation; } }
    public StationConnectionState State { get { lock (_sync) return _state; } }

    public ValueTask<OperationResult<SessionInfo>> ConnectAsync(StationId station,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(station.Value))
            return ValueTask.FromResult(OperationResult<SessionInfo>.Rejected(
                "STATION_ID_INVALID", "Station ID must be valid."));
        lock (_sync)
        {
            _station = station;
            _generation = checked(_generation + 1);
            _acknowledged.Clear();
            _state = StationConnectionState.Ready;
            var session = new SessionInfo(station, _generation, "SIMULATION_ONLY",
                "V0-NO-PHYSICAL-DEVICE", "NO_FIRMWARE", _state);
            return ValueTask.FromResult(OperationResult<SessionInfo>.Accepted(session));
        }
    }

    public ValueTask<OperationResult<Unit>> DisconnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _generation = checked(_generation + 1);
            _acknowledged.Clear();
            _state = StationConnectionState.Disconnected;
        }
        return ValueTask.FromResult(OperationResult.Accepted());
    }

    public async ValueTask<OperationResult<CommandReceipt>> ExecuteAsync(
        StationCommand command, Authorization authorization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        long entryGeneration;
        lock (_sync)
        {
            entryGeneration = _generation;
            if (command.Action != StationAction.MockProbe)
                return OperationResult<CommandReceipt>.Rejected("PHYSICAL_OUTPUT_FORBIDDEN",
                    "No physical actions or firmware programming exist in the V0 mock.", command.CorrelationId);
            if (authorization.Scope != AuthorizationScope.MockOnly ||
                authorization.Issuer != "V0_MOCK_ISSUER" ||
                authorization.Station != _station ||
                authorization.Generation != _generation ||
                authorization.ExpiresUtc <= _clock.GetUtcNow())
                return OperationResult<CommandReceipt>.Rejected("AUTH_MISSING",
                    "A current simulation-scoped authorization is required.", command.CorrelationId);
            if (_state != StationConnectionState.Ready)
                return OperationResult<CommandReceipt>.Rejected("SESSION_NOT_READY",
                    "No ready simulation session.", command.CorrelationId);
        }
        await Task.Delay(command.Scenario == MockScenario.DelayedResponse ? 140 : 25, ct)
            .ConfigureAwait(false);
        lock (_sync)
        {
            if (entryGeneration != _generation || _state != StationConnectionState.Ready)
                return OperationResult<CommandReceipt>.Rejected("STALE_SESSION",
                    "Response belongs to a superseded connection generation.", command.CorrelationId);
            if (command.Scenario == MockScenario.Timeout)
                return OperationResult<CommandReceipt>.Uncertain("MOCK_TIMEOUT",
                    "Injected timeout; ACK unknown, no observation.", command.CorrelationId, false);
            if (command.Scenario == MockScenario.Disconnect)
            {
                _generation = checked(_generation + 1);
                _state = StationConnectionState.Disconnected;
                _acknowledged.Clear();
                return OperationResult<CommandReceipt>.Uncertain("MOCK_DISCONNECTED",
                    "Injected disconnect; response cannot be trusted.", command.CorrelationId, false);
            }
            _acknowledged.Add(command.CorrelationId);
            var receipt = new CommandReceipt(command.CorrelationId,
                AckKind.SimulatorAcknowledged, PhysicalState.Unknown, entryGeneration,
                TimePoint.FromSimulation(_clock.GetUtcNow(), _clock.GetTimestamp()),
                EvidenceRef.New());
            return OperationResult<CommandReceipt>.Accepted(receipt,
                command.CorrelationId, ImmutableArray.Create(receipt.AckEvidence));
        }
    }

    public ValueTask<OperationResult<MockObservation>> ObserveMockAsync(
        Guid operationId, long sessionGeneration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (sessionGeneration != _generation || _state != StationConnectionState.Ready ||
                !_acknowledged.Remove(operationId))
                return ValueTask.FromResult(OperationResult<MockObservation>.Rejected(
                    "NO_CURRENT_MOCK_EVIDENCE", "No current independent simulator observation.", operationId));
            var observation = new MockObservation(operationId, "SIMULATED_INDICATOR_READY",
                _generation, TimePoint.FromSimulation(_clock.GetUtcNow(), _clock.GetTimestamp()),
                EvidenceRef.New(), EvidenceQuality.Derived);
            return ValueTask.FromResult(OperationResult<MockObservation>.Accepted(observation,
                operationId, ImmutableArray.Create(observation.Evidence)));
        }
    }
}

// In-memory append-only observation log. It is deliberately NEVER durable/committed.
public sealed class InMemoryMockFactSink : IRunFactSink, IRecordReader
{
    private readonly object _sync = new();
    private ImmutableArray<RunFact> _facts = ImmutableArray<RunFact>.Empty;
    public ValueTask<RecordWriteReceipt> AppendAsync(RunFact fact, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_facts.Any(existing => existing.EventId == fact.EventId))
                throw new InvalidOperationException("Duplicate fact event is not appendable.");
            _facts = _facts.Add(fact);
        }
        return ValueTask.FromResult(new RecordWriteReceipt(fact.Run, fact.EventId,
            false, "MOCK_MEMORY_ONLY_NOT_COMMITTED"));
    }
    public ValueTask<RecordCommitReceipt> CommitAsync(RunId run, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new RecordCommitReceipt(run,
            false, "NOT_IMPLEMENTED_DURABLE_COMMIT"));
    }
    public ValueTask<ImmutableArray<RunFact>> QueryAsync(RecordQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return ValueTask.FromResult(_facts.Where(f => f.Run == query.Run)
                .Take(Math.Max(0, query.MaxCount)).ToImmutableArray());
        }
    }
}
