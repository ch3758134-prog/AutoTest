using AutoTest.Desktop.Tools.Dbc;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Dbc;

public sealed class DbcToolViewModelTests
{
    [Fact] public void InitialState_DoesNotClaimRealDecoder()
    {
        var vm = new DbcToolViewModel();
        Assert.Equal(0, vm.Revision);
        Assert.Contains("Unknown", vm.Status);
        Assert.Empty(vm.Decoded);
    }
    [Fact] public void WithoutDocument_EditReportsErrorRatherThanPretendingSuccess()
    {
        var vm = new DbcToolViewModel();
        vm.AddMessageCommand.Execute(null);
        Assert.NotEmpty(vm.Error);
        Assert.Empty(vm.Messages);
    }
}
