using System;
using System.IO;
using System.Threading.Tasks;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Specialists.Scan.Lab;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Production;

public sealed class LabProductionToolViewModelTests
{
    [Fact]
    public void ViewModelCanPreviewUniqueMockScan()
    {
        var vm = NewVm();
        vm.ScanInput = "M000001";
        vm.PreviewScan();
        Assert.Contains("唯一匹配", vm.MatchDetail);
        Assert.Contains("MOCK", vm.Status);
    }

    [Fact]
    public async Task ViewModelDoesNotCountMockPassAsFactoryRelease()
    {
        var vm = NewVm();
        await vm.RunMockAsync();
        Assert.Equal(1, vm.MockRunCount);
        Assert.Equal(1, vm.DistinctMockUnits);
        Assert.Equal(0, vm.FactoryReleasedCount);
        Assert.Contains("出厂放行仍禁止", vm.Status);
    }

    [Fact]
    public async Task ViewModelBlocksDuplicateUntilRetestConfirmation()
    {
        var vm = NewVm();
        await vm.RunMockAsync();
        await vm.RunMockAsync();
        Assert.Equal(1, vm.MockRunCount);
        Assert.Contains("RETEST_CONFIRMATION_REQUIRED", vm.Status);
        vm.AllowRetest = true;
        await vm.RunMockAsync();
        Assert.Equal(2, vm.MockRunCount);
        Assert.Equal(1, vm.DistinctMockUnits);
    }

    [Fact]
    public async Task RuleDraftIsNotActiveUntilPublished()
    {
        var storePath = Path.Combine(Path.GetTempPath(), "labvm-" + Guid.NewGuid().ToString("N"));
        var vm = new LabProductionToolViewModel(new LabCatalogStore(storePath),
            new LabProductionOrchestrator(new LabDeterministicMockRunner(), new LabMemoryLedger()));
        try
        {
            var old = vm.RuleRevision;
            vm.Rules[0].Pattern = @"(?<serial>Z[0-9]{6})";
            await vm.SaveDraftAsync();
            Assert.Equal(old, vm.RuleRevision);
            vm.ScanInput = "M000001";
            vm.PreviewScan();
            Assert.Contains("唯一匹配", vm.MatchDetail);
            vm.Samples.Clear(); // Old sample expected M-code and would block publication.
            await vm.PublishAsync();
            Assert.NotEqual(old, vm.RuleRevision);
            vm.PreviewScan();
            Assert.Contains("IDENTITY_NO_MATCH", vm.MatchDetail);
        }
        finally
        {
            foreach (var suffix in new[] { ".published.json", ".draft.json" })
                if (File.Exists(storePath + suffix)) File.Delete(storePath + suffix);
        }
    }

    [Fact]
    public async Task ViewModelDisconnectNeverClaimsPass()
    {
        var vm = NewVm();
        vm.SelectedScenario = LabMockScenario.Disconnect;
        await vm.RunMockAsync();
        Assert.Equal(1, vm.MockRunCount);
        Assert.Equal(0, vm.FactoryReleasedCount);
        Assert.Contains("MockFail", vm.LastRun);
    }

    private static LabProductionToolViewModel NewVm() =>
        new(new LabCatalogStore(Path.Combine(Path.GetTempPath(), "unused-" + Guid.NewGuid().ToString("N"))),
            new LabProductionOrchestrator(new LabDeterministicMockRunner(), new LabMemoryLedger()));
}
