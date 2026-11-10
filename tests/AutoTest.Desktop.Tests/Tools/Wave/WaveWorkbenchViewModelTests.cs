using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Specialists.Wave;
using Xunit;

namespace AutoTest.Desktop.Tests.Tools.Wave;

public sealed class WaveWorkbenchViewModelTests
{
    private static readonly SignalRef First = new(
        new AssetRef(AssetId.New(), AssetRevision.Create(1),
            RevisionFingerprint.FromSha256(new string('c', 64))),
        BusRole.DutCan, 0x211, "Current", "A");
    private static readonly SignalRef Second = First with { SignalName = "Voltage", Unit = "V" };
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 6, 0, 0, TimeSpan.Zero);

    private static WaveSeries MakeSeries(SignalRef signal)
    {
        var buffer = new WaveSampleBuffer(signal, TimePoint.FromReplay(T0, 0), 32);
        for (var i = 1; i <= 10; i++)
            buffer.Append(new SignalObservation(signal, i % 5, signal.Unit,
                TimePoint.FromReplay(T0.AddMilliseconds(i * 20), i),
                EvidenceQuality.Derived, EvidenceRef.New(), false));
        return buffer.Snapshot();
    }

    private sealed class FakeBackend : IWaveWorkbenchBackend
    {
        public bool CanCaptureLive { get; set; }
        public int ReplayCount { get; private set; }
        public int CompareCount { get; private set; }
        public bool Stale { get; set; }
        public bool HoldReplayUntilCancellation { get; set; }
        public TaskCompletionSource<bool> ReplayStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<SignalRef, WaveReference> _references = new();
        public IReadOnlyList<WaveSignalChoice> ListSignals() => Stale ?
            [new WaveSignalChoice(First with { DbcAsset = First.DbcAsset with
                { Revision = AssetRevision.Create(2) } }, "Current new rev")] :
            [new WaveSignalChoice(First, "Current [A]"),
             new WaveSignalChoice(Second, "Voltage [V]")];
        public async Task<WaveWorkbenchCapture> ReplayAsync(SignalRef signal, int samples, CancellationToken ct)
        {
            ReplayCount++;
            ReplayStarted.TrySetResult(true);
            if (HoldReplayUntilCancellation)
                await Task.Delay(Timeout.Infinite, ct);
            var series = MakeSeries(signal);
            return new WaveWorkbenchCapture(
                new WaveEvidenceRef(EvidenceRef.New(), series.SourceRevision, signal.Unit,
                    TimeQuality.ReplayDerived), series, "Replay / Shared CAN", WaveInputKind.Replay);
        }
        public Task<WaveWorkbenchCapture> CaptureLiveAsync(SignalRef signal,
            TimeSpan duration, int maxSamples, CancellationToken ct)
        {
            if (!CanCaptureLive) throw new InvalidOperationException("No live feed");
            return ReplayAsync(signal, 80, ct);
        }
        public WaveReference? ReadReference(SignalRef signal) =>
            _references.GetValueOrDefault(signal);
        public WaveReference SaveReference(WaveSeries capture, string name)
        {
            var reference = new WaveReference(new ReferenceRef(new AssetRef(AssetId.New(),
                AssetRevision.Create(1), RevisionFingerprint.FromSha256(new string('d', 64)))),
                name, capture);
            _references[capture.Signal] = reference;
            return reference;
        }
        public Task<WaveWorkbenchComparison> CompareAsync(WaveWorkbenchCapture capture,
            WaveReference reference, WaveComparisonConfig config, CancellationToken ct)
        {
            CompareCount++;
            return Task.FromResult(new WaveWorkbenchComparison(WaveComparator.Compare(capture.Series,
                reference.Series, config), EvidenceRef.New()));
        }
    }

    [Fact]
    public void NoBackendNeverEnablesFakeReplayOrLive()
    {
        var vm = new WaveWorkbenchViewModel();
        Assert.False(vm.ReplayCommand.CanExecute(null));
        Assert.False(vm.LiveCommand.CanExecute(null));
        Assert.Contains("未接入", vm.Status);
    }

    [Fact]
    public async Task MultiSignalReplayHasSeparateScaledTraceAndCursorMeasurement()
    {
        var backend = new FakeBackend();
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(backend);
        Assert.Equal(2, vm.Signals.Count);
        Assert.False(vm.LiveCommand.CanExecute(null));
        vm.Signals[1].IsSelected = true;
        await vm.ReplaySelectedAsync();
        Assert.Equal(2, backend.ReplayCount);
        Assert.Equal(2, vm.Traces.Count);
        Assert.Contains("Replay", vm.Origin);
        Assert.Contains("0.2", vm.ViewRange);
        Assert.All(vm.Traces, x => Assert.NotEmpty(x.Points));
        vm.SetCursorFraction(.2, false);
        vm.SetCursorFraction(.8, true);
        Assert.Contains("Δt", vm.CursorInfo);
        vm.Zoom(.5);
        Assert.NotEmpty(vm.Traces);
        vm.ResetZoom();
        Assert.Equal(2, vm.CaptureCount);
    }

    [Fact]
    public async Task ReferenceSaveLoadAndComparisonBindRealBackendResults()
    {
        var backend = new FakeBackend();
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(backend);
        await vm.ReplaySelectedAsync();
        await vm.SaveReferenceSelectedAsync();
        Assert.Contains("参考曲线已保存", vm.Status);
        await vm.LoadReferenceSelectedAsync();
        Assert.Contains("重新加载", vm.Status);
        await vm.CompareSelectedAsync();
        Assert.Single(vm.Results);
        Assert.Equal("PASS", vm.Results[0].Verdict);
        Assert.Equal(1, backend.CompareCount);
        Assert.NotEqual("—", vm.Results[0].EvidenceId);
    }

    [Fact]
    public async Task DbcRevisionChangeInvalidatesDisplayedCapture()
    {
        var backend = new FakeBackend();
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(backend);
        await vm.ReplaySelectedAsync();
        Assert.Equal(1, vm.CaptureCount);
        backend.Stale = true;
        vm.RefreshSignals();
        Assert.Equal(0, vm.CaptureCount);
        Assert.Contains("DBC_REVISION_STALE", vm.Status);
        Assert.Empty(vm.Traces);
    }

    [Fact]
    public async Task InvalidToleranceCannotProducePass()
    {
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(new FakeBackend());
        await vm.ReplaySelectedAsync();
        await vm.SaveReferenceSelectedAsync();
        vm.ToleranceText = "NaN";
        await vm.CompareSelectedAsync();
        Assert.Empty(vm.Results);
        Assert.Contains("WAVE_ERROR", vm.Status);
    }
    [Fact]
    public async Task DbcRevisionChangeDuringReplayCancelsOldCaptureBeforeRefreshing()
    {
        var backend = new FakeBackend { HoldReplayUntilCancellation = true };
        var vm = new WaveWorkbenchViewModel();
        vm.BindBackend(backend);
        var operation = vm.ReplaySelectedAsync();
        await backend.ReplayStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(vm.Busy);
        backend.Stale = true;
        vm.RefreshSignals();
        await operation.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(vm.Busy);
        Assert.Equal(0, vm.CaptureCount);
        Assert.Single(vm.Signals);
        Assert.Contains("DBC_REVISION_CHANGED", vm.Status);
    }

}
