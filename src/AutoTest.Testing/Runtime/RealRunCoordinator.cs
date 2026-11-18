using System.Collections.Immutable;
using System.Globalization;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;

namespace AutoTest.Testing.Runtime;

public enum RealStepStatus { Completed, Skipped, ConditionFalse, NoData, Timeout, InvalidData, DeviceError, Cancelled }
public enum RealTestVerdict { Passed, Failed, Inconclusive, Error, Aborted }

public sealed record RealStepFact(StepId Step, string Type, RealStepStatus Status,
    string Expected, string Actual, string Detail, DateTimeOffset At, string? Evidence = null);

public sealed record RealRunResult(RunId Run, string PlanFingerprint, RealTestVerdict Verdict,
    ImmutableArray<RealStepFact> Facts, IReadOnlyDictionary<string, TestDatum> Variables)
{
    public string? EvidencePath { get; init; }
}

/// <summary>
/// A variable retains its data quality and source; no implicit verdict is created by Read.
/// The 'Value' is deliberately kept separately from a test step's comparison result.
/// </summary>
public sealed record TestDatum(string? Value, TestSignalStatus? Quality,
    DateTimeOffset At, string? Evidence = null);

public sealed record TestWaveResult(bool Available, string Code, string? Value,
    string? Evidence, string Detail);

public interface ITestWavePort
{
    Task StartAsync(TestTargetMapping target, string captureKey, int durationMs, CancellationToken ct);
    Task<TestWaveResult> StopAsync(string captureKey, CancellationToken ct);
    Task<TestWaveResult> AnalyzeAsync(string captureKey, string referenceKey,
        double tolerance, double minimumCoverage, CancellationToken ct);
    // Source-compatible default for existing wave-port tests; the physical Desktop
    // adapter provides the version-pinned implementation.
    Task<TestWaveResult> AnalyzeWithProfileAsync(string captureKey, TestTargetMapping profile,
        string referenceKey, CancellationToken ct) =>
        Task.FromResult(new TestWaveResult(false, "PROFILE_NOT_IMPLEMENTED", null, null,
            "This wave port cannot apply a configured analysis profile."));
    Task CleanupAsync(CancellationToken ct);
}

/// <summary>
/// The real TEST interpreter uses the same persisted TEST tree, with typed mapped targets.
/// Real output and observed evidence are injected as ports; there is NO MockStation fallback.
/// Read and wave analysis yield variables, while only explicit Expect and evaluated WaitUntil
/// contribute test pass/fail judgements.
/// </summary>
public sealed class RealRunCoordinator
{
    private readonly ITestSignalPort _signals;
    private readonly ITestActuationPort _actions;
    private readonly ITestWavePort _waves;
    private readonly object _gate = new();
    private CancellationTokenSource? _stop;
    private TaskCompletionSource<bool>? _resume;
    private bool _pause, _singleStep;
    public bool IsRunning { get { lock (_gate) return _stop is not null; } }

    public RealRunCoordinator(ITestSignalPort signals, ITestActuationPort actions, ITestWavePort waves)
    {
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _waves = waves ?? throw new ArgumentNullException(nameof(waves));
    }

    public void RequestStop()
    {
        lock (_gate) { _stop?.Cancel(); _resume?.TrySetResult(true); }
    }
    public void Pause()
    {
        lock (_gate) { if (_stop is null) throw new InvalidOperationException("No run."); _pause = true; }
    }
    public void Resume()
    {
        lock (_gate) { _pause = false; _singleStep = false; _resume?.TrySetResult(true); _resume = null; }
    }
    public void Step()
    {
        lock (_gate) { _pause = true; _singleStep = true; _resume?.TrySetResult(true); _resume = null; }
    }
    private async Task Boundary(CancellationToken ct)
    {
        Task? awaited = null;
        lock (_gate)
        {
            if (_pause && !_singleStep)
            {
                _resume ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                awaited = _resume.Task;
            }
            else if (_singleStep) _singleStep = false;
        }
        if (awaited is not null) await awaited.WaitAsync(ct);
        ct.ThrowIfCancellationRequested();
    }

