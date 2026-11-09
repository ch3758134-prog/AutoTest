using System.IO;
using System.IO.Compression;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Wave;

public sealed class WaveIssueWorkbenchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
    private sealed class IntervalBackend : IWaveWorkbenchBackend, IWaveArchiveDiagnosticSource, IWaveArchiveSource
    {
        private readonly SignalRef _signal = new(
            new AssetRef(AssetId.New(), AssetRevision.Create(1),
                RevisionFingerprint.FromSha256(new string('f', 64))),
            BusRole.DutCan, 0x321, "Current", "A");
        private readonly Dictionary<SignalRef, WaveReference> _references = new();
        public bool CanCaptureLive => false;
        public bool HasRecordedArchive => true;
        public Guid FrameId { get; } = Guid.NewGuid();
        public IReadOnlyList<WaveSignalChoice> ListSignals() => [new(_signal, "Current [A]")];
        public void OpenArchiveFile(string path) { }
        private WaveSeries Series(bool anomalous)
        {
            var buffer = new WaveSampleBuffer(_signal, TimePoint.FromReplay(T0, 0), 32);
            for (var i = 1; i <= 8; i++)
                buffer.Append(new SignalObservation(_signal, anomalous && i is 4 or 5 ? 2d : 1d,
                    "A", TimePoint.FromReplay(T0.AddMilliseconds(i * 20), i),
                    EvidenceQuality.Derived, new EvidenceRef(FrameId), false));
            return buffer.Snapshot();
        }
        public Task<WaveWorkbenchCapture> ReplayAsync(SignalRef signal, int samples, CancellationToken ct)
        {
            var wave = Series(true);
            return Task.FromResult(new WaveWorkbenchCapture(
                new WaveEvidenceRef(EvidenceRef.New(), wave.SourceRevision, "A", TimeQuality.ReplayDerived),
                wave, "Replay / saved CAN", WaveInputKind.Replay));
        }
        public Task<WaveWorkbenchCapture> CaptureLiveAsync(SignalRef signal, TimeSpan duration,
            int maxSamples, CancellationToken ct) => throw new NotSupportedException();
        public WaveReference? ReadReference(SignalRef signal) => _references.GetValueOrDefault(signal);
        public WaveReference SaveReference(WaveSeries capture, string name)
        {
            var reference = new WaveReference(new ReferenceRef(new AssetRef(AssetId.New(),
                AssetRevision.Create(1), RevisionFingerprint.FromSha256(new string('a', 64)))),
                name, Series(false));
            _references[capture.Signal] = reference;
            return reference;
        }
        public Task<WaveWorkbenchComparison> CompareAsync(WaveWorkbenchCapture capture,
            WaveReference reference, WaveComparisonConfig config, CancellationToken ct) =>
            Task.FromResult(new WaveWorkbenchComparison(
                WaveComparator.Compare(capture.Series, reference.Series, config), EvidenceRef.New()));
        public WaveArchiveReport ReadArchiveReport(int maxRows, string? filter = null)
        {
            var id = FrameId.ToString("D");
            var rows = filter is null || id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                ? new [] { new WaveRawFrameRow(1, T0, 20_000, "DutCan", "0x321", false,
                    "0100000000000000", 8, 0, id, "Current=1 A", "") }
                : Array.Empty<WaveRawFrameRow>();
            return new WaveArchiveReport(Guid.NewGuid(), new string('e', 64),
                new string('f', 64), 1, T0, 1, 0, rows.Length, false, "ReplayDerived", "OK", rows);
        }
        public byte[] ReadVerifiedOriginalArchive() => Array.Empty<byte>();
    }

    [Fact]
    public async Task CompareFailureShowsIntervalAndCanFocusOriginalEvidence()
    {
        var backend = new IntervalBackend();
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(backend);
        await vm.ReplaySelectedAsync();
        Assert.Empty(vm.Issues);
        await vm.SaveReferenceSelectedAsync();
        await vm.CompareSelectedAsync();
        Assert.Equal("FAIL", Assert.Single(vm.Results).Verdict);
        var issue = Assert.Single(vm.Issues);
        Assert.Equal("MISMATCH", issue.Category);
        Assert.NotEmpty(vm.IssueMarkers);
        vm.SelectedIssue = issue;
        await vm.FocusIssueSelectedAsync();
        Assert.Equal(backend.FrameId.ToString("D"), vm.TraceFilter);
        Assert.Single(vm.RawFrames);
        Assert.Contains("原始帧", vm.Status);
    }

    [Fact]
    public async Task ToleranceChangeInvalidatesPriorComparisonIntervals()
    {
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(new IntervalBackend());
        await vm.ReplaySelectedAsync();
        await vm.SaveReferenceSelectedAsync();
        await vm.CompareSelectedAsync();
        Assert.NotEmpty(vm.Issues);
        vm.ToleranceText = "3";
        Assert.Empty(vm.Results);
        Assert.Empty(vm.Issues);
        Assert.Empty(vm.IssueMarkers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IssueExportIsCompleteAndSensitiveValuesAreRedacted(bool includeRaw)
    {
        var folder = Path.Combine(Path.GetTempPath(), "wave-issue-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "issues.zip");
            var issues = Enumerable.Range(0, 1100).Select(i => new WaveDiagnosticIssue(
                "Current", "MISMATCH", "OUT_OF_TOLERANCE_SAMPLED_WINDOW", i * .01, i * .01,
                1, 1.23, .1, Guid.NewGuid().ToString("D"), "UNKNOWN", "UNKNOWN",
                "sensitive decoded value 123")).ToArray();
            WaveLabExportService.Export(path, null, null, Array.Empty<WaveOperationLogRow>(),
                Array.Empty<WaveWorkbenchCapture>(), Array.Empty<WaveResultRow>(),
                "Replay", "ReplayDerived", includeRawEvidence: includeRaw, issues: issues);
            using var zip = ZipFile.OpenRead(path);
            using var reader = new StreamReader(zip.GetEntry("issues.csv")!.Open());
            var csv = reader.ReadToEnd();
            Assert.Equal(1101, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Contains("OUT_OF_TOLERANCE_SAMPLED_WINDOW", csv);
            if (includeRaw) Assert.Contains("sensitive decoded value 123", csv);
            else
            {
                Assert.DoesNotContain("sensitive decoded value 123", csv);
                Assert.DoesNotContain("1.23", csv);
                Assert.Contains("[REDACTED]", csv);
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}
