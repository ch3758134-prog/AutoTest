using System.Diagnostics;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Specialists.Scan;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class Ut1ProjectRoutingTests
{
    [Fact]
    public async Task ScannerRoutesByProductAndHardwareAndRetainsGlobalSerial()
    {
        var parent = Path.Combine(Path.GetTempPath(),
            "ut1-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("R1", parent);
            var test = await workspace.CreateTestProgramAsync("HydEPB 功能测试");
            var scan = await workspace.CreateConfigurationAsync(
                ProjectWorkspaceService.ScanSettingsKind, "产品扫描路由");
            var session = await workspace.OpenConfigurationAsync(scan.Id);
            var config = new ScanMatchSettingsDocument(1, "产品扫描路由", true,
                [new ScanRuleDraft("UT1",
                    @"^UT1-(?<model>[A-Z0-9]{4,8})-(?<hardware>H[0-9]{2})-(?<serial>[A-Z0-9]{8,20})-(?<crc>[0-9A-F]{4})$",
                    "serial", true)],
                [new ScanRecipeDraft("EPBA-H02", "EPBA", "",
                    test.Id.Value, null, null, false, true,
                    test.Revision, test.Sha256,
                    FlashPolicy: ScanFlashPolicy.NoFlash, HardwareCode: "H02")]);
            await workspace.SaveConfigurationAsync(session, config);
            var matcher = new ProjectScanMatcher(workspace);

            BarcodeScanEvent Event(string input) => new(
                new RawScan(input, "keyboard",
                    TimePoint.FromHostArrival(DateTimeOffset.UtcNow,
                        Stopwatch.GetTimestamp()), true), EvidenceRef.New());

            var valid = await matcher.MatchAsync(Event("UT1-EPBA-H02-000000123456-4372"));
            Assert.True(valid.Accepted);
            Assert.Equal("000000123456", valid.Identity);
            Assert.Equal("EPBA-H02", valid.RecipeId);
            Assert.NotNull(valid.Preview);
            Assert.Equal(ScanFlashPolicy.NoFlash, valid.Preview.FlashPolicy);

            var badChecksum = await matcher.MatchAsync(
                Event("UT1-EPBA-H02-000000123456-4373"));
            Assert.Equal("BARCODE_CRC_MISMATCH", badChecksum.Code);
            Assert.False(badChecksum.Accepted);

            var differentHardware = await matcher.MatchAsync(
                Event("UT1-EPBA-H03-000000123458-D9DD"));
            Assert.Equal("CONFIG_NO_MATCH", differentHardware.Code);
            Assert.False(differentHardware.Accepted);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
}
