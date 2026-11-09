using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Application.Commands;

// The application owns orchestration and exposes a read-only projection. The UI never sees
// IStationSession, IStationControl or a device transport.
public sealed class ApplicationCommandRouter : IApplicationCommandGateway
{
    private static readonly StationId MockId = StationId.Create("V0-SIM-STATION");
    private readonly IStationSession _session;
    private readonly IStationControl _control;
    private readonly IMockObservationSource _observation;
    private readonly IRunFactSink _facts;
    private readonly IRecordReader _reader;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _execution = new(1, 1);
    private readonly object _projectionSync = new();
    private WorkbenchProjection _projection;

    public ApplicationCommandRouter(IStationSession session, IStationControl control,
        IMockObservationSource observation, IRunFactSink facts, IRecordReader reader,
        TimeProvider clock)
    {
        _session = session;
        _control = control;
        _observation = observation;
        _facts = facts;
        _reader = reader;
        _clock = clock;
        _projection = new WorkbenchProjection("V0 / SIMULATION ONLY", false,
            "NOT_STARTED", "NO_ACK", "NO_OBSERVATION", Verdict.Inconclusive, "",
            new RunProjection(null, RunState.NotStarted, null, Verdict.Inconclusive),
            new StationProjection(MockId, StationConnectionState.Disconnected, 0, true,
                PhysicalState.Unknown), new UiSelection(null, null, null),
            ImmutableArray<RunFact>.Empty);
    }

    public WorkbenchProjection Observe() { lock (_projectionSync) return _projection; }

    public string? GetCanExecuteReason(ApplicationCommand command) => command.Kind switch
    {
        ApplicationCommandKind.MockNormal or ApplicationCommandKind.MockTimeout or
        ApplicationCommandKind.MockDisconnect or ApplicationCommandKind.MockUnauthorized => null,
        _ => "V0_SCOPE_DENIED: physical outputs and real programming are forbidden"
    };

