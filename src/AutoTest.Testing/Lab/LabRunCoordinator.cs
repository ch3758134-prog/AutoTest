using System.Collections.Immutable;

namespace AutoTest.Testing.Lab;

public sealed record LabObservation(string Name, string? Value, LabQuality Quality, LabSource Source,
    Guid RunId, long SessionGeneration, string? TraceReference = null);
public sealed record LabActionReceipt(bool Accepted, string? ErrorCode, string? TraceReference = null);

/// <summary>Shared station adapter supplied by INTEGRATION. No physical transport is created here.</summary>
public interface ILabStation
{
    long SessionGeneration { get; }
    ValueTask<LabObservation> ObserveAsync(string name, Guid runId, LabSource source, CancellationToken cancellation);
    ValueTask<LabActionReceipt> SetControllerAsync(LabControllerRef binding, Guid runId,
        LabSource source, CancellationToken cancellation);
}

/// <summary>
/// A real read-only station must explicitly authorize a single execution scope.
/// This interface does not permit CAN TX, power, IGN, keypad or Flash.
/// </summary>
public interface IAuthorizedReadOnlyPhysicalLabStation : ILabStation
{
    bool IsReadOnlyAuthorized { get; }
}

/// <summary>
/// Narrow capability for the proven KEY_SIM CAN0 physical action family.
/// An ordinary real read-only UDS/CAN station does NOT implement this.
/// </summary>
public interface IAuthorizedPhysicalControllerLabStation : IAuthorizedReadOnlyPhysicalLabStation
{
    bool ControllerActionsAuthorized { get; }
}

/// <summary>Deterministic isolated fixture, explicitly Mock or Replay. Never performs CAN TX.</summary>
public sealed class SimulatedLabStation : ILabStation
{
    private readonly Dictionary<string, LabObservation> _observations = new(StringComparer.Ordinal);
    public long SessionGeneration { get; private set; } = 1;
    public void Reconnect() { SessionGeneration++; _observations.Clear(); }
    public void SetObservation(string name, string? value, LabQuality quality, LabSource source,
        Guid runId, string? trace = null) =>
        _observations[name] = new LabObservation(name, value, quality, source, runId, SessionGeneration, trace);
    public ValueTask<LabObservation> ObserveAsync(string name, Guid runId, LabSource source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_observations.TryGetValue(name, out var sample)) return ValueTask.FromResult(sample);
        return ValueTask.FromResult(new LabObservation(name, null, LabQuality.Missing,
            source, runId, SessionGeneration));
    }
    public ValueTask<LabActionReceipt> SetControllerAsync(LabControllerRef binding, Guid runId,
        LabSource source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return ValueTask.FromResult(source == LabSource.Physical
            ? new LabActionReceipt(false, "PHYSICAL_OUTPUT_DENIED")
            : new LabActionReceipt(true, null, $"{source}:controller:{binding.ControllerId}/{binding.StateId}"));
    }
}

/// <summary>Read-only samples originating from an explicitly stored Replay dataset.</summary>
public sealed record LabReplaySample(string SignalName, string? Value, LabQuality Quality,
    string TraceReference);

public sealed class RecordedReplayLabStation : ILabStation
{
    private readonly Dictionary<string, LabReplaySample> _records;
    public long SessionGeneration { get; }
    public RecordedReplayLabStation(IEnumerable<LabReplaySample> samples, long generation = 1)
    {
        _records = samples.ToDictionary(s => s.SignalName, StringComparer.Ordinal);
        SessionGeneration = generation;
    }
    public ValueTask<LabObservation> ObserveAsync(string name, Guid runId, LabSource source,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (source != LabSource.Replay || !_records.TryGetValue(name, out var record))
            return ValueTask.FromResult(new LabObservation(name, null, LabQuality.Missing,
                LabSource.Replay, runId, SessionGeneration));
        var valid = !string.IsNullOrWhiteSpace(record.TraceReference);
        return ValueTask.FromResult(new LabObservation(name, valid ? record.Value : null,
            valid ? record.Quality : LabQuality.Invalid, LabSource.Replay, runId,
            SessionGeneration, valid ? record.TraceReference : null));
    }
    public ValueTask<LabActionReceipt> SetControllerAsync(LabControllerRef binding,
        Guid runId, LabSource source, CancellationToken cancellation) =>
        ValueTask.FromResult(new LabActionReceipt(false, "REPLAY_IS_READ_ONLY"));
}

