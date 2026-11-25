using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using AutoTest.Testing.Runtime;
using Xunit;

namespace AutoTest.Testing.Tests;

/// <summary>Interpreter contract tests only. These do not prove ECU bus timing
/// or physical actuation; that requires a Windows station and CAN analyzer.</summary>
public sealed class ContinuousProjectRunTests
{
    private sealed class Signals : ITestSignalPort
    {
        public Task<TestSignalResult> ReadAsync(TestTargetMapping target,
            TestReadMode mode, TimeSpan timeout, TimeSpan maxAge, CancellationToken ct) =>
            Task.FromResult(new TestSignalResult(TestSignalStatus.NoFrame, null, "none"));
        public async IAsyncEnumerable<TestSignalResult> ObserveAsync(
            TestTargetMapping target,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
    private sealed class Actuation : ITestActuationPort
    {
        public int Writes;
        public int CleanupCount;
        public ValueTask<TestOperationResult> WriteAsync(TestTargetMapping target,
            string action, CancellationToken ct)
        {
            Writes++;
            return ValueTask.FromResult(new TestOperationResult(true, false,
                "USB_ACK_ONLY", "No ECU RX assertion", DateTimeOffset.UtcNow));
        }
        public ValueTask CleanupAsync(CancellationToken ct)
        {
            CleanupCount++;
            return ValueTask.CompletedTask;
        }
    }
    private sealed class Waves : ITestWavePort
    {
        public int CleanupCount;
        public Task StartAsync(TestTargetMapping t, string key, int durationMs,
            CancellationToken ct) => Task.CompletedTask;
        public Task<TestWaveResult> StopAsync(string key, CancellationToken ct) =>
            Task.FromResult(new TestWaveResult(false, "NONE", null, null, "none"));
        public Task<TestWaveResult> AnalyzeAsync(string key, string reference,
            double tolerance, double coverage, CancellationToken ct) =>
            Task.FromResult(new TestWaveResult(false, "NONE", null, null, "none"));
        public Task CleanupAsync(CancellationToken ct)
        {
            CleanupCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProjectKeepsPhysicalPortBetweenActionOnlyAndJudgementCase()
    {
        var can = new TestTargetMapping("VCU_65MS", TestTargetKind.RawCan,
            default, 0, "", BusRole.DutCan, 0x160);
        var prepare = new StepEditTransaction(TestDocument.Create("Prepare") with
        { Mappings = ImmutableArray.Create(can) });
        prepare.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("Write").WithProperty("target", can.Name)
                .WithProperty("value", "HEX:0011223344556677")
                .WithProperty("periodMs", "65")
                .WithProperty("durationMs", "0"));
        var judgement = new StepEditTransaction(TestDocument.Create("Park"));
        judgement.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("Expect").WithProperty("left", "1")
                .WithProperty("operator", "==").WithProperty("right", "1"));

        var actions = new Actuation();
        var waves = new Waves();
        var first = new RealRunCoordinator(new Signals(), actions, waves)
            .StartAsync(new PlanCompiler().Prepare(prepare.Current, allowRealOperations: true),
                RunId.New(), null, default, retainProjectActuation: true);
        var started = await first;
        Assert.Equal(RealTestVerdict.CompletedNoAssertion, started.Verdict);
        Assert.Equal(1, actions.Writes);
        Assert.Equal(0, actions.CleanupCount);
        Assert.Equal(1, waves.CleanupCount);

        var second = await new RealRunCoordinator(new Signals(), actions, waves)
            .StartAsync(new PlanCompiler().Prepare(judgement.Current, allowRealOperations: true),
                RunId.New(), null, default, retainProjectActuation: true);
        Assert.Equal(RealTestVerdict.Passed, second.Verdict);
        Assert.Equal(0, actions.CleanupCount);
        await actions.CleanupAsync(default);
        Assert.Equal(1, actions.CleanupCount);
    }

    [Fact]
    public async Task IsolatedActionOnlyCaseKeepsInconclusiveAndReleasesScope()
    {
        var tx = new StepEditTransaction(TestDocument.Create("Single"));
        tx.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("Wait").WithProperty("milliseconds", "1"));
        var actions = new Actuation();
        var result = await new RealRunCoordinator(new Signals(), actions, new Waves())
            .StartAsync(new PlanCompiler().Prepare(tx.Current, allowRealOperations: true));
        Assert.Equal(RealTestVerdict.Inconclusive, result.Verdict);
        Assert.Equal(1, actions.CleanupCount);
    }
}
