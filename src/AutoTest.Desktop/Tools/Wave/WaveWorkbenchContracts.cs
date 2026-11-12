using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Tools.Wave;

public sealed record WaveSignalChoice(SignalRef Signal, string DisplayName);
public enum WaveInputKind { Unknown, Physical, Replay, Mock }
public sealed record WaveWorkbenchCapture(WaveEvidenceRef Evidence, WaveSeries Series,
    string SourceLabel, WaveInputKind Kind);
public sealed record WaveWorkbenchComparison(WaveComparisonDetails Details, EvidenceRef Evidence);

/// <summary>One workbench backend per shared HOST lifetime. It never creates USB or CAN sessions.</summary>
public interface IWaveWorkbenchBackend
{
    bool CanCaptureLive { get; }
    IReadOnlyList<WaveSignalChoice> ListSignals();
    Task<WaveWorkbenchCapture> ReplayAsync(SignalRef signal, int samples, CancellationToken ct);
    Task<WaveWorkbenchCapture> CaptureLiveAsync(SignalRef signal, TimeSpan duration,
        int maxSamples, CancellationToken ct);
    WaveReference? ReadReference(SignalRef signal);
    WaveReference SaveReference(WaveSeries capture, string name);
    Task<WaveWorkbenchComparison> CompareAsync(WaveWorkbenchCapture capture,
        WaveReference reference, WaveComparisonConfig config, CancellationToken ct);
}

/// <summary>Optional, explicitly archived-frame Replay support; archive is never a physical source.</summary>
public interface IWaveArchiveSource
{
    bool HasRecordedArchive { get; }
    void OpenArchiveFile(string path);
}

/// <summary>Generated CAN examples are Mock, not saved-frame Replay.</summary>
public interface IWaveMockGenerator
{
    Task<WaveWorkbenchCapture> RunMockAsync(SignalRef signal, int samples, CancellationToken ct);
}
