using AutoTest.Desktop.Tools.Testing;
using AutoTest.Testing.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Testing;

public sealed class LabTestWorkspaceViewModelTests
{
    [Fact]
    public async Task DefaultPageRunsActualMockFactsAndDoesNotClaimPhysicalResult()
    {
        var vm = new LabTestWorkspaceViewModel(new SimulatedLabStation(), new LabPlanCompiler());
        await vm.RunMockAsync();
        Assert.Equal("PASS", vm.Verdict);
        Assert.Contains(vm.Facts, f => f.NodeType == "Assert" && f.Verdict == LabVerdict.Pass);
        Assert.All(vm.Facts, f => Assert.Equal(LabSource.Mock, f.Source));
        Assert.Contains("Mock", vm.Status);
    }
    [Fact]
    public void SelectionEditSaveReloadAndUndoAreBoundToEditor()
    {
        var vm = new LabTestWorkspaceViewModel();
        var first = vm.Tree.Single(p => p.Phase == LabPhase.Test).Children[0];
        vm.Select(first);
        vm.SelectedName = "Observed signal";
        vm.RenameCommand.Execute(null);
        Assert.Equal("Observed signal", vm.Snapshot.Phases[LabPhase.Test][0].Name);
        vm.UndoCommand.Execute(null);
        Assert.NotEqual("Observed signal", vm.Snapshot.Phases[LabPhase.Test][0].Name);
        var saved = vm.ExportJson();
        vm.ImportJson(saved);
        Assert.Equal(3, vm.Tree.Count);
        Assert.Equal(vm.Snapshot.Id, LabDocumentCodec.Open(saved).Id);
    }
    [Fact]
    public void UnsupportedControllerBindingBlocksVisiblePreflight()
    {
        var vm = new LabTestWorkspaceViewModel();
        vm.SelectedInsertType = "ControllerState";
        vm.InsertCommand.Execute(null);
        vm.PreflightCommand.Execute(null);
        Assert.Contains("Preflight blocked", vm.Status);
    }
}
