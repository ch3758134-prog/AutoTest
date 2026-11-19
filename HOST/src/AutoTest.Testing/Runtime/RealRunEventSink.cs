using AutoTest.Core;
using AutoTest.Testing.Authoring;

namespace AutoTest.Testing.Runtime;

/// <summary>Executed occurrence, not an authored step identity. Loop children receive new IDs.</summary>
public sealed record RealStepStarted(
    RunId Run, StepId Step, Guid StepOccurrenceId, Guid? ParentOccurrenceId,
    TestPhase Phase, string Type, string Name, string ExecutionPath,
    int? LoopIteration, DateTimeOffset StartedUtc);

/// <summary>
/// Awaited for every actual step boundary, never called on a fire-and-forget task.
/// An adapter must refuse to advance physical operations when a required fact is not durable.
/// TEST does not depend on the RECORD database or on Desktop.
/// </summary>
public interface IRealRunEventSink
{
    ValueTask StepStartedAsync(RealStepStarted started, CancellationToken ct);
    ValueTask StepCompletedAsync(RunId run, RealStepFact completed, CancellationToken ct);
    ValueTask RunCompletedAsync(RealRunResult completed, CancellationToken ct) => ValueTask.CompletedTask;
}
