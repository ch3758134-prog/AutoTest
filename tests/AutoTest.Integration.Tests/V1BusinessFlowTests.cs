using AutoTest.Application.V1;
using AutoTest.Application.Composition;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Integration.Tests;

/// <summary>End-to-end across PROJECT + DBC + TEST + APPLICATION without device I/O.</summary>
public sealed class V1BusinessFlowTests
{
    private const string Dbc = "BU_: ECU\nBO_ 123 STATUS: 1 ECU\n SG_ Pressure : 0|8@1+ (1,0) [0|255] \"bar\" ECU\n";
    private static string Root() => Path.Combine(Path.GetTempPath(), "AutoTest-V1-" + Guid.NewGuid().ToString("N"));
    private static V1Workspace Author()
    {
        var work = new V1Workspace();
        work.CreateProject("集成真实模型");
        work.ImportDbc(Dbc, "ecu.dbc");
        work.AddEditableMockExample();
        var assertion = work.Document.Walk().Single(n => n.Type == "Assert");
        work.PinStepToDbc(assertion.Id);
        return work;
    }

    [Fact]
    public async Task CreateImportEditSaveReopenRunRealTestDocumentAndFacts()
    {
        var root = Root(); var path = Path.Combine(root, "real.autotest-project.json");
        try
        {
            var work = Author();
            var oldId = work.Document.Id;
            var original = work.Database!.FindSignal(123, "Pressure").Factor;
            var before = work.Assets.Single(a => a.Kind == "dbc");
            await work.SaveProjectAsync(path);
            var saved = await AutoTest.Projects.Project.ProjectRepository.OpenDefinitionAsync(path);
            Assert.Equal("集成真实模型", saved.Name);
            Assert.Equal(2, saved.Assets.Length);
            Assert.Single(saved.Dependencies);
            var reopened = new V1Workspace();
            await reopened.OpenProjectAsync(path);
            Assert.Equal(oldId, reopened.Document.Id);
            Assert.Equal(work.Document.Walk().Select(n => n.Id), reopened.Document.Walk().Select(n => n.Id));
            Assert.Equal(original, reopened.Database!.FindSignal(123, "Pressure").Factor);
            Assert.Empty(reopened.Diagnostics());
            var report = await reopened.RunMockAsync();
            Assert.Equal(MockStepVerdict.Pass, report.Verdict);
            Assert.Equal(2, report.Facts.Count(f => f.Type == "Assert"));
            Assert.All(report.Facts.Where(f => f.Type == "Assert"), f =>
            {
                Assert.Equal("8", f.Expected); Assert.Equal("8", f.Actual);
                Assert.Equal(MockQuality.Observed, f.Quality);
                Assert.Equal(MockStepVerdict.Pass, f.Verdict);
            });
            Assert.Equal(MockStepVerdict.Pass, report.Facts.Single(f => f.Type == "SetVariable" && f.Detail == "Cleanup").Verdict);
            var edited = reopened.EditDbcSignal(123, "Pressure", 3);
            Assert.Equal(before.Revision + 1, edited.Revision);
            Assert.Equal(original, work.Database!.FindSignal(123, "Pressure").Factor);
            Assert.Contains(reopened.Diagnostics(), x => x.Contains("REFERENCE_STALE"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.RunMockAsync());
            await reopened.SaveProjectAsync(path);
            var staleReload = new V1Workspace();
            await staleReload.OpenProjectAsync(path);
            Assert.Single(staleReload.StaleDependencies);
            Assert.Contains("REFERENCE_STALE", staleReload.StepAssetStatus(
                staleReload.Document.Walk().Single(n => n.Type == "Assert")));
            var id = staleReload.Document.Walk().Single(n => n.Type == "Assert").Id;
            staleReload.RebindDbcReference(id);
            Assert.Empty(staleReload.Diagnostics());
            await staleReload.SaveProjectAsync(path);
            var final = new V1Workspace();
            await final.OpenProjectAsync(path);
            Assert.Empty(final.StaleDependencies);
            Assert.Equal(3, final.Database!.FindSignal(123, "Pressure").Factor);
            Assert.Equal(MockStepVerdict.Pass, (await final.RunMockAsync()).Verdict);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task NoAssertDoesNotPassAndAnotherRunCannotReuseOldObservation()
    {
        var work = new V1Workspace();
        work.Editor.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("MockObserve").WithProperty("variable", "pressure")
                .WithProperty("value", "8").WithProperty("quality", "Observed"));
        Assert.Equal(MockStepVerdict.Inconclusive, (await work.RunMockAsync()).Verdict);
        // New document: old station observations cannot be reused.
        work.Editor.Delete(work.Document.Get(TestPhase.Test)[0].Id);
        work.Editor.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("Assert").WithProperty("variable", "pressure").WithProperty("equals", "8"));
        var result = await work.RunMockAsync();
        Assert.Equal(MockStepVerdict.Inconclusive, result.Verdict);
        Assert.Equal(MockQuality.Missing, result.Facts.Single().Quality);
    }

    [Fact]
    public void EditorSupportsMoveDeleteUndoRedoAndPrepareBlocksUnsupported()
    {
        var work = new V1Workspace();
        var a = StepNode.Create("Wait").WithProperty("milliseconds", "0");
        var b = StepNode.Create("Assert").WithProperty("variable", "x").WithProperty("equals", "1");
        work.Editor.Insert(TestPhase.Test, null, null, 0, a);
        work.Editor.Insert(TestPhase.Test, null, null, 1, b);
        work.Editor.Move(b.Id, TestPhase.Test, null, null, 0);
        Assert.Equal(b.Id, work.Document.Get(TestPhase.Test)[0].Id);
        work.Editor.Undo(); Assert.Equal(a.Id, work.Document.Get(TestPhase.Test)[0].Id);
        work.Editor.Redo(); Assert.Equal(b.Id, work.Document.Get(TestPhase.Test)[0].Id);
        work.Editor.Delete(b.Id); Assert.Single(work.Document.Get(TestPhase.Test));
        work.Editor.Undo(); Assert.Equal(2, work.Document.Get(TestPhase.Test).Length);
        work.Editor.Insert(TestPhase.Test, null, null, 2, StepNode.Create("Flash"));
        Assert.Contains(work.Diagnostics(), d => d.Contains("not executable"));
    }

    [Fact]
    public async Task StopAlwaysRunsMockCleanup()
    {
        var work = new V1Workspace();
        work.Editor.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("Wait").WithProperty("milliseconds", "5000"));
        work.Editor.Insert(TestPhase.Cleanup, null, null, 0,
            StepNode.Create("SetVariable").WithProperty("variable", "mode").WithProperty("value", "clean"));
        var task = work.RunMockAsync();
        work.Stop();
        var report = await task;
        Assert.True(report.Stopped);
        Assert.Equal(MockStepVerdict.Inconclusive, report.Verdict);
        Assert.Contains(report.Facts, f => f.Detail == "Cleanup");
    }
    [Fact]
    public async Task ExternalAssetChangeAndManifestConflictAreRejectedWithoutSilentOverwrite()
    {
        var root = Root(); var path = Path.Combine(root, "source.autotest-project.json");
        try
        {
            var work = Author();
            await work.SaveProjectAsync(path);
            var dbc = work.Assets.Single(a => a.Kind == "dbc");
            var assetPath = Path.Combine(root, dbc.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            await File.AppendAllTextAsync(assetPath, "\n# tampered");
            await Assert.ThrowsAsync<InvalidOperationException>(() => work.SaveProjectAsync(path));
            var reopened = new V1Workspace();
            await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.OpenProjectAsync(path));
            Assert.Null(reopened.ProjectPath);
            // Restore only the asset to test manifest collision separately.
            await File.WriteAllTextAsync(assetPath, Dbc);
            await File.AppendAllTextAsync(path, "\n ");
            await Assert.ThrowsAsync<InvalidOperationException>(() => work.SaveProjectAsync(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InvalidProgramDoesNotEmitMockStepFacts()
    {
        var work = new V1Workspace();
        work.Editor.Insert(TestPhase.Test, null, null, 0, StepNode.Create("Flash"));
        Assert.NotEmpty(work.Diagnostics());
        await Assert.ThrowsAsync<InvalidOperationException>(() => work.RunMockAsync());
        Assert.Null(work.LastReport);
    }

    [Fact]
    public async Task V0ReconnectRejectsOldSessionAckAndObservation()
    {
        var mock = ApplicationComposition.RegisterMock();
        var station = StationId.Create("V0-SIM-STATION");
        var first = (await mock.Station.ConnectAsync(station, CancellationToken.None)).Value!;
        var command = new StationCommand(StationAction.MockProbe, MockScenario.DelayedResponse, Guid.NewGuid());
        var executing = mock.Station.ExecuteAsync(command, Authorization.MockOnly(first, DateTimeOffset.UtcNow),
            CancellationToken.None).AsTask();
        await Task.Delay(5);
        await mock.Station.DisconnectAsync(CancellationToken.None);
        var next = (await mock.Station.ConnectAsync(station, CancellationToken.None)).Value!;
        Assert.NotEqual(first.Generation, next.Generation);
        var result = await executing;
        Assert.Equal(Outcome.Rejected, result.Outcome);
        Assert.Equal("STALE_SESSION", result.FailureCode);
        var old = await mock.Station.ObserveMockAsync(command.CorrelationId, first.Generation, CancellationToken.None);
        Assert.Equal(Outcome.Rejected, old.Outcome);
        Assert.Equal("NO_CURRENT_MOCK_EVIDENCE", old.FailureCode);
    }

}
