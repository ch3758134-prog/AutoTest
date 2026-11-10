using System;
using System.IO;
using System.Threading.Tasks;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Specialists.Scan.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Production;

public sealed class R1ProductionScannerViewModelTests
{
    [Fact]
    public async Task StartupWithoutCatalogDoesNotInjectDemoIdentityOrRecipe()
    {
        var file = Path.Combine(Path.GetTempPath(), "r1-real-scan-" + Guid.NewGuid().ToString("N"));
        var vm = new R1ProductionScannerViewModel(new LabCatalogStore(file));
        await vm.LoadPublishedAsync();
        Assert.Equal("—", vm.RuleRevision);
        Assert.Contains("缺少有效", vm.CatalogSource);
        Assert.Empty(vm.ScanEvents);

        vm.AcceptSerialBarcode("M000001", "Physical COM5");
        Assert.Contains("真实条码已收到", vm.MatchDetail);
        Assert.Equal("CATALOG_NOT_READY", Assert.Single(vm.ScanEvents).Result);
        Assert.DoesNotContain("MOCK PASS", vm.Status);
    }

    [Fact]
    public async Task RealInputIsRecordedWithoutCreatingFactoryRun()
    {
        var file = Path.Combine(Path.GetTempPath(), "r1-real-scan-" + Guid.NewGuid().ToString("N"));
        var vm = new R1ProductionScannerViewModel(new LabCatalogStore(file));
        await vm.LoadPublishedAsync();
        vm.AcceptSerialBarcode("SN00000042", "USB COM · COM7");
        var received = Assert.Single(vm.ScanEvents);
        Assert.Equal("SN00000042", received.Input);
        Assert.Contains("COM7", received.Origin);
        Assert.Contains("没有启动自动测试", vm.Status);
    }

    [Fact]
    public async Task InvalidBarcodeIsRejectedWithoutDemoFallback()
    {
        var file = Path.Combine(Path.GetTempPath(), "r1-real-scan-" + Guid.NewGuid().ToString("N"));
        var vm = new R1ProductionScannerViewModel(new LabCatalogStore(file));
        await vm.LoadPublishedAsync();
        vm.AcceptSerialBarcode("", "USB COM · COM7");
        Assert.NotEqual("CATALOG_NOT_READY", Assert.Single(vm.ScanEvents).Result);
        Assert.Contains("没有执行测试", vm.Status);
    }
}
