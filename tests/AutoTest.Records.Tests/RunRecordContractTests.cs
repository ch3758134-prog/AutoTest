using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Exports;
using AutoTest.Records.Model;
using AutoTest.Records.Queries;
using AutoTest.Records.Storage;
using Xunit;

namespace AutoTest.Records.Tests;

/// <summary>Approved G4 R1-T01...R1-T06 offline/mock verification suite.</summary>
public sealed class RunRecordContractTests
{
    [Fact]
    public async Task Verify_R1_T01()
    {
        using var fixture = new TempFixture();
        await fixture.SaveAsync("ECU-A", Verdict.Pass);
        await fixture.SaveAsync("ECU-A", Verdict.Fail);
        await fixture.SaveAsync("ECU-B", Verdict.Pass);
        var stats = await fixture.Statistics.GetSummaryAsync();
        Assert.Equal(3, stats.RunCount);
        Assert.Equal(2, stats.DistinctDutCount);
        Assert.Equal(3, stats.CommittedRuns);
        Assert.Equal(2, stats.PassedRuns);
        Assert.Equal(1, stats.FailedRuns);
        Assert.Equal(2, (await fixture.History.FindByDutAsync("ECU-A")).Length);
    }

    [Fact]
    public async Task Verify_R1_T02()
    {
        using var fixture = new TempFixture();
        var run = await fixture.SaveAsync("ECU-A", Verdict.Fail);
        await fixture.SaveAsync("ECU-A", Verdict.Pass);
        var original = await fixture.History.FindByRunAsync(run);
        Assert.NotNull(original);
        Assert.Equal(Verdict.Fail, original.Verdict);
        Assert.Equal(RecordPersistenceState.Committed, original.Persistence);
        var report = await new RunReportExporter(fixture.Db, fixture.Artifacts).ExportSummaryAsync(run);
        Assert.Contains("Fail", report.Html);
        Assert.Single(report.Facts);
    }

    [Fact]
    public async Task Verify_R1_T03()
    {
        using var fixture = new TempFixture();
        var interrupted = fixture.CreateRun("ECU-A");
        await fixture.Writer.BeginAsync(interrupted);
        var originalFact = fixture.Fact(interrupted.RunId, 1);
        Assert.True((await fixture.Writer.AppendAsync(originalFact, default)).Durable);
        // Simulate exclusive app restart; do not invoke recovery concurrently with active runs.
        var restarted = new RecordDatabase(fixture.Db.DatabasePath);
        Assert.Equal(1, await restarted.RecoverInterruptedRunsAsync());
        var history = await new RunHistoryQuery(restarted).FindByRunAsync(interrupted.RunId);
        Assert.Equal(RecordPersistenceState.Interrupted, history!.Persistence);
        Assert.Equal("Unknown", history.TerminalState);
        Assert.Null(history.Verdict);
        Assert.Single(await restarted.ReadFactsAsync(new RecordQuery(interrupted.RunId, 50)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await restarted.AppendAsync(fixture.Fact(interrupted.RunId, 2)));
        Assert.Equal(0, await restarted.RecoverInterruptedRunsAsync());
    }

    [Fact]
    public async Task Verify_R1_T04()
    {
        using var fixture = new TempFixture();
        await fixture.SaveAsync("ECU-A", Verdict.Pass, RecordRunMode.Production);
        await fixture.SaveAsync("ECU-A", Verdict.Fail, RecordRunMode.Production);
        await fixture.SaveAsync("ECU-B", Verdict.Pass, RecordRunMode.Development);
        var production = await fixture.Statistics.GetSummaryAsync();
        var development = await fixture.Statistics.GetSummaryAsync(RecordRunMode.Development);
        Assert.Equal(2, production.RunCount);
        Assert.Equal(1, production.DistinctDutCount);
        Assert.Equal(1, development.RunCount);
        Assert.Equal(1, development.DistinctDutCount);
    }

    [Fact]
    public async Task Verify_R1_T05()
    {
        using var fixture = new TempFixture();
        var run = fixture.CreateRun("ECU-A");
        await fixture.Writer.BeginAsync(run);
        var bytes = new byte[] { 0x01, 0x33, 0x55, 0x7F };
        await using var content = new MemoryStream(bytes);
        var artifact = await fixture.Artifacts.PutAsync(content, "CanTrace", "application/octet-stream", "Simulation");
        await fixture.Writer.AttachArtifactAsync(run.RunId, artifact);
        await fixture.Writer.CompleteAsync(new RunCompletion(run.RunId, run.StartedUtc.AddMinutes(1),
            RunState.Completed, Verdict.Pass, null));
        Assert.Equal(ArtifactIntegrity.Present, await fixture.Artifacts.VerifyAsync(artifact));
        var path = Path.Combine(fixture.ArtifactFolder, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4 });
        Assert.Equal(ArtifactIntegrity.Corrupt, await fixture.Artifacts.VerifyAsync(artifact));
        var report = await new RunReportExporter(fixture.Db, fixture.Artifacts).ExportSummaryAsync(run.RunId);
        Assert.Equal(ArtifactIntegrity.Corrupt, Assert.Single(report.Artifacts).Integrity);
        File.Delete(path);
        Assert.Equal(ArtifactIntegrity.Missing, await fixture.Artifacts.VerifyAsync(artifact));
    }

