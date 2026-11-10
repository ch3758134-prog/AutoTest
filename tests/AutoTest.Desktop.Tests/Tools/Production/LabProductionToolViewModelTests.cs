using System;
using System.IO;
using System.Threading.Tasks;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Desktop.Tools.Testing;
using AutoTest.Application.V1;
using AutoTest.Testing.Lab;
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
        Assert.Contains("缺少已发布", vm.CatalogSource);
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
    [Fact]
    public async Task PublishedPhysicalRuleBindsRealScannerToExactSavedProjectTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "r1-scan-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new V1Workspace();
            project.ImportDbc("BU_: ECU\nBO_ 1 FRAME: 1 ECU\n SG_ Value : 0|8@1+ (1,0) [0|255] \"V\" ECU\n");
            var doc = LabDocument.New("Real project program");
            project.ImportLabProgram(LabDocumentCodec.Save(doc));
            await project.SaveProjectAsync(Path.Combine(root, "real.autotest-project.json"));
            var profile = Path.Combine(root, "actual-station.json");
            await File.WriteAllTextAsync(profile, "{\"station\":\"configured-by-user\"}");
            var vm = new R1ProductionScannerViewModel(new LabCatalogStore(
                Path.Combine(root, "catalog")));
            vm.AttachProject(project);
            vm.RulePattern = @"(?<serial>SN[0-9]{6})";
            vm.IdentityFields = "serial";
            vm.RecipeId = "Actual-EOL";
            vm.StationProfileFile = profile;
            vm.StationProfileRevision = "7";
            vm.ExampleBarcode = "SN000042";
            await vm.PublishCurrentProjectRuleAsync();
            Assert.NotEqual("—", vm.RuleRevision);
            R1PhysicalScanBinding? accepted = null;
            var callbacks = 0;
            vm.PhysicalScanMatched += item => { accepted = item; callbacks++; };
            vm.ScanInput = "SN000042";
            vm.PreviewScan();
            Assert.Equal(0, callbacks); // preview never executes physical test
            vm.AcceptSerialBarcode("SN000042", "USB COM · COM7");
            Assert.Equal(1, callbacks);
            Assert.NotNull(accepted);
            Assert.Equal("SN000042", accepted!.RawBarcode);
            Assert.Equal(project.Assets.Single(a => a.Kind == "test-program").Sha256,
                accepted.TestSha256);
            accepted.Verify(project);
            Assert.True(vm.IsRunning);
            vm.AcceptSerialBarcode("SN000043", "USB COM · COM7");
            Assert.Equal("STATION_BUSY", vm.ScanEvents[0].Result);
            Assert.Equal(1, callbacks);
            vm.MarkRunCompleted(accepted, Guid.NewGuid(), "Diagnostic Pass", "real-evidence.json");
            Assert.False(vm.IsRunning);
            Assert.Contains("real-evidence.json", vm.LastEvidence);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task StalePublishedRecipeCannotStartPhysicalTestAfterProgramRevisionChange()
    {
        var root = Path.Combine(Path.GetTempPath(), "r1-scan-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new V1Workspace();
            project.ImportDbc("BU_: ECU\nBO_ 1 FRAME: 1 ECU\n SG_ Value : 0|8@1+ (1,0) [0|255] \"V\" ECU\n");
            var doc = LabDocument.New("Actual program");
            var first = project.ImportLabProgram(LabDocumentCodec.Save(doc));
            await project.SaveProjectAsync(Path.Combine(root, "test.autotest-project.json"));
            var profile = Path.Combine(root, "profile.json");
            await File.WriteAllTextAsync(profile, "{\"station\":1}");
            var vm = new R1ProductionScannerViewModel(new LabCatalogStore(Path.Combine(root, "catalog")));
            vm.AttachProject(project);
            vm.RulePattern = @"(?<serial>SN[0-9]{6})";
            vm.IdentityFields = "serial";
            vm.RecipeId = "Actual";
            vm.StationProfileFile = profile;
            vm.StationProfileRevision = "1";
            vm.ExampleBarcode = "SN000042";
            await vm.PublishCurrentProjectRuleAsync();
            var changed = doc.Copy();
            changed.Phases[LabPhase.Prepare].Add(LabNode.New("Log", "Revision changed") with
            {
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                { ["message"] = "real change" }
            });
            project.ReplaceLabProgram(LabDocumentCodec.Save(changed), first.ToReference());
            await project.SaveProjectAsync(project.ProjectPath!);
            var callbacks = 0;
            vm.PhysicalScanMatched += _ => callbacks++;
            vm.AcceptSerialBarcode("SN000042", "USB COM · COM7");
            Assert.Equal(0, callbacks);
            Assert.Equal("PROJECT_TEST_BINDING_INVALID", Assert.Single(vm.ScanEvents).Result);
            Assert.False(vm.IsRunning);
        }
        finally { Directory.Delete(root, true); }
    }

}