    public async ValueTask<ApplicationCommandResult> ExecuteAsync(ApplicationCommand command,
        CancellationToken ct)
    {
        var reason = GetCanExecuteReason(command);
        if (reason is not null)
            return await BuildFailure(RunId.New(), command.CorrelationId,
                "V0_SCOPE_DENIED", Outcome.Rejected, "NO_ACK", "NO_OBSERVATION",
                0, StationConnectionState.Disconnected, ct).ConfigureAwait(false);
        await _execution.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ExecuteMockAsync(command, ct).ConfigureAwait(false);
        }
        finally { _execution.Release(); }
    }

    private async ValueTask<ApplicationCommandResult> ExecuteMockAsync(
        ApplicationCommand appCommand, CancellationToken ct)
    {
        var run = RunId.New();
        var operationId = appCommand.CorrelationId;
        await AddFact(run, operationId, RunFactKind.Request, "MOCK_ONLY request accepted for evaluation",
            EvidenceQuality.Unknown, ImmutableArray<EvidenceRef>.Empty, ct).ConfigureAwait(false);
        var connected = await _session.ConnectAsync(MockId, ct).ConfigureAwait(false);
        if (connected.Outcome != Outcome.Accepted || connected.Value is null)
            return await BuildFailure(run, operationId, "CONNECT_REJECTED", Outcome.Rejected,
                "NO_ACK", "NO_OBSERVATION", 0, StationConnectionState.Disconnected, ct)
                .ConfigureAwait(false);
        var session = connected.Value;
        var scenario = appCommand.Kind switch
        {
            ApplicationCommandKind.MockTimeout => MockScenario.Timeout,
            ApplicationCommandKind.MockDisconnect => MockScenario.Disconnect,
            _ => MockScenario.Normal
        };
        var auth = appCommand.Kind == ApplicationCommandKind.MockUnauthorized
            ? new Authorization(AuthorizationScope.None, MockId, session.Generation,
                _clock.GetUtcNow().AddSeconds(60), "NO_ISSUER")
            : Authorization.MockOnly(session, _clock.GetUtcNow());
        var receipt = await _control.ExecuteAsync(
            new StationCommand(StationAction.MockProbe, scenario, operationId), auth, ct)
            .ConfigureAwait(false);
        if (receipt.Outcome != Outcome.Accepted || receipt.Value is null)
        {
            var code = receipt.FailureCode ?? "NO_ACK";
            var observedState = code == "MOCK_DISCONNECTED"
                ? StationConnectionState.Disconnected : session.State;
            return await BuildFailure(run, operationId, code, receipt.Outcome, "NO_ACK",
                "NO_OBSERVATION", session.Generation, observedState, ct).ConfigureAwait(false);
        }
        // Simulator ACK is not a physical observation. Evidence is appended separately.
        var ack = receipt.Value;
        await AddFact(run, operationId, RunFactKind.CommandAck,
            "SIMULATOR_ACK; PHYSICAL_STATE_UNKNOWN", EvidenceQuality.AcknowledgedOnly,
            ImmutableArray.Create(ack.AckEvidence), ct).ConfigureAwait(false);
        var observation = await _observation.ObserveMockAsync(operationId,
            session.Generation, ct).ConfigureAwait(false);
        if (observation.Outcome != Outcome.Accepted || observation.Value is null)
            return await BuildFailure(run, operationId, observation.FailureCode ?? "NO_OBSERVATION",
                Outcome.Uncertain, "SIMULATOR_ACK", "NO_OBSERVATION",
                session.Generation, session.State, ct).ConfigureAwait(false);
        var observed = observation.Value;
        await AddFact(run, operationId, RunFactKind.MockObservation,
            observed.Indicator + " [SIMULATION_NOT_HARDWARE]", observed.Quality,
            ImmutableArray.Create(observed.Evidence), ct).ConfigureAwait(false);
        await AddFact(run, operationId, RunFactKind.Judgement,
            "INCONCLUSIVE: V0 mock cannot prove physical state", EvidenceQuality.Unknown,
            ImmutableArray<EvidenceRef>.Empty, ct).ConfigureAwait(false);
        return await Publish(run, Outcome.Accepted, "ACCEPTED_MOCK",
            "SIMULATOR_ACK_ONLY", "SIMULATED_INDICATOR_READY", "", session.Generation,
            session.State, ct).ConfigureAwait(false);
    }

    private async ValueTask<ApplicationCommandResult> BuildFailure(RunId run,
        Guid operationId, string failureCode, Outcome outcome, string ack,
        string observation, long generation, StationConnectionState state, CancellationToken ct)
    {
        await AddFact(run, operationId,
            outcome == Outcome.Rejected ? RunFactKind.Rejection : RunFactKind.Uncertainty,
            failureCode, EvidenceQuality.Unknown, ImmutableArray<EvidenceRef>.Empty, ct)
            .ConfigureAwait(false);
        return await Publish(run, outcome, outcome.ToString().ToUpperInvariant(),
            ack, observation, failureCode, generation, state, ct).ConfigureAwait(false);
    }

    private async ValueTask AddFact(RunId run, Guid operationId, RunFactKind kind,
        string description, EvidenceQuality quality, ImmutableArray<EvidenceRef> refs,
        CancellationToken ct)
    {
        await _facts.AppendAsync(new RunFact(run, Guid.NewGuid(), operationId,
            kind, description,
            TimePoint.FromSimulation(_clock.GetUtcNow(), _clock.GetTimestamp()),
            quality, refs), ct).ConfigureAwait(false);
    }

    private async ValueTask<ApplicationCommandResult> Publish(RunId run, Outcome outcome,
        string status, string ack, string observation, string reason, long generation,
        StationConnectionState stationState, CancellationToken ct)
    {
        var facts = await _reader.QueryAsync(new RecordQuery(run, 100), ct)
            .ConfigureAwait(false);
        var projection = new WorkbenchProjection("V0 / SIMULATION ONLY", false,
            status, ack, observation, Verdict.Inconclusive, reason,
            new RunProjection(run, outcome == Outcome.Accepted ? RunState.Completed : RunState.Unknown,
                null, Verdict.Inconclusive),
            new StationProjection(MockId, stationState, generation, true, PhysicalState.Unknown),
            new UiSelection(null, null, null), facts);
        lock (_projectionSync) _projection = projection;
        return new ApplicationCommandResult(outcome, projection,
            string.IsNullOrEmpty(reason) ? null : reason);
    }
}
