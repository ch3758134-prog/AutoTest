using System.Collections.Immutable;
using System.Text.Json;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Model;
using AutoTest.Records.Queries;
using AutoTest.Records.Storage;
using AutoTest.Testing.Runtime;

namespace AutoTest.Application.G4;

/// <summary>
/// Bridges the actual TEST RunCoordinator report into append-only SQLite. Every step has its own
/// expected/actual/quality fact. A simulated PASS is persisted as Inconclusive, never production PASS.
/// This service does not invent measurements or change the original TEST report.
/// </summary>
public sealed class MockRecordCoordinator
{
    private readonly V1Workspace _workspace;
    private readonly RunRecordWriter _writer;
    private readonly RecordDatabase _database;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;
    public MockRecordCoordinator(V1Workspace workspace, RecordDatabase database)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _writer = new RunRecordWriter(database);
    }

    public RecordDatabase Database => _database;

    // Exclusively recover unfinished append transactions exactly once before any new run in this host.
    // Recovery is never invoked during a run, so it cannot misclassify active work as interrupted.
    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initializeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await _database.MigrateAsync(ct).ConfigureAwait(false);
            await _database.RecoverInterruptedRunsAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally { _initializeGate.Release(); }
    }

    public async Task<ImmutableArray<RunHistoryEntry>> HistoryAsync(string? dut = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        return await new RunHistoryQuery(_database)
            .FilterAsync(new RunHistoryFilter(DutId: dut, Limit: 100), ct).ConfigureAwait(false);
    }
    public async Task<ImmutableArray<RunStepFact>> StepsAsync(RunId run, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        return await _database.ReadStepFactsAsync(run, ct: ct).ConfigureAwait(false);
    }

    public async Task<RecordedMockRun> ExecuteAsync(string dutIdentity,
        string identityRuleHash, RunId? assignedRun = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dutIdentity)) throw new ArgumentException("DUT identity required");
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var started = DateTimeOffset.UtcNow;
        // RunId comes from TEST. An optional assigned ID is used by a confirmed production draft.
        var report = await _workspace.RunMockAsync(ct, assignedRun).ConfigureAwait(false);
        return await PersistReportAsync(report, dutIdentity, identityRuleHash, started, ct).ConfigureAwait(false);
    }

    public async Task<RecordedMockRun> PersistReportAsync(MockRunReport report,
        string dutIdentity, string identityRuleHash, DateTimeOffset started, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(report);
        if (report.Run.Value == Guid.Empty || report.Facts.IsDefault)
            throw new InvalidDataException("TEST report is not a completed snapshot");
        // Reject stale authoring resources even if the caller tries to persist an old report.
        if (_workspace.Diagnostics().Count != 0)
            throw new InvalidOperationException("REFERENCE_STALE_OR_TEST_INVALID");
        var project = _workspace.Project;
        var assets = project.Assets.Select(a => a.ToReference()).ToImmutableArray();
        var projectFingerprint = RevisionFingerprint.FromBytes(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(project)));
        var snapshot = new RunSnapshot(report.Run,
            new ProjectSnapshot(project.Id, new ProjectRevision(project.Revision),
                projectFingerprint, assets, project.SchemaVersion),
            new StationProfile(StationId.Create("G4-MOCK-STATION"),
                new BusRoleMap(RevisionFingerprint.FromBytes("G4-SIM-ROLES"u8), 1, 2),
                "SIMULATED_NO_SUPPLY", RevisionFingerprint.FromBytes("G4-SIM-STATION"u8)),
            RevisionFingerprint.FromSha256(report.PlanFingerprint), assets);
        var header = RunRecord.CreateFromSnapshot(snapshot,
            new SessionIdentity(dutIdentity, identityRuleHash, "ManualSimulation"),
            // Simulated scans are NOT allowed into physical production counters.
            RecordRunMode.Development, started, "G4-V1-MOCK-ONLY");
        await _writer.BeginAsync(header, ct).ConfigureAwait(false);
        foreach (var step in report.Facts)
        {
            var stamp = TimePoint.FromSimulation(DateTimeOffset.UtcNow, 1);
            if (step.StepId.Value != Guid.Empty)
                await _writer.AppendStepAsync(new RunStepFact(report.Run, Guid.NewGuid(),
                    step.StepId, 1, step.Verdict.ToString(), step.Expected, step.Actual,
                    stamp, step.Quality == MockQuality.Observed ? EvidenceQuality.Derived : EvidenceQuality.Unknown,
                    ImmutableArray<EvidenceRef>.Empty), ct).ConfigureAwait(false);
            var detail = $"[SIMULATED] {step.Type} expected={step.Expected} actual={step.Actual ?? "<missing>"} " +
                $"quality={step.Quality} mock-verdict={step.Verdict}";
            var receipt = await _writer.AppendAsync(new RunFact(report.Run, Guid.NewGuid(),
                step.OperationId, RunFactKind.MockObservation, detail, stamp,
                step.Quality == MockQuality.Observed ? EvidenceQuality.Derived : EvidenceQuality.Unknown,
                ImmutableArray<EvidenceRef>.Empty), ct).ConfigureAwait(false);
            if (!receipt.Durable) throw new IOException("SQLITE_FACT_NOT_DURABLE");
        }
        var completed = DateTimeOffset.UtcNow;
        if (completed < started) completed = started;
        var completion = new RunCompletion(report.Run, completed,
            report.Stopped ? RunState.Aborted : report.Verdict is MockStepVerdict.Error ? RunState.Failed : RunState.Completed,
            Verdict.Inconclusive, "SIMULATED_ONLY / TEST_MOCK_" + report.Verdict);
        var commit = await _writer.CompleteAsync(completion, ct).ConfigureAwait(false);
        if (!commit.Durable || commit.Run != report.Run) throw new IOException("SQLITE_COMMIT_NOT_DURABLE");
        var reopened = await new RunHistoryQuery(new RecordDatabase(_database.DatabasePath))
            .FindByRunAsync(report.Run, ct).ConfigureAwait(false);
        if (reopened is null || reopened.Persistence != RecordPersistenceState.Committed ||
            reopened.Verdict != Verdict.Inconclusive || reopened.StepFactCount != report.Facts.Count(f => f.StepId.Value != Guid.Empty))
            throw new IOException("SQLITE_READBACK_FAILED");
        return new RecordedMockRun(report, reopened, false);
    }
}

public sealed record RecordedMockRun(MockRunReport TestReport, RunHistoryEntry Stored,
    bool PhysicalReleaseAllowed);
