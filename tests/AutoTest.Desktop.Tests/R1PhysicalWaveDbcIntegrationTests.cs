using System.IO;
using System.Diagnostics;
using System.Text.Json;
using AutoTest.Devices.Can;
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

    [Fact]
    public async Task ActualHubRxFramesProduceReplayCompatibleArchiveWithoutTransmit()
    {
        using var hub = new CanFrameHub();
        var workspace = new V1Workspace();
        var asset = workspace.ImportDbc(DbcText, "demo.dbc");
        const long generation = 7;
        var capture = R1RecordedCanArchiveCapture.CaptureAsync(
            hub, () => true, asset.ToReference(), generation,
            TimeSpan.FromMilliseconds(400), 64);
        for (var attempt = 0; hub.SubscriberCount == 0 && attempt < 400; attempt++)
            await Task.Delay(5);
        Assert.Equal(1, hub.SubscriberCount);
        for (var n = 0; n < 2; n++)
        {
            var payload = new byte[] { (byte)(n + 1) };
            var frame = CanFrame.Create(BusRole.DutCan, false, 1, payload);
            var stamp = TimePoint.FromHostArrival(
                DateTimeOffset.UtcNow.AddMilliseconds(10 + n * 20),
                Stopwatch.GetTimestamp());
            hub.Publish(new CanObservation(frame, stamp, EvidenceQuality.HostObserved,
                generation, 0, EvidenceRef.New()));
            await Task.Delay(20);
        }
        var archive = await capture;
        Assert.Equal(2, archive.Frames.Length);
        Assert.Equal(asset.Id.Value, archive.DbcId);
        Assert.Equal("01", archive.Frames[0].PayloadHex);
        Assert.Equal("02", archive.Frames[1].PayloadHex);
        Assert.All(archive.Frames, x => Assert.Equal(BusRole.DutCan, x.Role));

        var directory = Path.Combine(Path.GetTempPath(), "AutoTest-Wave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "canrx.json");
            await File.WriteAllBytesAsync(file, JsonSerializer.SerializeToUtf8Bytes(archive));
            using var replayHub = new CanFrameHub();
            var recorded = new RecordedCanWaveReplayProvider(
                replayHub, replayHub.Publish, () => true, workspace, directory);
            recorded.LoadArchiveFile(file);
            Assert.True(recorded.HasArchive);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CanRxArchiveRefusesStaleSessionBeforeSubscribing()
    {
        using var hub = new CanFrameHub();
        var workspace = new V1Workspace();
        var asset = workspace.ImportDbc(DbcText, "demo.dbc");
        var failure = await Assert.ThrowsAsync<WaveCaptureException>(async () =>
        {
            await R1RecordedCanArchiveCapture.CaptureAsync(
                hub, () => false, asset.ToReference(), 1,
                TimeSpan.FromMilliseconds(200), 100);
        });
        Assert.Equal("PHYSICAL_RX_ARCHIVE_NOT_READY", failure.Code);
        Assert.Equal(0, hub.SubscriberCount);
    }

    [Fact]
    public async Task ExplicitDbcRevisionPublishPreservesPinnedTestReferencesAndRejectsOldTokens()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTest-DBC-r-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "revision.dbc");
        try
        {
            await File.WriteAllTextAsync(path, DbcText);
            var workspace = new V1Workspace();
            var editor = new DbcToolViewModel(workspace) { FilePath = path };
            await editor.OpenFileAsync();
            await editor.ImportToProjectAsync();
            var previous = workspace.Assets.Single(x => x.Kind == "dbc");
            workspace.AddEditableMockExample();
            var step = workspace.Document.Walk().First();
            workspace.PinStepToDbc(step.Id);
            var oldToken = workspace.Document.Walk().First(x => x.Id == step.Id).Properties["asset"];

            await File.WriteAllTextAsync(path, DbcText.Replace("(1,0)", "(2,0)"));
            await editor.OpenFileAsync();
            await editor.PublishProjectRevisionAsync();
            Assert.Equal(2, editor.Revision);
            Assert.Equal(2, workspace.Database!.FindSignal(1, "Value").Factor);
            Assert.Equal(oldToken,
                workspace.Document.Walk().First(x => x.Id == step.Id).Properties["asset"]);
            Assert.NotEqual(previous.Sha256, workspace.Assets.Single(x => x.Kind == "dbc").Sha256);

            var error = Assert.Throws<InvalidOperationException>(() =>
                workspace.ReplaceDbcSource(DbcText, previous.ToReference(), "revision.dbc"));
            Assert.Contains("DBC_REVISION_CONFLICT", error.Message);

            await editor.PublishProjectRevisionAsync();
            Assert.Contains("DBC_UNCHANGED", editor.Error);
            Assert.Equal(2, workspace.Assets.Single(x => x.Kind == "dbc").Revision);
        }
        finally { Directory.Delete(folder, true); }
    }

}
