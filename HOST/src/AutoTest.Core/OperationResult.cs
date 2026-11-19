using System.Collections.Immutable;

namespace AutoTest.Core;

public readonly record struct Unit
{
    public static Unit Value => default;
}
public enum Outcome { Accepted, Rejected, Uncertain }
public enum EvidenceQuality { DirectMeasured, HostObserved, Derived, AcknowledgedOnly, Unknown }
public enum Verdict { Pass, Fail, Inconclusive, Error, Skipped }
public sealed record OperationFailure(string Code, string Message, bool SideEffectMayHaveOccurred);

// Accepted describes request handling only; it never establishes a physical effect.
public sealed record OperationResult<T>(
    Outcome Outcome, T? Value, OperationFailure? Failure, Guid OperationId,
    ImmutableArray<EvidenceRef> EvidenceRefs)
{
    public string? FailureCode => Failure?.Code;
    public static OperationResult<T> Accepted(T value, Guid? operationId = null,
        ImmutableArray<EvidenceRef> evidence = default) =>
        new(Outcome.Accepted, value, null, operationId ?? Guid.NewGuid(),
            evidence.IsDefault ? ImmutableArray<EvidenceRef>.Empty : evidence);
    public static OperationResult<T> Rejected(string code, string message, Guid? operationId = null) =>
        new(Outcome.Rejected, default, new(code, message, false), operationId ?? Guid.NewGuid(),
            ImmutableArray<EvidenceRef>.Empty);
    public static OperationResult<T> Uncertain(string code, string message, Guid? operationId = null,
        bool sideEffectMayHaveOccurred = true) =>
        new(Outcome.Uncertain, default, new(code, message, sideEffectMayHaveOccurred),
            operationId ?? Guid.NewGuid(), ImmutableArray<EvidenceRef>.Empty);
}

public static class OperationResult
{
    public static OperationResult<Unit> Accepted(Guid? operationId = null) =>
        OperationResult<Unit>.Accepted(Unit.Value, operationId);
    public static OperationResult<Unit> Rejected(string code, string message) =>
        OperationResult<Unit>.Rejected(code, message);
    public static OperationResult<Unit> Uncertain(string code, string message) =>
        OperationResult<Unit>.Uncertain(code, message);
}
