using System.Collections.Immutable;
using System.Globalization;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;

namespace AutoTest.Testing.Runtime;

public enum RealStepStatus { Completed, ConditionFalse, NoData, Timeout, InvalidData, DeviceError, Cancelled }
public enum RealTestVerdict { Passed, Failed, Inconclusive, Error, Aborted }

public sealed record RealStepFact(StepId Step, string Type, RealStepStatus Status,
    string Expected, string Actual, string Detail, DateTimeOffset At, string? Evidence = null);

public sealed record RealRunResult(RunId Run, string PlanFingerprint, RealTestVerdict Verdict,
    ImmutableArray<RealStepFact> Facts, IReadOnlyDictionary<string, TestDatum> Variables);

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
    Task<TestWaveResult> AnalyzeAsync(string captureKey, string referenceKey, CancellationToken ct);
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
        bool asserted = false, failed = false, error = false, aborted = false;
        string Evaluate(string input) =>
            input.StartsWith("$", StringComparison.Ordinal) &&
                variables.TryGetValue(input[1..], out var datum) ? datum.Value ?? "" : input;
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
                if (!cleanup) await Boundary(ct);
                else ct.ThrowIfCancellationRequested();
                var kind = node.Type;
                switch (kind)
                {
                    case "Sequence":
                        Fact(node, RealStepStatus.Completed);
                        await Execute(node.Slots["body"], ct, cleanup);
                        break;
                    case "If":
                    {
                        var actual = Evaluate("$" + Property(node, "variable"));
                        var expected = Evaluate(Property(node, "equals"));
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
                        var value = Evaluate(Property(node, "value"));
                        variables[Property(node, "variable")] = new TestDatum(value, null, DateTimeOffset.UtcNow);
                        Fact(node, RealStepStatus.Completed, "", value);
                        break;
                    }
                    case "Write":
                    {
                        var result = await _actions.WriteAsync(Target(node), Evaluate(Property(node, "value")), ct);
                        Fact(node, result.Acknowledged ? RealStepStatus.Completed : RealStepStatus.DeviceError,
                            Property(node, "value"), result.Code, result.Diagnostic, result.Evidence);
                        if (!result.Acknowledged) throw new IOException("Device did not acknowledge: " + result.Code);
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
                        variables[Property(node, "variable")] = new TestDatum(value, result.Status,
                            result.Observation?.Time.Utc ?? DateTimeOffset.UtcNow,
                            result.Observation?.Evidence.ToString());
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
                        var expected = Evaluate(Property(node, "expected"));
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
                        Fact(node, matched ? RealStepStatus.Completed :
                            sawValid ? RealStepStatus.ConditionFalse : RealStepStatus.NoData,
                            op + " " + expected, actual, matched ? "Condition observed" :
                            sawValid ? "Condition not observed before timeout" : "No valid CAN signal");
                        break;
                    }
                    case "Calculate":
                    {
                        var left = Evaluate(Property(node, "left"));
                        var right = Evaluate(Property(node, "right"));
                        var op = Property(node, "operator");
                        if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                            !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                        {
                            variables[Property(node, "variable")] = new TestDatum(null,
                                TestSignalStatus.NoFrame, DateTimeOffset.UtcNow);
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
                        var value = Evaluate(left);
                        var expected = Evaluate(Property(node, "right"));
                        // Unavailable data must NOT become a simple false comparison.
                        var missing = left.StartsWith("$", StringComparison.Ordinal) &&
                            (!variables.TryGetValue(left[1..], out var datum) || datum.Value is null);
                        if (missing)
                        {
                            Fact(node, RealStepStatus.NoData, expected, "", "Missing or invalid observation");
                            error = true;
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
                        Fact(node, sample.Available ? RealStepStatus.Completed : RealStepStatus.NoData,
                            actual: sample.Value ?? "", detail: sample.Code + ": " + sample.Detail,
                            evidence: sample.Evidence);
                        break;
                    }
                    case "WaveAnalyze":
                    {
                        var analysis = await _waves.AnalyzeAsync(Property(node, "captureKey"),
                            Property(node, "reference"), ct);
                        variables[Property(node, "variable")] = new TestDatum(analysis.Value,
                            analysis.Available ? TestSignalStatus.Valid : TestSignalStatus.DecodeError,
                            DateTimeOffset.UtcNow, analysis.Evidence);
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
                await Execute(document.Get(TestPhase.Prepare), cts.Token);
                await Execute(document.Get(TestPhase.Test), cts.Token);
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
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                try { await Execute(document.Get(TestPhase.Cleanup), cleanup.Token, cleanup: true); }
                catch (Exception ex)
                {
                    error = true;
                    facts.Add(new RealStepFact(default, "Cleanup", RealStepStatus.DeviceError,
                        "", "", ex.Message, DateTimeOffset.UtcNow));
                }
                try { await _waves.CleanupAsync(cleanup.Token); }
                catch (Exception ex)
                {
                    error = true;
                    facts.Add(new RealStepFact(default, "WaveCleanup", RealStepStatus.DeviceError,
                        "", "", ex.Message, DateTimeOffset.UtcNow));
                }
                try { await _actions.CleanupAsync(cleanup.Token); }
                catch (Exception ex)
                {
                    error = true;
                    facts.Add(new RealStepFact(default, "StationCleanup", RealStepStatus.DeviceError,
                        "", "", ex.Message, DateTimeOffset.UtcNow));
                }
            }
            verdict = aborted ? RealTestVerdict.Aborted :
                error ? RealTestVerdict.Error :
                failed ? RealTestVerdict.Failed :
                asserted ? RealTestVerdict.Passed : RealTestVerdict.Inconclusive;
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
