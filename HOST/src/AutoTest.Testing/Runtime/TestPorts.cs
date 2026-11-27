using System.IO;
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
    // The real station reports asynchronous physical sender failures before
    // the next step. Default preserves source compatibility for pure adapters.
    ValueTask CheckHealthAsync(CancellationToken ct) => ValueTask.CompletedTask;
    // Real station implementations stop all active ECU CAN periodic senders.
    // Other adapters fail explicitly instead of pretending to stop hardware.
    ValueTask StopPeriodicAsync(CancellationToken ct) =>
        throw new NotSupportedException("当前台架不支持停止周期 CAN 发送。");
    ValueTask CleanupAsync(CancellationToken ct);
}