    [Fact]
    public async Task Verify_R1_T06()
    {
        using var fixture = new TempFixture();
        var record = fixture.CreateRun("ECU-A");
        await fixture.Writer.BeginAsync(record);
        await fixture.Writer.CompleteAsync(new RunCompletion(record.RunId, record.StartedUtc.AddMinutes(1),
            RunState.Completed, Verdict.Inconclusive, "No physical measurement"));
        var query = await fixture.History.FindByRunAsync(record.RunId);
        Assert.NotNull(query);
        Assert.Equal(record.CanonicalSnapshotJson(), query.SnapshotJson);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(record.CanonicalSnapshotJson()))).ToLowerInvariant(), query.SnapshotSha256);
        Assert.Equal(Verdict.Inconclusive, query.Verdict);
        Assert.Contains("HostBuild", query.SnapshotJson);
        Assert.Contains("PlanFingerprint", query.SnapshotJson);
    }
}

internal sealed class TempFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AutoTest-G4-Records-" + Guid.NewGuid().ToString("N"));
    public string ArtifactFolder => Path.Combine(_root, "artifacts");
    public RecordDatabase Db { get; }
    public RunRecordWriter Writer { get; }
    public RunHistoryQuery History { get; }
    public ProductionStatistics Statistics { get; }
    public ArtifactRepository Artifacts { get; }
    public TempFixture()
    {
        Directory.CreateDirectory(_root);
        Db = new RecordDatabase(Path.Combine(_root, "runs.db"));
        Writer = new RunRecordWriter(Db);
        History = new RunHistoryQuery(Db);
        Statistics = new ProductionStatistics(Db);
        Artifacts = new ArtifactRepository(ArtifactFolder);
    }

    public RunRecord CreateRun(string dut, RecordRunMode mode = RecordRunMode.Production)
    {
        var project = new ProjectSnapshot(ProjectId.New(), new ProjectRevision(5),
            RevisionFingerprint.FromSha256(new string('a', 64)), ImmutableArray<AssetRef>.Empty, 1);
        var station = new StationProfile(StationId.Create("MOCK-STATION"),
            new BusRoleMap(RevisionFingerprint.FromSha256(new string('c', 64)), 1, 2),
            "MOCK-NO-PHYSICAL-OUTPUT", RevisionFingerprint.FromSha256(new string('d', 64)));
        var snapshot = new RunSnapshot(RunId.New(), project, station,
            RevisionFingerprint.FromSha256(new string('b', 64)), ImmutableArray<AssetRef>.Empty);
        return RunRecord.CreateFromSnapshot(snapshot, new SessionIdentity(dut, new string('e', 64), "MOCK"),
            mode, new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero), "V0-be08abe");
    }

    public RunFact Fact(RunId run, int number) => new(run, Guid.NewGuid(), Guid.NewGuid(),
        RunFactKind.Judgement, "Mock verdict input " + number,
        TimePoint.FromSimulation(new DateTimeOffset(2026, 11, 4, 12, 0, number, TimeSpan.Zero), number),
        EvidenceQuality.Derived, ImmutableArray<EvidenceRef>.Empty);

    public async Task<RunId> SaveAsync(string dut, Verdict verdict, RecordRunMode mode = RecordRunMode.Production)
    {
        var record = CreateRun(dut, mode);
        await Writer.BeginAsync(record);
        await Writer.AppendAsync(Fact(record.RunId, 1), default);
        await Writer.CompleteAsync(new RunCompletion(record.RunId, record.StartedUtc.AddMinutes(1),
            RunState.Completed, verdict, null));
        return record.RunId;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* SQLite handles are closing, test temp cleanup is best effort */ }
    }
}
