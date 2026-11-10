using AutoTest.Desktop.Tools.Flash;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Flash;

public sealed class FlashToolViewModelTests
{
    [Fact]
    public async Task FirmwareImageParsesWithoutProgrammingOrCreatingFakeResult()
    {
        var vm = new FlashToolViewModel();
        Assert.False(vm.PhysicalProgrammingEnabled);
        vm.LoadBytes("test.bin", Enumerable.Range(0, 180).Select(i => (byte)i).ToArray(), 0x1000);
        Assert.Equal("180", vm.Bytes);
        Assert.Equal("BIN", vm.Format);
        Assert.Single(vm.Segments);
        Assert.Equal("IMAGE_PARSED_NOT_FLASHED", vm.Status);

        await vm.PreflightAsync();
        Assert.Equal("BLOCKED", vm.Preflight);
        Assert.Contains("CANTOOL_CONFIG_REQUIRED", vm.Failure);
        Assert.False(vm.PhysicalProgrammingEnabled);
    }

    [Fact]
    public void InvalidImageNeverReplacesExistingValidatedImage()
    {
        var vm = new FlashToolViewModel();
        vm.LoadBytes("good.bin", new byte[] { 1, 2, 3 }, 0x1000);
        var oldHash = vm.Hash;
        Assert.ThrowsAny<FormatException>(() => vm.LoadBytes(
            "bad.hex", System.Text.Encoding.UTF8.GetBytes(":00000001FE"), 0));
        Assert.Equal(oldHash, vm.Hash);
        Assert.Equal("3", vm.Bytes);
    }

    [Fact]
    public void HardwarePreflightCannotGrantProgrammingPermission()
    {
        var vm = new FlashToolViewModel();
        vm.LoadBytes("test.bin", new byte[] { 1, 2, 3 }, 0x1000);
        vm.EvaluatePhysicalPreflight(null);
        Assert.Equal("PHYSICAL_WRITE_BLOCKED", vm.Preflight);
        Assert.False(vm.PhysicalProgrammingEnabled);
        Assert.Contains(vm.PhysicalChecks, x => x.Category == "PHYSICAL_WRITE" && !x.Passed);
    }
}
