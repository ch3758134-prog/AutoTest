using AutoTest.Desktop.Tools.Uds;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Uds;

public sealed class UdsToolViewModelTests
{
    [Fact]
    public async Task ReadDidWithoutPhysicalConnectionNeverGeneratesFakePositiveResponse()
    {
        var vm = new UdsToolViewModel();
        Assert.Equal("Physical Read", vm.SourceMode);
        Assert.False(vm.PhysicalTxEnabled);
        Assert.DoesNotContain("Mock", vm.SourceModes);
        await vm.SendAsync();
        Assert.Equal("WORKBENCH_ERROR", vm.Status);
        Assert.Contains("P02", vm.Error);
        Assert.Equal("—", vm.ResponseHex);
        Assert.Empty(vm.TraceRows);
    }

    [Fact]
    public async Task NonReadonlyRequestNeverUsesSimulatedEcu()
    {
        var vm = new UdsToolViewModel { RequestHex = "2E F1 90 00" };
        await vm.SendAsync();
        Assert.Equal("WORKBENCH_ERROR", vm.Status);
        Assert.Contains("只允许", vm.Error);
        Assert.Empty(vm.TraceRows);
    }

    [Fact]
    public void CanEditAndDeleteSavedServices()
    {
        var vm = new UdsToolViewModel { ServiceName = "New DID", RequestHex = "22 12 34" };
        int before = vm.Services.Count;
        vm.SaveService();
        Assert.Equal(before + 1, vm.Services.Count);
        vm.SelectedService = Assert.Single(vm.Services, x => x.Name == "New DID");
        vm.DeleteService();
        Assert.Equal(before, vm.Services.Count);
    }
}
