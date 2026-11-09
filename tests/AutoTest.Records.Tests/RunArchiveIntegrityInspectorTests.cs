using System.Text.Json;
using AutoTest.Core;
using AutoTest.Records.Model;
using AutoTest.Records.Queries;
using AutoTest.Records.Storage;
using Xunit;

namespace AutoTest.Records.Tests;

public sealed class RunArchiveIntegrityInspectorTests
{
    [Fact]
    public async Task MockProductionPassWithNoArtifacts_IsMetadataOnlyAndNotPhysicalRelease()
    {
        using var fixture = new TempFixture();
        var run = await fixture.SaveAsync("ECU-A", Verdict.Pass);
        var result = await NewInspector(fixture).InspectAsync(run);
        Assert.Equal(RunArchiveIntegrityStatus.MetadataOnly, result.Status);
        Assert.Equal("Pass", result.Verdict);
        Assert.Equal(RecordRunMode.Production, result.Mode);
        Assert.Equal("NOT_EVALUATED_NO_RELEASE_CONTRACT", result.PhysicalReleaseAssessment);
        Assert.Contains(result.Issues, x => x.Code == "NOT_A_PHYSICAL_RELEASE");
        Assert.Contains(result.Issues, x => x.Code == "NO_LINKED_ARTIFACTS");
        using var document = JsonDocument.Parse(result.ToManifestJson());
        Assert.Equal("MetadataOnly", document.RootElement.GetProperty("integrity").GetString());
        Assert.Equal("NOT_EVALUATED_NO_RELEASE_CONTRACT", document.RootElement.GetProperty("physicalRelease").GetString());
    }

    [Fact]
    public async Task EvidenceIsVerifiedByActualBytesAndReclassifiedAfterCorruptionOrDeletion()
    {
        using var fixture = new TempFixture();
        var run = fixture.CreateRun("ECU-A");
        await fixture.Writer.BeginAsync(run);
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        await using var input = new MemoryStream(bytes);
        var artifact = await fixture.Artifacts.PutAsync(input, "CAN", "application/octet-stream", "Replay");
        await fixture.Writer.AttachArtifactAsync(run.RunId, artifact);
        await fixture.Writer.CompleteAsync(new RunCompletion(run.RunId, run.StartedUtc.AddMinutes(1),
            RunState.Completed, Verdict.Inconclusive, "No physical observation"));
        var inspector = NewInspector(fixture);
        var good = await inspector.InspectAsync(run.RunId);
        Assert.Equal(RunArchiveIntegrityStatus.EvidenceVerified, good.Status);
        Assert.Equal(1, good.VerifiedArtifacts);

        var file = Path.Combine(fixture.ArtifactFolder,
            artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllBytesAsync(file, new byte[] { 1, 2, 3, 4, 6 });
        var bad = await inspector.InspectAsync(run.RunId);
        Assert.Equal(RunArchiveIntegrityStatus.EvidenceCorrupt, bad.Status);
        Assert.Equal(1, bad.CorruptArtifacts);
        Assert.Contains(bad.Issues, x => x.Code == "EVIDENCE_HASH_MISMATCH" && x.Blocking);

        File.Delete(file);
        var missing = await inspector.InspectAsync(run.RunId);
        Assert.Equal(RunArchiveIntegrityStatus.EvidenceMissing, missing.Status);
        Assert.Equal(1, missing.MissingArtifacts);
        Assert.Contains(missing.Issues, x => x.Code == "EVIDENCE_MISSING" && x.Blocking);
    }

    [Fact]
    public async Task UnfinishedRun_IsNotCommittedRatherThanPass()
    {
        using var fixture = new TempFixture();
        var run = fixture.CreateRun("ECU-A");
        await fixture.Writer.BeginAsync(run);
        await fixture.Writer.AppendAsync(fixture.Fact(run.RunId, 1), default);
        var partial = await NewInspector(fixture).InspectAsync(run.RunId);
        Assert.Equal(RunArchiveIntegrityStatus.NotCommitted, partial.Status);
        Assert.Equal(RecordPersistenceState.Writing, partial.Persistence);
        Assert.Null(partial.Verdict);
        Assert.Contains(partial.Issues, x => x.Code == "RUN_NOT_COMMITTED");
        await fixture.Db.RecoverInterruptedRunsAsync();
        var recovered = await NewInspector(fixture).InspectAsync(run.RunId);
        Assert.Equal(RunArchiveIntegrityStatus.NotCommitted, recovered.Status);
        Assert.Equal(RecordPersistenceState.Interrupted, recovered.Persistence);
    }

    [Fact]
    public async Task StoredFactHashMismatch_IsRecordCorruptNotEmptyData()
    {
        using var fixture = new TempFixture();
        var run = await fixture.SaveAsync("ECU-A", Verdict.Fail);
        await using (var connection = await fixture.Db.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            // Deliberately simulate offline database tampering by removing the protection trigger.
            command.CommandText = "DROP TRIGGER IF EXISTS run_facts_no_update; " +
                "UPDATE run_facts SET payload_json='{}' WHERE run_id=$run;";
            command.Parameters.AddWithValue("$run", run.ToString());
            await command.ExecuteNonQueryAsync();
        }
        var inspection = await NewInspector(fixture).InspectAsync(run);
        Assert.Equal(RunArchiveIntegrityStatus.RecordCorrupt, inspection.Status);
        Assert.Contains(inspection.Issues, x => x.Code == "RECORD_DATA_INVALID" && x.Blocking);
    }

    [Fact]
    public async Task MissingRunDoesNotBecomeSuccessfulEmptyReport()
    {
        using var fixture = new TempFixture();
        var result = await NewInspector(fixture).InspectAsync(RunId.New());
        Assert.Equal(RunArchiveIntegrityStatus.NotFound, result.Status);
        Assert.Contains(result.Issues, x => x.Code == "RUN_NOT_FOUND");
    }

    [Fact]
    public async Task BatchScanHonorsModeAndHasBoundedWindow()
    {
        using var fixture = new TempFixture();
        await fixture.SaveAsync("ECU-A", Verdict.Pass, RecordRunMode.Production);
        await fixture.SaveAsync("ECU-B", Verdict.Fail, RecordRunMode.Development);
        var inspector = NewInspector(fixture);
        var result = await inspector.InspectHistoryAsync(new RunHistoryFilter(
            Mode: RecordRunMode.Production, Limit: 10));
        Assert.Single(result);
        Assert.Equal(RecordRunMode.Production, result[0].Mode);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            inspector.InspectHistoryAsync(new RunHistoryFilter(Limit: 501)));
    }

    [Fact]
    public async Task CancellationIsPropagatedWithoutReportingIntegritySuccess()
    {
        using var fixture = new TempFixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NewInspector(fixture).InspectAsync(RunId.New(), cts.Token));
    }

    private static RunArchiveIntegrityInspector NewInspector(TempFixture fixture) =>
        new(fixture.Db, fixture.Artifacts);
}
