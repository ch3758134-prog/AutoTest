using AutoTest.Desktop.Tools.ControllerConfig;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.ControllerConfig;

public sealed class R0ControllerDiagnosticTests
{
    [Fact] public void EditWithoutProfile_HasTraceAndExplicitSource()
    {
        var vm = new ControllerConfigToolViewModel();
        vm.AddStateCommand.Execute(null);
        Assert.Contains(vm.TraceEvents, e => e.Outcome == "REJECTED" && e.Stage == "EDIT");
        Assert.Contains("未绑定", vm.ProjectBindingSummary);
        Assert.Contains("SOFTWARE_ONLY", vm.TraceQuality);
    }
}
