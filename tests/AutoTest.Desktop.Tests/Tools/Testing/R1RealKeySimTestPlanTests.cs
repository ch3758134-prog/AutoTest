using AutoTest.Desktop.Tools.Testing;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Testing.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Testing;

public sealed class R1RealKeySimTestPlanTests
{
    private static (EcuControlProfile Profile, Guid Controller, Guid Active, Guid Release) Profile()
    {
        var editor = new ControllerEditSession(
            EcuControlProfile.Create("EPB ECU", "actual KEY_SIM", Guid.NewGuid()));
        editor.AddController("Key resistor-network");
        var controller = editor.Current.Controllers.Single();
        editor.AddState(controller.Id, "Apply first key", 1);
        var actual = editor.Current.Controllers.Single();
        return (editor.Current, actual.Id,
            actual.States.Single(x => x.KeyMask == 1).Id,
            actual.States.Single(x => x.KeyMask == 0).Id);
    }

    private static LabNode State(Guid controller, Guid state, int revision)
    {
        var n = LabNode.New("ControllerState", "Real CAN0 KEY_SIM");
        n.Parameters["controllerId"] = controller.ToString("D");
        n.Parameters["stateId"] = state.ToString("D");
        n.Parameters["revision"] = revision.ToString();
        return n;
    }

    [Fact]
    public void SingleRealControllerCatalogResolvesOnlyCurrentRevisionAndStates()
    {
        var data = Profile();
        var catalog = new R1KeySimControllerCatalog(() => data.Profile);
        var valid = new LabControllerRef(data.Controller.ToString("D"),
            data.Active.ToString("D"), data.Profile.Revision.ToString());
        Assert.True(catalog.Resolve(valid).Found);
        Assert.False(catalog.Resolve(valid with
        { Revision = (data.Profile.Revision - 1).ToString() }).Found);
        Assert.False(catalog.Resolve(valid with { StateId = Guid.NewGuid().ToString("D") }).Found);
        Assert.False(new R1KeySimControllerCatalog(() => null).Resolve(valid).Found);
    }

    [Fact]
    public void ProgramCompilerPinsActualControllerAndReleaseWithoutMockSteps()
    {
        var data = Profile();
        var doc = LabDocument.New("EPB physical KEY_SIM sequence");
        doc.Phases[LabPhase.Test].Add(State(data.Controller, data.Active, data.Profile.Revision));
        doc.Phases[LabPhase.Cleanup].Add(
            State(data.Controller, data.Release, data.Profile.Revision));
        var prepared = new LabPlanCompiler(
            new R1KeySimControllerCatalog(() => data.Profile)).Prepare(doc);
        Assert.True(prepared.Ready, string.Join(" | ", prepared.Diagnostics));
        Assert.Equal(2, prepared.ControllerBindings.Length);
        Assert.DoesNotContain(doc.Walk(), x => x.Type == "MockObserve");
    }

    [Fact]
    public async Task UnconnectedControllerNeverAuthorizesPhysicalKeymaskExecution()
    {
        var data = Profile();
        var workspace = new AutoTest.Application.V1.V1Workspace();
        await using var controller =
            new AutoTest.Devices.Diagnostics.HardwareBenchController();
        var refId = new LabControllerRef(data.Controller.ToString("D"),
            data.Active.ToString("D"), data.Profile.Revision.ToString());
        Assert.ThrowsAny<Exception>(() => new R1PhysicalKeySimLabStation(
            workspace, controller, data.Profile, () => true,
            new[] { refId }, operatorApproved: true));
    }
    [Fact]
    public void FormalTestEditorBindsP05ConfiguredStateWithoutManualGuidEntry()
    {
        var data = Profile();
        var project = new AutoTest.Application.V1.V1Workspace();
        var vm = new LabTestWorkspaceViewModel(project);
        vm.AttachPhysicalController(() => data.Profile, () => null, _ => false);
        Assert.NotEmpty(vm.ControllerStateChoices);
        var chosen = vm.ControllerStateChoices.Single(x => x.StateId == data.Active);
        vm.SelectedInsertType = "ControllerState";
        vm.InsertCommand.Execute(null);
        var node = vm.Tree.Single(x => x.Phase == LabPhase.Test).Children.Single();
        vm.Select(node);
        vm.SelectedControllerChoice = chosen;
        vm.ApplyControllerState();
        var applied = Assert.Single(vm.Snapshot.Phases[LabPhase.Test]);
        Assert.Equal("ControllerState", applied.Type);
        Assert.Equal(data.Controller.ToString("D"), applied.Parameters["controllerId"]);
        Assert.Equal(data.Active.ToString("D"), applied.Parameters["stateId"]);
        Assert.Equal(data.Profile.Revision.ToString(), applied.Parameters["revision"]);
        Assert.Contains("已绑定", vm.Status);
    }

