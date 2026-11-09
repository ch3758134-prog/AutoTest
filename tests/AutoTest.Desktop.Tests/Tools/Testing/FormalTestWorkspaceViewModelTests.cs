using AutoTest.Desktop.Tools.Testing;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Testing;

public sealed class FormalTestWorkspaceViewModelTests
{
    [Fact]
    public async Task OfficialDocumentMockRunIsVisibleAndExportable()
    {
        var vm = new FormalTestWorkspaceViewModel();
        await vm.RunMockAsync();
        Assert.Equal("PASS", vm.Verdict);
        Assert.False(vm.Busy);
        Assert.Equal(3, vm.Tree.Count);
        Assert.Contains(vm.Facts, f => f.Type == "Assert");
        Assert.Contains(vm.Timeline, e => e.EventKind == "STEP_FACT" && e.Source == "Mock");
        Assert.Contains(vm.Timeline, e => e.EventKind == "RUN_COMPLETED");
        Assert.Contains("MOCK", vm.Status);
        Assert.Contains("SOFTWARE_ONLY", vm.ModeLabel);
        Assert.True(vm.CreateEvidenceBundle().ValuesRedacted);
        Assert.Contains("PlanFingerprint", vm.ExportEvidenceJson());
        Assert.Contains("TimestampUtc", vm.ExportEvidenceCsv());
    }

    [Fact]
    public async Task FilterAndCopyPayloadNeverChangeFullArchive()
    {
        var vm = new FormalTestWorkspaceViewModel();
        await vm.RunMockAsync();
        vm.TraceFilter = "STEP_FACT";
        Assert.NotEmpty(vm.FilteredTimeline);
        Assert.All(vm.FilteredTimeline, e => Assert.Equal("STEP_FACT", e.EventKind));
        vm.TraceFilter = "";
        Assert.Equal(vm.Timeline.Count, vm.FilteredTimeline.Count);
        Assert.Contains("Timeline", vm.VisibleTraceText());
        vm.RedactValuesOnExport = false;
        Assert.False(vm.CreateEvidenceBundle().ValuesRedacted);
        var withContext = new FormalTestWorkspaceViewModel(TestDocument.Create("context"),
            new FormalTestContext("project-id", "ecu", "station", "P1", "R2", "NOT_CONNECTED", "not-built"));
        Assert.Contains("project-id", withContext.ResourceStatus);
        Assert.Equal("P1", withContext.CreateEvidenceBundle().ProjectRevision);
    }

    [Fact]
    public void EditsAndReloadUseFormalTestDocumentCodec()
    {
        var vm = new FormalTestWorkspaceViewModel();
        var firstTest = vm.Tree.Single(p => p.Phase == TestPhase.Test).Children[0];
        vm.Select(firstTest);
        vm.SelectedName = "Observed mock signal";
        vm.ApplyNameCommand.Execute(null);
        Assert.Equal("Observed mock signal", vm.Snapshot.Get(TestPhase.Test)[0].Name);
        vm.UndoCommand.Execute(null);
        Assert.NotEqual("Observed mock signal", vm.Snapshot.Get(TestPhase.Test)[0].Name);
        var json = vm.ExportDocumentJson();
        var id = vm.Snapshot.Id;
        vm.ImportDocumentJson(json);
        Assert.Equal(id, vm.Snapshot.Id);
        Assert.Equal(TestDocumentCodec.Deserialize(json).Walk().Select(s => s.Id),
            vm.Snapshot.Walk().Select(s => s.Id));
    }

    [Fact]
    public void MissingParamsAndUnsupportedNodesHaveVisiblePreflightDiagnostics()
    {
        var vm = new FormalTestWorkspaceViewModel();
        vm.SelectedInsertType = "Flash";
        vm.InsertCommand.Execute(null);
        var result = vm.Preflight();
        Assert.Contains("BLOCKED", result);
        Assert.Contains(vm.Diagnostics, d => d.Contains("Flash not executable", StringComparison.Ordinal));
    }
}
