using AutoTest.Desktop.Tools.Uds;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Uds;

public sealed class UdsToolViewModelTests
{
    [Fact]
    public async Task PresetCanExecuteAndDisplaysMockPositiveResponse()
    {
        var vm=new UdsToolViewModel();
        Assert.False(vm.PhysicalTxEnabled);
        Assert.True(vm.SendCommand.CanExecute());
        await vm.SendAsync();
        Assert.Equal("MOCK_ACCEPTED",vm.Status);
        Assert.StartsWith("62 F1 90",vm.ResponseHex);
        Assert.NotEmpty(vm.TraceRows);
    }

    [Fact]
    public async Task UnknownDidDoesNotAppearAsSuccessfulRead()
    {
        var vm=new UdsToolViewModel { RequestHex="22 AA BB" };
        await vm.SendAsync();
        Assert.Equal("MOCK_REJECTED",vm.Status);
        Assert.Equal("0x31",vm.Nrc);
    }

    [Fact]
    public void CanEditAndDeleteSavedServices()
    {
        var vm=new UdsToolViewModel { ServiceName="New DID",RequestHex="22 12 34" };
        int before=vm.Services.Count;
        vm.SaveService();
        Assert.Equal(before+1,vm.Services.Count);
        vm.SelectedService=Assert.Single(vm.Services, x => x.Name == "New DID");
        vm.DeleteService();
        Assert.Equal(before,vm.Services.Count);
    }
}
