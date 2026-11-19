using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

/// <summary>
/// Adapter boundary for one shared decoded-signal broadcast. Integration owns the actual
/// CAN/DBC subscription and clock mapping; Wave never opens an independent CAN receiver.
/// </summary>
public interface IWaveSignalSource
{
    ValueTask<WaveSignalSubscription> OpenAsync(SignalRef signal, CancellationToken ct);
}

public sealed record WaveSignalSubscription(TimePoint Origin,
    IAsyncEnumerable<SignalObservation> Observations);

public sealed class WaveCaptureException : IOException
{
    public string Code { get; }
    public WaveCaptureException(string code, string message) : base(message) => Code = code;
}

public sealed class WaveCaptureService : IWaveformService
{
    private const int MaxConcurrentSessions = 16;
    private readonly SemaphoreSlim _sessionSlots = new(MaxConcurrentSessions, MaxConcurrentSessions);
    private readonly IWaveSignalSource _source;
    private readonly WaveEvidenceStore _evidence;
    private readonly WaveReferenceRepository _references;
    private readonly ConcurrentDictionary<WaveCaptureId, CaptureSession> _sessions = new();

    private sealed record CaptureSession(WaveSampleBuffer Buffer,
        CancellationTokenSource StopSource, Task<WaveSeries> Completion);

    public WaveCaptureService(IWaveSignalSource source, WaveEvidenceStore evidence,
        WaveReferenceRepository references)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _references = references ?? throw new ArgumentNullException(nameof(references));
    }

    public async ValueTask<WaveCaptureId> StartAsync(SignalRef signal,
        WaveCaptureConfig config, CancellationToken ct)
    {
        WaveTimeQuality.ValidateSignal(signal);
        if (config is null || config.Duration <= TimeSpan.Zero ||
            config.Duration > TimeSpan.FromHours(1) ||
            config.MaxSamples is < 2 or > 5_000_000)
            throw new ArgumentOutOfRangeException(nameof(config), "Invalid waveform capture window or capacity.");
        ct.ThrowIfCancellationRequested();
        if (!_sessionSlots.Wait(0))
            throw new WaveCaptureException("TOO_MANY_CAPTURES", "Wave session capacity exhausted.");
        WaveSignalSubscription stream;
        try { stream = await _source.OpenAsync(signal, ct).ConfigureAwait(false); }
        catch { _sessionSlots.Release(); throw; }
        if (stream is null || stream.Observations is null)
        {
            _sessionSlots.Release();
            throw new WaveCaptureException("SIGNAL_SOURCE_UNAVAILABLE", "Shared decoded signal source unavailable.");
        }
        if (stream.Origin.Quality == TimeQuality.Unknown)
        {
            _sessionSlots.Release();
            throw new WaveCaptureException("UNKNOWN_CLOCK", "Shared signal clock quality is unknown.");
        }
        var buffer = new WaveSampleBuffer(signal, stream.Origin, config.MaxSamples);
        var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(config.Duration);
        async Task<WaveSeries> CollectAsync()
        {
            try
            {
                await foreach (var item in stream.Observations.WithCancellation(stop.Token)
                    .ConfigureAwait(false))
                {
                    // Timestamp-window bound is deterministic for replay and independent of wall speed.
                    if (item.Time.Utc - stream.Origin.Utc > config.Duration) break;
                    buffer.Append(item);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Duration or an explicit StopAsync: retain the partial series as evidence.
            }
            return buffer.Snapshot();
        }
        var id = WaveCaptureId.New();
        var state = new CaptureSession(buffer, stop, CollectAsync());
        if (!_sessions.TryAdd(id, state))
        {
            stop.Cancel(); stop.Dispose();
            _sessionSlots.Release();
            throw new InvalidOperationException("Wave capture identity collision.");
        }
        return id;
    }

    public WaveSeries Observe(WaveCaptureId id) =>
        _sessions.TryGetValue(id, out var session)
            ? session.Buffer.Snapshot()
            : throw new KeyNotFoundException("Wave capture not active: " + id.Value);

    public ValueTask<WaveEvidenceRef> StopAsync(WaveCaptureId id, CancellationToken ct) =>
        FinishAsync(id, true, null, null, ct);

    public async ValueTask<WaveEvidenceRef> CaptureAsync(SignalRef signal,
        WaveCaptureConfig config, CancellationToken ct) =>
        await CaptureForRunAsync(signal, config, null, null, ct).ConfigureAwait(false);

    public async ValueTask<WaveEvidenceRef> CaptureForRunAsync(SignalRef signal,
        WaveCaptureConfig config, RunId? run, StepId? step, CancellationToken ct)
    {
        var id = await StartAsync(signal, config, ct).ConfigureAwait(false);
        return await FinishAsync(id, false, run, step, ct).ConfigureAwait(false);
    }

    private async ValueTask<WaveEvidenceRef> FinishAsync(WaveCaptureId id,
        bool stop, RunId? run, StepId? step, CancellationToken ct)
    {
        if (!_sessions.TryRemove(id, out var state))
            throw new KeyNotFoundException("Wave capture session no longer exists.");
        try
        {
            if (stop) state.StopSource.Cancel();
            var series = await state.Completion.WaitAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (series.Samples.Length == 0)
                throw new WaveCaptureException("NO_SAMPLES", "Wave capture completed without valid samples.");
            var evidenceId = EvidenceRef.New();
            _evidence.Write(new WaveEvidenceDocument(evidenceId, "Capture", series,
                null, null, null, null, null, null, run, step, DateTimeOffset.UtcNow));
            return new WaveEvidenceRef(evidenceId, series.SourceRevision, series.Signal.Unit,
                series.Origin.Quality);
        }
        finally
        {
            state.StopSource.Cancel();
            state.StopSource.Dispose();
            _sessionSlots.Release();
        }
    }

    public ValueTask<ComparisonResult> CompareAsync(WaveEvidenceRef evidence,
        ReferenceRef reference, CompareProfile profile) =>
        CompareForRunAsync(evidence, reference, profile, null, null);

    public ValueTask<ComparisonResult> CompareForRunAsync(WaveEvidenceRef evidence,
        ReferenceRef reference, CompareProfile profile, RunId? run, StepId? step)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        return CompareWithConfigForRunAsync(evidence, reference,
            WaveComparisonConfig.FromContract(profile), run, step);
    }

    /// <summary>
    /// Use the complete authored waveform criteria (including time/coverage constraints)
    /// instead of flattening a wave configuration down to a two-field CompareProfile.
    /// The analysis and its criteria are persisted as separate evidence from the test verdict.
    /// </summary>
    public ValueTask<ComparisonResult> CompareWithConfigForRunAsync(WaveEvidenceRef evidence,
        ReferenceRef reference, WaveComparisonConfig config, RunId? run = null, StepId? step = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        WaveEvidenceDocument captured;
        WaveReference expected;
        try
        {
            captured = _evidence.Read(evidence.Evidence);
            if (captured.Kind != "Capture" ||
                !captured.Actual.SourceRevision.Matches(evidence.SourceRevision) ||
                captured.Actual.Signal.Unit != evidence.Unit ||
                captured.Actual.Origin.Quality != evidence.Quality)
                throw new InvalidDataException("Pinned capture identity/version/unit differs.");
            if ((run.HasValue && captured.Run != run) ||
                (step.HasValue && captured.Step != step))
                throw new InvalidDataException("Run/step context differs from captured evidence.");
            expected = _references.LoadReference(reference);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return ValueTask.FromResult(new ComparisonResult(Verdict.Error, null,
                "WAVE_REFERENCE_OR_EVIDENCE_UNAVAILABLE: " + ex.Message,
                ImmutableArray.Create(evidence.Evidence)));
        }

        var evaluation = WaveComparator.Compare(captured.Actual, expected.Series, config);
        var comparisonId = EvidenceRef.New();
        _evidence.Write(new WaveEvidenceDocument(comparisonId, "Comparison", captured.Actual,
            expected.Series, reference, expected.Name, evidence.Evidence, config, evaluation,
            run ?? captured.Run, step ?? captured.Step, DateTimeOffset.UtcNow));
        return ValueTask.FromResult(new ComparisonResult(evaluation.Verdict, evaluation.Coverage,
            evaluation.Code + ": " + evaluation.Reason,
            ImmutableArray.Create(evidence.Evidence, comparisonId)));
    }

    public WaveEvidenceDocument ReadEvidence(EvidenceRef id) => _evidence.Read(id);
}

