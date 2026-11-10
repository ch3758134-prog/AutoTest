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
}
