using System.Collections.Immutable;
using System.Diagnostics;
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
    private readonly object _lock = new();
    private readonly Dictionary<string, MockEvidence> _evidence = new(StringComparer.Ordinal);
    public void Set(string name, string? value, MockQuality quality)
    {
        lock (_lock) _evidence[name] = new MockEvidence(value, quality);
    }
    public MockEvidence Observe(string name)
    {
        lock (_lock) return _evidence.TryGetValue(name, out var result)
            ? result : new MockEvidence(null, MockQuality.Missing);
    }
}

/// <summary>
/// Official TestDocument Mock runner. All execution controls act only at step boundaries.
/// No CAN, USB, station or ECU physical operation exists in this runner.
/// </summary>
public sealed class RunCoordinator
{
    private sealed class MockFlowFailureException : Exception
    {
        public MockFlowFailureException(string message) : base(message) { }
    }
    private const int MaxMockStepOperations = 50_000;
    private const int MaxSubroutineCallDepth = 32;
    private readonly object _gate = new();
    private readonly HashSet<StepId> _breakpoints = new();
    private CancellationTokenSource? _stop;
    private TaskCompletionSource<bool>? _resume;
    private bool _paused;
    private bool _stepOnce;
    private StepId? _runTo;
    private StepId? _skip;
    private HashSet<StepId>? _activeControllableSteps;

    /// <summary>Diagnostic observation; subscriber faults never change the run verdict.</summary>
    public event Action<StepFact>? FactProduced;
    /// <summary>Raised when a step boundary is actually waiting to resume.</summary>
    public event Action<StepId>? BoundaryPaused;
    public bool IsRunning { get { lock (_gate) return _stop is not null; } }