public sealed record LabStepFact(Guid OperationId, Guid CorrelationId, Guid RunId, Guid StepId,
    LabPhase Phase, string NodeType, string Expected, string? Actual, LabQuality Quality,
    LabVerdict Verdict, LabSource Source, long SessionGeneration, DateTimeOffset AtUtc,
    string? TraceReference, string? Diagnostic);

public interface ILabOperationAuditSink
{
    void Append(LabStepFact fact);
}

/// <summary>Test-only fallback; integration must supply a durable global operation audit.</summary>
public sealed class InMemoryLabOperationAuditSink : ILabOperationAuditSink
{
    private readonly List<LabStepFact> _facts = new();
    public IReadOnlyList<LabStepFact> Facts => _facts;
    public void Append(LabStepFact fact) => _facts.Add(fact);
}

public sealed record LabRunReport(Guid RunId, string PlanFingerprint, LabSource Source,
    LabVerdict Verdict, bool Stopped, ImmutableArray<LabStepFact> Facts)
{
    public bool PhysicalProductionPass => false;
}

/// <summary>One run; boundary-only debugging, immutable prepared document, fail-closed physical fence.</summary>
public sealed class LabRunCoordinator
{
    private readonly object _sync = new();
    private readonly ILabOperationAuditSink _audit;
    public LabRunCoordinator(ILabOperationAuditSink? audit = null) =>
        _audit = audit ?? new InMemoryLabOperationAuditSink();
    private CancellationTokenSource? _running;
    private TaskCompletionSource<bool>? _resume;
    private bool _pause, _stepOnce;
    private Guid? _skip, _runTo;
    public event Action<LabStepFact>? FactProduced;
    public bool IsRunning { get { lock (_sync) return _running is not null; } }
    public void Pause()
    {
        lock (_sync) { RequireRunning(); _pause = true; }
    }
    public void Resume()
    {
        lock (_sync) { RequireRunning(); _pause = false; _stepOnce = false; _runTo = null; Release(); }
    }
    public void Step()
    {
        lock (_sync) { RequireRunning(); _pause = true; _stepOnce = true; Release(); }
    }
    public void Skip(Guid stepId)
    {
        // Advance until this exact step; skip it and pause at the next boundary.
        lock (_sync) { RequireRunning(); _skip = stepId; _pause = false; _stepOnce = false; Release(); }
    }
    public void RunTo(Guid stepId)
    {
        lock (_sync) { RequireRunning(); _runTo = stepId; _pause = false; _stepOnce = false; Release(); }
    }
    public void Stop()
    {
        lock (_sync) { _running?.Cancel(); Release(); }
    }
    private void RequireRunning()
    {
        if (_running is null) throw new InvalidOperationException("No active run");
    }
    private void Release() { _resume?.TrySetResult(true); _resume = null; }
    private async Task<bool> Boundary(Guid id, CancellationToken ct)
    {
        Task? waiter = null;
        bool skip = false;
        lock (_sync)
        {
            if (_runTo == id) { _runTo = null; _pause = true; }
            if (_skip == id) { _skip = null; skip = true; _pause = true; }
            if (_pause && !_stepOnce && !skip)
            {
                _resume ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                waiter = _resume.Task;
            }
            else if (_stepOnce) _stepOnce = false;
        }
        if (waiter is not null) await waiter.WaitAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return skip;
    }
    public async Task<LabRunReport> RunAsync(LabPreparedPlan plan, ILabStation station,
        LabSource source, CancellationToken external = default)
    {
        if (source == LabSource.Physical)
        {
            if (station is not IAuthorizedReadOnlyPhysicalLabStation physical ||
                !physical.IsReadOnlyAuthorized || station.SessionGeneration <= 0)
                throw new InvalidOperationException("PHYSICAL_TEST_EXECUTION_DENIED: no authorized read-only station.");
            var controllerAllowed = station is IAuthorizedPhysicalControllerLabStation
                { ControllerActionsAuthorized: true };
            var forbidden = plan.Restore().Walk()
                .FirstOrDefault(n => n.Type == "MockObserve" ||
                    (n.Type == "ControllerState" && !controllerAllowed));
            if (forbidden is not null)
                throw new InvalidOperationException("PHYSICAL_ACTION_NOT_AUTHORIZED: " +
                    forbidden.Type + " / " + forbidden.Id);
        }
        if (!plan.Ready) throw new InvalidOperationException(string.Join("; ", plan.Diagnostics));
        var document = plan.Restore();
        if (source == LabSource.Replay && document.Walk().Any(n => n.Type == "MockObserve"))
            throw new InvalidOperationException("MOCK_OBSERVE_CANNOT_BE_REPLAY_EVIDENCE");
        CancellationTokenSource cts;
        lock (_sync)
        {
            if (_running is not null) throw new InvalidOperationException("Run already active");
            cts = CancellationTokenSource.CreateLinkedTokenSource(external);
            _running = cts; _resume = null; _pause = false; _stepOnce = false; _skip = null; _runTo = null;
        }
        var runId = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        var facts = ImmutableArray.CreateBuilder<LabStepFact>();
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var observations = new Dictionary<string, LabObservation>(StringComparer.Ordinal);
        var subroutines = document.Walk().Where(x => x.Type == "Subroutine")
            .ToDictionary(n => n.Parameters["name"], StringComparer.Ordinal);
        var sourceGeneration = station.SessionGeneration;
        var stopped = false;
        var hadVerifiedAssertion = false;
        var worst = LabVerdict.Pass;
        var executedSteps = 0;
        string Param(LabNode n, string key) => n.Parameters.GetValueOrDefault(key, "");
        void Emit(LabNode n, LabPhase phase, string expected, string? actual,
            LabQuality quality, LabVerdict verdict, string? diagnostic = null, string? trace = null)
        {
            var fact = new LabStepFact(Guid.NewGuid(), correlation, runId, n.Id, phase, n.Type,
                expected, actual, quality, verdict, source, station.SessionGeneration,
                DateTimeOffset.UtcNow, trace, diagnostic);
            facts.Add(fact);
            _audit.Append(fact);
            FactProduced?.Invoke(fact);
            if (verdict is LabVerdict.Error) worst = LabVerdict.Error;
            else if (verdict is LabVerdict.Fail && worst != LabVerdict.Error) worst = LabVerdict.Fail;
            else if (verdict is LabVerdict.Inconclusive && worst == LabVerdict.Pass) worst = LabVerdict.Inconclusive;
        }
        LabObservation Current(string name)
        {
            if (!observations.TryGetValue(name, out var sample))
                return new LabObservation(name, null, LabQuality.Missing, source, runId, sourceGeneration);
            if (sample.RunId != runId || sample.SessionGeneration != station.SessionGeneration ||
                sample.Source != source || sample.SessionGeneration != sourceGeneration)
                return sample with { Quality = LabQuality.Stale, Value = null };
            return sample;
        }
        (string? Value, LabQuality Quality, string? Trace) ControlValue(string name)
        {
            // Ordinary program variables and observed ECU signal facts are
            // different sources. A real Observe/When can drive IF/SWITCH.
            // An absent or stale observation must never select a branch.
            // Once a real sample has been observed for this name, its quality
            // and provenance win over any local variable with the same name.
            // Otherwise a SetVariable could silently mask an ECU fault.
            if (observations.ContainsKey(name))
            {
                var observed = Current(name);
                return (observed.Value, observed.Quality, observed.TraceReference);
            }
            if (variables.TryGetValue(name, out var declared))
                return (declared, LabQuality.Observed, null);
            return (null, LabQuality.Missing, null);
        }

        async Task Execute(IEnumerable<LabNode> nodes, LabPhase phase, CancellationToken ct, bool cleanup = false)
        {
            foreach (var node in nodes)
            {
                if (!cleanup && await Boundary(node.Id, ct).ConfigureAwait(false))
                {
                    Emit(node, phase, "", null, LabQuality.Missing, LabVerdict.Skipped, "User skipped step");
                    continue;
                }
                ct.ThrowIfCancellationRequested();
                if (++executedSteps > 50000) throw new InvalidOperationException("EXECUTION_STEP_LIMIT_EXCEEDED");
                var expected = ""; string? actual = null; string? trace = null;
                var quality = LabQuality.Observed; var verdict = LabVerdict.Pass;
                string? diagnostic = null;
                List<LabNode>? branch = null;
                switch (node.Type)
                {
                    case "Sequence": branch = node.Slots["body"]; break;
                    case "SetVariable":
                        variables[Param(node, "variable")] = Param(node, "value"); break;
                    case "MockObserve":
                        quality = Enum.Parse<LabQuality>(Param(node, "quality"));
                        actual = quality == LabQuality.Observed ? Param(node, "value") : null;
                        observations[Param(node, "variable")] = new LabObservation(Param(node, "variable"),
                            actual, quality, source, runId, sourceGeneration, $"{source}:mock-observation");
                        if (quality != LabQuality.Observed) verdict = LabVerdict.Inconclusive;
                        break;
                    case "Observe":
                        var observed = await station.ObserveAsync(Param(node, "variable"), runId, source, ct)
                            .ConfigureAwait(false);
                        observations[Param(node, "variable")] = observed;
                        var checkedObservation = Current(Param(node, "variable"));
                        actual = checkedObservation.Value; quality = checkedObservation.Quality;
                        trace = checkedObservation.TraceReference;
                        if (quality != LabQuality.Observed) verdict = LabVerdict.Inconclusive;
                        break;
                    case "Assert":
                        expected = Param(node, "equals");
                        var sample = Current(Param(node, "variable"));
                        actual = sample.Value; quality = sample.Quality; trace = sample.TraceReference;
                        verdict = quality != LabQuality.Observed || actual is null
                            ? LabVerdict.Inconclusive : actual == expected ? LabVerdict.Pass : LabVerdict.Fail;
                        if (verdict == LabVerdict.Pass) hadVerifiedAssertion = true;
                        break;
                    case "If":
                        expected = Param(node, "equals");
                        var ifValue = ControlValue(Param(node, "variable"));
                        actual = ifValue.Value; quality = ifValue.Quality; trace = ifValue.Trace;
                        if (actual is null || quality != LabQuality.Observed)
                        {
                            if (quality == LabQuality.Observed) quality = LabQuality.Missing;
                            verdict = LabVerdict.Inconclusive;
                        }
                        else branch = node.Slots[actual == expected ? "then" : "else"];
                        break;
                    case "Switch":
                        var switchValue = ControlValue(Param(node, "variable"));
                        actual = switchValue.Value; quality = switchValue.Quality; trace = switchValue.Trace;
                        if (actual is null || quality != LabQuality.Observed)
                        {
                            if (quality == LabQuality.Observed) quality = LabQuality.Missing;
                            verdict = LabVerdict.Inconclusive;
                        }
                        else
                        {
                            var entries = node.Slots["cases"];
                            var chosen = entries.FirstOrDefault(c => c.Type == "Case" && Param(c, "equals") == actual)
                                ?? entries.FirstOrDefault(c => c.Type == "Default");
                            if (chosen is null) verdict = LabVerdict.Inconclusive;
                            else branch = chosen.Slots["body"];
                        }
                        break;
                    case "When":
                        expected = Param(node, "equals");
                        var signalName = Param(node, "variable");
                        using (var budget = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            var timeoutMs = int.Parse(Param(node, "timeoutMs"));
                            budget.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
                            try
                            {
                                do
                                {
                                    // A defined local variable can be a control condition.
                                    // Otherwise obtain a FRESH station observation on each
                                    // poll; do not reuse a prior ECU signal sample as if live.
                                    if (!observations.ContainsKey(signalName) &&
                                        variables.TryGetValue(signalName, out var local))
                                    {
                                        actual = local;
                                        quality = LabQuality.Observed;
                                    }
                                    else
                                    {
                                        var live = await station.ObserveAsync(signalName, runId,
                                            source, budget.Token).ConfigureAwait(false);
                                        observations[signalName] = live;
                                        var checkedLive = Current(signalName);
                                        actual = checkedLive.Value;
                                        quality = checkedLive.Quality;
                                        trace = checkedLive.TraceReference;
                                    }
                                    if (actual == expected && quality == LabQuality.Observed)
                                    { branch = node.Slots["body"]; break; }
                                    await Task.Delay(10, budget.Token).ConfigureAwait(false);
                                } while (true);
                            }
                            catch (OperationCanceledException) when
                                (budget.IsCancellationRequested && !ct.IsCancellationRequested)
                            {
                                // The explicit step timeout is an inconclusive observation,
                                // not a successful ECU acknowledgement.
                            }
                        }
                        ct.ThrowIfCancellationRequested();
                        if (branch is null)
                        {
                            verdict = LabVerdict.Inconclusive;
                            if (quality == LabQuality.Observed) quality = LabQuality.Missing;
                            diagnostic = "WHEN_TIMEOUT_OR_CONDITION_NOT_MET";
                        }
                        break;
                    case "Loop": expected = Param(node, "count"); break;
                    case "Wait":
                        expected = Param(node, "milliseconds");
                        await Task.Delay(int.Parse(expected), ct).ConfigureAwait(false); break;
                    case "Subroutine": verdict = LabVerdict.Skipped; diagnostic = "Definition only"; break;
                    case "CallSubroutine": expected = Param(node, "target"); break;
                    case "TryCatch": break;
                    case "Log": actual = Param(node, "message"); break;
                    case "ControllerState":
                        expected = $"{Param(node, "controllerId")}/{Param(node, "stateId")}@{Param(node, "revision")}";
                        var binding = new LabControllerRef(Param(node, "controllerId"),
                            Param(node, "stateId"), Param(node, "revision"));
                        var receipt = await station.SetControllerAsync(binding, runId, source, ct).ConfigureAwait(false);
                        trace = receipt.TraceReference;
                        if (!receipt.Accepted)
                        { verdict = LabVerdict.Error; quality = LabQuality.Invalid; diagnostic = receipt.ErrorCode; }
                        // ACK never counts as a verified assertion.
                        break;
                    case "Fail": verdict = LabVerdict.Fail; diagnostic = Param(node, "message"); break;
                    default: throw new NotSupportedException($"LAB unsupported node {node.Type}");
                }
                Emit(node, phase, expected, actual, quality, verdict, diagnostic, trace);
                if (verdict == LabVerdict.Error) throw new InvalidOperationException(diagnostic ?? "Action rejected");
                if (branch is not null) await Execute(branch, phase, ct, cleanup).ConfigureAwait(false);
                if (node.Type == "Loop")
                    for (var i = 0; i < int.Parse(expected); i++)
                        await Execute(node.Slots["body"], phase, ct, cleanup).ConfigureAwait(false);
                if (node.Type == "CallSubroutine")
                    await Execute(subroutines[expected].Slots["body"], phase, ct, cleanup).ConfigureAwait(false);
                if (node.Type == "TryCatch")
                {
                    try { await Execute(node.Slots["try"], phase, ct, cleanup).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        variables["exception"] = ex.GetType().Name;
                        await Execute(node.Slots["catch"], phase, ct, cleanup).ConfigureAwait(false);
                    }
                    finally { await Execute(node.Slots["finally"], phase, ct, cleanup).ConfigureAwait(false); }
                }
            }
        }
        try
        {
            await Execute(document.Phases[LabPhase.Prepare], LabPhase.Prepare, cts.Token).ConfigureAwait(false);
            await Execute(document.Phases[LabPhase.Test], LabPhase.Test, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        { stopped = true; if (worst == LabVerdict.Pass) worst = LabVerdict.Inconclusive; }
        catch (Exception ex)
        {
            worst = LabVerdict.Error;
            var fake = LabNode.New("RuntimeError");
            Emit(fake, LabPhase.Test, "", null, LabQuality.Invalid, LabVerdict.Error, ex.Message);
        }
        finally
        {
            // Cleanup is independent of stop; bounded to avoid indefinite shutdown.
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Execute(document.Phases[LabPhase.Cleanup], LabPhase.Cleanup, cleanup.Token, cleanup: true)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var fake = LabNode.New("CleanupError");
                Emit(fake, LabPhase.Cleanup, "", null, LabQuality.Invalid, LabVerdict.Error, ex.Message);
            }
            lock (_sync) { _running = null; _pause = false; _stepOnce = false; _runTo = null; _skip = null; Release(); }
            cts.Dispose();
        }
        if (!stopped && worst == LabVerdict.Pass && !hadVerifiedAssertion) worst = LabVerdict.Inconclusive;
        return new LabRunReport(runId, plan.Digest, source, worst, stopped, facts.ToImmutable());
    }
}
