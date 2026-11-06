using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

public readonly record struct WaveCaptureId(Guid Value)
{
    public static WaveCaptureId New() => new(Guid.NewGuid());
}

// Retain the originating signal fact: a plotted value never substitutes for raw CAN evidence.
public sealed record WaveCapturedSample(WaveSample Sample, EvidenceRef SourceEvidence,
    EvidenceQuality InputQuality);

public sealed record WaveSeries(SignalRef Signal, TimePoint Origin,
    ImmutableArray<WaveCapturedSample> Samples, long ObservedEvents, int RejectedEvents,
    bool Overflow, bool NonMonotonic, bool MixedClock, bool InvalidInput, bool HasGap,
    ImmutableArray<EvidenceRef> InputEvidence)
{
    public RevisionFingerprint SourceRevision => Signal.DbcAsset.Fingerprint;
    public bool IsReplay => Origin.Source == TimeSource.Replay;
    public bool IsSimulation => Origin.Source == TimeSource.Simulation;
}

/// <summary>Bounded, thread-safe append-only observation buffer; never sorts incoming evidence.</summary>
public sealed class WaveSampleBuffer
{
    private readonly object _gate = new();
    private readonly SignalRef _signal;
    private readonly TimePoint _origin;
    private readonly int _capacity;
    private readonly List<WaveCapturedSample> _samples = new();
    private readonly List<EvidenceRef> _inputEvidence = new();
    private long _observed;
    private int _rejected;
    private bool _overflow, _nonMonotonic, _mixedClock, _invalidInput, _hasGap;
    private TimePoint? _previous;

    public WaveSampleBuffer(SignalRef signal, TimePoint origin, int maxSamples)
    {
        if (maxSamples < 2 || maxSamples > 5_000_000)
            throw new ArgumentOutOfRangeException(nameof(maxSamples));
        WaveTimeQuality.ValidateSignal(signal);
        _signal = signal;
        _origin = origin;
        _capacity = maxSamples;
    }

    public void Append(SignalObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (_gate)
        {
            _observed++;
            if (_inputEvidence.Count < _capacity + 1) _inputEvidence.Add(observation.Evidence);
            if (observation.Signal != _signal ||
                !string.Equals(observation.Unit, _signal.Unit, StringComparison.Ordinal))
            {
                _invalidInput = true;
                _rejected++;
                return;
            }
            if (observation.Time.Source != _origin.Source ||
                observation.Time.Quality != _origin.Quality)
                _mixedClock = true;
            if (observation.Quality is EvidenceQuality.Unknown or EvidenceQuality.AcknowledgedOnly ||
                observation.Evidence.Value == Guid.Empty)
                _invalidInput = true;
            if (observation.HasGap) _hasGap = true;
            if (!observation.Value.HasValue || !double.IsFinite(observation.Value.Value))
            {
                _invalidInput = true;
                _rejected++;
                return;
            }
            if (observation.Time.Utc < _origin.Utc ||
                observation.Time.MonotonicTicks < _origin.MonotonicTicks ||
                (_previous is { } prev && (observation.Time.Utc <= prev.Utc ||
                observation.Time.MonotonicTicks <= prev.MonotonicTicks)))
                _nonMonotonic = true;
            _previous = observation.Time;
            if (_samples.Count >= _capacity)
            {
                _overflow = true;
                return;
            }
            _samples.Add(new WaveCapturedSample(
                new WaveSample(observation.Value.Value, observation.Unit,
                    observation.Time, observation.HasGap), observation.Evidence, observation.Quality));
        }
    }

    public WaveSeries Snapshot()
    {
        lock (_gate)
            return new WaveSeries(_signal, _origin, _samples.ToImmutableArray(), _observed,
                _rejected, _overflow, _nonMonotonic, _mixedClock, _invalidInput, _hasGap,
                _inputEvidence.ToImmutableArray());
    }
}