    public void PauseAtBoundary()
    {
        lock (_gate)
        {
            RequireRunning();
            _runTo = null;
            _paused = true;
        }
    }
    public void Resume()
    {
        lock (_gate)
        {
            RequireRunning();
            _paused = false;
            _stepOnce = false;
            _runTo = null;
            Wake();
        }
    }
    public void Step()
    {
        lock (_gate)
        {
            RequireRunning();
            // If we are already waiting, release precisely that one boundary.
            // Otherwise permit the next boundary once and then pause.
            _stepOnce = _resume is null;
            _paused = true;
            _runTo = null;
            Wake();
        }
    }
    public void RunTo(StepId target)
    {
        if (target.Value == Guid.Empty) throw new ArgumentException("StepId required", nameof(target));
        lock (_gate)
        {
            RequireRunning();
            RequireControllableStep(target);
            _runTo = target;
            _paused = false;
            _stepOnce = false;
            Wake();
        }
    }
    public void Skip(StepId target)
    {
        if (target.Value == Guid.Empty) throw new ArgumentException("StepId required", nameof(target));
        lock (_gate)
        {
            RequireRunning();
            RequireControllableStep(target);
            _skip = target;
            _runTo = null;
            _paused = false;
            _stepOnce = false;
            Wake();
        }
    }
    public void SetBreakpoint(StepId id, bool enabled)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("StepId required", nameof(id));
        lock (_gate)
        {
            if (enabled) _breakpoints.Add(id);
            else _breakpoints.Remove(id);
        }
    }
    public bool HasBreakpoint(StepId id)
    {
        lock (_gate) return _breakpoints.Contains(id);
    }
    public void RequestStop()
    {
        lock (_gate)
        {
            _stop?.Cancel();
            Wake();
        }
    }
    private void RequireRunning()
    {
        if (_stop is null) throw new InvalidOperationException("No active run");
    }
    private void RequireControllableStep(StepId id)
    {
        if (_activeControllableSteps?.Contains(id) != true)
            throw new ArgumentException($"Step {id} does not exist in the active Prepare/Test snapshot");
    }
    private void Wake()
    {
        _resume?.TrySetResult(true);
        _resume = null;
    }
    private static void Notify<T>(Action<T>? handler, T value)
    {
        if (handler is null) return;
        foreach (Action<T> observer in handler.GetInvocationList())
        {
            try { observer(value); }
            catch { /* An external observer is not an execution handler. */ }
        }
    }
    private async Task<bool> Boundary(StepId id, CancellationToken ct)
    {
        Task? wait = null;
        var announce = false;
        lock (_gate)
        {
            var runningTo = _runTo is not null;
            if (_runTo == id)
            {
                _runTo = null;
                _paused = true;
            }
            else if (runningTo)
            {
                _paused = false;
                _stepOnce = false;
            }
            else if (_breakpoints.Contains(id) && _skip != id)
                _paused = true;

            if (_paused && !_stepOnce)
            {
                _resume ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = _resume.Task;
                announce = true;
            }
            else if (_stepOnce) _stepOnce = false;
        }
        if (announce) Notify(BoundaryPaused, id);
        if (wait is not null) await wait.WaitAsync(ct);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_skip == id)
            {
                _skip = null;
                return true;
            }
        }
        return false;
    }

    public async Task<MockRunReport> StartAsync(PreparedTestPlan plan, MockStation station,
        CancellationToken external = default, RunId? requestedRun = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(station);
        if (!plan.ValidateFingerprint()) throw new InvalidOperationException("Plan fingerprint mismatch");
        if (!plan.Diagnostics.IsEmpty) throw new InvalidOperationException(string.Join("; ", plan.Diagnostics));
        var run = requestedRun ?? RunId.New();
        if (run.Value == Guid.Empty) throw new ArgumentException("RunId must be nonempty");
        // Reject invalid/tampered documents before claiming an active run.
        var doc = plan.RestoreDocument();
        var controllableSteps = Enum.GetValues<TestPhase>()
            .Where(p => p != TestPhase.Cleanup)
            .SelectMany(p => TestDocument.WalkNodes(doc.Get(p)))
            .Select(n => n.Id).ToHashSet();
        CancellationTokenSource stop;
        lock (_gate)
        {
            if (_stop is not null) throw new InvalidOperationException("Already running");
            stop = CancellationTokenSource.CreateLinkedTokenSource(external);
            _stop = stop;
            _paused = false;
            _stepOnce = false;
            _runTo = null;
            _skip = null;
            _resume = null;
            _activeControllableSteps = controllableSteps;
        }
        var facts = ImmutableArray.CreateBuilder<StepFact>();
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        // Static declarations share a run-local, immutable reference map.
        var subroutines = doc.Walk().Where(n => n.Type == "Subroutine")
            .ToDictionary(n => n.Properties["name"], n => n, StringComparer.Ordinal);
        var callStack = new Stack<string>();
        var stopped = false;
        var hasVerifiedAssertion = false;
        var executedOperations = 0;
        var verdict = MockStepVerdict.Pass;
        void Append(StepFact fact)
        {
            facts.Add(fact);
            Notify(FactProduced, fact);
        }
        async Task Execute(ImmutableArray<StepNode> steps)
        {
            foreach (var node in steps)
            {
                if (++executedOperations > MaxMockStepOperations)
                    throw new InvalidOperationException("MOCK_STEP_BUDGET_EXCEEDED: maximum 50000 step executions");
                if (await Boundary(node.Id, stop.Token))
                {
                    Append(new StepFact(Guid.NewGuid(), node.Id, node.Type, "Skip requested",
                        null, MockQuality.Missing, MockStepVerdict.Skipped, "Mock boundary skip; subtree not executed"));
                    if (verdict == MockStepVerdict.Pass) verdict = MockStepVerdict.Inconclusive;
                    continue;
                }
                var p = node.Properties;
                string Get(string key) => p.TryGetValue(key, out var v) ? v : "";
                var expected = "";
                string? actual = null;
                var quality = MockQuality.Observed;
                var result = MockStepVerdict.Pass;
                string? detail = null;
                StepNode? switchArm = null;
                var whenSatisfied = false;
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
                        expected = StepConditionEvaluator.Describe(node);
                        var observation = station.Observe(Get("variable"));
                        quality = observation.Quality;
                        actual = observation.Value;
                        if (quality != MockQuality.Observed)
                        {
                            result = MockStepVerdict.Inconclusive;
                            detail = "MOCK_ASSERT_NO_VALID_OBSERVATION: input quality is not Observed";
                        }
                        else
                        {
                            var judgement = StepConditionEvaluator.Evaluate(node, actual);
                            result = !judgement.Comparable ? MockStepVerdict.Inconclusive
                                : judgement.Matched ? MockStepVerdict.Pass : MockStepVerdict.Fail;
                            detail = judgement.Diagnostic;
                        }
                        if (result == MockStepVerdict.Pass) hasVerifiedAssertion = true;
                        break;
                    case "If":
                        expected = StepConditionEvaluator.Describe(node);
                        vars.TryGetValue(Get("variable"), out actual);
                        var condition = StepConditionEvaluator.Evaluate(node, actual);
                        quality = actual is null ? MockQuality.Missing : MockQuality.Observed;
                        if (!condition.Comparable)
                        {
                            result = MockStepVerdict.Inconclusive;
                            detail = condition.Diagnostic;
                        }
                        break;
                    case "Switch":
                        expected = $"Case match for {Get("variable")}";
                        vars.TryGetValue(Get("variable"), out actual);
                        quality = actual is null ? MockQuality.Missing : MockQuality.Observed;
                        if (actual is null)
                        {
                            result = MockStepVerdict.Inconclusive;
                            detail = "MOCK_SWITCH_VARIABLE_MISSING: no branch executed";
                        }
                        else
                        {
                            var arms = node.Slots["cases"];
                            switchArm = arms.FirstOrDefault(x => x.Type == "Case" &&
                                x.Properties.TryGetValue("equals", out var match) && match == actual)
                                ?? arms.FirstOrDefault(x => x.Type == "Default");
                            if (switchArm is null)
                            {
                                result = MockStepVerdict.Inconclusive;
                                detail = "MOCK_SWITCH_NO_MATCH: no Case or Default matched";
                            }
                            else detail = $"Mock branch selected: {switchArm.Type} ({switchArm.Id})";
                        }
                        break;
                    case "Case":
                    case "Default":
                        detail = "Selected Mock SWITCH branch";
                        break;
                    case "When":
                        expected = Get("equals");
                        var clock = Stopwatch.StartNew();
                        var timeout = int.Parse(Get("timeoutMs"));
                        MockEvidence sample;
                        StepConditionEvaluator.Result whenEvaluation;
                        while (true)
                        {
                            stop.Token.ThrowIfCancellationRequested();
                            sample = station.Observe(Get("variable"));
                            whenEvaluation = sample.Quality == MockQuality.Observed
                                ? StepConditionEvaluator.Evaluate(node, sample.Value)
                                : new(false, false, "MOCK_WHEN_NO_OBSERVATION: input quality is not Observed");
                            if (whenEvaluation.Comparable && whenEvaluation.Matched)
                            {
                                whenSatisfied = true;
                                break;
                            }
                            var left = timeout - clock.ElapsedMilliseconds;
                            if (left <= 0) break;
                            await Task.Delay((int)Math.Min(10, left), stop.Token);
                        }
                        expected = StepConditionEvaluator.Describe(node);
                        quality = sample.Quality;
                        actual = sample.Value;
                        if (!whenSatisfied)
                        {
                            result = whenEvaluation.Comparable ? MockStepVerdict.Fail : MockStepVerdict.Inconclusive;
                            detail = !whenEvaluation.Comparable
                                ? whenEvaluation.Diagnostic ?? "MOCK_WHEN_NO_OBSERVATION"
                                : "MOCK_WHEN_TIMEOUT: observed value never matched";
                        }
                        else detail = $"Mock condition satisfied after {clock.ElapsedMilliseconds}ms";
                        break;
                    case "TryCatch":
                        detail = "Mock exception boundary: try/catch/finally";
                        break;
                    case "Subroutine":
                        detail = "Declaration only; body runs only when called";
                        break;
                    case "CallSubroutine":
                        expected = Get("target");
                        detail = "Mock subroutine call (statically preflighted)";
                        break;
                    case "Log":
                        actual = Get("message");
                        detail = "Mock log annotation (no ECU observation)";
                        break;
                    case "Fail":
                        actual = Get("message");
                        result = MockStepVerdict.Fail;
                        detail = "Explicit simulated failure";
                        break;
                    case "Loop": expected = Get("count"); break;
                    case "Wait":
                        expected = Get("milliseconds");
                        await Task.Delay(int.Parse(expected), stop.Token);
                        break;
                    case "Sequence": break;
                    default: throw new InvalidOperationException($"Not implemented: {node.Type}");
                }
                Append(new StepFact(Guid.NewGuid(), node.Id, node.Type, expected, actual,
                    quality, result, detail));
                if (result == MockStepVerdict.Fail) verdict = MockStepVerdict.Fail;
                else if (result == MockStepVerdict.Inconclusive && verdict == MockStepVerdict.Pass)
                    verdict = MockStepVerdict.Inconclusive;
                // A deliberate Fail terminates this control-flow path. TryCatch may
                // handle it, but its FAIL fact is never rewritten into PASS.
                if (node.Type == "Fail") throw new MockFlowFailureException(Get("message"));
                if (node.Type == "If")
                {
                    var decision = StepConditionEvaluator.Evaluate(node, actual);
                    if (decision.Comparable)
                        await Execute(node.Slots[decision.Matched ? "then" : "else"]);
                }
                else if (node.Type == "Loop")
                    for (int i = 0; i < int.Parse(expected); i++) await Execute(node.Slots["body"]);
                else if (node.Type == "Sequence") await Execute(node.Slots["body"]);
                else if (node.Type == "Switch" && switchArm is not null)
                    await Execute(ImmutableArray.Create(switchArm));
                else if (node.Type is "Case" or "Default")
                    await Execute(node.Slots["body"]);
                else if (node.Type == "When" && whenSatisfied)
                    await Execute(node.Slots["body"]);
                else if (node.Type == "CallSubroutine")
                {
                    var target = Get("target");
                    if (!subroutines.TryGetValue(target, out var definition))
                        throw new InvalidOperationException($"SUBROUTINE_MISSING: {target}");
                    if (callStack.Count >= MaxSubroutineCallDepth || callStack.Contains(target))
                        throw new InvalidOperationException($"SUBROUTINE_RECURSION_GUARD: {target}");
                    callStack.Push(target);
                    try { await Execute(definition.Slots["body"]); }
                    finally { callStack.Pop(); }
                }
                else if (node.Type == "TryCatch")
                {
                    try
                    {
                        try { await Execute(node.Slots["try"]); }
                        catch (MockFlowFailureException)
                        {
                            await Execute(node.Slots["catch"]);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException || !stop.IsCancellationRequested)
                        {
                            // An unexpected software error remains ERROR even when a
                            // scripted catch branch records a recovery attempt.
                            verdict = MockStepVerdict.Error;
                            Append(new StepFact(Guid.NewGuid(), node.Id, "TryCatchError", "",
                                null, MockQuality.Invalid, MockStepVerdict.Error,
                                $"MOCK_TRY_EXCEPTION: {ex.GetType().Name}: {ex.Message}"));
                            await Execute(node.Slots["catch"]);
                        }
                    }
                    finally
                    {
                        // Safe Mock-only finally must run after cancellation as well.
                        // PlanCompiler blocks nodes with external side effects here.
                        await ExecuteCleanup(node.Slots["finally"], Append, vars, "Finally");
                    }
                }
            }
        }
        try
        {
            await Execute(doc.Get(TestPhase.Prepare));
            await Execute(doc.Get(TestPhase.Test));
            StepId? unreachableRunTo;
            StepId? unreachableSkip;
            lock (_gate)
            {
                unreachableRunTo = _runTo;
                unreachableSkip = _skip;
            }
            if (unreachableRunTo is StepId target)
            {
                Append(new StepFact(Guid.NewGuid(), target, "RunToNotReached", "Step boundary",
                    null, MockQuality.Missing, MockStepVerdict.Inconclusive,
                    "Run-to target was not reached on the executed control-flow path"));
                if (verdict == MockStepVerdict.Pass) verdict = MockStepVerdict.Inconclusive;
            }
            if (unreachableSkip is StepId skipped)
            {
                Append(new StepFact(Guid.NewGuid(), skipped, "SkipNotReached", "Step boundary",
                    null, MockQuality.Missing, MockStepVerdict.Inconclusive,
                    "Skip target was not reached on the executed control-flow path"));
                if (verdict == MockStepVerdict.Pass) verdict = MockStepVerdict.Inconclusive;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            stopped = true;
            verdict = MockStepVerdict.Inconclusive;
        }
        catch (MockFlowFailureException)
        {
            // FAIL fact was already emitted at the responsible step.
            verdict = MockStepVerdict.Fail;
        }
        catch (Exception ex)
        {
            verdict = MockStepVerdict.Error;
            Append(new StepFact(Guid.NewGuid(), default, "RuntimeError", "", null,
                MockQuality.Invalid, MockStepVerdict.Error, ex.Message));
        }
        finally
        {
            // Cleanup is always attempted after a stop. It is explicitly uncancellable
            // and currently permits only safe Mock SetVariable/Wait/Sequence operations.
            try { await ExecuteCleanup(doc.Get(TestPhase.Cleanup), Append, vars); }
            catch (Exception ex)
            {
                verdict = MockStepVerdict.Error;
                Append(new StepFact(Guid.NewGuid(), default, "CleanupError", "", null,
                    MockQuality.Invalid, MockStepVerdict.Error, ex.Message));
            }
            lock (_gate)
            {
                _stop = null;
                _resume = null;
                _paused = false;
                _stepOnce = false;
                _runTo = null;
                _skip = null;
                _activeControllableSteps = null;
            }
            stop.Dispose();
        }
        if (!stopped && verdict == MockStepVerdict.Pass && !hasVerifiedAssertion)
            verdict = MockStepVerdict.Inconclusive;
        return new MockRunReport(run, facts.ToImmutable(), verdict, stopped, plan.Fingerprint);
    }
    private static async Task ExecuteCleanup(ImmutableArray<StepNode> nodes,
        Action<StepFact> append, Dictionary<string, string> vars, string detail = "Cleanup")
    {
        foreach (var node in nodes)
        {
            if (node.Type == "SetVariable") vars[node.Properties["variable"]] = node.Properties["value"];
            else if (node.Type == "Wait") await Task.Delay(int.Parse(node.Properties["milliseconds"]));
            else if (node.Type == "Log") { /* Mock cleanup annotation, no side effect. */ }
            else if (node.Type == "Sequence")
                await ExecuteCleanup(node.Slots["body"], append, vars, detail);
            else throw new InvalidOperationException($"Cleanup node not supported: {node.Type}");
            append(new StepFact(Guid.NewGuid(), node.Id, node.Type, "",
                node.Type == "Log" ? node.Properties["message"] : null,
                MockQuality.Observed, MockStepVerdict.Pass, detail));
        }
    }
}
