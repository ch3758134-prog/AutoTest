using System.Collections.Immutable;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;

namespace AutoTest.Testing.Runtime;

public enum MockQuality { Observed, Missing, Invalid }
public sealed record MockEvidence(string? Value, MockQuality Quality);
public enum MockStepVerdict { Pass, Fail, Inconclusive, Skipped, Error }
public sealed record StepFact(Guid OperationId, StepId StepId, string Type, string Expected,
    string? Actual, MockQuality Quality, MockStepVerdict Verdict, string? Detail);
public sealed record MockRunReport(RunId Run, ImmutableArray<StepFact> Facts,
    MockStepVerdict Verdict, bool Stopped, string PlanFingerprint);

public sealed class MockStation
{
    private readonly Dictionary<string, MockEvidence> _evidence = new(StringComparer.Ordinal);
    public void Set(string name, string? value, MockQuality quality) =>
        _evidence[name] = new MockEvidence(value, quality);
    public MockEvidence Observe(string name) => _evidence.TryGetValue(name, out var result)
        ? result : new MockEvidence(null, MockQuality.Missing);
}

/// <summary>Single-run Mock-only engine. Pause/step occurs before each step boundary.</summary>
public sealed class RunCoordinator
{
    private readonly object _gate = new();
    private CancellationTokenSource? _stop;
    private TaskCompletionSource<bool>? _resume;
    private bool _paused;
    private bool _stepOnce;
    public bool IsRunning { get { lock (_gate) return _stop is not null; } }
    public void PauseAtBoundary()
    {
        lock (_gate) { if (_stop is null) throw new InvalidOperationException("No run"); _paused = true; }
    }
    public void Resume()
    {
        lock (_gate) { _paused = false; _stepOnce = false; _resume?.TrySetResult(true); _resume = null; }
    }
    public void Step()
    {
        lock (_gate) { _paused = true; _stepOnce = true; _resume?.TrySetResult(true); _resume = null; }
    }
    public void RequestStop()
    {
        lock (_gate) { _stop?.Cancel(); _resume?.TrySetResult(true); }
    }
    private async Task Boundary(CancellationToken ct)
    {
        Task? wait = null;
        lock (_gate)
        {
            if (_paused && !_stepOnce)
            {
                _resume ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = _resume.Task;
            }
            else if (_stepOnce) _stepOnce = false;
        }
        if (wait is not null) await wait.WaitAsync(ct);
        ct.ThrowIfCancellationRequested();
    }
    public async Task<MockRunReport> StartAsync(PreparedTestPlan plan, MockStation station,
        CancellationToken external = default, RunId? requestedRun = null)
    {
        if (!plan.ValidateFingerprint()) throw new InvalidOperationException("Plan fingerprint mismatch");
        if (!plan.Diagnostics.IsEmpty) throw new InvalidOperationException(string.Join("; ", plan.Diagnostics));
        CancellationTokenSource stop;
        lock (_gate)
        {
            if (_stop is not null) throw new InvalidOperationException("Already running");
            stop = CancellationTokenSource.CreateLinkedTokenSource(external);
            _stop = stop; _paused = false; _stepOnce = false; _resume = null;
        }
        var run = requestedRun ?? RunId.New();
        if (run.Value == Guid.Empty) throw new ArgumentException("RunId must be nonempty");
        var facts = ImmutableArray.CreateBuilder<StepFact>();
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        var stopped = false;
        var hasVerifiedAssertion = false;
        var verdict = MockStepVerdict.Pass;
        var doc = plan.RestoreDocument();
        async Task Execute(ImmutableArray<StepNode> steps)
        {
            foreach (var node in steps)
            {
                await Boundary(stop.Token);
                var p = node.Properties;
                string Get(string key) => p.TryGetValue(key, out var v) ? v : "";
                var expected = ""; string? actual = null; var quality = MockQuality.Observed;
                var result = MockStepVerdict.Pass; string? detail = null;
                switch (node.Type)
                {
                    case "SetVariable": vars[Get("variable")] = Get("value"); break;
                    case "MockObserve":
                        quality = Enum.Parse<MockQuality>(Get("quality"));
                        station.Set(Get("variable"), Get("value"), quality);
                        result = quality == MockQuality.Observed ? MockStepVerdict.Pass : MockStepVerdict.Inconclusive;
                        actual = quality == MockQuality.Observed ? Get("value") : null;
                        break;
                    case "Assert":
                        expected = Get("equals");
                        var observation = station.Observe(Get("variable"));
                        quality = observation.Quality; actual = observation.Value;
                        result = quality != MockQuality.Observed || actual is null
                            ? MockStepVerdict.Inconclusive
                            : actual == expected ? MockStepVerdict.Pass : MockStepVerdict.Fail;
                        if (result == MockStepVerdict.Pass) hasVerifiedAssertion = true;
                        break;
                    case "If":
                        expected = Get("equals"); vars.TryGetValue(Get("variable"), out actual);
                        quality = actual is null ? MockQuality.Missing : MockQuality.Observed;
                        if (actual is null) result = MockStepVerdict.Inconclusive;
                        break;
                    case "Loop": expected = Get("count"); break;
                    case "Wait":
                        expected = Get("milliseconds");
                        await Task.Delay(int.Parse(expected), stop.Token); break;
                    case "Sequence": break;
                    default: throw new InvalidOperationException($"Not implemented: {node.Type}");
                }
                facts.Add(new StepFact(Guid.NewGuid(), node.Id, node.Type, expected, actual,
                    quality, result, detail));
                if (result == MockStepVerdict.Fail) verdict = MockStepVerdict.Fail;
                else if (result == MockStepVerdict.Inconclusive && verdict == MockStepVerdict.Pass)
                    verdict = MockStepVerdict.Inconclusive;
                if (node.Type == "If")
                {
                    if (actual is not null)
                        await Execute(node.Slots[actual == expected ? "then" : "else"]);
                }
                else if (node.Type == "Loop")
                    for (int i = 0; i < int.Parse(expected); i++) await Execute(node.Slots["body"]);
                else if (node.Type == "Sequence") await Execute(node.Slots["body"]);
            }
        }
        try
        {
            await Execute(doc.Get(TestPhase.Prepare));
            await Execute(doc.Get(TestPhase.Test));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            stopped = true; verdict = MockStepVerdict.Inconclusive;
        }
        catch (Exception ex)
        {
            verdict = MockStepVerdict.Error;
            facts.Add(new StepFact(Guid.NewGuid(), default, "RuntimeError", "", null,
                MockQuality.Invalid, MockStepVerdict.Error, ex.Message));
        }
        finally
        {
            // Cleanup runs even after stop; uncancellable mock-only actions.
            try { await ExecuteCleanup(doc.Get(TestPhase.Cleanup), facts, vars); }
            catch (Exception ex)
            {
                verdict = MockStepVerdict.Error;
                facts.Add(new StepFact(Guid.NewGuid(), default, "CleanupError", "", null,
                    MockQuality.Invalid, MockStepVerdict.Error, ex.Message));
            }
            lock (_gate) { _stop = null; _resume = null; _paused = false; _stepOnce = false; }
            stop.Dispose();
        }
        if (!stopped && verdict == MockStepVerdict.Pass && !hasVerifiedAssertion)
            verdict = MockStepVerdict.Inconclusive;
        return new MockRunReport(run, facts.ToImmutable(), verdict, stopped, plan.Fingerprint);
    }
    private static async Task ExecuteCleanup(ImmutableArray<StepNode> nodes,
        ImmutableArray<StepFact>.Builder facts, Dictionary<string, string> vars)
    {
        foreach (var node in nodes)
        {
            if (node.Type == "SetVariable") vars[node.Properties["variable"]] = node.Properties["value"];
            else if (node.Type == "Wait") await Task.Delay(int.Parse(node.Properties["milliseconds"]));
            else if (node.Type == "Sequence")
                await ExecuteCleanup(node.Slots["body"], facts, vars);
            else throw new InvalidOperationException($"Cleanup node not supported: {node.Type}");
            facts.Add(new StepFact(Guid.NewGuid(), node.Id, node.Type, "", null,
                MockQuality.Observed, MockStepVerdict.Pass, "Cleanup"));
        }
    }
}
