using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Projects.Project;
using AutoTest.Specialists.Scan;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProjectConfigurationWorkflowTests
{
    private static string Parent()
    {
        var path = Path.Combine(Path.GetTempPath(), "autotest-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Theory]
    [InlineData(ProjectWorkspaceService.ControllerKind)]
    [InlineData(ProjectWorkspaceService.WaveSettingsKind)]
    [InlineData(ProjectWorkspaceService.FlashSettingsKind)]
    [InlineData(ProjectWorkspaceService.ScanSettingsKind)]
    public async Task CreateReopen_AllConfigKinds_AreFormalAssets(string kind)
    {
        var parent = Parent();
        try
        {
            var workspace = new ProjectWorkspaceService();
            var project = await workspace.CreateAsync("Lab", parent);
            var created = await workspace.CreateConfigurationAsync(kind, "配置甲");
            Assert.True(File.Exists(Path.Combine(project.RootDirectory, created.RelativePath)));
            var opened = await workspace.OpenConfigurationAsync(created.Id);
            Assert.Equal(kind, opened.Kind);
            var other = new ProjectWorkspaceService();
            await other.OpenAsync(project.ManifestPath);
            var reloaded = await other.OpenConfigurationAsync(created.Id);
            Assert.Equal(opened.FileSha256, reloaded.FileSha256);
            Assert.Equal(opened.AssetRevision, reloaded.AssetRevision);
        }
        finally { Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task ControllerProfile_UsesActualControllerRepositoryAndPersistsKeyMask()
    {
        var parent = Parent();
        try
        {
            var workspace = new ProjectWorkspaceService();
            var project = await workspace.CreateAsync("Lab", parent);
            var asset = await workspace.CreateConfigurationAsync(ProjectWorkspaceService.ControllerKind, "EcuA");
            var session = await workspace.OpenConfigurationAsync(asset.Id);
            var edit = new ControllerEditSession((EcuControlProfile)session.Document);
            edit.AddController("驻车开关");
            var controller = Assert.Single(edit.Current.Controllers);
            edit.AddState(controller.Id, "驻车请求", 0x00012);
            var state = edit.Current.Controllers.Single().States.Last();
            edit.SetWindow(controller.Id, state.Id, new VoltageWindow(1, 1.3, 1.2, 1.4));
            var saved = await workspace.SaveConfigurationAsync(session, edit.Current);
            Assert.Equal(2, saved.AssetRevision);

            var reopened = new ProjectWorkspaceService();
            await reopened.OpenAsync(project.ManifestPath);
            var loaded = await reopened.OpenConfigurationAsync(asset.Id);
            var profile = (EcuControlProfile)loaded.Document;
            Assert.Equal(0x00012u, profile.Controllers.Single().States.Last().KeyMask);
            Assert.Equal(1.3, profile.Controllers.Single().States.Last().Windows.Single().TargetVolts);
            Assert.Equal(saved.FileSha256, loaded.FileSha256);
        }
        finally { Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task ScanRule_RequiresPublishedRecipeAndPinnedTestForMatch()
    {
        var parent = Parent();
        try
        {
            var workspace = new ProjectWorkspaceService();
            var project = await workspace.CreateAsync("Lab", parent);
            var test = await workspace.CreateTestProgramAsync("EOL");
            var scan = await workspace.CreateConfigurationAsync(ProjectWorkspaceService.ScanSettingsKind, "工厂扫码");
            var opened = await workspace.OpenConfigurationAsync(scan.Id);
            var edited = new ScanMatchSettingsDocument(1, "工厂扫码", true,
                [new ScanRuleDraft("Serial", "^(?<serial>[A-Z]{2}[0-9]{3})$", "serial", true)],
                [new ScanRecipeDraft("EOL_A", "", "", test.Id.Value, null, null, false, true,
                    test.Revision, test.Sha256)]);
            var saved = await workspace.SaveConfigurationAsync(opened, edited);
            Assert.Equal(2, saved.AssetRevision);

            var matcher = new ProjectScanMatcher(workspace);
            var scanEvent = new BarcodeScanEvent(new RawScan("AB123", "keyboard",
                TimePoint.FromHostArrival(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()), true),
                EvidenceRef.New());
            var matched = await matcher.MatchAsync(scanEvent);
            Assert.True(matched.Accepted);
            Assert.Equal("EOL_A", matched.RecipeId);

            var rejected = await matcher.MatchAsync(scanEvent with
            { Scan = scanEvent.Scan with { Text = "BAD" } });
            Assert.False(rejected.Accepted);
            Assert.Equal("IDENTITY_NO_MATCH", rejected.Code);

            var wrong = edited with
            {
                Recipes = [edited.Recipes[0] with { TestProgramRevision = test.Revision + 1 }]
            };
            var error = await Assert.ThrowsAsync<ProjectDataException>(
                () => workspace.SaveConfigurationAsync(saved, wrong));
            // The schema accepts authoring a future revision but matching must reject stale asset refs.
            Assert.Equal("CONFIG_ASSET_STALE", error.Code);
        }
        finally { Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task ExternalConfigWrite_CannotOverwriteFileOrAdvanceManifest()
    {
        var parent = Parent();
        try
        {
            var workspace = new ProjectWorkspaceService();
            await workspace.CreateAsync("Lab", parent);
            var asset = await workspace.CreateConfigurationAsync(ProjectWorkspaceService.WaveSettingsKind, "Wave");
            var opened = await workspace.OpenConfigurationAsync(asset.Id);
            var edited = ((WaveSettingsDocument)opened.Document) with { SignalName = "MotorCurrent" };
            await File.AppendAllTextAsync(opened.FilePath, "\n ", Encoding.UTF8);
            var before = workspace.Current!.Definition.Revision;
            var error = await Assert.ThrowsAsync<ProjectDataException>(
                () => workspace.SaveConfigurationAsync(opened, edited));
            Assert.Equal("CONFIG_FILE_CONFLICT", error.Code);
            Assert.Equal(before, workspace.Current!.Definition.Revision);
        }
        finally { Directory.Delete(parent, true); }
    }

    [Fact]
    public void FlashSettings_DoNotContainExecutablePhysicalAuthorization()
    {
        var profile = FlashSettingsDocument.Create("Flash");
        Assert.Throws<ProjectDataException>(() =>
            (profile with { ApprovedByOperator = true }).Validate());
    }
}
