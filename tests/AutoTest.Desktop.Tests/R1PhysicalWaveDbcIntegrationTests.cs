using System.IO;
using AutoTest.Application.V1;
using AutoTest.Application.Composition;
using AutoTest.Desktop.Shell;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Tools.Dbc;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Desktop.Tests;

public sealed class R1PhysicalWaveDbcIntegrationTests
{
    private const string DbcText =
        "BU_: ECU\nBO_ 1 FRAME: 1 ECU\n SG_ Value : 0|8@1+ (1,0) [0|255] \"V\" ECU\n";

    [Fact]
    public async Task DbcEditorImportsOnlyAfterExplicitProjectRegistration()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTest-DBC-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "demo.dbc");
        try
        {
            await File.WriteAllTextAsync(path, DbcText);
            var workspace = new V1Workspace();
            var editor = new DbcToolViewModel(workspace) { FilePath = path };
            await editor.OpenFileAsync();
            Assert.Null(workspace.DbcAssetId);
            await editor.ImportToProjectAsync();
            Assert.NotNull(workspace.DbcAssetId);
            Assert.NotNull(workspace.Database);
            Assert.Single(workspace.Database!.Messages);
            Assert.Equal(1, editor.Revision);

            // Duplicate admission must not silently create a second project asset.
            await editor.ImportToProjectAsync();
            Assert.Single(workspace.Assets.Where(x => x.Kind == "dbc"));
            Assert.Contains("已经有主 DBC", editor.Error);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task WaveLiveCannotClaimPhysicalCaptureWithoutSharedDevice()
    {
        var workspace = new V1Workspace();
        var asset = workspace.ImportDbc(DbcText, "demo.dbc");
        var signal = new SignalRef(asset.ToReference(), BusRole.DutCan, 1, "Value", "V");
        var source = new R1PhysicalWaveSignalSource(workspace, () => null);
        var failure = await Assert.ThrowsAsync<WaveCaptureException>(async () =>
        {
            await source.OpenAsync(signal, CancellationToken.None);
        });
        Assert.Equal("PHYSICAL_CAN_NOT_CONNECTED", failure.Code);
    }
    [Fact]
    public void ProjectAssetNotificationsTrackImportRevisionAndReset()
    {
        var workspace = new V1Workspace();
        var vm = new MainWindowViewModel(ApplicationComposition.RegisterMock().Gateway, workspace);
        var notifications = 0;
        vm.WorkspaceAssetsChanged += (_, _) => notifications++;

        workspace.ImportDbc(DbcText, "demo.dbc");
        vm.RefreshExternalWorkspaceChanges();
        Assert.Equal(1, notifications);
        vm.RefreshExternalWorkspaceChanges();
        Assert.Equal(1, notifications);

        workspace.EditDbcSignal(1, "Value", 2);
        vm.RefreshExternalWorkspaceChanges();
        Assert.Equal(2, notifications);

        workspace.CreateProject("new");
        vm.RefreshExternalWorkspaceChanges();
        Assert.Equal(3, notifications);
    }

}
