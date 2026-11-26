using System.Collections.Immutable;
using System.Diagnostics;
using AutoTest.Contracts;
using System.Globalization;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;

namespace AutoTest.Testing.Runtime;

public enum RealStepStatus { Completed, Skipped, ConditionFalse, NoData, Timeout, InvalidData, DeviceError, Cancelled }
public enum RealTestVerdict { Passed, Failed, Inconclusive, Error, Aborted, CompletedNoAssertion }

public sealed record RealStepFact(StepId Step, string Type, RealStepStatus Status,
    string Expected, string Actual, string Detail, DateTimeOffset At, string? Evidence = null)
{
    // Optional properties preserve legacy result readers without assigning fake occurrence IDs.
    public Guid StepOccurrenceId { get; init; }
    public Guid? ParentOccurrenceId { get; init; }
    public TestPhase? Phase { get; init; }
    public string ExecutionPath { get; init; } = "";
    public int? LoopIteration { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public TimeSpan? Duration { get; init; }
    public SignalObservation? Observation { get; init; }
    public TestSignalStatus? ObservationStatus { get; init; }
    public string? RequirementId { get; init; }
    public string? ReportNote { get; init; }
    public bool ShowEvidence { get; init; } = true;
    public ImmutableDictionary<string, TestDatum> AdditionalResults { get; init; } =
        ImmutableDictionary<string, TestDatum>.Empty;
}

public sealed record RealRunResult(RunId Run, string PlanFingerprint, RealTestVerdict Verdict,
    ImmutableArray<RealStepFact> Facts, IReadOnlyDictionary<string, TestDatum> Variables)
{
    public string? EvidencePath { get; init; }
    // Links the actual RECORD CaseAttemptId into the parent ProjectRun receipt.
    public Guid CaseAttemptId { get; init; }
}

/// <summary>
/// A variable retains its data quality and source; no implicit verdict is created by Read.
/// The 'Value' is deliberately kept separately from a test step's comparison result.
/// </summary>
public sealed record TestDatum(string? Value, TestSignalStatus? Quality,
    DateTimeOffset At, string? Evidence = null);

public sealed record TestWaveResult(bool Available, string Code, string? Value,
    string? Evidence, string Detail);

/// <summary>Optional physical-write provenance for actual periodic tasks.</summary>
public interface ITestStepContextPort
{
    void BindStep(TestDocumentId caseId, StepId stepId, Guid stepOccurrenceId);
}

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
    private readonly ITestFlashPort? _flash;
    private readonly object _gate = new();
    private CancellationTokenSource? _stop;
    private TaskCompletionSource<bool>? _resume;
    private bool _pause, _singleStep;
    public bool IsRunning { get { lock (_gate) return _stop is not null; } }
    /// <summary>Emitted only after the corresponding reviewed RECORD sink has
    /// accepted step completion, so UI feedback follows durable step evidence.</summary>
    public event Action<RealStepFact>? StepCompleted;
    /// <summary>Real step opening, emitted only after the durable RECORD sink accepts it.</summary>
    public event Action<StepId, string, TestPhase>? StepStarted;

    public RealRunCoordinator(ITestSignalPort signals, ITestActuationPort actions, ITestWavePort waves,
        ITestFlashPort? flash = null)
    {
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _waves = waves ?? throw new ArgumentNullException(nameof(waves));
        _flash = flash;
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

    public Task<RealRunResult> StartAsync(PreparedTestPlan prepared,
        CancellationToken external = default) =>
        StartAsync(prepared, RunId.New(), null, external);

    /// <summary>RunId must match an already begun RunRecord before any DUT write.</summary>
    public async Task<RealRunResult> StartAsync(PreparedTestPlan prepared, RunId run,
        IRealRunEventSink? events, CancellationToken external = default,
        bool retainProjectActuation = false)
    {
        if (run.Value == Guid.Empty) throw new ArgumentException("Invalid RunId.", nameof(run));
        if (!prepared.ValidateFingerprint() || !prepared.Diagnostics.IsEmpty)
            throw new InvalidOperationException("Real test plan must be compiled and valid.");
        var document = prepared.RestoreDocument();
        var mappings = document.Mappings.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        TestTargetMapping Target(StepNode node) =>
            mappings.TryGetValue(Property(node, "target"), out var found) ? found
            : throw new KeyNotFoundException("Unresolved test target: " + Property(node, "target"));

        // Verify port observations against the version-pinned mapping, not only
        // the decoded numeric value. A stale DBC signal must never satisfy an assertion.
        static bool MatchesFrozenSignal(TestTargetMapping target, SignalObservation observation)
        {
            var source = observation.Signal;
            return target.Kind == TestTargetKind.Signal &&
                source.DbcAsset.Id == target.Asset &&
                source.DbcAsset.Revision.Value == target.AssetRevision &&
                string.Equals(source.DbcAsset.Fingerprint.Sha256, target.AssetSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                source.Role == target.Role && source.FrameId == target.CanId &&
                string.Equals(source.SignalName, target.SignalName, StringComparison.Ordinal) &&
                string.Equals(source.Unit, target.Unit, StringComparison.Ordinal) &&
                string.Equals(observation.Unit, target.Unit, StringComparison.Ordinal);
        }

        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_stop is not null) throw new InvalidOperationException("Station already executing.");
            cts = CancellationTokenSource.CreateLinkedTokenSource(external);
            _stop = cts; _pause = false; _singleStep = false; _resume = null;
        }

        var facts = ImmutableArray.CreateBuilder<RealStepFact>();
        var variables = new Dictionary<string, TestDatum>(StringComparer.OrdinalIgnoreCase);
        var verdict = RealTestVerdict.Inconclusive;
        bool asserted = false, failed = false, error = false, aborted = false, incomplete = false;
        bool preparing = false;
        // Unlike FALSE, an UNKNOWN IF never permits later normal TEST writes.
        // Scripted station cleanup still runs independently in the finally block.
        bool stopAfterUnknownBranch = false;
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

        async Task Execute(ImmutableArray<StepNode> nodes, CancellationToken ct, bool cleanup = false,
            TestPhase phase = TestPhase.Test, Guid? parentOccurrenceId = null,
            string parentPath = "", int? loopIteration = null)
        {
            foreach (var node in nodes)
            {
                // A failed preparation prerequisite stops the rest of Prepare,
                // including nested steps, before any further physical writes.
                if (!cleanup && (stopAfterUnknownBranch || preparing && (failed || incomplete)))
                    return;
                if (!cleanup)
                {
                    await Boundary(ct);
                    await _actions.CheckHealthAsync(ct);
                }
                else ct.ThrowIfCancellationRequested();
                var startedUtc = DateTimeOffset.UtcNow;
                var startedTick = Stopwatch.GetTimestamp();
                var occurrence = Guid.NewGuid();
                var path = parentPath + "/" + node.Id.Value.ToString("N") +
                    (loopIteration is { } iteration ? "[iteration=" + iteration + "]" : "");
                var start = new RealStepStarted(run, node.Id, occurrence, parentOccurrenceId,
                    phase, node.Type, node.Name, path, loopIteration, startedUtc);
                if (events is not null) await events.StepStartedAsync(start, ct);
                try { StepStarted?.Invoke(node.Id, node.Name, phase); }
                catch { /* Read-only presentation cannot interrupt DUT execution. */ }
                if (!StepExecutionPolicy.IsEnabled(node))
                {
                    var skipped = new RealStepFact(node.Id, node.Type, RealStepStatus.Skipped,
                        "", "", "步骤已关闭，本次未执行。", DateTimeOffset.UtcNow)
                    {
                        StepOccurrenceId = occurrence,
                        ParentOccurrenceId = parentOccurrenceId,
                        Phase = phase,
                        ExecutionPath = path,
                        LoopIteration = loopIteration,
                        StartedUtc = startedUtc,
                        CompletedUtc = DateTimeOffset.UtcNow,
                        Duration = Stopwatch.GetElapsedTime(startedTick)
                    };
                    facts.Add(skipped);
                    if (events is not null)
                        await events.StepCompletedAsync(run, skipped, CancellationToken.None);
                    try { StepCompleted?.Invoke(skipped); } catch { /* UI cannot stop DUT cleanup */ }
                    continue;
                }
                var firstFact = facts.Count;
                TestSignalResult? observationForStep = null;
                bool gapObserved = false;
                bool bindingInvalid = false;
                Exception? stepFailure = null;
                try
                {
                var kind = node.Type;
                switch (kind)
                {
                    case "Sequence":
                        Fact(node, RealStepStatus.Completed);
                        await Execute(node.Slots["body"], ct, cleanup, phase, occurrence, path, loopIteration);
                        break;
                    case "If":
                    {
                        var sourceName = Property(node, "variable");
                        if (!variables.TryGetValue(sourceName, out var source) ||
                            source.Value is null ||
                            (source.Quality is not null and not TestSignalStatus.Valid))
                        {
                            incomplete = true;
                            if (!cleanup) stopAfterUnknownBranch = true;
                            Fact(node, RealStepStatus.NoData,
                                expected: node.Properties.GetValueOrDefault("equals", ""),
                                actual: source?.Value ?? "",
                                detail: "IF 无法判断：" + sourceName +
                                    " 未产生有效数据；未进入 THEN/ELSE，已阻断后续正常测试步骤。");
                            break;
                        }
                        var actual = source.Value;
                        if (!TryEvaluate(Property(node, "equals"), out var expected))
                        {
                            incomplete = true;
                            if (!cleanup) stopAfterUnknownBranch = true;
                            Fact(node, RealStepStatus.NoData,
                                expected: Property(node, "equals"), actual: actual,
                                detail: "IF 比较值引用了尚无有效数据的变量；未执行任何分支，已阻断后续正常测试步骤。");
                            break;
                        }
                        // Existing TEST files omit 'operator' and retain ==.
                        var comparator = node.Properties.GetValueOrDefault("operator", "==");
                        var pass = Compare(actual, comparator, expected);
                        Fact(node, RealStepStatus.Completed, comparator + " " + expected,
                            actual, "IF " + sourceName + " " + comparator + " " +
                                expected + " → " + (pass ? "THEN" : "ELSE"));
                        await Execute(node.Slots[pass ? "then" : "else"], ct, cleanup,
                            phase, occurrence, path, loopIteration);
                        break;
                    }
                    case "Switch":
                    {
                        var sourceName = Property(node, "variable");
                        if (!variables.TryGetValue(sourceName, out var source) ||
                            source.Value is null ||
                            (source.Quality is not null and not TestSignalStatus.Valid))
                        {
                            incomplete = true;
                            if (!cleanup) stopAfterUnknownBranch = true;
                            Fact(node, RealStepStatus.NoData, actual: source?.Value ?? "",
                                detail: "SWITCH 缺少有效变量：" + sourceName +
                                    "；未选择任何 CASE，已阻断后续正常测试步骤。");
                            break;
                        }
                        var actual = source.Value;
                        var arms = node.Slots["cases"]
                            .Where(StepExecutionPolicy.IsEnabled).ToArray();
                        var match = arms.FirstOrDefault(arm =>
                            !Property(arm, "equals").Trim().Equals("default",
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(Property(arm, "equals").Trim(), actual,
                                StringComparison.OrdinalIgnoreCase));
                        match ??= arms.FirstOrDefault(arm =>
                            Property(arm, "equals").Trim().Equals("default",
                                StringComparison.OrdinalIgnoreCase));
                        if (match is null)
                        {
                            Fact(node, RealStepStatus.ConditionFalse, actual: actual,
                                detail: "SWITCH 没有匹配值且未设置 DEFAULT；未执行任何 CASE。");
                            break;
                        }
                        Fact(node, RealStepStatus.Completed,
                            expected: Property(match, "equals"), actual: actual,
                            detail: "SWITCH " + sourceName + " → CASE " +
                                Property(match, "equals") + "（不会继续执行后续分支）");
                        await Execute(ImmutableArray.Create(match), ct, cleanup,
                            phase, occurrence, path, loopIteration);
                        break;
                    }
                    case "Case":
                    {
                        // CASE may only occur under its parent SWITCH after
                        // structural validation. One selected arm gets its
                        // own StepOccurrence and never falls through.
                        Fact(node, RealStepStatus.Completed,
                            expected: Property(node, "equals"));
                        await Execute(node.Slots["body"], ct, cleanup,
                            phase, occurrence, path, loopIteration);
                        break;
                    }
                    case "Loop":
                    {
                        var count = Milliseconds(node, "count", 1000);
                        Fact(node, RealStepStatus.Completed, count.ToString(CultureInfo.InvariantCulture));
                        for (var i = 0; i < count; i++)
                            await Execute(node.Slots["body"], ct, cleanup,
                                phase, occurrence, path, i + 1);
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
                        if (_actions is ITestStepContextPort context)
                            context.BindStep(document.Id, node.Id, occurrence);
                        var target = Target(node);
                        var action = Evaluate(Property(node, "value"));
                        if (node.Properties.TryGetValue("periodMs", out var intervalText) &&
                            intervalText.Length > 0)
                        {
                            var interval = Milliseconds(node, "periodMs");
                            if (interval > 0)
                            {
                                if (target.Kind is not (TestTargetKind.CanMessage or TestTargetKind.RawCan))
                                    throw new ArgumentException("周期发送只能用于 ECU CAN 报文。");
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
                    case "Flash":
                    {
                        var port = _flash ?? throw new NotSupportedException(
                            "FLASH 未接入真实硬件会话；禁止模拟成功。");
                        var flash = await port.ExecuteAsync(node, ct);
                        var status = !flash.ProtocolCompleted ? RealStepStatus.DeviceError :
                            flash.AppVerified ? RealStepStatus.Completed : RealStepStatus.NoData;
                        Fact(node, status, Property(node, "flashJobFingerprint"),
                            flash.Code + " ACK=" + flash.TransferAcknowledged +
                            " VERIFIED=" + flash.VerifiedBytes, flash.Diagnostic, flash.Evidence);
                        if (!flash.ProtocolCompleted)
                            throw new IOException("真实烧录协议未完成：" + flash.Code);
                        if (!flash.AppVerified)
                        {
                            // An ACKed download/reset is not evidence that the ECU
                            // returned to its application. Do not run subsequent TEST
                            // actions on a potentially bootloader-only ECU.
                            incomplete = true;
                            throw new IOException(
                                "FLASH 传输后未确认 ECU APP 恢复；已终止正常测试步骤并进入安全清理。");
                        }
                        break;
                    }
                    case "CanStopPeriodic":
                    {
                        await _actions.StopPeriodicAsync(ct);
                        Fact(node, RealStepStatus.Completed, "", "", "已停止当前运行作用域的全部 ECU CAN 周期发送。");
                        break;
                    }
                    case "Read":
                    {
                        var mode = Enum.Parse<TestReadMode>(Property(node, "mode"), true);
                        var target = Target(node);
                        var result = await _signals.ReadAsync(target, mode,
                            TimeSpan.FromMilliseconds(Milliseconds(node, "timeoutMs")),
                            TimeSpan.FromMilliseconds(Milliseconds(node, "maxAgeMs")), ct);
                        if (result.Observation is { } sample &&
                            !MatchesFrozenSignal(target, sample))
                        {
                            bindingInvalid = true;
                            result = new TestSignalResult(TestSignalStatus.DecodeError,
                                sample, "DBC_SNAPSHOT_MISMATCH");
                        }
                        observationForStep = result;
                        // A failed read is a real test-data gap. A later unrelated
                        // successful assertion must not make the overall run PASS.
                        if (!result.HasUsableValue) incomplete = true;
                        if (result.Status == TestSignalStatus.LostFrames)
                        {
                            incomplete = true;
                            gapObserved = true;
                        }
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
                            await foreach (var raw in _signals.ObserveAsync(match, timer.Token)
                                .WithCancellation(timer.Token))
                            {
                                var sample = raw;
                                if (raw.Observation is { } signal &&
                                    !MatchesFrozenSignal(match, signal))
                                {
                                    bindingInvalid = true;
                                    incomplete = true;
                                    sample = new TestSignalResult(TestSignalStatus.DecodeError,
                                        signal, "DBC_SNAPSHOT_MISMATCH");
                                }
                                if (sample.HasUsableValue || observationForStep is null)
                                    observationForStep = sample;
                                if (sample.Status == TestSignalStatus.LostFrames)
                                {
                                    incomplete = true;
                                    gapObserved = true;
                                }
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
                            await foreach (var raw in _signals.ObserveAsync(target, timeoutStop.Token)
                                .WithCancellation(timeoutStop.Token))
                            {
                                var sample = raw;
                                if (raw.Observation is { } signal &&
                                    !MatchesFrozenSignal(target, signal))
                                {
                                    bindingInvalid = true;
                                    incomplete = true;
                                    sample = new TestSignalResult(TestSignalStatus.DecodeError,
                                        signal, "DBC_SNAPSHOT_MISMATCH");
                                }
                                if (sample.HasUsableValue || observationForStep is null)
                                    observationForStep = sample;
                                if (sample.Status == TestSignalStatus.LostFrames)
                                {
                                    incomplete = true;
                                    gapObserved = true;
                                }
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
                    case "WaveCompare":
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
                            throw new InvalidDataException(
                                "真实波形判定必须绑定经过工程师复核的 WAVE 配置资源。" +
                                "旧式自由参考名与步骤临时容差不能执行，也不能生成有效判定。");
                        }
                        variables[Property(node, "variable")] = new TestDatum(analysis.Value,
                            analysis.Available ? TestSignalStatus.Valid :
                                analysis.Code == "Inconclusive"
                                    ? TestSignalStatus.NoFrame : TestSignalStatus.DecodeError,
                            DateTimeOffset.UtcNow, analysis.Evidence);
                        if (kind == "WaveCompare")
                        {
                            // Only a completed, quality-checked real waveform comparison
                            // can produce an assertion. Missing/reference-incompatible
                            // evidence stays inconclusive; it is never a PASS.
                            var status = analysis.Available && analysis.Code == "Pass"
                                ? RealStepStatus.Completed
                                : analysis.Available && analysis.Code == "Fail"
                                    ? RealStepStatus.ConditionFalse
                                    : analysis.Code == "Inconclusive"
                                        ? RealStepStatus.NoData : RealStepStatus.InvalidData;
                            if (status is RealStepStatus.Completed or RealStepStatus.ConditionFalse)
                                asserted = true;
                            if (status == RealStepStatus.ConditionFalse)
                                failed = true;
                            if (status is RealStepStatus.NoData or RealStepStatus.InvalidData)
                                incomplete = true;
                            Fact(node, status, expected: "Pass",
                                actual: analysis.Value ?? analysis.Code,
                                detail: "Waveform judgement " + analysis.Code + ": " + analysis.Detail,
                                evidence: analysis.Evidence);
                        }
                        else
                        {
                            // Analysis only produces data; never an implicit verdict.
                            if (!analysis.Available) incomplete = true;
                            Fact(node, analysis.Available ? RealStepStatus.Completed :
                                analysis.Code == "Inconclusive" ? RealStepStatus.NoData :
                                    RealStepStatus.InvalidData, actual: analysis.Value ?? "",
                                detail: analysis.Code + ": " + analysis.Detail,
                                evidence: analysis.Evidence);
                        }
                        break;
                    }
                    default:
                        throw new NotSupportedException("Real runner cannot execute step " + kind);
                }
                }
                catch (Exception ex)
                {
                    stepFailure = ex;
                    throw;
                }
                finally
                {
                    var own = -1;
                    for (var i = firstFact; i < facts.Count; i++)
                        if (facts[i].Step == node.Id) { own = i; break; }
                    if (own < 0)
                    {
                        Fact(node, stepFailure is OperationCanceledException
                            ? RealStepStatus.Cancelled : RealStepStatus.DeviceError,
                            detail: stepFailure?.Message ?? "Step exited without a result fact.");
                        own = facts.Count - 1;
                    }
                    var original = facts[own];
                    var recording = StepResultRecordingOptions.Read(node);
                    var extra = ImmutableDictionary.CreateBuilder<string, TestDatum>(
                        StringComparer.OrdinalIgnoreCase);
                    if (recording.Capture == ExtraResultCapture.Always ||
                        recording.Capture == ExtraResultCapture.NonPass &&
                        original.Status != RealStepStatus.Completed)
                        foreach (var name in recording.ExtraVariables)
                            extra[name] = variables.TryGetValue(name, out var datum) ? datum :
                                new TestDatum(null, TestSignalStatus.NoFrame, DateTimeOffset.UtcNow);

                    var completed = original with
                    {
                        Status = stepFailure is null
                            ? (gapObserved || bindingInvalid) &&
                                original.Status == RealStepStatus.Completed
                                ? RealStepStatus.InvalidData : original.Status
                            : stepFailure is OperationCanceledException
                                ? RealStepStatus.Cancelled : RealStepStatus.DeviceError,
                        Detail = original.Detail +
                            (gapObserved ? " | SIGNAL_GAP: frame/record loss" : "") +
                            (bindingInvalid ? " | DBC_SNAPSHOT_MISMATCH" : "") +
                            (stepFailure is null ? "" : " | " + stepFailure.Message),
                        Evidence = original.Evidence ??
                            (stepFailure is TestFlashExecutionException flashFailure
                                ? flashFailure.EvidenceId : null),
                        StepOccurrenceId = occurrence,
                        ParentOccurrenceId = parentOccurrenceId,
                        Phase = phase,
                        ExecutionPath = path,
                        LoopIteration = loopIteration,
                        StartedUtc = startedUtc,
                        CompletedUtc = DateTimeOffset.UtcNow,
                        Duration = Stopwatch.GetElapsedTime(startedTick),
                        Observation = observationForStep?.Observation,
                        ObservationStatus = observationForStep?.Status,
                        RequirementId = recording.RequirementId,
                        ReportNote = recording.ReportNote,
                        ShowEvidence = recording.ShowEvidence,
                        AdditionalResults = extra.ToImmutable()
                    };
                    facts[own] = completed;
                    // Durable observers must finish before the next physical action.
                    if (events is not null)
                        await events.StepCompletedAsync(run, completed, CancellationToken.None);
                    try { StepCompleted?.Invoke(completed); } catch { /* UI cannot interrupt physical run */ }
                }
            }
        }
        try
        {
            try
            {
                preparing = true;
                await Execute(document.Get(TestPhase.Prepare), cts.Token, phase: TestPhase.Prepare);
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
                else await Execute(document.Get(TestPhase.Test), cts.Token, phase: TestPhase.Test);
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
                            cleanup: true, phase: TestPhase.Cleanup);
                    }
                    catch (Exception ex)
                    {
                        error = true;
                        facts.Add(new RealStepFact(default, "Cleanup", RealStepStatus.DeviceError,
                            "", "", ex.Message, DateTimeOffset.UtcNow));
                    }
                }
                // A project's physical scope outlives each independently recorded
                // case. Authored Cleanup steps ran above; only the project owner
                // can stop shared CAN tasks and revoke the physical USB fence.
                if (!retainProjectActuation)
                {
                    using var stationCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(6));
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
                asserted && !incomplete ? RealTestVerdict.Passed :
                retainProjectActuation && !incomplete ? RealTestVerdict.CompletedNoAssertion :
                RealTestVerdict.Inconclusive;
        }
        finally
        {
            lock (_gate) { _stop = null; _resume = null; _pause = false; _singleStep = false; }
            cts.Dispose();
        }
        var completedRun = new RealRunResult(run, prepared.Fingerprint, verdict,
            facts.ToImmutable(), new Dictionary<string, TestDatum>(variables, StringComparer.OrdinalIgnoreCase));
        if (events is not null)
            await events.RunCompletedAsync(completedRun, CancellationToken.None);
        return completedRun;
    }
}
