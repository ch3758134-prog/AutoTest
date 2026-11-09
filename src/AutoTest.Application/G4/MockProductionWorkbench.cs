using System.Collections.Immutable;
using System.Text;
using AutoTest.Application.Production;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Scan;

namespace AutoTest.Application.G4;

public sealed record ScanMockResult(string Code, string? Identity, RunId? Run,
    RecordedMockRun? Record, bool Durable, bool PhysicalReleaseAllowed);

/// <summary>
/// Explicit manual-simulation scanner pipeline: scanner ingress → published identity rule →
/// unique recipe → station preflight → production session → actual TEST run → SQLite record.
/// Never enables keyboard/serial hardware capture or physical product release.
/// </summary>
public sealed class MockProductionWorkbench
{
    private const string ScanPattern = @"^(?<model>[A-Z]{3})-(?<serial>[0-9]{5})$";
    private readonly BarcodeInputRouter _router = new();
    private readonly InputPolicy _policy = new(true, 256, "manual-simulation");
    private readonly SemaphoreSlim _admission = new(1, 1);
    private readonly V1Workspace _workspace;
    private readonly MockRecordCoordinator _records;
    private readonly IdentityRuleSet _rules;
    public MockProductionWorkbench(V1Workspace workspace, MockRecordCoordinator records,
        IdentityRuleSet? rules = null)
    {
        _workspace = workspace; _records = records;
        _rules = rules ?? new IdentityRuleSet(new[] {
            new IdentityRule("MOCK_EPB_SN", ScanPattern,
                ImmutableArray.Create("model", "serial"), RulePublication.Published)
        }, RevisionFingerprint.FromBytes(Encoding.UTF8.GetBytes("G4-MOCK-IDENTITY-V1:" + ScanPattern)), true);
        _router.AttachMode(ScannerCaptureMode.ManualSimulation, _policy);
        // Semaphore serializes admission before publishing a scanner event.
    }

    public async Task<ScanMockResult> SimulateScanAsync(string text, CancellationToken ct = default)
    {
        if (!await _admission.WaitAsync(0, ct).ConfigureAwait(false))
            return new("STATION_BUSY", null, null, null, false, false);
        try
        {
            var raw = new RawScan(text, _policy.InputOrigin,
                TimePoint.FromSimulation(DateTimeOffset.UtcNow, 1), true);
            var ingress = _router.PublishRaw(raw, ScannerCaptureMode.ManualSimulation);
            if (!ingress.Accepted) return new(ingress.Code, null, null, null, false, false);
            // Consume the exact accepted scanner event; never parse UI text a second time.
            await using var reader = _router.ObserveAsync(_policy, ct).GetAsyncEnumerator(ct);
            if (!await reader.MoveNextAsync().ConfigureAwait(false))
                return new("SCAN_INGRESS_LOST", null, null, null, false, false);
            var accepted = reader.Current.Scan;
            var project = _workspace.Project;
            var recipeAssets = project.Assets.Select(x => x.ToReference()).ToImmutableArray();
            var recipes = recipeAssets.IsDefaultOrEmpty
                ? Array.Empty<ProductionRecipe>()
                : new[] { new ProductionRecipe("G4-MOCK-TEST", 1, "EPB", null,
                    recipeAssets, false, true) };
            var matcher = new ConfigMatchService(_rules, new ProjectRevision(project.Revision), recipes);
            var ruleRevision = new RuleRevision(_rules.Fingerprint);
            var projectRevision = new ProjectRevision(project.Revision);
            var match = matcher.Evaluate(accepted, ruleRevision, projectRevision);
            if (!match.Decision.Allowed)
                return new(match.Decision.RejectionCode ?? "MATCH_BLOCKED", null,
                    null, null, false, false);
            if (_workspace.Diagnostics().Count > 0)
                return new("TEST_PREPARE_BLOCKED", match.Decision.DutIdentity, null, null, false, false);
            var session = new ProductionSession();
            if (session.StartDraft(accepted).Outcome != Outcome.Accepted ||
                session.SetMatch(match.Decision).Outcome != Outcome.Accepted)
                return new("PRODUCTION_DRAFT_INVALID", null, null, null, false, false);
            var config = new ProductionConfiguration(match.RecipeId!, match.RecipeRevision!.Value,
                false, "SIMULATED_NO_SUPPLY", recipeAssets);
            var readiness = new ProductionStationReadiness(StationId.Create("G4-MOCK-STATION"),
                StationConnectionState.Ready, "SIMULATED_NO_SUPPLY", true, true, AuthorizationScope.MockOnly);
            var preflight = new ProductionPreflight().Validate(session, config, readiness);
            if (!preflight.Ready)
                return new(string.Join(";", preflight.Blockers), match.Decision.DutIdentity,
                    null, null, false, false);
            var confirm = session.Confirm(config, ruleRevision, projectRevision, readiness.Station, true);
            if (confirm.Outcome != Outcome.Accepted || confirm.Value is null)
                return new("PRODUCTION_CONFIRM_DENIED", match.Decision.DutIdentity, null, null, false, false);
            var job = confirm.Value;
            session.BeginExecution();
            try
            {
                var result = await _records.ExecuteAsync(job.DutIdentity,
                    _rules.Fingerprint.Sha256, job.Run, ct).ConfigureAwait(false);
                session.Mark(result.TestReport.Stopped ? ProductionState.Aborted : ProductionState.Completed);
                return new("MOCK_RECORDED_NOT_PRODUCTION_PASS", job.DutIdentity,
                    job.Run, result, true, false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                session.Mark(ProductionState.Aborted, "STOPPED");
                return new("MOCK_CANCELLED", job.DutIdentity, job.Run, null, false, false);
            }
            catch (Exception)
            {
                session.Mark(ProductionState.Unknown, "EXECUTION_OR_SQLITE_UNCERTAIN");
                return new("EXECUTION_OR_SQLITE_UNCERTAIN", job.DutIdentity, job.Run,
                    null, false, false);
            }
        }
        finally { _admission.Release(); }
    }
}