/// <summary>
/// Fixed synthetic replay: deterministic SignalObservation sequence. It cannot claim physical CAN.
/// The fixture must explicitly declare Replay or Simulation time provenance.
/// </summary>
public sealed class WaveReplaySignalSource : IWaveSignalSource
{
    private readonly SignalRef _signal;
    private readonly TimePoint _origin;
    private readonly ImmutableArray<SignalObservation> _observations;

    public WaveReplaySignalSource(SignalRef signal, TimePoint origin,
        IEnumerable<SignalObservation> observations)
    {
        WaveTimeQuality.ValidateSignal(signal);
        if (!((origin.Source == TimeSource.Replay && origin.Quality == TimeQuality.ReplayDerived) ||
              (origin.Source == TimeSource.Simulation && origin.Quality == TimeQuality.Simulated)))
            throw new ArgumentException("Mock source must preserve Replay/Simulation provenance.");
        _signal = signal;
        _origin = origin;
        _observations = observations?.ToImmutableArray() ??
            throw new ArgumentNullException(nameof(observations));
    }

    public ValueTask<WaveSignalSubscription> OpenAsync(SignalRef signal, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (signal != _signal)
            throw new WaveCaptureException("SIGNAL_NOT_FOUND", "Replay SignalRef differs from fixture.");
        return ValueTask.FromResult(new WaveSignalSubscription(_origin, Enumerate(ct)));
    }

    private async IAsyncEnumerable<SignalObservation> Enumerate(
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var observation in _observations)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return observation;
        }
    }
}
