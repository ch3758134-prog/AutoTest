using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTest.Specialists.Scan.Lab;

public sealed class LabMemoryLedger : ILabRunLedger
{
    private readonly object _sync = new();
    private readonly List<LabRunRecord> _runs = new();
    public Task AppendAsync(LabRunRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync) _runs.Add(record);
        return Task.CompletedTask;
    }
    public IReadOnlyList<LabRunRecord> Snapshot()
    {
        lock (_sync) return _runs.ToArray();
    }
}

public sealed class LabDeterministicMockRunner : ILabTestRunner
{
    public async Task<LabTestResult> RunAsync(LabRunRequest request, LabMockScenario scenario,
        CancellationToken cancellationToken)
    {
        await Task.Delay(scenario == LabMockScenario.SlowPass ? 3000 : 60, cancellationToken)
            .ConfigureAwait(false);
        var result = scenario switch
        {
            LabMockScenario.Pass or LabMockScenario.SlowPass => new LabTestResult(true, LabOrigin.Mock,
                "MOCK_STEP_VERDICT_PASS", new[] { "Mock StepFact: request", "Mock StepFact: observation", "Mock StepFact: verdict PASS" },
                true, false, false, false, ""),
            LabMockScenario.Fail => new LabTestResult(false, LabOrigin.Mock,
                "MOCK_STEP_VERDICT_FAIL", new[] { "Mock StepFact: verdict FAIL" }, true, false, false, false, ""),
            LabMockScenario.Disconnect => new LabTestResult(false, LabOrigin.Mock,
                "MOCK_DEVICE_DISCONNECTED", new[] { "Mock StepFact: disconnect" }, false, false, false, false, ""),
            LabMockScenario.PowerLoss => new LabTestResult(false, LabOrigin.Mock,
                "MOCK_POWER_LOSS", new[] { "Mock StepFact: power loss" }, false, false, false, false, ""),
            LabMockScenario.Abort => throw new OperationCanceledException("MOCK_OPERATOR_ABORT"),
            LabMockScenario.Exception => throw new InvalidOperationException("MOCK_RUNNER_EXCEPTION"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        return result;
    }
}

public sealed class LabProductionOrchestrator
{
    private readonly ILabTestRunner _runner;
    private readonly ILabRunLedger _ledger;
    private readonly ILabProductionPhysicalGate _physical;
    private int _busy;

    public LabProductionOrchestrator(ILabTestRunner runner, ILabRunLedger ledger,
        ILabProductionPhysicalGate? physical = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _physical = physical ?? new DenyPhysicalGate();
    }
    public bool IsBusy => Volatile.Read(ref _busy) != 0;
    public IReadOnlyList<LabRunRecord> History => _ledger.Snapshot();
    public int DistinctDemoUnits => History.Select(x => x.Request.UnitKey).Distinct(StringComparer.Ordinal).Count();
    public int FactoryReleasedUnits => History.Where(x => x.FactoryReleased).Select(x => x.Request.UnitKey)
        .Distinct(StringComparer.Ordinal).Count();

    public LabPreflight Preflight(LabCatalog catalog, LabMatch match,
        LabOrigin origin, bool allowRetest)
    {
        var blockers = new List<string>();
        if (IsBusy) blockers.Add("STATION_BUSY");
        if (origin == LabOrigin.Unknown) blockers.Add("SOURCE_UNKNOWN");
        if (origin == LabOrigin.Physical) blockers.Add("PHYSICAL_PRODUCTION_NOT_AUTHORIZED");
        if (!catalog.Published || LabCatalogValidator.Validate(catalog, true).Count > 0)
            blockers.Add("CATALOG_NOT_RELEASED");
        if (!match.Accepted || match.Identity is null || match.Recipe is null)
            blockers.Add("IDENTITY_OR_RECIPE_NOT_UNIQUE");
        if (!StringComparer.Ordinal.Equals(match.CatalogRevision, catalog.Revision))
            blockers.Add("MATCH_SNAPSHOT_STALE");
        if (match.Identity is not null && !allowRetest &&
            History.Any(x => StringComparer.Ordinal.Equals(x.Request.UnitKey, match.Identity.UnitKey)))
            blockers.Add("RETEST_CONFIRMATION_REQUIRED");
        if (match.Recipe is not null && origin != LabOrigin.Physical &&
            (match.Recipe.TestProgram.Source != LabOrigin.Mock ||
             match.Recipe.StationProfile.Source != LabOrigin.Mock))
            blockers.Add("MOCK_RESOURCES_REQUIRED");
        return new(blockers.Count == 0, blockers);
    }

    public async Task<LabRunRecord> RunAsync(LabCatalog catalog, LabMatch match,
        string rawScan, LabOrigin origin, LabMockScenario scenario, bool allowRetest,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("STATION_BUSY_SCAN_REJECTED");
        try
        {
            // Preflight after exclusive acquisition (not after a time-of-check race).
            var blockers = NewPreflight(catalog, match, origin, allowRetest);
            if (!StringComparer.Ordinal.Equals(rawScan.TrimEnd('\r', '\n'), match.RawScan))
                blockers = blockers.Concat(new[] { "SCAN_CHANGED_AFTER_MATCH" }).ToArray();
            var currentMatch = LabIdentityMatcher.Match(catalog, rawScan, catalog.Revision);
            if (!currentMatch.Accepted ||
                !StringComparer.Ordinal.Equals(currentMatch.Identity?.UnitKey, match.Identity?.UnitKey) ||
                !StringComparer.Ordinal.Equals(currentMatch.Recipe?.RecipeId, match.Recipe?.RecipeId) ||
                !StringComparer.Ordinal.Equals(currentMatch.Recipe?.Revision, match.Recipe?.Revision))
                blockers = blockers.Concat(new[] { "MATCH_OR_RECIPE_EXPIRED" }).ToArray();
            if (blockers.Count != 0)
                throw new InvalidOperationException("PRODUCTION_PREFLIGHT_BLOCKED:" + string.Join("|", blockers));
            var identity = match.Identity!;
            var recipe = match.Recipe!;
            var request = new LabRunRequest(Guid.NewGuid(), Guid.NewGuid(), rawScan,
                identity.UnitKey, identity.RuleId, catalog.Revision, recipe.RecipeId,
                recipe.Revision, origin, DateTimeOffset.UtcNow, Guid.NewGuid(), null, "UNKNOWN");
            LabRunRecord record;
            try
            {
                if (origin == LabOrigin.Physical)
                {
                    var auth = _physical.Check(request, recipe);
                    throw new InvalidOperationException("PHYSICAL_GATE_BLOCKED:" +
                        string.Join("|", auth.Blockers));
                }
                var result = await _runner.RunAsync(request, scenario, cancellationToken)
                    .ConfigureAwait(false);
                // Even a runner claiming Physical can't elevate a Mock/Replay request.
                var reported = result.Source == origin ? origin : LabOrigin.Unknown;
                var status = reported == LabOrigin.Unknown ? LabRunStatus.Rejected :
                    result.Passed && result.ConnectionHealthy ? LabRunStatus.MockPass : LabRunStatus.MockFail;
                record = new(request, status, reported,
                    reported == LabOrigin.Unknown ? "SOURCE_MISMATCH" : result.Summary,
                    DateTimeOffset.UtcNow, result.StepFacts ?? Array.Empty<string>(),
                    FactoryReleased: false, result.TraceReference);
            }
            catch (OperationCanceledException)
            {
                record = new(request, LabRunStatus.Interrupted, origin, "RUN_CANCELLED",
                    DateTimeOffset.UtcNow, new[] { "Cancellation/abort recorded" }, false, "");
            }
            catch (Exception ex)
            {
                record = new(request, LabRunStatus.Error, origin, "RUN_FAILURE:" + ex.GetType().Name,
                    DateTimeOffset.UtcNow, new[] { ex.Message }, false, "");
            }
            // A failed write means the caller must not advertise a completed durable history entry.
            await _ledger.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
            return record;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private IReadOnlyList<string> NewPreflight(LabCatalog catalog, LabMatch match,
        LabOrigin origin, bool allowRetest)
    {
        var blockers = new List<string>();
        if (origin == LabOrigin.Unknown) blockers.Add("SOURCE_UNKNOWN");
        if (origin == LabOrigin.Physical) blockers.Add("PHYSICAL_PRODUCTION_NOT_AUTHORIZED");
        if (!catalog.Published || LabCatalogValidator.Validate(catalog, true).Count != 0)
            blockers.Add("CATALOG_NOT_RELEASED");
        if (!match.Accepted || match.Identity is null || match.Recipe is null)
            blockers.Add("IDENTITY_OR_RECIPE_NOT_UNIQUE");
        if (!StringComparer.Ordinal.Equals(match.CatalogRevision, catalog.Revision))
            blockers.Add("MATCH_SNAPSHOT_STALE");
        if (match.Identity is not null && !allowRetest &&
            History.Any(x => StringComparer.Ordinal.Equals(x.Request.UnitKey, match.Identity.UnitKey)))
            blockers.Add("RETEST_CONFIRMATION_REQUIRED");
        if (match.Recipe is not null && origin != LabOrigin.Physical &&
            (match.Recipe.TestProgram.Source != LabOrigin.Mock ||
             match.Recipe.StationProfile.Source != LabOrigin.Mock))
            blockers.Add("MOCK_RESOURCES_REQUIRED");
        return blockers;
    }
}
