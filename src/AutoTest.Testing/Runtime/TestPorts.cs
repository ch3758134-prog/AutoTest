using AutoTest.Contracts;
using AutoTest.Testing.Authoring;

namespace AutoTest.Testing.Runtime;

public enum TestSignalStatus
{
    Valid, NoFrame, Stale, DecodeError, LostFrames, Disconnected, Timeout
}
public enum TestReadMode { Latest, Next }

/// <summary>Actual signal data, provenance and availability are separate from a test verdict.</summary>
public sealed record TestSignalResult(TestSignalStatus Status, SignalObservation? Observation,
    string Diagnostic)
{
    public bool HasUsableValue => Status == TestSignalStatus.Valid &&
        Observation?.Value is double number && double.IsFinite(number);
}

public interface ITestSignalPort
{
    Task<TestSignalResult> ReadAsync(TestTargetMapping target, TestReadMode mode,
        TimeSpan timeout, TimeSpan maxAge, CancellationToken ct);
    IAsyncEnumerable<TestSignalResult> ObserveAsync(TestTargetMapping target,
        CancellationToken ct);
}

/// <summary>Real operations must return a device fact, not a DUT pass/fail judgement.</summary>
public sealed record TestOperationResult(bool Acknowledged, bool StateKnown,
    string Code, string Diagnostic, DateTimeOffset At, string? Evidence = null);

public interface ITestActuationPort
{
    ValueTask<TestOperationResult> WriteAsync(TestTargetMapping target, string action,
        CancellationToken ct);
    ValueTask CleanupAsync(CancellationToken ct);
}