    [Fact]
    public void EditorRejectsStateChoiceAfterP05RevisionHasChanged()
    {
        var data = Profile();
        var current = data.Profile;
        var vm = new LabTestWorkspaceViewModel(new AutoTest.Application.V1.V1Workspace());
        vm.AttachPhysicalController(() => current, () => null, _ => false);
        var state = vm.ControllerStateChoices.Single(x => x.StateId == data.Active);
        vm.SelectedInsertType = "ControllerState";
        vm.InsertCommand.Execute(null);
        vm.Select(vm.Tree.Single(x => x.Phase == LabPhase.Test).Children.Single());
        vm.SelectedControllerChoice = state;
        current = current with { Revision = current.Revision + 1 };
        vm.ApplyControllerState();
        Assert.Contains("状态已经变化", vm.Status);
        Assert.Equal("", vm.Snapshot.Phases[LabPhase.Test][0].Parameters["controllerId"]);
    }

    [Fact]
    public void CleanupMustBeginWithUnconditionalPhysicalAllKeyRelease()
    {
        var data = Profile();
        var bindings = new[] { new LabControllerRef(
            data.Controller.ToString("D"), data.Active.ToString("D"),
            data.Profile.Revision.ToString()) };
        var doc = LabDocument.New("Real first release policy");
        doc.Phases[LabPhase.Test].Add(
            State(data.Controller, data.Active, data.Profile.Revision));
        var conditional = LabNode.New("If");
        conditional.Parameters["variable"] = "mode";
        conditional.Parameters["equals"] = "ready";
        conditional.Slots.Add("then", new List<LabNode> {
            State(data.Controller, data.Release, data.Profile.Revision)
        });
        conditional.Slots.Add("else", new List<LabNode>());
        doc.Phases[LabPhase.Cleanup].Add(conditional);
        Assert.Contains("KEYSIM_CLEANUP_FIRST_RELEASE_REQUIRED",
            Assert.Throws<InvalidOperationException>(() =>
                R1KeySimCleanupPolicy.RequireUnconditionalRelease(
                    doc, data.Profile, bindings)).Message);
        doc.Phases[LabPhase.Cleanup].Insert(0,
            State(data.Controller, data.Release, data.Profile.Revision));
        R1KeySimCleanupPolicy.RequireUnconditionalRelease(doc, data.Profile, bindings);
    }

    [Fact]
    public void CleanupCannotReactivateAnyKeyAfterRelease()
    {
        var data = Profile();
        var binding = new LabControllerRef(data.Controller.ToString("D"),
            data.Active.ToString("D"), data.Profile.Revision.ToString());
        var doc = LabDocument.New("No reactivation");
        doc.Phases[LabPhase.Test].Add(
            State(data.Controller, data.Active, data.Profile.Revision));
        doc.Phases[LabPhase.Cleanup].Add(
            State(data.Controller, data.Release, data.Profile.Revision));
        var nested = LabNode.New("Sequence");
        nested.Slots.Add("body", new List<LabNode>
        {
            State(data.Controller, data.Active, data.Profile.Revision)
        });
        doc.Phases[LabPhase.Cleanup].Add(nested);
        Assert.Contains("KEYSIM_CLEANUP_REACTIVATION_FORBIDDEN",
            Assert.Throws<InvalidOperationException>(() =>
                R1KeySimCleanupPolicy.RequireUnconditionalRelease(
                    doc, data.Profile, new[] { binding })).Message);
    }

    [Fact]
    public void ReleaseOnlyProgramDoesNotRequireRedundantSecondRelease()
    {
        var data = Profile();
        var binding = new LabControllerRef(data.Controller.ToString("D"),
            data.Release.ToString("D"), data.Profile.Revision.ToString());
        var doc = LabDocument.New("Release only");
        doc.Phases[LabPhase.Test].Add(
            State(data.Controller, data.Release, data.Profile.Revision));
        R1KeySimCleanupPolicy.RequireUnconditionalRelease(
            doc, data.Profile, new[] { binding });
    }

}
