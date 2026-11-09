using System;
using System.IO;
using System.Linq;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Specialists.Scan.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Production;

public sealed class ScannerR0ViewModelTests
{
    private static LabProductionToolViewModel NewVm() => new(
        new LabCatalogStore(Path.Combine(Path.GetTempPath(), "scanner-r0-" + Guid.NewGuid().ToString("N"))),
        new LabProductionOrchestrator(new LabDeterministicMockRunner(), new LabMemoryLedger()));

    [Fact]
    public void FocusedKeyboardEnterProducesLinkedDiagnosticAndOnlyPreview()
    {
        var vm = NewVm();
        vm.ScannerFieldFocused();
        vm.ScannerTextChanged("M000001", focused: true);
        vm.PreviewScannerEnter("M000001", focused: true);
        var capture = Assert.Single(vm.DiagnosticEvents.Where(x => x.Stage == "ScannerAcquisition"));
        Assert.Equal("SCAN_CAPTURED", capture.Code);
        Assert.NotNull(capture.CaptureAttemptId);
        Assert.Contains("唯一匹配", vm.MatchDetail);
        Assert.Equal(0, vm.MockRunCount);
        Assert.Equal(0, vm.FactoryReleasedCount);
        Assert.Contains("未区分", vm.ScannerEvidence);
        vm.ScannerOnly = true;
        Assert.All(vm.DiagnosticEvents, x => Assert.Equal("ScannerAcquisition", x.Stage));
    }

    [Fact]
    public void PressingEnterWithoutEditDoesNotReusePreviousIdentity()
    {
        var vm = NewVm();
        vm.ScannerFieldFocused();
        vm.PreviewScannerEnter("M000001", focused: true);
        Assert.Contains("SCAN_NO_NEW_INPUT", vm.Status);
        Assert.Equal(0, vm.MockRunCount);
        vm.ScannerTextChanged("M000001", focused: true);
        vm.PreviewScannerEnter("M000001", focused: true);
        Assert.Contains("唯一匹配", vm.MatchDetail);
        vm.PreviewScannerEnter("M000001", focused: true);
        Assert.Contains("SCAN_NO_NEW_INPUT", vm.MatchDetail);
        Assert.Equal(0, vm.FactoryReleasedCount);
    }
}
