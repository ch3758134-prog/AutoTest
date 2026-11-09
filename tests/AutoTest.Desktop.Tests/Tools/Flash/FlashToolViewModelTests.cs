using AutoTest.Desktop.Tools.Flash;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Flash;

public sealed class FlashToolViewModelTests
{
    [Fact]
    public async Task LoadedImagePreflightAndDryRunAreUsableWithoutHardware()
    {
        var vm=new FlashToolViewModel();
        Assert.False(vm.PhysicalProgrammingEnabled);
        vm.LoadBytes("test.bin",Enumerable.Range(0,180).Select(i=>(byte)i).ToArray(),0x1000);
        Assert.Equal("180",vm.Bytes);
        await vm.PreflightAsync();
        Assert.Equal("MOCK_READY",vm.Preflight);
        await vm.RunDryAsync();
        Assert.Equal("MOCK_COMPLETED",vm.Status);
        Assert.NotEmpty(vm.Stages);
    }

    [Fact]
    public async Task WrongIdentityRejectsAndPreventsRun()
    {
        var vm=new FlashToolViewModel();
        vm.LoadBytes("test.bin",new byte[]{1,2,3},0x1000);
        vm.ObservedIdentity="OTHER_ECU";
        await vm.RunDryAsync();
        Assert.Equal("PREFLIGHT_BLOCKED",vm.Status);
        Assert.Contains("ECU_IDENTITY_MISMATCH",vm.Failure);
    }
}