    private static string Property(StepNode n, string key) =>
        n.Properties.TryGetValue(key, out var value) ? value : "";
    private static int Milliseconds(StepNode n, string key, int max = 600000)
    {
        var value = int.Parse(Property(n, key), CultureInfo.InvariantCulture);
        if (value < 0 || value > max) throw new ArgumentOutOfRangeException(key);
        return value;
    }

    public async Task<RealRunResult> StartAsync(PreparedTestPlan prepared,
        CancellationToken external = default)
    {
        if (!prepared.ValidateFingerprint() || !prepared.Diagnostics.IsEmpty)
            throw new InvalidOperationException("Real test plan must be compiled and valid.");
        var document = prepared.RestoreDocument();
        var mappings = document.Mappings.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        TestTargetMapping Target(StepNode node) =>
            mappings.TryGetValue(Property(node, "target"), out var found) ? found
            : throw new KeyNotFoundException("Unresolved test target: " + Property(node, "target"));

        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_stop is not null) throw new InvalidOperationException("Station already executing.");
            cts = CancellationTokenSource.CreateLinkedTokenSource(external);
            _stop = cts; _pause = false; _singleStep = false; _resume = null;
        }

        var run = RunId.New();
        var facts = ImmutableArray.CreateBuilder<RealStepFact>();
        var variables = new Dictionary<string, TestDatum>(StringComparer.OrdinalIgnoreCase);
        var verdict = RealTestVerdict.Inconclusive;
        bool asserted = false, failed = false, error = false, aborted = false, incomplete = false;
        bool preparing = false;
        // Missing or invalid data must never become an empty string that could
        // accidentally satisfy an equality or be encoded as a hardware command.
        bool TryEvaluate(string input, out string value)
        {
            if (!input.StartsWith("$", StringComparison.Ordinal))
            {
                value = input;
                return true;
            }
            if (input.Length > 1 && variables.TryGetValue(input[1..], out var datum) &&
                datum.Value is { } actual &&
                (datum.Quality is null or TestSignalStatus.Valid))
            {
                value = actual;
                return true;
            }
            value = "";
            return false;
        }
        string Evaluate(string input) =>
            TryEvaluate(input, out var value) ? value
                : throw new InvalidOperationException(
                    "步骤引用的变量尚无有效数据：" + input + "；未执行后续硬件写入。");
        static bool Compare(string lhs, string op, string rhs)
        {
            if (double.TryParse(lhs, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) &&
                double.TryParse(rhs, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
                return op switch
                {
                    "==" => l == r, "!=" => l != r, ">" => l > r,
                    ">=" => l >= r, "<" => l < r, "<=" => l <= r,
                    _ => throw new FormatException("Unsupported comparator: " + op)
                };
            return op switch
            {
                "==" => lhs == rhs, "!=" => lhs != rhs,
                _ => throw new FormatException("Numeric comparison requires valid numbers.")
            };
        }
        void Fact(StepNode n, RealStepStatus state, string expected = "", string actual = "",
            string detail = "", string? evidence = null) =>
            facts.Add(new RealStepFact(n.Id, n.Type, state, expected, actual,
                detail, DateTimeOffset.UtcNow, evidence));

        async Task Execute(ImmutableArray<StepNode> nodes, CancellationToken ct, bool cleanup = false)
        {
            foreach (var node in nodes)
            {
                // A failed preparation prerequisite stops the rest of Prepare,
                // including nested steps, before any further physical writes.
                if (!cleanup && preparing && (failed || incomplete))
                    return;
                if (!cleanup)
                {
                    await Boundary(ct);
                    await _actions.CheckHealthAsync(ct);
                }
                else ct.ThrowIfCancellationRequested();
                if (!StepExecutionPolicy.IsEnabled(node))
                {
                    Fact(node, RealStepStatus.Skipped, detail: "步骤已关闭，本次未执行。");
                    continue;
                }
                var kind = node.Type;
                switch (kind)
                {
                    case "Sequence":
                        Fact(node, RealStepStatus.Completed);
                        await Execute(node.Slots["body"], ct, cleanup);
                        break;
                    case "If":
                    {
                        var sourceName = Property(node, "variable");
                        if (!variables.TryGetValue(sourceName, out var source) || source.Value is null ||
                            (source.Quality is not null and not TestSignalStatus.Valid))
                        {
                            incomplete = true;
                            Fact(node, RealStepStatus.NoData, detail:
                                "Branch source unavailable: " + sourceName);
                            break; // Never treat missing DUT feedback as the else branch.
                        }
                        var actual = source.Value;
                        if (!TryEvaluate(Property(node, "equals"), out var expected))
                        {
                            incomplete = true;
                            Fact(node, RealStepStatus.NoData, detail: "分支期望值引用了尚无有效数据的变量。");
                            break;
                        }
                        var pass = Compare(actual, "==", expected);
                        Fact(node, RealStepStatus.Completed, expected, actual, "Branch: " + (pass ? "then" : "else"));
                        await Execute(node.Slots[pass ? "then" : "else"], ct, cleanup);
                        break;
                    }
                    case "Loop":
                    {
                        var count = Milliseconds(node, "count", 1000);
                        Fact(node, RealStepStatus.Completed, count.ToString(CultureInfo.InvariantCulture));
                        for (var i = 0; i < count; i++)
                            await Execute(node.Slots["body"], ct, cleanup);
                        break;
                    }
                    case "Wait":
                        await Task.Delay(Milliseconds(node, "milliseconds", 60000), ct);
                        Fact(node, RealStepStatus.Completed);
                        break;
                    case "SetVariable":
                    {
                        if (!TryEvaluate(Property(node, "value"), out var value))
                        {
                            incomplete = true;
                            variables[Property(node, "variable")] = new TestDatum(null,
                                TestSignalStatus.NoFrame, DateTimeOffset.UtcNow);
                            Fact(node, RealStepStatus.NoData, detail: "赋值来源变量没有有效数据。");
                            break;
                        }
                        variables[Property(node, "variable")] = new TestDatum(value, null, DateTimeOffset.UtcNow);
                        Fact(node, RealStepStatus.Completed, "", value);
                        break;
                    }
                    case "Write":
                    {
                        var target = Target(node);
                        var action = Evaluate(Property(node, "value"));
                        if (node.Properties.TryGetValue("periodMs", out var intervalText) &&
                            intervalText.Length > 0)
                        {
                            var interval = Milliseconds(node, "periodMs");
                            if (interval > 0)
                            {
                                if (target.Kind is not (TestTargetKind.CanMessage or TestTargetKind.RawCan))
                                    throw new ArgumentException("周期发送只能用于 ECU CAN0 报文。");
                                if (interval < 20)
                                    throw new ArgumentException("上位机 CAN 周期不能小于 20 毫秒。");
                                var duration = node.Properties.TryGetValue("durationMs", out var d) &&
                                    !string.IsNullOrWhiteSpace(d)
                                    ? Milliseconds(node, "durationMs", 1_800_000) : 0;
                                action = "PERIODIC:" + interval.ToString(CultureInfo.InvariantCulture) +
                                    ":" + duration.ToString(CultureInfo.InvariantCulture) + ":" + action;
                            }
                        }
                        var result = await _actions.WriteAsync(target, action, ct);
                        Fact(node, result.Acknowledged ? RealStepStatus.Completed : RealStepStatus.DeviceError,
                            Property(node, "value"), result.Code, result.Diagnostic, result.Evidence);
                        if (!result.Acknowledged) throw new IOException("Device did not acknowledge: " + result.Code);
                        break;
                    }
                    case "CanStopPeriodic":
                    {
                        await _actions.StopPeriodicAsync(ct);
                        Fact(node, RealStepStatus.Completed, "", "", "已停止本次测试的全部 ECU CAN0 周期发送。");
                        break;
                    }
                    case "Read":
                    {
                        var mode = Enum.Parse<TestReadMode>(Property(node, "mode"), true);
                        var result = await _signals.ReadAsync(Target(node), mode,
                            TimeSpan.FromMilliseconds(Milliseconds(node, "timeoutMs")),
                            TimeSpan.FromMilliseconds(Milliseconds(node, "maxAgeMs")), ct);
                        var value = result.HasUsableValue
                            ? result.Observation!.Value!.Value.ToString("R", CultureInfo.InvariantCulture)
                            : null;
                        var variableName = Property(node, "variable");
                        variables[variableName] = new TestDatum(value, result.Status,
                            result.Observation?.Time.Utc ?? DateTimeOffset.UtcNow,
                            result.Observation?.Evidence.ToString());
                        variables[variableName + ".status"] = new TestDatum(result.Status.ToString(),
                            null, DateTimeOffset.UtcNow);
                        Fact(node, result.Status == TestSignalStatus.Valid ? RealStepStatus.Completed
                            : result.Status is TestSignalStatus.NoFrame or TestSignalStatus.Stale or TestSignalStatus.Timeout
                                ? RealStepStatus.NoData : RealStepStatus.InvalidData,
                            "", value ?? "", result.Status + ": " + result.Diagnostic,
                            result.Observation?.Evidence.ToString());
                        break;
                    }
                    case "WaitUntil":
                    {
                        var match = Target(node);
                        if (!TryEvaluate(Property(node, "expected"), out var expected))
                        {
                            incomplete = true;
                            Fact(node, RealStepStatus.NoData, detail: "等待条件引用的期望值变量无有效数据。");
                            break;
                        }
                        var op = Property(node, "operator");
                        var ms = Milliseconds(node, "timeoutMs");
                        var varName = Property(node, "variable");
                        var sawValid = false; var matched = false;
                        string actual = "";
                        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timer.CancelAfter(ms);
                        try
                        {
                            await foreach (var sample in _signals.ObserveAsync(match, timer.Token)
                                .WithCancellation(timer.Token))
                            {
                                if (sample.HasUsableValue)
                                {
                                    sawValid = true;
                                    actual = sample.Observation!.Value!.Value.ToString("R", CultureInfo.InvariantCulture);
                                    variables[varName] = new TestDatum(actual, sample.Status,
                                        sample.Observation.Time.Utc, sample.Observation.Evidence.ToString());
                                    if (Compare(actual, op, expected)) { matched = true; break; }
                                }
                                else
                                {
                                    variables[varName] = new TestDatum(null, sample.Status,
                                        DateTimeOffset.UtcNow, sample.Observation?.Evidence.ToString());
                                }
                            }
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                        variables[varName + ".met"] = new TestDatum(matched ? "true" : "false",
                            sawValid ? TestSignalStatus.Valid : TestSignalStatus.NoFrame,
                            DateTimeOffset.UtcNow);
                        variables[varName + ".status"] = new TestDatum(matched ? "Met" :
                            sawValid ? "ConditionNotMet" : "NoFrame",
                            null, DateTimeOffset.UtcNow);
                        // WaitUntil is a real condition assertion, not merely a delay:
                        // an observed mismatch must fail the run even if a later Expect passes.
                        if (sawValid) { asserted = true; failed |= !matched; }
                        else incomplete = true;
                        Fact(node, matched ? RealStepStatus.Completed :
                            sawValid ? RealStepStatus.ConditionFalse : RealStepStatus.NoData,
                            op + " " + expected, actual, matched ? "Condition observed" :
                            sawValid ? "Condition not observed before timeout" : "No valid CAN signal");
                        break;
                    }
                    case "AssertSignal":
                    {
                        // Existing DBC decoder, live CAN observations and real TEST verdict.
                        // No raw-hex second signal interpreter and no mock fallback.
                        var target = Target(node);
                        if (target.Kind != TestTargetKind.Signal)
                            throw new InvalidOperationException("CAN 信号判定必须选择 DBC 信号。");
                        var op = Property(node, "operator");
                        if (!TryEvaluate(Property(node, "expected"), out var expected))
                        {
                            incomplete = true;
                            Fact(node, RealStepStatus.NoData, detail: "CAN 判定期望值引用的变量无有效数据。");
                            break;
                        }
                        var timeout = Milliseconds(node, "timeoutMs");
                        bool sawValid = false, matched = false;
                        string actual = "";
                        string? evidence = null;
                        using var timeoutStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutStop.CancelAfter(timeout);
                        try
                        {
                            await foreach (var sample in _signals.ObserveAsync(target, timeoutStop.Token)
                                .WithCancellation(timeoutStop.Token))
                            {
                                if (!sample.HasUsableValue) continue;
                                sawValid = true;
                                actual = sample.Observation!.Value!.Value.ToString("R",
                                    CultureInfo.InvariantCulture);
                                evidence = sample.Observation.Evidence.ToString();
                                if (Compare(actual, op, expected))
                                {
                                    matched = true;
                                    break;
                                }
                            }
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                        if (!sawValid)
                        {
                            incomplete = true;
                            Fact(node, RealStepStatus.NoData, op + " " + expected, "",
                                "等待超时，未收到可用的 DBC 信号数据。", evidence);
                        }
                        else
                        {
                            asserted = true;
                            failed |= !matched;
                            Fact(node, matched ? RealStepStatus.Completed : RealStepStatus.ConditionFalse,
                                op + " " + expected, actual,
                                matched ? "CAN 信号判定通过" : "CAN 信号未在规定时间内满足判定条件",
                                evidence);
                        }
                        break;
                    }
                    case "Calculate":
                    {
                        var hasLeft = TryEvaluate(Property(node, "left"), out var left);
                        var hasRight = TryEvaluate(Property(node, "right"), out var right);
                        var op = Property(node, "operator");
                        if (!hasLeft || !hasRight ||
                            !double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                            !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                        {
                            variables[Property(node, "variable")] = new TestDatum(null,
                                TestSignalStatus.NoFrame, DateTimeOffset.UtcNow);
                            incomplete = true;
                            Fact(node, RealStepStatus.NoData, op, "", "Operand unavailable");
                            break;
                        }
                        double answer = op switch
                        {
                            "+" => x + y, "-" => x - y, "*" => x * y,
                            "/" when y != 0 => x / y,
                            _ => throw new FormatException("Unsupported arithmetic or divide by zero")
                        };
                        if (!double.IsFinite(answer)) throw new ArithmeticException("Non-finite calculation");
                        var value = answer.ToString("R", CultureInfo.InvariantCulture);
                        variables[Property(node, "variable")] = new TestDatum(value, null, DateTimeOffset.UtcNow);
                        Fact(node, RealStepStatus.Completed, "", value);
                        break;
                    }
                    case "Expect":
                    {
                        var left = Property(node, "left");
                        var right = Property(node, "right");
                        // Both operands must exist and carry usable quality before
                        // an equality can create a PASS judgement.
                        var hasLeft = TryEvaluate(left, out var value);
                        var hasRight = TryEvaluate(right, out var expected);
                        if (!hasLeft || !hasRight)
                        {
                            Fact(node, RealStepStatus.NoData, hasRight ? expected : right,
                                hasLeft ? value : "", "Missing or invalid comparison operand");
                            incomplete = true;
                            break;
                        }
                        var pass = Compare(value, Property(node, "operator"), expected);
                        asserted = true;
                        failed |= !pass;
                        Fact(node, pass ? RealStepStatus.Completed : RealStepStatus.ConditionFalse,
                            expected, value, pass ? "Expectation met" : "Expectation failed");
                        break;
                    }
                    case "WaveStart":
                        await _waves.StartAsync(Target(node), Property(node, "captureKey"),
                            Milliseconds(node, "durationMs", 3600000), ct);
                        Fact(node, RealStepStatus.Completed, detail: "Capture started");
                        break;
                    case "WaveStop":
                    {
                        var sample = await _waves.StopAsync(Property(node, "captureKey"), ct);
                        variables[Property(node, "variable")] = new TestDatum(sample.Value,
                            sample.Available ? TestSignalStatus.Valid : TestSignalStatus.NoFrame,
                            DateTimeOffset.UtcNow, sample.Evidence);
                        if (!sample.Available) incomplete = true;
                        Fact(node, sample.Available ? RealStepStatus.Completed : RealStepStatus.NoData,
                            actual: sample.Value ?? "", detail: sample.Code + ": " + sample.Detail,
                            evidence: sample.Evidence);
                        break;
                    }
                    case "WaveAnalyze":
                    {
                        var profileName = Property(node, "profile");
                        TestWaveResult analysis;
                        if (!string.IsNullOrWhiteSpace(profileName))
                        {
                            if (!mappings.TryGetValue(profileName, out var mapping) ||
                                mapping.Kind != TestTargetKind.WaveProfile)
                                throw new InvalidOperationException("Wave analysis profile is not mapped: " + profileName);
                            analysis = await _waves.AnalyzeWithProfileAsync(
                                Property(node, "captureKey"), mapping,
                                Property(node, "reference"), ct);
                        }
                        else
                        {
                            analysis = await _waves.AnalyzeAsync(Property(node, "captureKey"),
                                Property(node, "reference"),
                                double.Parse(Property(node, "tolerance"), CultureInfo.InvariantCulture),
                                double.Parse(Property(node, "coverage"), CultureInfo.InvariantCulture), ct);
                        }
                        variables[Property(node, "variable")] = new TestDatum(analysis.Value,
                            analysis.Available ? TestSignalStatus.Valid : TestSignalStatus.DecodeError,
                            DateTimeOffset.UtcNow, analysis.Evidence);
                        if (!analysis.Available) incomplete = true;
                        Fact(node, analysis.Available ? RealStepStatus.Completed : RealStepStatus.InvalidData,
                            actual: analysis.Value ?? "", detail: analysis.Code + ": " + analysis.Detail,
                            evidence: analysis.Evidence);
                        break;
                    }
                    default:
                        throw new NotSupportedException("Real runner cannot execute step " + kind);
                }
            }
        }
        try
        {
            try
            {
                preparing = true;
                await Execute(document.Get(TestPhase.Prepare), cts.Token);
                preparing = false;
                if (failed || incomplete)
                {
                    // Prepare is an eligibility gate: never physically actuate
                    // the test phase after a failed/no-data safety prerequisite.
                    facts.Add(new RealStepFact(default, "PrepareGate",
                        failed ? RealStepStatus.ConditionFalse : RealStepStatus.NoData,
                        "", "", "准备阶段未通过，未进入测试阶段；仍将执行清理。",
                        DateTimeOffset.UtcNow));
                }
                else await Execute(document.Get(TestPhase.Test), cts.Token);
                // An async CAN sender can fail after the last foreground TEST
                // step. Do not publish a PASS without checking it.
                await _actions.CheckHealthAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                aborted = true;
                facts.Add(new RealStepFact(default, "Run", RealStepStatus.Cancelled,
                    "", "", "Operator stopped run", DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                error = true;
                facts.Add(new RealStepFact(default, "Run", RealStepStatus.DeviceError,
                    "", "", ex.Message, DateTimeOffset.UtcNow));
            }
            finally
            {
                using (var scriptedCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
                {
                    try
                    {
                        await Execute(document.Get(TestPhase.Cleanup), scriptedCleanup.Token,
                            cleanup: true);
                    }
                    catch (Exception ex)
                    {
                        error = true;
                        facts.Add(new RealStepFact(default, "Cleanup", RealStepStatus.DeviceError,
                            "", "", ex.Message, DateTimeOffset.UtcNow));
                    }
                }
                // Physical KEY release and CAN sender cancellation must have their
                // own timeout budget, even when a scripted cleanup step stalled.
                using (var stationCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
                {
                    try { await _actions.CleanupAsync(stationCleanup.Token); }
                    catch (Exception ex)
                    {
                        error = true;
                        facts.Add(new RealStepFact(default, "StationCleanup", RealStepStatus.DeviceError,
                            "", "", ex.Message, DateTimeOffset.UtcNow));
                    }
                }
                using (var waveCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
                {
                    try { await _waves.CleanupAsync(waveCleanup.Token); }
                    catch (Exception ex)
                    {
                        error = true;
                        facts.Add(new RealStepFact(default, "WaveCleanup", RealStepStatus.DeviceError,
                            "", "", ex.Message, DateTimeOffset.UtcNow));
                    }
                }
            }
            verdict = aborted ? RealTestVerdict.Aborted :
                error ? RealTestVerdict.Error :
                failed ? RealTestVerdict.Failed :
                asserted && !incomplete ? RealTestVerdict.Passed : RealTestVerdict.Inconclusive;
        }
        finally
        {
            lock (_gate) { _stop = null; _resume = null; _pause = false; _singleStep = false; }
            cts.Dispose();
        }
        return new RealRunResult(run, prepared.Fingerprint, verdict,
            facts.ToImmutable(), new Dictionary<string, TestDatum>(variables, StringComparer.OrdinalIgnoreCase));
    }
}
