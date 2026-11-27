using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Application.Production;

public sealed record ProductionCommandFeedback(string Code, ProductionState State,
    string? DutIdentity, RunId? Run, Verdict Verdict, bool IsMock, bool CanRelease);

// Capturing a new scan while execution is active is rejected immediately, never queued.
public sealed class ProductionCommandHandler
{
    private readonly IIdentityMatcher _matcher;
    private readonly Func<MatchDecision, ProductionConfiguration?> _recipeResolver;
    private readonly ProductionSession _session;
    private readonly ProductionWorkflow _workflow;
    private readonly RuleRevision _ruleRevision;
    private readonly ProjectRevision _projectRevision;
    private readonly object _sync = new();
    // Both START sources claim the same frozen RunId; there is no second
    // production executor and no scan-to-start shortcut in the operator UI.
    private readonly ProductionStartGate _startGate = new();
    public ProductionStartSource? LastStartSource { get; private set; }
    private CancellationTokenSource? _runCancellation;
    private int _handling;
    public bool IsBusy => Volatile.Read(ref _handling) != 0 ||
        _session.State is ProductionState.Ready or ProductionState.Running or
            ProductionState.AwaitingCommit or ProductionState.Unknown or ProductionState.Aborted;
    public bool CanAdmitScan => !IsBusy;
    public ProductionSession Session => _session;

    public ProductionCommandHandler(IIdentityMatcher matcher,
        Func<MatchDecision, ProductionConfiguration?> recipeResolver,
        ProductionSession session, ProductionWorkflow workflow,
        RuleRevision ruleRevision, ProjectRevision projectRevision)
    {
        _matcher = matcher; _recipeResolver = recipeResolver;
        _session = session; _workflow = workflow;
        _ruleRevision = ruleRevision; _projectRevision = projectRevision;
    }

