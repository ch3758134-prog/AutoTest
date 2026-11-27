using System.Collections.Immutable;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using AutoTest.Testing.Authoring;
using AutoTest.Core;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ScanFlashRecipeIntegrationTests
{
    [Fact]
    public async Task NoFlashPublishedRecipeBindsEntireOrderedTestProjectWithoutPhysicalWrites()
    {
        var parent = Path.Combine(Path.GetTempPath(),
            "scan-testproject-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("R1", parent);
            var first = await workspace.CreateTestProgramAsync("驻车");
            var second = await workspace.CreateTestProgramAsync("释放");
            var project = await workspace.CreateTestProjectAsync("生产功能检测");
            var opened = await workspace.OpenTestProjectEditorAsync(project.Id);
            var saved = await workspace.SaveTestProjectEditorAsync(opened,
                opened.Document with { Packages = [
                    new TestProjectEntry(Guid.NewGuid(), first.Id.Value, "驻车"),
                    new TestProjectEntry(Guid.NewGuid(), second.Id.Value, "释放")] });
            var recipe = new ScanRecipeDraft("EPB-EOL", "EPBA", "",
                null, null, null, false, true,
                TestProjectAssetId: project.Id.Value,
                TestProjectRevision: saved.AssetRevision,
                TestProjectFingerprint: saved.FileSha256,
                FlashPolicy: ScanFlashPolicy.NoFlash, TargetEcuCanKbps: 500);
            var preview = await ScanRecipePreparationService.InspectAsync(
                workspace, workspace.Current!, recipe, requireFrozenJob: true);
            Assert.Equal(ScanFlashPolicy.NoFlash, preview.FlashPolicy);
            Assert.Equal(new[] { "驻车", "释放" }, preview.Cases);
            Assert.Null(preview.FlashJobFingerprint);
            var published = new ScanMatchSettingsDocument(1, "发布", true,
                [new ScanRuleDraft("Rule", "^(?<serial>[A-Z]{2}[0-9]{3})$", "serial", true)],
                [recipe]);
            var frozen = await ScanRecipePreparationService.FreezePublishedAsync(
                workspace, published);
            Assert.Equal(ScanFlashPolicy.NoFlash, frozen.Recipes.Single().FlashPolicy);
            Assert.Null(frozen.Recipes.Single().FlashJobFingerprint);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public async Task NoFlashExplicitlyRejectsAnEnabledFlashStep()
    {
        var parent = Path.Combine(Path.GetTempPath(),
            "scan-noflash-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("R1", parent);
            var program = await workspace.CreateTestProgramAsync("烧录功能测试");
            var opened = await workspace.OpenTestEditorAsync(program.Id);
            var edited = opened.Document with
            {
                Phases = opened.Document.Phases.SetItem(
                    TestPhase.Test, ImmutableArray.Create(StepNode.Create("Flash", "烧录")))
            };
            var saved = await workspace.SaveTestEditorAsync(opened, edited);
            var recipe = new ScanRecipeDraft("NoFlash", "", "",
                program.Id.Value, null, null, false, true,
                saved.AssetRevision, saved.FileHash,
                FlashPolicy: ScanFlashPolicy.NoFlash);
            var error = await Assert.ThrowsAsync<ProjectDataException>(() =>
                ScanRecipePreparationService.InspectAsync(
                    workspace, workspace.Current!, recipe, requireFrozenJob: true));
            Assert.Equal("SCAN_FLASH_POLICY", error.Code);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public void FlashRequiredCannotPublishWithoutPinnedConfigurationAndTargetSoftware()
    {
        var draft = new ScanMatchSettingsDocument(1, "扫码配置", true,
            [new ScanRuleDraft("SN", "^(?<serial>[A-Z0-9]{8})$", "serial", true)],
            [new ScanRecipeDraft("Flash", "EPBA", "",
                Guid.NewGuid(), Guid.NewGuid(), null, true, true,
                1, new string('A', 64),
                1, new string('B', 64),
                FlashPolicy: ScanFlashPolicy.FlashRequired,
                TargetEcuCanKbps: 500)]);
        var error = Assert.Throws<ProjectDataException>(() => draft.Validate());
        Assert.Equal("SCAN_FLASH_BUNDLE", error.Code);
    }

    [Fact]
    public void HistoricalNoFlashScanJsonStillLoadsWithoutInventedFlashPermissions()
    {
        var prior = new ScanMatchSettingsDocument(1, "历史扫码", true,
            [new ScanRuleDraft("SN", "^(?<serial>[A-Z0-9]{8})$", "serial", true)],
            [new ScanRecipeDraft("EOL", "", "", Guid.NewGuid(), null,
                null, false, true, 1, new string('A', 64))]);
        var roundTrip = ProjectConfigurationCodec.Decode(ProjectWorkspaceService.ScanSettingsKind,
            ProjectConfigurationCodec.Encode(ProjectWorkspaceService.ScanSettingsKind, prior));
        var loaded = Assert.IsType<ScanMatchSettingsDocument>(roundTrip);
        Assert.Equal(ScanFlashPolicy.NoFlash,
            Assert.Single(loaded.Recipes).EffectiveFlashPolicy);
    }
}
