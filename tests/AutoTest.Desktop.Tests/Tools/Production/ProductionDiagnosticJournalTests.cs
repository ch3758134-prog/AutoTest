using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Specialists.Scan.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Production;

public sealed class ProductionDiagnosticJournalTests
{
    [Fact]
    public async Task JsonLinesExportRedactsBarcodeAndCarriesEvidenceFields()
    {
        var file = Path.Combine(Path.GetTempPath(), "prod-trace-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var journal = new ProductionDiagnosticJournal();
        var runId = Guid.NewGuid();
        journal.Append("Scan", "Info", "IDENTITY_MATCHED", "唯一匹配；非物理",
            "Mock/ProductionUI", "catalog-v1", "recipe-v2", runId, "S000042");
        try
        {
            await journal.ExportJsonLinesAsync(file);
            var contents = await File.ReadAllTextAsync(file);
            Assert.Contains("UI_ONLY_NOT_PHYSICAL_NOT_FACTORY_RELEASE", contents);
            Assert.Contains("IDENTITY_MATCHED", contents);
            Assert.Contains("catalog-v1", contents);
            Assert.Contains(runId.ToString(), contents);
            Assert.Contains("[REDACTED]", contents);
            Assert.Contains("UNKNOWN_RUNTIME_REVISION_NOT_INJECTED", contents);
            Assert.Contains("IN_MEMORY_UI_EVENTS_ONLY", contents);
            Assert.DoesNotContain("S000042", contents);
            Assert.Equal(2, File.ReadLines(file).Count());
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public async Task ExplicitLocalExportOptInCanIncludeBarcodeButClipboardRemainsRedacted()
    {
        var file = Path.Combine(Path.GetTempPath(), "prod-local-sensitive-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var journal = new ProductionDiagnosticJournal();
        journal.Append("ScanCapture", "Info", "SCAN_CAPTURED", "专用扫码框收到输入",
            rawBarcode: "M000001");
        try
        {
            Assert.DoesNotContain("M000001", journal.CopyText());
            await journal.ExportJsonLinesAsync(file, includeRawBarcode: true);
            var contents = await File.ReadAllTextAsync(file);
            Assert.Contains("M000001", contents);
            Assert.Contains("\"RawBarcodeIncluded\":true", contents);
            Assert.Contains("UI_ONLY_NOT_PHYSICAL_NOT_FACTORY_RELEASE", contents);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void BoundedJournalReportsLostEventsRatherThanClaimingCompleteTrace()
    {
        var journal = new ProductionDiagnosticJournal(capacity: 2);
        journal.Append("Scan", "Info", "FIRST", "test");
        journal.Append("Scan", "Warning", "SECOND", "test");
        journal.Append("Scan", "Error", "THIRD", "test");
        Assert.Equal(1, journal.DroppedCount);
        Assert.Equal(new[] { "SECOND", "THIRD" }, journal.Snapshot().Select(x => x.Code));
        Assert.Equal(new[] { "SECOND", "THIRD" }, journal.Snapshot(errorsOnly: true).Select(x => x.Code));
    }

    [Fact]
    public async Task ViewModelExportsFailedPreflightAsStructuredEvidence()
    {
        var vm = new LabProductionToolViewModel(
            new LabCatalogStore(Path.Combine(Path.GetTempPath(), "unused-" + Guid.NewGuid().ToString("N"))),
            new LabProductionOrchestrator(new LabDeterministicMockRunner(), new LabMemoryLedger()));
        vm.ScanInput = "M000001";
        await vm.RunMockAsync();
        await vm.RunMockAsync(); // Retest not authorized, blocked before run.
        Assert.Contains(vm.DiagnosticEvents, x => x.Code == "RETEST_CONFIRMATION_REQUIRED");
        Assert.Equal(1, vm.MockRunCount);
        Assert.Equal(0, vm.FactoryReleasedCount);
        var file = Path.Combine(Path.GetTempPath(), "vm-trace-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            await vm.ExportDiagnosticsAsync(file);
            Assert.Contains("RETEST_CONFIRMATION_REQUIRED", await File.ReadAllTextAsync(file));
            Assert.DoesNotContain("M000001", await File.ReadAllTextAsync(file));
            Assert.True(File.Exists(file));
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public async Task ViewModelRetainsRunIdAndMockOnlyStatusInTrace()
    {
        var vm = new LabProductionToolViewModel(
            new LabCatalogStore(Path.Combine(Path.GetTempPath(), "unused-" + Guid.NewGuid().ToString("N"))),
            new LabProductionOrchestrator(new LabDeterministicMockRunner(), new LabMemoryLedger()));
        await vm.RunMockAsync();
        Assert.Contains(vm.DiagnosticEvents, x => x.Code == "MOCK_STEP_VERDICT_PASS" &&
                                                x.RunId.HasValue);
        Assert.Equal(0, vm.FactoryReleasedCount);
        Assert.Contains("MOCK", vm.Status);
    }

    [Fact]
    public async Task RawBarcodeIsRedactedEvenIfAccidentallyPresentInSummary()
    {
        var journal = new ProductionDiagnosticJournal();
        journal.Append("ScannerAcquisition", "Warning", "SCAN_REJECTED",
            "Rejected sensitive M000003", rawBarcode: "M000003", captureId: Guid.NewGuid());
        journal.Append("Run", "Info", "NON_SCANNER", "unrelated");
        var file = Path.Combine(Path.GetTempPath(), "scanner-redact-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            await journal.ExportJsonLinesAsync(file);
            var value = await File.ReadAllTextAsync(file);
            Assert.DoesNotContain("M000003", value);
            Assert.DoesNotContain("M000003", journal.CopyText());
            Assert.Single(journal.Snapshot(stage: "ScannerAcquisition"));
            Assert.Contains("CaptureAttemptId", value);
            Assert.DoesNotContain("NON_SCANNER", journal.CopyText(stage: "ScannerAcquisition"));
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
