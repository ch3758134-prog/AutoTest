using AutoTest.Desktop.Tools.Testing;
using AutoTest.Application.V1;
using AutoTest.Testing.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Testing;

public sealed class LabTestWorkspaceViewModelTests
{
    [Fact]
    public void FormalTestEditorHasNoMockRunnerOrInjectedSample()
    {
        var vm = new LabTestWorkspaceViewModel(new AutoTest.Application.V1.V1Workspace());
        Assert.Empty(vm.Snapshot.Walk());
        Assert.DoesNotContain("MockObserve", vm.StepTypes);
        Assert.Empty(vm.Facts);
        Assert.Equal("NOT_RUN", vm.Verdict);
        Assert.Contains("真实设备", vm.SourceLabel);
    }
    [Fact]
    public void SelectionEditSaveReloadAndUndoAreBoundToEditor()
    {
        var vm = new LabTestWorkspaceViewModel();
        vm.SelectedInsertType = "Observe";
        vm.InsertCommand.Execute(null);
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
        Assert.Contains("运行前检查未通过", vm.Status);
    }
    [Fact]
    public async Task PhysicalRunRejectsMissingProjectProgramBeforeDeviceAuthorization()
    {
        var project = new V1Workspace();
        var vm = new LabTestWorkspaceViewModel(project);
        var approvals = 0;
        vm.AttachPhysicalReadOnly(() => null, () => { approvals++; return true; });
        var result = await vm.RunPhysicalReadOnlyAsync();
        Assert.Null(result);
        Assert.Equal(0, approvals);
        Assert.Equal("ERROR / NOT_PRODUCTION", vm.Verdict);
        Assert.Contains("工程没有已保存的测试程序", vm.Status);
    }

    [Fact]
    public async Task PhysicalRunRejectsUnsavedProjectBeforeDeviceAuthorization()
    {
        var project = new V1Workspace();
        var vm = new LabTestWorkspaceViewModel(project);
        vm.SaveToProjectCommand.Execute(null);
        Assert.NotNull(project.LabProgramAssetId);
        var approvals = 0;
        vm.AttachPhysicalReadOnly(() => null, () => { approvals++; return true; });
        var result = await vm.RunPhysicalReadOnlyAsync();
        Assert.Null(result);
        Assert.Equal(0, approvals);
        Assert.Equal("ERROR / NOT_PRODUCTION", vm.Verdict);
        Assert.Contains("工程尚未保存到磁盘", vm.Status);
    }

}