    public async Task<ProductionCommandFeedback> ScanAsync(RawScan scan,
        ProductionStationReadiness station, bool autoStart, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _handling, 1, 0) != 0)
            return Feedback("STATION_BUSY", Verdict.Inconclusive);
        try
        {
            if (_session.State is ProductionState.Ready or ProductionState.Running or
                ProductionState.AwaitingCommit)
                return Feedback("STATION_BUSY", Verdict.Inconclusive);
            var accepted = _session.StartDraft(scan);
            if (accepted.Outcome != Outcome.Accepted) return Feedback(accepted.FailureCode!, Verdict.Inconclusive);
            var match = _matcher.Match(scan, _ruleRevision, _projectRevision);
            var matched = _session.SetMatch(match);
            if (matched.Outcome != Outcome.Accepted) return Feedback(matched.FailureCode!, Verdict.Inconclusive);
            var recipe = _recipeResolver(match);
            if (recipe is null) return Feedback("RECIPE_NOT_RESOLVED", Verdict.Inconclusive);
            var preflight = _workflow.Prepare(_session, recipe, station);
            if (!preflight.Ready) return Feedback(string.Join(";", preflight.Blockers), Verdict.Inconclusive);
            var confirmed = _session.Confirm(recipe, _ruleRevision, _projectRevision,
                station.Station, station.IsMockEnvironment);
            if (confirmed.Outcome != Outcome.Accepted) return Feedback(confirmed.FailureCode!, Verdict.Inconclusive);
            _startGate.Arm(confirmed.Value!.Run);
            LastStartSource = null;
            if (!autoStart) return Feedback("READY_FOR_START", Verdict.Inconclusive);
            // Legacy auto-start is only for explicit simulation contract tests.
            // Never turn a REAL physical scan into a software START.
            if (!confirmed.Value!.IsMock)
                return Feedback("REAL_PRODUCTION_PHYSICAL_START_REQUIRED", Verdict.Inconclusive);
            return await StartPreparedInternalAsync(ProductionStartSource.Software, false, ct)
                .ConfigureAwait(false);
        }
        finally { Interlocked.Exchange(ref _handling, 0); }
    }

    public Task<ProductionCommandFeedback> StartAsync(CancellationToken ct) =>
        StartAsync(ProductionStartSource.Software, ct);

    public async Task<ProductionCommandFeedback> StartAsync(ProductionStartSource source,
        CancellationToken ct)
    {
        if (source != ProductionStartSource.Software)
            return Feedback("PHYSICAL_START_REQUIRES_VERIFIED_INPUT", Verdict.Inconclusive);
        // Software START exists for isolated domain simulation and the
        // separate engineering execution workflow, never as the formal
        // physical station's START substitute.
        if (_session.Job is { IsMock: false })
            return Feedback("REAL_PRODUCTION_PHYSICAL_START_REQUIRED", Verdict.Inconclusive);
        if (Interlocked.CompareExchange(ref _handling, 1, 0) != 0)
            return Feedback("STATION_BUSY", Verdict.Inconclusive);
        try
        {
            return await StartPreparedInternalAsync(ProductionStartSource.Software, false, ct)
                .ConfigureAwait(false);
        }
        finally { Interlocked.Exchange(ref _handling, 0); }
    }

    /// <summary>
    /// Begin the station-controlled production cycle, NOT a software START.
    /// The workflow itself persists fixture intent, verifies real mechanical
    /// closure, waits for a trusted physical START edge, and only then may
    /// invoke destructive ECU FLASH. With no actual station adapter it will
    /// return PHYSICAL_STATION_ADAPTER_NOT_CONNECTED without side effects.
    /// </summary>
    public async Task<ProductionCommandFeedback> BeginStationCycleAsync(
        CancellationToken ct)
    {
        if (_session.Job is not { IsMock: false } ||
            _session.State != ProductionState.Ready)
            return Feedback("REAL_STATION_NOT_READY", Verdict.Inconclusive);
        if (Interlocked.CompareExchange(ref _handling, 1, 0) != 0)
            return Feedback("STATION_BUSY", Verdict.Inconclusive);
        try
        {
            // Never call ProductionStartGate.RequestSoftware for a real DUT.
            // The real verified START must come from IProductionPhysicalStation
            // inside the production workflow after fixture closure.
            return await ExecuteStationCycleInternalAsync(ct).ConfigureAwait(false);
        }
        finally { Interlocked.Exchange(ref _handling, 0); }
    }

    private async Task<ProductionCommandFeedback> ExecuteStationCycleInternalAsync(
        CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_sync) _runCancellation = linked;
        try
        {
            var result = await _workflow.ExecuteAsync(_session, linked.Token)
                .ConfigureAwait(false);
            // Do not claim a verified physical START from a terminal
            // Unknown/Failed state. This field remains unset until an
            // explicit trusted start receipt can be surfaced.
            return Feedback(result.Code, result.Verdict);
        }
        finally { lock (_sync) _runCancellation = null; }
    }

    /// <summary>
    /// The eventual verified hardware adapter reports a debounced active
    /// level here. Releasing after Ready must be observed before a new press
    /// edge can claim this run. This is not connected to an MCU input yet.
    /// </summary>
    public async Task<ProductionCommandFeedback> PhysicalStartSignalAsync(bool pressed,
        CancellationToken ct)
    {
        // No authenticated USB START-frame source exists in the firmware
        // contract. A public bool could be forged by UI/test code. Until the
        // physical adapter supplies a trusted, epoch-bound event, refuse this
        // legacy entry point for all REAL stations.
        if (_session.Job is { IsMock: false })
            return Feedback("PHYSICAL_START_SOURCE_NOT_CONNECTED", Verdict.Inconclusive);
        if (_session.Job is not { } job || _session.State != ProductionState.Ready)
            return Feedback("RUN_NOT_READY", Verdict.Inconclusive);
        if (!pressed)
        {
            var release = _startGate.ObservePhysical(job.Run, false);
            return Feedback(release.Code, Verdict.Inconclusive);
        }
        if (Interlocked.CompareExchange(ref _handling, 1, 0) != 0)
            return Feedback("STATION_BUSY", Verdict.Inconclusive);
        try
        {
            var start = _startGate.ObservePhysical(job.Run, true);
            if (!start.Accepted) return Feedback(start.Code, Verdict.Inconclusive);
            return await StartPreparedInternalAsync(
                ProductionStartSource.PhysicalButton, true, ct).ConfigureAwait(false);
        }
        finally { Interlocked.Exchange(ref _handling, 0); }
    }

    private async Task<ProductionCommandFeedback> StartPreparedInternalAsync(
        ProductionStartSource source, bool claimedByPhysicalEdge, CancellationToken ct)
    {
        if (_session.State != ProductionState.Ready || _session.Job is not { } job)
            return Feedback("RUN_NOT_READY", Verdict.Inconclusive);
        if (!claimedByPhysicalEdge)
        {
            var claim = _startGate.RequestSoftware(job.Run);
            if (!claim.Accepted) return Feedback(claim.Code, Verdict.Inconclusive);
        }
        LastStartSource = source;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_sync) _runCancellation = linked;
        try
        {
            var result = await _workflow.ExecuteAsync(_session, linked.Token).ConfigureAwait(false);
            return Feedback(result.Code, result.Verdict);
        }
        finally { lock (_sync) _runCancellation = null; }
    }

    public async Task<ProductionCommandFeedback> StopAsync(CancellationToken ct)
    {
        CancellationTokenSource? cancellation;
        RunId? run;
        lock (_sync)
        {
            cancellation = _runCancellation; run = _session.Job?.Run;
            cancellation?.Cancel();
        }
        if (cancellation is null || run is null) return Feedback("NO_ACTIVE_RUN", Verdict.Inconclusive);
        try { await _workflow.RequestStopAsync(run.Value, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        return Feedback("STOP_REQUESTED", Verdict.Inconclusive);
    }

    public OperationResult<Unit> RetryWithNewRun()
    {
        if (IsBusy) return OperationResult.Rejected("STATION_BUSY", "Cannot retry an unfinished run.");
        var reset = _session.Reset();
        if (reset.Outcome == Outcome.Accepted)
        {
            _startGate.Disarm();
            LastStartSource = null;
        }
        return reset; // A new scan/inspection must create a distinct RunId.
    }

    private ProductionCommandFeedback Feedback(string code, Verdict verdict)
    {
        var job = _session.Job;
        return new(code, _session.State, job?.DutIdentity ?? _session.Match?.DutIdentity,
            job?.Run, verdict, job?.IsMock ?? false, false);
    }
}
