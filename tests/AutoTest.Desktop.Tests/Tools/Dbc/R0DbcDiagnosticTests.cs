using AutoTest.Desktop.Tools.Dbc;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Dbc;

public sealed class R0DbcDiagnosticTests
{
    [Fact] public void UnloadedEdit_ProducesUserVisibleStructuredError()
    {
        var vm = new DbcToolViewModel();
        vm.AddNodeCommand.Execute(null);
        Assert.Equal(0, vm.Revision);
        Assert.Contains(vm.TraceEvents, e => e.Stage == "EDIT" && e.Outcome == "REJECTED" && e.ErrorCode.Length > 0);
        Assert.Contains("未绑定", vm.ProjectBindingSummary);
    }
    [Fact] public void TraceFilter_IsCaseInsensitive()
    {
        var vm = new DbcToolViewModel();
        vm.AddNodeCommand.Execute(null);
        vm.TraceFilter = "edit";
        Assert.Single(vm.VisibleTraceEvents);
        vm.TraceFilter = "missing-no-such-entry";
        Assert.Empty(vm.VisibleTraceEvents);
    }
}
