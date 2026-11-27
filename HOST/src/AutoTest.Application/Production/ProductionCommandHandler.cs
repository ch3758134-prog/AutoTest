using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Application.Production;

public sealed record ProductionCommandFeedback(
    string Code, ProductionState State, string? FullBarcode,
    RunId? Run, Verdict Verdict, bool CanRelease);

/// <summary>
/// One production command boundary. A scan selects one immutable project
/// version but NEVER presses a fixture, starts FLASH/TEST, or records PASS.
/// BeginStationCycleAsync is not software START: the real station process
/// must first prove clamp position and observe a fresh physical START edge.
/// </summary>
public sealed class ProductionCommandHandler
{
    private readonly IIdentityMatcher _matcher;
    private readonly Func<MatchDecision, ProductionProjectExecution?> _projectResolver;
    private readonly ProductionSession _session;
    private readonly ProductionWorkflow _workflow;
    private readonly RuleRevision _ruleRevision;
    private readonly ProjectRevision _projectRevision;
    private readonly object _sync = new();
    private CancellationTokenSource? _runCancellation;
    private int _handling;

    public bool IsBusy => Volatile.Read(ref _handling) != 0 ||
        _session.State is ProductionState.Ready or ProductionState.Running or
            ProductionState.AwaitingCommit or ProductionState.Unknown or
            ProductionState.Aborted;

    public bool CanAdmitScan => !IsBusy;
    public ProductionSession Session => _session;

    public ProductionCommandHandler(IIdentityMatcher matcher,
        Func<MatchDecision, ProductionProjectExecution?> projectResolver,
        ProductionSession session, ProductionWorkflow workflow,
        RuleRevision ruleRevision, ProjectRevision projectRevision)
    {
        _matcher = matcher;
        _projectResolver = projectResolver;
        _session = session;
        _workflow = workflow;
        _ruleRevision = ruleRevision;
        _projectRevision = projectRevision;
    }

    public Task<ProductionCommandFeedback> ScanAsync(
        RawScan scan, ProductionStationReadiness station, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _handling, 1, 0) != 0)
            return Task.FromResult(Feedback("STATION_BUSY", Verdict.Inconclusive));
        try
        {
            ct.ThrowIfCancellationRequested();
            if (_session.State is ProductionState.Ready or ProductionState.Running or
                ProductionState.AwaitingCommit or ProductionState.Unknown or
                ProductionState.Aborted)
                return Task.FromResult(Feedback("STATION_BUSY", Verdict.Inconclusive));
            if (!FixedMarkerBarcode.TryParse(scan.Text, out _, out _))
                return Task.FromResult(Feedback("BARCODE_INVALID", Verdict.Inconclusive));
            var draft = _session.StartDraft(scan);
            if (draft.Outcome != Outcome.Accepted)
                return Task.FromResult(Feedback(draft.FailureCode!, Verdict.Inconclusive));
            var match = _matcher.Match(scan, _ruleRevision, _projectRevision);
            var matched = _session.SetMatch(match);
            if (matched.Outcome != Outcome.Accepted)
                return Task.FromResult(Feedback(matched.FailureCode!, Verdict.Inconclusive));
            var execution = _projectResolver(match);
            if (execution is null)
                return Task.FromResult(Feedback("PROJECT_EXECUTION_NOT_RESOLVED",
                    Verdict.Inconclusive));
            var preflight = _workflow.Prepare(_session, execution, station);
            if (!preflight.Ready)
                return Task.FromResult(Feedback(string.Join(";", preflight.Blockers),
                    Verdict.Inconclusive));
            var ready = _session.Confirm(execution, _ruleRevision, _projectRevision,
                station.Station);
            if (ready.Outcome != Outcome.Accepted)
                return Task.FromResult(Feedback(ready.FailureCode!, Verdict.Inconclusive));
            // No automatic start or executable stage is invoked by the scan.
            return Task.FromResult(Feedback("READY_FOR_STATION", Verdict.Inconclusive));
        }
        finally { Interlocked.Exchange(ref _handling, 0); }
    }

    /// <summary>
    /// Begin the physical cycle (fixture press, proof, trusted START, FLASH,
    /// TEST, verified RECORD). Does not accept or simulate a software START.
    /// Without the actual physical station adapter, no stage can be reached.
    /// </summary>
    public async Task<ProductionCommandFeedback> BeginStationCycleAsync(
        CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _handling, 1, 0) != 0)
            return Feedback("STATION_BUSY", Verdict.Inconclusive);
        try
        {
            if (_session.Job is null || _session.State != ProductionState.Ready)
                return Feedback("REAL_STATION_NOT_READY", Verdict.Inconclusive);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lock (_sync) _runCancellation = linked;
            try
            {
                var result = await _workflow.ExecuteAsync(_session, linked.Token)
                    .ConfigureAwait(false);
                return Feedback(result.Code, result.Verdict);
            }
            finally { lock (_sync) _runCancellation = null; }
        }
        finally { Interlocked.Exchange(ref _handling, 0); }
    }

    public async Task<ProductionCommandFeedback> StopAsync(CancellationToken ct)
    {
        CancellationTokenSource? cancellation;
        RunId? run;
        lock (_sync)
        {
            cancellation = _runCancellation;
            run = _session.Job?.Run;
            cancellation?.Cancel();
        }
        if (cancellation is null || run is null)
            return Feedback("NO_ACTIVE_RUN", Verdict.Inconclusive);
        try { await _workflow.RequestStopAsync(run.Value, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        return Feedback("STOP_REQUESTED", Verdict.Inconclusive);
    }

    /// <summary>
    /// Clear an unstarted or durably completed unit. UNKNOWN/ABORTED physical
    /// side effects need independent station recovery before a new ECU.
    /// </summary>
    public OperationResult<Unit> ClearForNextScan()
    {
        if (Volatile.Read(ref _handling) != 0 ||
            _session.State is ProductionState.Running or ProductionState.AwaitingCommit
                or ProductionState.Unknown or ProductionState.Aborted)
            return OperationResult.Rejected("RECOVERY_REQUIRED",
                "Physical work or uncertain side effects require reconciliation.");
        return _session.Reset();
    }

    private ProductionCommandFeedback Feedback(string code, Verdict verdict)
    {
        var job = _session.Job;
        return new(code, _session.State,
            job?.DutIdentity ?? _session.Match?.DutIdentity,
            job?.Run, verdict, false);
    }
}
