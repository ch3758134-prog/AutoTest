using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Wave;

public sealed record WaveStepRequest(RunId Run, StepId Step, SignalRef Signal,
    WaveCaptureConfig Capture, ReferenceRef Reference, CompareProfile Compare);
public sealed record WaveStepResult(RunId Run, StepId Step, Verdict Verdict,
    string Reason, double? Coverage, ImmutableArray<EvidenceRef> Evidence);

/// <summary>Run-layer adapter. No physical actions, CAN receiver or implicit retry.</summary>
public sealed class WaveStepHandler
{
    private readonly WaveCaptureService _wave;
    public WaveStepHandler(WaveCaptureService wave) =>
        _wave = wave ?? throw new ArgumentNullException(nameof(wave));

    public async ValueTask<WaveStepResult> ExecuteAsync(WaveStepRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Run.Value == Guid.Empty || request.Step.Value == Guid.Empty)
            throw new ArgumentException("A wave run step must be tied to valid RunId/StepId.");
        WaveEvidenceRef? capture = null;
        try
        {
            capture = await _wave.CaptureForRunAsync(request.Signal, request.Capture,
                request.Run, request.Step, ct).ConfigureAwait(false);
            var comparison = await _wave.CompareForRunAsync(capture, request.Reference,
                request.Compare, request.Run, request.Step).ConfigureAwait(false);
            return new WaveStepResult(request.Run, request.Step, comparison.Verdict,
                comparison.Reason, comparison.Coverage, comparison.Evidence);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is WaveCaptureException or ArgumentException or
                                   IOException or InvalidDataException or KeyNotFoundException)
        {
            return new WaveStepResult(request.Run, request.Step, Verdict.Error,
                ex is WaveCaptureException wave ? wave.Code + ": " + wave.Message
                   : "WAVE_STEP_ERROR: " + ex.Message, null,
                capture is null ? ImmutableArray<EvidenceRef>.Empty
                    : ImmutableArray.Create(capture.Evidence));
        }
    }
}
