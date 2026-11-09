using System.IO;
using System.Text.Json;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Wave;

public sealed class WaveOfflineLabSessionTests
{
    private const string Dbc = "BU_: ECU\nBO_ 801 MOTOR: 8 ECU\n SG_ Current : 0|8@1+ (0.5,0) [0|127.5] \"A\" ECU";
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OfflineSession_UsesExistingHubDecoderAndNeverOffersPhysicalLive()
    {
        var root = NewFolder();
        try
        {
            using var lab = new WaveOfflineLabSession(root);
            Assert.False(lab.PhysicalTransmitAvailable);
            Assert.False(lab.Backend.CanCaptureLive);
            lab.ImportDbcText(Dbc);
            var signal = Assert.Single(lab.Backend.ListSignals()).Signal;
            var archive = NewArchive(lab, 801, 8);
            var path = WriteArchive(root, archive);
            lab.Backend.OpenArchiveFile(path);
            var result = await lab.Backend.ReplayAsync(signal, 40, CancellationToken.None);
            Assert.Equal(WaveInputKind.Replay, result.Kind);
            Assert.Equal(TimeSource.Replay, result.Series.Origin.Source);
            Assert.Equal(TimeQuality.ReplayDerived, result.Series.Origin.Quality);
            Assert.Equal(8, result.Series.Samples.Length);
            Assert.Equal(.5, result.Series.Samples[0].Sample.Value);
            Assert.All(result.Series.Samples, sample =>
                Assert.NotEqual(Guid.Empty, sample.SourceEvidence.Value));
            var reference = lab.Backend.SaveReference(result.Series, "local baseline");
            var comparison = await lab.Backend.CompareAsync(result, reference,
                WaveComparisonConfig.FromContract(new CompareProfile(.1, .9)), CancellationToken.None);
            Assert.Equal(Verdict.Pass, comparison.Details.Verdict);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MutatedOriginalArchiveIsDeniedBeforeReplayAndPreview()
    {
        var root = NewFolder();
        try
        {
            using var lab = new WaveOfflineLabSession(root);
            lab.ImportDbcText(Dbc);
            var signal = Assert.Single(lab.Backend.ListSignals()).Signal;
            var path = WriteArchive(root, NewArchive(lab, 801, 5));
            lab.Backend.OpenArchiveFile(path);
            File.AppendAllText(path, " ");
            var preview = Assert.Throws<WaveCaptureException>(() => lab.Backend.ReadArchiveReport(10));
            Assert.Equal("CAN_REPLAY_ARCHIVE_CHANGED", preview.Code);
            var replay = await Assert.ThrowsAsync<WaveCaptureException>(async () =>
                await lab.Backend.ReplayAsync(signal, 10, CancellationToken.None));
            Assert.Equal("CAN_REPLAY_ARCHIVE_CHANGED", replay.Code);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ANewDbcCannotInheritAnOldArchiveOrOldSignalIdentity()
    {
        var root = NewFolder();
        try
        {
            using var lab = new WaveOfflineLabSession(root);
            lab.ImportDbcText(Dbc);
            var originalSignal = Assert.Single(lab.Backend.ListSignals()).Signal;
            lab.Backend.OpenArchiveFile(WriteArchive(root, NewArchive(lab, 801, 5)));
            lab.ImportDbcText(Dbc.Replace("(0.5,0)", "(1.0,0)", StringComparison.Ordinal));
            var current = Assert.Single(lab.Backend.ListSignals()).Signal;
            Assert.NotEqual(originalSignal.DbcAsset, current.DbcAsset);
            var stale = await Assert.ThrowsAsync<WaveCaptureException>(async () =>
                await lab.Backend.ReplayAsync(current, 10, CancellationToken.None));
            Assert.Equal("DBC_REVISION_STALE", stale.Code);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MalformedDbcCannotDiscardPreviouslyLoadedWorkspace()
    {
        var root = NewFolder();
        try
        {
            using var lab = new WaveOfflineLabSession(root);
            lab.ImportDbcText(Dbc);
            var original = Assert.Single(lab.Backend.ListSignals()).Signal;
            Assert.ThrowsAny<Exception>(() => lab.ImportDbcText(
                "BU_: ECU\nBO_ 801 MOTOR: 99 ECU\n SG_ Current : 0|8@1+ (0.5,0) [0|127.5] \"A\" ECU"));
            Assert.Equal(original, Assert.Single(lab.Backend.ListSignals()).Signal);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100001")]
    [InlineData("abc")]
    public async Task InvalidReplayFrameLimitIsReportedBeforeAnyArchivePublish(string budget)
    {
        var root = NewFolder();
        try
        {
            using var lab = new WaveOfflineLabSession(root);
            lab.ImportDbcText(Dbc);
            var vm = new WaveWorkbenchViewModel();
            vm.BindBackend(lab.Backend);
            vm.ReplayFrameLimitText = budget;
            await vm.ReplaySelectedAsync();
            Assert.Contains("REPLAY_FRAME_BUDGET_INVALID", vm.Status);
            Assert.Equal(0, vm.CaptureCount);
            Assert.Contains(vm.OperationLogs, x => x.ErrorCode == "REPLAY_FRAME_BUDGET_INVALID");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ComparisonRulesChangeCannotLeaveAnObsoletePassVisible()
    {
        var root = NewFolder();
        try
        {
            using var lab = new WaveOfflineLabSession(root);
            lab.ImportDbcText(Dbc);
            var file = WriteArchive(root, NewArchive(lab, 801, 8));
            var vm = new WaveWorkbenchViewModel();
            vm.BindBackend(lab.Backend);
            vm.ArchivePath = file;
            await vm.OpenArchiveSelectedAsync();
            await vm.ReplaySelectedAsync();
            await vm.SaveReferenceSelectedAsync();
            await vm.CompareSelectedAsync();
            Assert.Equal("PASS", Assert.Single(vm.Results).Verdict);
            Assert.Contains("RMSE", vm.Results[0].Detail);
            vm.ToleranceText = "not-a-number";
            Assert.Empty(vm.Results);
            await vm.CompareSelectedAsync();
            Assert.Empty(vm.Results);
            Assert.Contains("WAVE_ERROR", vm.Status);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StandaloneResourceActionsAreRecordedAsExportableDiagnosticEvents()
    {
        var vm = new WaveWorkbenchViewModel();
        vm.ReportExternalOperation("OpenProject", "Failed", "Invalid DBC asset", "WAVE_PROJECT_LOAD_FAILED");
        var entry = Assert.Single(vm.OperationLogs);
        Assert.Equal("OpenProject", entry.Operation);
        Assert.Equal("Resource", entry.Stage);
        Assert.Equal("Failed", entry.State);
        Assert.Equal("WAVE_PROJECT_LOAD_FAILED", entry.ErrorCode);
        Assert.Contains("WAVE_PROJECT_LOAD_FAILED", vm.Status);
    }

    private static RecordedCanArchive NewArchive(WaveOfflineLabSession lab, uint frame, int count)
    {
        var asset = lab.Workspace.Assets.Single(x => x.Id == lab.Workspace.DbcAssetId!.Value);
        return new RecordedCanArchive(1, Guid.NewGuid(), asset.Id.Value, asset.Revision, asset.Sha256,
            T0, Enumerable.Range(1, count).Select(i =>
                new RecordedCanRxRow(i * 20_000L, BusRole.DutCan, frame, false,
                    i.ToString("X2") + "00000000000000", 0, Guid.NewGuid())).ToArray());
    }

    private static string WriteArchive(string root, RecordedCanArchive archive)
    {
        var path = Path.Combine(root, "can-rx.json");
        File.WriteAllText(path, JsonSerializer.Serialize(archive));
        return path;
    }

    private static string NewFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "autotest-wave-offline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
