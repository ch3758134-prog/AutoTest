using AutoTest.Application.G4;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Records.Model;
using AutoTest.Records.Queries;
using AutoTest.Records.Storage;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Application.Tests;

/// <summary>Cross-direction tests use an actual SQLite file, real DBC decoder and TEST runner.</summary>
public sealed class G4CrossDirectionIntegrationTests
{
    private const string Dbc = "BU_: ECU\nBO_ 123 STATUS: 1 ECU\n SG_ Pressure : 0|8@1+ (1,0) [0|255] \"bar\" ECU\n";
    private static V1Workspace Workspace()
    {
        var workspace = new V1Workspace();
        workspace.CreateProject("G4 integration");
        workspace.ImportDbc(Dbc, "sim.dbc");
        workspace.AddEditableMockExample();
        return workspace;
    }
    private static string Folder() => Path.Combine(Path.GetTempPath(), "AutoTest-G4-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TestRunIdStepFactsAndSimulatedVerdictSurviveNewSqliteConnection()
    {
        var dir = Folder();
        try
        {
            var work = Workspace();
            var dbFile = Path.Combine(dir, "records.sqlite");
            var sink = new MockRecordCoordinator(work, new RecordDatabase(dbFile));
            var hash = RevisionFingerprint.FromBytes("G4-RULE"u8).Sha256;
            var recorded = await sink.ExecuteAsync("EPB-00001", hash);
            Assert.Equal(MockStepVerdict.Pass, recorded.TestReport.Verdict);
            Assert.Equal(recorded.TestReport.Run, recorded.Stored.Run);
            Assert.True(recorded.Stored.StepFactCount >= 2);
            Assert.Equal(Verdict.Inconclusive, recorded.Stored.Verdict);
            Assert.Equal(RecordRunMode.Development, recorded.Stored.Mode);
            Assert.False(recorded.PhysicalReleaseAllowed);
            var reopened = new RecordDatabase(dbFile);
            var fromDisk = await new RunHistoryQuery(reopened).FindByRunAsync(recorded.TestReport.Run);
            Assert.NotNull(fromDisk);
            Assert.Equal(recorded.Stored.Run, fromDisk.Run);
            var facts = await reopened.ReadStepFactsAsync(fromDisk.Run);
            Assert.Contains(facts, f => f.Expected == "8" && f.Actual == "8");
            Assert.All(facts, f => Assert.Equal(fromDisk.Run, f.Run));
            var production = await new ProductionStatistics(reopened).GetSummaryAsync();
            Assert.Equal(0, production.RunCount); // simulator never increments physical production counts
            Assert.Equal(1, (await new ProductionStatistics(reopened)
                .GetSummaryAsync(RecordRunMode.Development)).RunCount);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ScanRuleAndRecipeExecuteCurrentTestWithSameRunIdAndAppendOnlyHistory()
    {
        var dir = Folder();
        try
        {
            using var host = new G4IntegrationHost(Workspace(), dir);
            var blocked = await host.Production.SimulateScanAsync("INVALID-CODE");
            Assert.Null(blocked.Run);
            Assert.False(blocked.Durable);
            Assert.False(blocked.PhysicalReleaseAllowed);
            var first = await host.Production.SimulateScanAsync("EPB-00001");
            var second = await host.Production.SimulateScanAsync("EPB-00001");
            Assert.Equal("MOCK_RECORDED_NOT_PRODUCTION_PASS", first.Code);
            Assert.True(first.Durable);
            Assert.NotNull(first.Record);
            Assert.Equal(first.Run, first.Record.TestReport.Run);
            Assert.NotEqual(first.Run, second.Run);
            Assert.Equal(first.Identity, second.Identity);
            var history = await host.Records.HistoryAsync();
            Assert.Equal(2, history.Length);
            Assert.All(history, x => Assert.Equal(Verdict.Inconclusive, x.Verdict));
            Assert.All(history, x => Assert.Equal(RecordRunMode.Development, x.Mode));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task StaleDbcRevisionBlocksScanAndWaveDoesNotSilentlyRebind()
    {
        var dir = Folder();
        try
        {
            var work = Workspace();
            var assertion = work.Document.Walk().First(x => x.Type == "Assert");
            work.PinStepToDbc(assertion.Id);
            using var host = new G4IntegrationHost(work, dir);
            work.EditDbcSignal(123, "Pressure", 2);
            var scan = await host.Production.SimulateScanAsync("EPB-00001");
            Assert.Equal("TEST_PREPARE_BLOCKED", scan.Code);
            Assert.False(scan.Durable);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.Wave.ReplayAsync());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task SharedHubDbcWaveReplayReferenceComparisonProducesTraceableEvidence()
    {
        var dir = Folder();
        try
        {
            using var host = new G4IntegrationHost(Workspace(), dir);
            var first = await host.Wave.ReplayAsync();
            Assert.Equal("CAN-HUB/DBC/REPLAY", first.SourceLabel);
            Assert.Equal(TimeSource.Replay, first.Series.Origin.Source);
            Assert.Equal(12, first.Series.Samples.Length);
            Assert.All(first.Series.Samples, x => Assert.NotEqual(Guid.Empty, x.SourceEvidence.Value));
            var reference = host.Wave.SaveReference();
            Assert.Equal(first.Signal, reference.Series.Signal);
            var second = await host.Wave.ReplayAsync();
            Assert.NotEqual(first.Evidence.Evidence, second.Evidence.Evidence);
            var compare = await host.Wave.CompareAsync();
            Assert.Equal(Verdict.Pass, compare.Verdict); // comparison inside replay only
            Assert.True(compare.Evidence.Length >= 2);
            Assert.Equal(0, host.Wave.SubscriberCount);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task CanLeaseIsoTpSessionIsMockAndProgrammingCannotTraverseHub()
    {
        using var router = new MockCanHubRouter();
        var uds = new MockHubDiagnosticWorkbench(router);
        var result = await uds.ReadDtcMockAsync();
        Assert.Equal(Outcome.Accepted, result.Outcome);
        Assert.Equal(new byte[] { 0x59, 0x02, 0x00 }, result.Response);
        Assert.True(result.Simulated);
        Assert.False(result.PhysicalEffectVerified);
        Assert.True(result.SharedHubRequests > 0);
        var oldCount = result.SharedHubRequests;
        var blocked = await uds.RejectStateChangeOnHubAsync();
        Assert.Equal(Outcome.Rejected, blocked.Outcome);
        Assert.Equal(oldCount, router.MockRequestCount);
    }

    [Fact]
    public async Task MockFlashRunsBootloaderAndWrongIdentityOrDisconnectNeverProducesPhysicalPass()
    {
        var flash = new MockFlashWorkbench();
        var success = await flash.DemoAsync();
        Assert.Equal("MOCK_FLASH_VERIFIED_ONLY", success.Code);
        Assert.False(success.PhysicalWriteAllowed);
        Assert.NotNull(success.Job);
        var mismatch = await flash.ExecuteBinAsync([1, 2, 3], observedModel: "WRONG-ECU");
        Assert.Equal("MOCK_FLASH_PREFLIGHT_BLOCKED", mismatch.Code);
        Assert.Contains("ECU_IDENTITY_MISMATCH", mismatch.Blockers);
        var disconnect = await flash.ExecuteBinAsync([1, 2, 3], injectDisconnect: true);
        Assert.NotEqual("MOCK_FLASH_VERIFIED_ONLY", disconnect.Code);
        Assert.False(disconnect.PhysicalWriteAllowed);
    }
}
