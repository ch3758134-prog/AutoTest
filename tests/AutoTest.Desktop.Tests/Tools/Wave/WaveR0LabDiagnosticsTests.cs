using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AutoTest.Application.V1;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Devices.Can;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Wave;

public sealed class WaveR0LabDiagnosticsTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
    private const string Dbc = "BU_: ECU\nBO_ 801 MOTOR: 8 ECU\n SG_ Current : 0|8@1+ (0.5,0) [0|127.5] \"A\" ECU";

    private static (V1Workspace Workspace, string Path, RecordedCanArchive Archive) Fixture(
        string folder, int frames = 5, bool loss = false)
    {
        var workspace = new V1Workspace();
        workspace.ImportDbc(Dbc);
        var asset = workspace.Assets.Single(a => a.Id == workspace.DbcAssetId!.Value);
        var rows = Enumerable.Range(1, frames).Select(i => new RecordedCanRxRow(
            i * 20_000L, BusRole.DutCan, 801, false,
            (i % 200).ToString("X2") + "00000000000000", loss && i == 3 ? 2 : 0,
            Guid.NewGuid())).ToArray();
        var archive = new RecordedCanArchive(1, Guid.NewGuid(), asset.Id.Value,
            asset.Revision, asset.Sha256, Start, rows);
        var file = Path.Combine(folder, "can-rx.json");
        File.WriteAllText(file, JsonSerializer.Serialize(archive));
        return (workspace, file, archive);
    }

    [Fact]
    public void RawPreviewContainsRealFrameHexTimestampTraceAndDecoding()
    {
        var folder = NewFolder();
        try
        {
            var (workspace, file, _) = Fixture(folder, loss: true);
            using var hub = new CanFrameHub();
            var provider = new RecordedCanWaveReplayProvider(hub, hub.Publish, () => true, workspace, folder);
            provider.LoadArchiveFile(file);
            var report = provider.ReadArchiveReport(2);
            Assert.Equal(5, report.TotalFrames);
            Assert.Equal(2, report.DisplayedFrames);
            Assert.True(report.DisplayTruncated);
            Assert.Equal(2, report.MaximumReportedLostFrames);
            Assert.Contains("ReplayDerived", report.TimeSemantics);
            Assert.Equal("0x321", report.Frames[0].CanId);
            Assert.Equal("0100000000000000", report.Frames[0].DataHex);
            Assert.Contains("Current=0.5 A", report.Frames[0].DecodedSummary);
            Assert.NotEqual(Guid.Empty.ToString("D"), report.Frames[0].TraceEvidenceId);
            Assert.Equal(Start.AddMilliseconds(20), report.Frames[0].TimestampUtc);
            var bad = provider.ReadArchiveReport(2000).Frames[2];
            Assert.Equal("LOSS_REPORTED", bad.ErrorCode);
            Assert.DoesNotContain("Current=", bad.DecodedSummary);
            var filter = provider.ReadArchiveReport(2, "LOSS_REPORTED");
            Assert.Single(filter.Frames);
            Assert.Equal(3, filter.Frames[0].Index);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void StaleDbcRetainsRawFramesButRejectsOldDecode()
    {
        var folder = NewFolder();
        try
        {
            var (workspace, file, _) = Fixture(folder);
            using var hub = new CanFrameHub();
            var provider = new RecordedCanWaveReplayProvider(hub, hub.Publish, () => true, workspace, folder);
            provider.LoadArchiveFile(file);
            workspace.EditDbcSignal(801, "Current", 1.0);
            var report = provider.ReadArchiveReport(3);
            Assert.Equal("DBC_REVISION_STALE", report.ValidationCode);
            Assert.All(report.Frames, f => Assert.Equal("DBC_REVISION_STALE", f.ErrorCode));
            Assert.Equal("0100000000000000", report.Frames[0].DataHex);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void OriginalArchiveChangedAfterLoadIsBlockedFromEvidenceExport()
    {
        var folder = NewFolder();
        try
        {
            var (workspace, file, _) = Fixture(folder);
            using var hub = new CanFrameHub();
            var provider = new RecordedCanWaveReplayProvider(hub, hub.Publish, () => true, workspace, folder);
            provider.LoadArchiveFile(file);
            File.AppendAllText(file, " ");
            var error = Assert.Throws<WaveCaptureException>(() => provider.ReadVerifiedOriginalArchive());
            Assert.Equal("CAN_REPLAY_ARCHIVE_CHANGED", error.Code);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ExportKeepsAllRawFramesAndSourceHashDespitePreviewLimit()
    {
        var folder = NewFolder();
        try
        {
            var (workspace, file, _) = Fixture(folder, 2105);
            using var hub = new CanFrameHub();
            var provider = new RecordedCanWaveReplayProvider(hub, hub.Publish, () => true, workspace, folder);
            provider.LoadArchiveFile(file);
            var preview = provider.ReadArchiveReport(2000);
            Assert.True(preview.DisplayTruncated);
            Assert.Equal(2000, preview.DisplayedFrames);
            var full = provider.ReadArchiveReport(200000);
            var export = Path.Combine(folder, "diagnostic.zip");
            WaveLabExportService.Export(export, full, provider.ReadVerifiedOriginalArchive(),
                [new WaveOperationLogRow(Start, "Replay", "Result", "Completed", "", "ok", "Replay", 12)],
                Array.Empty<WaveWorkbenchCapture>(),
                [new WaveResultRow("=ATTACK", "INCONCLUSIVE", "REFERENCE_MISSING", "-", "-")],
                "Replay / saved CAN RX", "ReplayDerived");
            using var zip = ZipFile.OpenRead(export);
            Assert.Contains(zip.Entries, e => e.FullName == "original-can-rx.json");
            Assert.Contains(zip.Entries, e => e.FullName == "raw-can.csv");
            Assert.Contains(zip.Entries, e => e.FullName == "operations.csv");
            Assert.Contains(zip.Entries, e => e.FullName == "samples.csv");
            var original = Read(zip, "original-can-rx.json");
            Assert.Equal(File.ReadAllText(file), original);
            Assert.Equal(2106, Read(zip, "raw-can.csv").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Contains("\"'=ATTACK\"", Read(zip, "comparisons.csv"));
            Assert.Contains(full.ArchiveSha256, Read(zip, "manifest.json"));
            Assert.Contains("software-only", Read(zip, "manifest.json"));
            Assert.Throws<IOException>(() => WaveLabExportService.Export(export, full,
                provider.ReadVerifiedOriginalArchive(), Array.Empty<WaveOperationLogRow>(),
                Array.Empty<WaveWorkbenchCapture>(), Array.Empty<WaveResultRow>(), "Replay", "ReplayDerived"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task ViewModelFailedReplayPreservesOperationCodeAndNoFakePass()
    {
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(new FailingReplayBackend());
        await vm.ReplaySelectedAsync();
        Assert.Contains("CAN_REPLAY_ARCHIVE_REQUIRED", vm.Status);
        Assert.Empty(vm.Results);
        Assert.Contains(vm.OperationLogs, e => e.Stage == "Request" && e.State == "Requested");
        Assert.Contains(vm.OperationLogs, e => e.State == "Failed" &&
            e.ErrorCode == "CAN_REPLAY_ARCHIVE_REQUIRED");
    }

    [Fact]
    public async Task CursorCanLocateMatchingSourceFrameEvidence()
    {
        var backend = new CursorArchiveBackend();
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(backend);
        await vm.ReplaySelectedAsync();
        vm.SetCursorFraction(.5, false);
        await vm.LocateCursorFrameSelectedAsync();
        Assert.Equal(backend.FrameEvidence.ToString("D"), vm.TraceFilter);
        Assert.Single(vm.RawFrames);
        Assert.Contains("已定位游标", vm.Status);
    }

    [Fact]
    public async Task FailedSourceSwitchCannotLeaveStaleCaptureOrPassVisible()
    {
        var backend = new CursorArchiveBackend();
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(backend);
        await vm.ReplaySelectedAsync();
        Assert.Equal(1, vm.CaptureCount);
        backend.FailOpenArchive = true;
        vm.ArchivePath = "invalid.json";
        await vm.OpenArchiveSelectedAsync();
        Assert.Equal(0, vm.CaptureCount);
        Assert.Empty(vm.Traces);
        Assert.Empty(vm.Results);
        Assert.Contains("WAVE_ERROR", vm.Status);
    }

    [Fact]
    public async Task WorkbenchDoesNotSilentlyDropPreviousOperationEvents()
    {
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(new CursorArchiveBackend());
        for (var i = 0; i < 2010; i++) await vm.RefreshTraceSelectedAsync();
        Assert.True(vm.OperationLogs.Count > 2000);
        Assert.Contains(vm.OperationLogs, e => e.Stage == "Request");
    }

    [Fact]
    public void RedactedExportRemovesPayloadDecodeValuesAndOriginalArchive()
    {
        var folder = NewFolder();
        try
        {
            var (workspace, file, _) = Fixture(folder);
            using var hub = new CanFrameHub();
            var provider = new RecordedCanWaveReplayProvider(hub, hub.Publish, () => true, workspace, folder);
            provider.LoadArchiveFile(file);
            var export = Path.Combine(folder, "redacted.zip");
            WaveLabExportService.Export(export, provider.ReadArchiveReport(200000),
                provider.ReadVerifiedOriginalArchive(),
                [new WaveOperationLogRow(Start, "Inspect", "Result", "Completed", "", "ECU VIN 123", "Replay", 1)],
                Array.Empty<WaveWorkbenchCapture>(),
                [new WaveResultRow("Current", "INCONCLUSIVE", "raw value 123", "0%", "-")],
                "Replay", "ReplayDerived", includeRawEvidence: false);
            using var zip = ZipFile.OpenRead(export);
            Assert.Null(zip.GetEntry("original-can-rx.json"));
            Assert.Contains("[REDACTED]", Read(zip, "raw-can.csv"));
            Assert.DoesNotContain("0100000000000000", Read(zip, "raw-can.csv"));
            Assert.DoesNotContain("Current=", Read(zip, "raw-can.csv"));
            Assert.DoesNotContain("ECU VIN 123", Read(zip, "operations.csv"));
            Assert.DoesNotContain("raw value 123", Read(zip, "comparisons.csv"));
            Assert.Contains("\"IncludeRawEvidence\": false", Read(zip, "manifest.json"));
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class CursorArchiveBackend : IWaveWorkbenchBackend, IWaveArchiveSource, IWaveArchiveDiagnosticSource
    {
        private readonly SignalRef _signal = new(
            new AssetRef(AssetId.New(), AssetRevision.Create(1),
                RevisionFingerprint.FromSha256(new string('b', 64))),
            BusRole.DutCan, 0x321, "Current", "A");
        public Guid FrameEvidence { get; } = Guid.NewGuid();
        public bool FailOpenArchive { get; set; }
        public bool HasRecordedArchive => true;
        public bool CanCaptureLive => false;
        public IReadOnlyList<WaveSignalChoice> ListSignals() =>
            [new WaveSignalChoice(_signal, "Current [A]")];
        public void OpenArchiveFile(string path)
        {
            if (FailOpenArchive) throw new WaveCaptureException("CAN_REPLAY_ARCHIVE_INVALID_JSON", "Invalid archive");
        }
        public Task<WaveWorkbenchCapture> ReplayAsync(SignalRef signal, int samples, CancellationToken ct)
        {
            var origin = TimePoint.FromReplay(Start, 0);
            var buffer = new WaveSampleBuffer(signal, origin, 32);
            for (var i = 1; i <= 4; i++)
                buffer.Append(new SignalObservation(signal, i, signal.Unit,
                    TimePoint.FromReplay(Start.AddMilliseconds(i * 20), i),
                    EvidenceQuality.Derived, new EvidenceRef(FrameEvidence), false));
            var series = buffer.Snapshot();
            return Task.FromResult(new WaveWorkbenchCapture(
                new WaveEvidenceRef(EvidenceRef.New(), series.SourceRevision, series.Signal.Unit,
                    TimeQuality.ReplayDerived), series, "Replay", WaveInputKind.Replay));
        }
        public Task<WaveWorkbenchCapture> CaptureLiveAsync(SignalRef signal, TimeSpan duration,
            int maxSamples, CancellationToken ct) => throw new NotSupportedException();
        public WaveReference? ReadReference(SignalRef signal) => null;
        public WaveReference SaveReference(WaveSeries capture, string name) => throw new NotSupportedException();
        public Task<WaveWorkbenchComparison> CompareAsync(WaveWorkbenchCapture capture,
            WaveReference reference, WaveComparisonConfig config, CancellationToken ct) =>
            throw new NotSupportedException();
        public WaveArchiveReport ReadArchiveReport(int maxRows, string? filter = null)
        {
            var id = FrameEvidence.ToString("D");
            var rows = filter is null || id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                ? new[] { new WaveRawFrameRow(1, Start, 20_000, "DutCan", "0x321", false,
                    "0100000000000000", 8, 0, id, "Current=1 A", "") }
                : Array.Empty<WaveRawFrameRow>();
            return new WaveArchiveReport(Guid.NewGuid(), new string('f', 64), new string('b', 64), 1,
                Start, 1, 0, rows.Length, false, "ReplayDerived", "OK", rows);
        }
        public byte[] ReadVerifiedOriginalArchive() => Array.Empty<byte>();
    }

    private sealed class FailingReplayBackend : IWaveWorkbenchBackend
    {
        private readonly SignalRef _signal = new(
            new AssetRef(AssetId.New(), AssetRevision.Create(1),
                RevisionFingerprint.FromSha256(new string('c', 64))),
            BusRole.DutCan, 0x321, "Current", "A");
        public bool CanCaptureLive => false;
        public IReadOnlyList<WaveSignalChoice> ListSignals() =>
            [new WaveSignalChoice(_signal, "Current [A]")];
        public Task<WaveWorkbenchCapture> ReplayAsync(SignalRef signal, int samples, CancellationToken ct) =>
            throw new WaveCaptureException("CAN_REPLAY_ARCHIVE_REQUIRED", "No archived RX frames");
        public Task<WaveWorkbenchCapture> CaptureLiveAsync(SignalRef signal, TimeSpan duration,
            int maxSamples, CancellationToken ct) => throw new NotSupportedException();
        public WaveReference? ReadReference(SignalRef signal) => null;
        public WaveReference SaveReference(WaveSeries capture, string name) => throw new NotSupportedException();
        public Task<WaveWorkbenchComparison> CompareAsync(WaveWorkbenchCapture capture,
            WaveReference reference, WaveComparisonConfig config, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static string Read(ZipArchive zip, string path)
    {
        using var reader = new StreamReader(zip.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }

    private static string NewFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "autotest-wave-r0-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
