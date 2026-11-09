using System.IO;
using AutoTest.Application.G4;
using AutoTest.Application.V1;
using AutoTest.Core;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Wave;

public sealed class WaveG4BackendTests
{
    [Fact]
    public async Task G4Replay_UsesSharedCanHubDbcAndCreatesVerifiableComparisonEvidence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wave-g4-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new V1Workspace();
            workspace.ImportDbc("BU_: ECU\nBO_ 801 MOTOR: 8 ECU\n SG_ Current : 0|8@1+ (0.5,0) [0|127.5] \"A\" ECU");
            using var host = new G4IntegrationHost(workspace, Path.Combine(directory, "host"));
            var backend = new G4WaveReplayBackend(host, Path.Combine(directory, "workbench"));
            var signal = Assert.Single(backend.ListSignals()).Signal;
            Assert.False(backend.CanCaptureLive);
            var replay = await backend.RunMockAsync(signal, 12, CancellationToken.None);
            Assert.Equal(12, replay.Series.Samples.Length);
            Assert.True(replay.Series.IsReplay);
            Assert.Contains("Mock", replay.SourceLabel);
            var sourceFactPath = Path.Combine(directory, "workbench", "provenance",
                replay.Evidence.Evidence.Value.ToString("N") + ".origin.json");
            Assert.True(File.Exists(sourceFactPath));
            var sourceFact = System.Text.Json.JsonSerializer.Deserialize<WaveCaptureOriginFact>(
                File.ReadAllBytes(sourceFactPath));
            Assert.Equal(WaveInputKind.Mock, sourceFact!.InputKind);
            Assert.Equal(TimeSource.Replay, sourceFact.TimeSource); // legacy synthetic timer
            var reference = backend.SaveReference(replay.Series, "Replay baseline");
            Assert.NotNull(backend.ReadReference(signal));
            var result = await backend.CompareAsync(replay, reference,
                WaveComparisonConfig.FromContract(new AutoTest.Contracts.CompareProfile(.01, 1)),
                CancellationToken.None);
            Assert.Equal(Verdict.Pass, result.Details.Verdict);
            var evidence = new WaveEvidenceStore(Path.Combine(directory, "workbench", "evidence"));
            Assert.True(evidence.Verify(result.Evidence));
            // A different scaling revision must not silently reuse the old reference.
            workspace.EditDbcSignal(801, "Current", 2.0);
            Assert.DoesNotContain(backend.ListSignals(), x => x.Signal == signal);
            await Assert.ThrowsAsync<WaveCaptureException>(async () =>
                await backend.RunMockAsync(signal, 12, CancellationToken.None));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
