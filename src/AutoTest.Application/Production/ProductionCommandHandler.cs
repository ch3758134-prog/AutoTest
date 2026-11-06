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
            if (!autoStart) return Feedback("READY_FOR_START", Verdict.Inconclusive);
            return await StartPreparedInternalAsync(ct).ConfigureAwait(false);
        }
        finally { Interlocked.Exchange(ref _handling, 0); }
    }

    public async Task<ProductionCommandFeedback> StartAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _handling, 1, 0) != 0)
            return Feedback("STATION_BUSY", Verdict.Inconclusive);
        try { return await StartPreparedInternalAsync(ct).ConfigureAwait(false); }
        finally { Interlocked.Exchange(ref _handling, 0); }
    }

    private async Task<ProductionCommandFeedback> StartPreparedInternalAsync(CancellationToken ct)
    {
        if (_session.State != ProductionState.Ready) return Feedback("RUN_NOT_READY", Verdict.Inconclusive);
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
        return _session.Reset(); // Next Scan creates a distinct RunId; no record is overwritten.
    }

    private ProductionCommandFeedback Feedback(string code, Verdict verdict)
    {
        var job = _session.Job;
        return new(code, _session.State, job?.DutIdentity ?? _session.Match?.DutIdentity,
            job?.Run, verdict, job?.IsMock ?? false, false);
    }
}
