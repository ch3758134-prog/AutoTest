using AutoTest.Desktop.Tools.ControllerConfig;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.ControllerConfig;

public sealed class ControllerConfigToolViewModelTests
{
    [Fact] public void EmptyView_IsUnknownNotPhysicalPass()
    {
        var vm = new ControllerConfigToolViewModel();
        Assert.Equal(0, vm.CurrentRevision);
        Assert.Contains("Unknown", vm.Status);
        Assert.Empty(vm.Controllers);
    }
    [Fact] public void CreateThenAddController_HasEditableDefaultState()
    {
        var vm = new ControllerConfigToolViewModel
        {
            EcuName = "ECU-1", BoardModel = "KEY-SIM", HardwareProfileId = Guid.NewGuid().ToString(),
            ControllerName = "CUSTOM"
        };
        vm.CreateCommand.Execute(null);
        vm.AddControllerCommand.Execute(null);
        Assert.Single(vm.Controllers);
        Assert.Single(vm.States);
        Assert.True(vm.CurrentRevision > 0);
        Assert.DoesNotContain("失败", vm.Status);
    }
}
