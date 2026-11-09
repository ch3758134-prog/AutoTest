using System.IO;
using System.Text.Json;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Devices.Can;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Wave;

public sealed class RecordedCanWaveReplayTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedCanRxArchive_UsesSharedHubAndRetainsGapEvidence(bool withLoss)
    {
        var root = Path.Combine(Path.GetTempPath(), "wave-recorded-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new V1Workspace();
            workspace.ImportDbc("BU_: ECU\nBO_ 801 MOTOR: 8 ECU\n SG_ Current : 0|8@1+ (0.5,0) [0|127.5] \"A\" ECU");
            var dbc = workspace.Assets.Single(x => x.Id == workspace.DbcAssetId!.Value);
            var signal = new SignalRef(dbc.ToReference(), BusRole.DutCan, 801, "Current", "A");
            var frames = Enumerable.Range(1, 5).Select(i =>
                new RecordedCanRxRow(i * 20000L, BusRole.DutCan, 801, false,
                    i.ToString("X2") + "00000000000000", withLoss && i == 3 ? 1 : 0,
                    Guid.NewGuid())).ToArray();
            var archive = new RecordedCanArchive(1, Guid.NewGuid(), dbc.Id.Value,
                dbc.Revision, dbc.Sha256, T0, frames);
            Directory.CreateDirectory(root);
            var file = Path.Combine(root, "trace.json");
            File.WriteAllText(file, JsonSerializer.Serialize(archive));
            using var hub = new CanFrameHub();
            var inReplayMode = true;
            var replay = new RecordedCanWaveReplayProvider(hub, hub.Publish,
                () => inReplayMode, workspace, Path.Combine(root, "evidence"));
            replay.LoadArchiveFile(file);
            var captured = await replay.ReplayAsync(signal, 16, CancellationToken.None);
            Assert.Equal(TimeSource.Replay, captured.Series.Origin.Source);
            Assert.StartsWith("Replay / recorded CAN RX", captured.SourceLabel);
            Assert.All(captured.Series.Samples, x => Assert.NotEqual(Guid.Empty, x.SourceEvidence.Value));
            var judgement = WaveTimeQuality.Describe(captured.Series);
            if (withLoss)
            {
                Assert.True(captured.Series.HasGap);
                Assert.False(judgement.CanJudge);
            }
            else
            {
                Assert.Equal(5, captured.Series.Samples.Length);
                Assert.True(judgement.CanJudge);
                Assert.Equal(0.5, captured.Series.Samples[0].Sample.Value);
            }
            inReplayMode = false;
            var exception = await Assert.ThrowsAsync<WaveCaptureException>(async () =>
                await replay.ReplayAsync(signal, 16, CancellationToken.None));
            Assert.Equal("CAN_REPLAY_MODE_BLOCKED", exception.Code);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CorruptedRxArchiveIsRejectedBeforeAnyCanPublish()
    {
        var workspace = new V1Workspace();
        using var hub = new CanFrameHub();
        var root = Path.Combine(Path.GetTempPath(), "wave-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var file = Path.Combine(root, "trace.json");
            File.WriteAllText(file, "{\"schemaVersion\":0}");
            var replay = new RecordedCanWaveReplayProvider(hub, hub.Publish, () => true, workspace, root);
            Action loadInvalidArchive = () => replay.LoadArchiveFile(file);
            Assert.Throws<InvalidDataException>(loadInvalidArchive);
            Assert.False(replay.HasArchive);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
