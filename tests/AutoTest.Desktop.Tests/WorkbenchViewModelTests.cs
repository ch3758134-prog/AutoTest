using System.IO;
using AutoTest.Application.Composition;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Desktop.Shell;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Desktop.Tests;

public sealed class WorkbenchViewModelTests
{
    private static MainWindowViewModel Create() =>
        new(ApplicationComposition.RegisterMock().Gateway, new V1Workspace());

    [Fact]
    public async Task V0GatewayCompatibilityStillSeparatesMockAckAndObservations()
    {
        var mock = ApplicationComposition.RegisterMock();
        var vm = new MainWindowViewModel(mock.Gateway);
        Assert.True(((System.Windows.Input.ICommand)vm.NormalCommand).CanExecute(null));
        await vm.ExecuteScenarioAsync(ApplicationCommandKind.MockNormal);
        Assert.Equal("SIMULATOR_ACK_ONLY", vm.AckStatus);
        Assert.Equal("SIMULATED_INDICATOR_READY", vm.ObservationStatus);
        Assert.Equal("Inconclusive", vm.Verdict);
        Assert.NotEmpty(vm.FactLines);
        await vm.ExecuteScenarioAsync(ApplicationCommandKind.MockTimeout);
        Assert.Equal("NO_ACK", vm.AckStatus);
        Assert.Equal("MOCK_TIMEOUT", vm.FailureReason);
    }

    [Fact]
    public async Task UnsafeCommandCannotBeSentViaVm()
    {
        var mock = ApplicationComposition.RegisterMock();
        var vm = new MainWindowViewModel(mock.Gateway);
        await vm.ExecuteScenarioAsync(ApplicationCommandKind.RealProgramming);
        Assert.StartsWith("V0_SCOPE_DENIED", vm.FailureReason);
        Assert.Equal(0, mock.Station.CurrentGeneration);
    }

    [Fact]
    public void ActualTestDocumentCommandsAddMoveDeleteUndoRedo()
    {
        var vm = Create();
        Assert.Empty(vm.Steps);
        vm.NewStepType = "Sequence";
        vm.InsertAfterCommand.Execute();
        Assert.Equal("Sequence", Assert.Single(vm.Workspace.Document.Walk()).Type);
        vm.NewStepType = "Assert";
        vm.InsertChildCommand.Execute();
        Assert.Equal(2, vm.Workspace.Document.Walk().Count());
        Assert.Equal(1, vm.SelectedStep!.Depth);
        vm.UndoCommand.Execute();
        Assert.Single(vm.Workspace.Document.Walk());
        vm.RedoCommand.Execute();
        Assert.Equal(2, vm.Workspace.Document.Walk().Count());
        vm.SelectedStep = vm.Steps.Single(s => s.NodeType == "Assert");
        vm.DeleteCommand.Execute();
        Assert.Single(vm.Workspace.Document.Walk());
        vm.UndoCommand.Execute();
        Assert.Equal(2, vm.Workspace.Document.Walk().Count());
    }

    [Fact]
    public void RealModelPropertyEditAndSearchAreConsistent()
    {
        var vm = Create(); vm.AddExampleCommand.Execute();
        var assertion = vm.Steps.Single(s => s.NodeType == "Assert");
        vm.SelectedStep = assertion;
        vm.EditedExpected = "9";
        vm.ApplyPropertiesCommand.Execute();
        Assert.Equal("9", vm.Workspace.Document.Walk().Single(n => n.Type == "Assert").Properties["equals"]);
        vm.SearchText = "断言";
        Assert.Single(vm.FilteredSteps);
        Assert.Equal(6, vm.Steps.Count);
        Assert.Empty(vm.Workspace.Diagnostics());
    }

    [Fact]
    public async Task F5UsesRunCoordinatorActualEditedDocumentAndShowsMockFacts()
    {
        var vm = Create(); vm.AddExampleCommand.Execute();
        var report = await vm.RunCurrentDocumentAsync();
        Assert.NotNull(report);
        Assert.Equal(MockStepVerdict.Pass, report!.Verdict);
        Assert.Equal(2, report.Facts.Count(f => f.Type == "Assert"));
        Assert.Contains(vm.FactLines, s => s.Contains("[MOCK/Simulated]") && s.Contains("期待="));
        Assert.Equal("NO_HARDWARE_ACK", vm.AckStatus);
        Assert.Contains("NOT_PRODUCTION", vm.Verdict);
        Assert.Contains(vm.Steps, s => s.RunBadge.Contains("Mock: Pass"));
        vm.SelectedStep = vm.Steps.Single(s => s.NodeType == "Assert");
        vm.EditedExpected = "9"; vm.ApplyPropertiesCommand.Execute();
        var failed = await vm.RunCurrentDocumentAsync();
        Assert.Equal(MockStepVerdict.Fail, failed!.Verdict);
    }

    [Fact]
    public async Task VmProjectDbcTestPersistAndReopen()
    {
        var vm = Create();
        vm.Workspace.ImportDbc("BU_: ECU\nBO_ 1 FRAME: 1 ECU\n SG_ Value : 0|8@1+ (1,0) [0|255] \"\" ECU");
        vm.AddExampleCommand.Execute();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "vm.autotest-project.json");
        try
        {
            await vm.SaveProjectAsync(path);
            Assert.True(File.Exists(path));
            var other = Create(); await other.OpenProjectAsync(path);
            Assert.Equal(vm.Workspace.Document.Id, other.Workspace.Document.Id);
            Assert.Equal(vm.Workspace.Project.Name, other.Workspace.Project.Name);
            Assert.Single(other.Workspace.Database!.Messages);
            Assert.Equal(MockStepVerdict.Pass, (await other.RunCurrentDocumentAsync())!.Verdict);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
    [Fact]
    public async Task R1EditorCanCreateAndExecuteSwitchCaseMockProgram()
    {
        var vm = Create();
        vm.NewStepPhase = "Prepare";
        vm.NewStepType = "SetVariable";
        vm.InsertRootCommand.Execute();
        vm.NewStepPhase = "Test";
        vm.NewStepType = "Switch";
        vm.InsertRootCommand.Execute();
        Assert.Equal("Switch", vm.SelectedStep!.NodeType);
        vm.NewStepType = "Case";
        vm.InsertChildCommand.Execute();
        Assert.Equal("Case", vm.SelectedStep!.NodeType);
        vm.NewStepType = "MockObserve";
        vm.InsertChildCommand.Execute();
        vm.NewStepType = "Assert";
        vm.InsertAfterCommand.Execute();

        Assert.Empty(vm.Workspace.Diagnostics());
        var result = await vm.RunCurrentDocumentAsync();
        Assert.NotNull(result);
        Assert.Equal(MockStepVerdict.Pass, result!.Verdict);
        Assert.Contains(result.Facts, x => x.Type == "Switch");
        Assert.Contains(result.Facts, x => x.Type == "Case");
        Assert.Contains(result.Facts, x => x.Type == "Assert");
        Assert.Contains("MOCK", vm.RequestStatus);
    }

    [Fact]
    public void R1EditorRejectsStandaloneCaseNodes()
    {
        var vm = Create();
        vm.NewStepType = "Case";
        vm.NewStepPhase = "Test";
        vm.InsertRootCommand.Execute();
        Assert.Empty(vm.Workspace.Document.Walk());
        Assert.Contains("Case 只能插入 Switch", vm.Diagnostic);
    }

}
