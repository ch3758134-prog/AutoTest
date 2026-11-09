using System.Runtime.CompilerServices;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

/// <summary>
/// Live-ready input adapter only; INTEGRATION provides the one shared decoded-signal feed,
/// current DBC revision check and connection generation. This adapter never opens USB/CAN.
/// A reconnect or DBC edit terminates the old feed without pretending to have valid samples.
/// </summary>
public sealed class WaveSharedStreamAdapter : IWaveSignalSource
{
    private readonly Func<SignalRef, CancellationToken, ValueTask<WaveSignalSubscription>> _open;
    private readonly Func<SignalRef, bool> _revisionIsCurrent;
    private readonly Func<long> _sessionGeneration;

    public WaveSharedStreamAdapter(
        Func<SignalRef, CancellationToken, ValueTask<WaveSignalSubscription>> open,
        Func<SignalRef, bool> revisionIsCurrent, Func<long> sessionGeneration)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _revisionIsCurrent = revisionIsCurrent ?? throw new ArgumentNullException(nameof(revisionIsCurrent));
        _sessionGeneration = sessionGeneration ?? throw new ArgumentNullException(nameof(sessionGeneration));
    }

    public async ValueTask<WaveSignalSubscription> OpenAsync(SignalRef signal, CancellationToken ct)
    {
        WaveTimeQuality.ValidateSignal(signal);
        ct.ThrowIfCancellationRequested();
        if (!_revisionIsCurrent(signal)) throw new WaveCaptureException("DBC_REVISION_STALE", "Signal DBC revision changed.");
        var generation = _sessionGeneration();
        var opened = await _open(signal, ct).ConfigureAwait(false);
        if (opened is null || opened.Observations is null)
            throw new WaveCaptureException("SIGNAL_SOURCE_UNAVAILABLE", "No shared decoded-signal feed.");
        if (opened.Origin.Quality == TimeQuality.Unknown)
            throw new WaveCaptureException("UNKNOWN_CLOCK", "Shared decoded feed has unknown time quality.");
        return new WaveSignalSubscription(opened.Origin,
            Validate(opened.Observations, signal, generation, ct));
    }

    private async IAsyncEnumerable<SignalObservation> Validate(
        IAsyncEnumerable<SignalObservation> observations, SignalRef signal, long generation,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var sample in observations.WithCancellation(ct).ConfigureAwait(false))
        {
            if (_sessionGeneration() != generation)
                throw new WaveCaptureException("SESSION_GENERATION_CHANGED", "Device reconnected during capture.");
            if (!_revisionIsCurrent(signal))
                throw new WaveCaptureException("DBC_REVISION_STALE", "DBC changed during capture.");
            if (sample is null || sample.Signal != signal)
                throw new WaveCaptureException("SIGNAL_IDENTITY_MISMATCH", "Decoded input does not match pinned signal.");
            yield return sample;
        }
        if (_sessionGeneration() != generation)
            throw new WaveCaptureException("SESSION_GENERATION_CHANGED", "Device reconnected during capture.");
        if (!_revisionIsCurrent(signal))
            throw new WaveCaptureException("DBC_REVISION_STALE", "DBC changed during capture.");
    }
}
