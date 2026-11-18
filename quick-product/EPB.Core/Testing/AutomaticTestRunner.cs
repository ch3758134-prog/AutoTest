using System.Globalization;
using System.Text.RegularExpressions;
using EPB.Core.Communication;
using EPB.Core.Configuration;

namespace EPB.Core.Testing;

public sealed class AutomaticTestRunner
{
    private readonly EcuConfiguration _configuration;
    private readonly ITestExecutionPort _port;
    private readonly IWaveformAssertionSource _waveformSource;
    private readonly IWaveformAssertionEvaluator _waveformEvaluator;
    private readonly TimeSpan _observationPollInterval;
    private readonly ResourceRelationService _resourceRelations = new();
    private ExecutionPlan? _executionPlan;

    public AutomaticTestRunner(
        EcuConfiguration configuration,
        ITestExecutionPort port,
        TimeSpan? observationPollInterval = null,
        IWaveformAssertionSource? waveformSource = null,
        IWaveformAssertionEvaluator? waveformEvaluator = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _port = port ?? throw new ArgumentNullException(nameof(port));
        _waveformSource = waveformSource ?? UnavailableWaveformAssertionSource.Instance;
        _waveformEvaluator = waveformEvaluator ?? new WaveformAssertionEvaluator();
        _observationPollInterval = observationPollInterval ?? TimeSpan.FromMilliseconds(20);
    }

    public Task<TestPlanResult> RunAsync(CancellationToken cancellationToken = default, IProgress<TestNodeResult>? progress = null)
        => RunAsync(_configuration.TestPlan, cancellationToken, progress);

    public async Task<TestPlanResult> RunAsync(TestPlan plan, CancellationToken cancellationToken = default, IProgress<TestNodeResult>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var result = new TestPlanResult { PlanId = plan.Id, Name = plan.Name, Status = TestResultStatus.运行中 };
        try
        {
            _executionPlan = new ExecutionPreflightService().Build(_configuration, plan);
            if (!_executionPlan.CanRun)
            {
                result.Status = TestResultStatus.执行错误;
                result.Error = string.Join(Environment.NewLine, _executionPlan.Diagnostics.Select(x =>
                    $"[{x.Code}] 用例={x.CaseName}，节点={x.NodeName}，步骤={x.StepName}，DefinitionId={x.DefinitionId}：{x.Message}"));
                foreach (var testCase in plan.Cases.Where(x => x.Enabled).OrderBy(x => x.Order))
                {
                    var caseDiagnostics = _executionPlan.Diagnostics.Where(x => x.CaseName == testCase.Name).ToList();
                    if (caseDiagnostics.Count == 0) continue;
                    var caseResult = new TestCaseResult { CaseId = testCase.Id, Name = testCase.Name, Status = TestResultStatus.执行错误 };
                    foreach (var diagnostic in caseDiagnostics)
                        caseResult.Nodes.Add(new TestNodeResult
                        {
                            NodeId = testCase.Flow.FirstOrDefault(x => x.Name == diagnostic.NodeName)?.Id ?? "",
                            Name = diagnostic.NodeName,
                            Status = TestResultStatus.执行错误,
                            Error = diagnostic.Message,
                            CompletedAt = DateTimeOffset.UtcNow
                        });
                    result.Cases.Add(caseResult);
                }
                return result;
            }

            var orderedCases = plan.Cases.OrderBy(x => x.Order).ToList();
            var enabledCases = orderedCases.Where(x => x.Enabled).ToList();
            for (var caseIndex = 0; caseIndex < orderedCases.Count; caseIndex++)
            {
                var testCase = orderedCases[caseIndex];
                cancellationToken.ThrowIfCancellationRequested();
                var caseResult = await RunCaseAsync(testCase, cancellationToken, progress).ConfigureAwait(false);
                result.Cases.Add(caseResult);
                if (caseResult.Status == TestResultStatus.已取消)
                {
                    result.Status = TestResultStatus.已取消;
                    return result;
                }
                if (caseResult.Status == TestResultStatus.执行错误)
                {
                    result.Status = TestResultStatus.执行错误;
                    return result;
                }

                var nextEnabledIndex = enabledCases.IndexOf(testCase) + 1;
                if (testCase.Enabled && testCase.WaitAfterMs > 0 && nextEnabledIndex > 0 && nextEnabledIndex < enabledCases.Count)
                    await Task.Delay(testCase.WaitAfterMs, cancellationToken).ConfigureAwait(false);
            }

            result.Status = result.Cases.Count == 0
                ? TestResultStatus.仅观察
                : result.Cases.Any(x => x.Status == TestResultStatus.判定失败)
                    ? TestResultStatus.判定失败
                    : result.Cases.All(x => x.Status is TestResultStatus.仅观察 or TestResultStatus.跳过)
                        ? TestResultStatus.仅观察
                        : TestResultStatus.通过;
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Status = TestResultStatus.已取消;
            return result;
        }
    }

    private async Task<TestCaseResult> RunCaseAsync(TestCaseDefinition testCase, CancellationToken cancellationToken, IProgress<TestNodeResult>? progress)
    {
        var result = new TestCaseResult { CaseId = testCase.Id, Name = testCase.Name };
        if (!testCase.Enabled)
        {
            result.Status = TestResultStatus.跳过;
            return result;
        }

        var executionFailed = false;
        var judgementFailed = false;
        var hasExpectation = false;
        var judgementPassed = false;
        var judgementCount = 0;
        var scope = new ExecutionScope();
        try
        {
            for (var nodeIndex = 0; nodeIndex < testCase.Flow.Count; nodeIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = testCase.Flow[nodeIndex];
                if (IsInlinePeriodicBroadcast(node))
                {
                    var group = new List<TestFlowNode>();
                    while (nodeIndex < testCase.Flow.Count && IsInlinePeriodicBroadcast(testCase.Flow[nodeIndex]))
                        group.Add(testCase.Flow[nodeIndex++]);
                    var groupedResults = await RunInlinePeriodicBroadcastGroupAsync(group, cancellationToken, progress).ConfigureAwait(false);
                    foreach (var nodeResult in groupedResults)
                    {
                        result.Nodes.Add(nodeResult);
                        hasExpectation |= ContainsExpectation(nodeResult);
                        executionFailed |= ContainsStatus(nodeResult, TestResultStatus.执行错误);
                        judgementFailed |= ContainsStatus(nodeResult, TestResultStatus.判定失败);
                        judgementPassed |= ContainsPassedExpectation(nodeResult);
                        judgementCount += CountExpectations(nodeResult);
                        if (ContainsStatus(nodeResult, TestResultStatus.已取消))
                        {
                            result.Status = TestResultStatus.已取消;
                            return result;
                        }
                    }
                    nodeIndex--;
                    if (executionFailed) break;
                    continue;
                }

                var singleResult = await RunNodeAsync(node, new HashSet<string>(), scope, cancellationToken, progress).ConfigureAwait(false);
                result.Nodes.Add(singleResult);
                hasExpectation |= ContainsExpectation(singleResult);
                executionFailed |= ContainsStatus(singleResult, TestResultStatus.执行错误);
                judgementFailed |= ContainsStatus(singleResult, TestResultStatus.判定失败);
                judgementPassed |= ContainsPassedExpectation(singleResult);
                judgementCount += CountExpectations(singleResult);
                if (ContainsStatus(singleResult, TestResultStatus.已取消))
                {
                    result.Status = TestResultStatus.已取消;
                    return result;
                }
                if (executionFailed) break;
            }

            result.Status = executionFailed
                ? TestResultStatus.执行错误
                : testCase.JudgementLogic == TestJudgementLogic.任一满足 && judgementCount > 0
                    ? judgementPassed ? TestResultStatus.通过 : TestResultStatus.判定失败
                    : judgementFailed
                    ? TestResultStatus.判定失败
                    : hasExpectation ? TestResultStatus.通过 : TestResultStatus.仅观察;
        }
        catch (OperationCanceledException)
        {
            result.Status = TestResultStatus.已取消;
        }
        return result;
    }

    private static bool IsInlinePeriodicBroadcast(TestFlowNode node) =>
        node.Enabled && node.Operation?.Kind == ConfigurationStepKind.广播报文 &&
        node.Operation.Broadcast.Mode == BroadcastMode.开始周期发送;

    private async Task<List<TestNodeResult>> RunInlinePeriodicBroadcastGroupAsync(
        IReadOnlyList<TestFlowNode> nodes,
        CancellationToken cancellationToken,
        IProgress<TestNodeResult>? progress)
    {
        var command = await _port.StartPeriodicBroadcastAsync(
            nodes.Select(x => SnapshotStep(x.Operation!).Broadcast).ToArray(), cancellationToken).ConfigureAwait(false);
        var results = new List<TestNodeResult>(nodes.Count);
        foreach (var node in nodes)
        {
            var child = new TestNodeResult
            {
                NodeId = node.Operation!.Id,
                Name = node.Operation.Name,
                Status = command.Succeeded ? TestResultStatus.通过 : TestResultStatus.执行错误,
                Error = command.Error,
                CompletedAt = DateTimeOffset.UtcNow
            };
            var result = new TestNodeResult
            {
                NodeId = node.Id,
                Name = node.Name,
                Status = child.Status,
                Error = child.Error,
                Children = { child },
                CompletedAt = child.CompletedAt
            };
            results.Add(result);
            progress?.Report(child);
            progress?.Report(result);
        }
        return results;
    }

    private async Task<TestNodeResult> RunNodeAsync(
        TestFlowNode node,
        HashSet<string> callStack,
        ExecutionScope scope,
        CancellationToken cancellationToken,
        IProgress<TestNodeResult>? progress)
    {
        var result = new TestNodeResult { NodeId = node.Id, Name = node.Name, Status = TestResultStatus.运行中 };
        if (!node.Enabled)
        {
            result.Status = TestResultStatus.跳过;
            result.CompletedAt = DateTimeOffset.UtcNow;
            progress?.Report(result);
            return result;
        }

        try
        {
            if (node.FlowControl is { } flowControl)
                return await RunFlowControlAsync(node, flowControl, callStack, scope, cancellationToken, progress).ConfigureAwait(false);

            // Operation is the authoritative inline step. A node may retain a
            // stale reference field during editing, but an explicit operation
            // must never be reinterpreted as a resource reference.
            if (node.Operation is { } directOperation)
            {
                var resolvedOperation = node.ParseAssertion is { } assertion
                    ? ResolveTestOperation(directOperation, assertion)
                    : directOperation;
                var childResult = await RunConfigurationStepAsync(resolvedOperation, callStack, cancellationToken, progress).ConfigureAwait(false);
                result.Children.Add(childResult);
                result.ProducedSignal = childResult.ProducedSignal;
                scope.Publish(node, result);
                if (ContainsStatus(childResult, TestResultStatus.已取消))
                {
                    result.Status = TestResultStatus.已取消;
                    result.Error = childResult.Error;
                    result.CompletedAt = DateTimeOffset.UtcNow;
                    progress?.Report(result);
                    return result;
                }
                if (ContainsStatus(childResult, TestResultStatus.执行错误))
                    return Fail(result, childResult.Error ?? "测试步骤执行失败。", progress);
                result.Status = ContainsStatus(childResult, TestResultStatus.判定失败)
                    ? TestResultStatus.判定失败
                    : ContainsExpectation(childResult)
                        ? TestResultStatus.通过
                        : childResult.Status == TestResultStatus.仅观察
                            ? TestResultStatus.仅观察
                            : TestResultStatus.通过;
            }
            else switch (node.Kind)
            {
                case TestFlowNodeKind.组合:
                    foreach (var child in node.Children)
                    {
                        var childResult = await RunNodeAsync(child, callStack, scope, cancellationToken, progress).ConfigureAwait(false);
                        result.Children.Add(childResult);
                        if (ContainsStatus(childResult, TestResultStatus.已取消))
                        {
                            result.Status = TestResultStatus.已取消;
                            result.Error = childResult.Error;
                            result.CompletedAt = DateTimeOffset.UtcNow;
                            progress?.Report(result);
                            return result;
                        }
                        if (ContainsStatus(childResult, TestResultStatus.执行错误))
                        {
                            result.Status = TestResultStatus.执行错误;
                            result.Error = childResult.Error;
                            result.CompletedAt = DateTimeOffset.UtcNow;
                            progress?.Report(result);
                            return result;
                        }
                    }
                    result.Status = result.Children.Any(x => ContainsStatus(x, TestResultStatus.判定失败))
                        ? TestResultStatus.判定失败
                        : result.Children.Any(ContainsExpectation)
                            ? TestResultStatus.通过
                            : TestResultStatus.仅观察;
                    break;
                case TestFlowNodeKind.引用序列:
                    var sequence = ResolveReferencedSequence(node);
                    if (sequence is null)
                        return Fail(result, DescribeMissingNodeReference(node), progress);
                    if (!callStack.Add(sequence.Id))
                        return Fail(result, $"检测到循环引用：{sequence.Name}", progress);
                    try
                    {
                        result.Children.AddRange(await RunConfigurationStepsAsync(sequence.Steps, callStack, cancellationToken, progress).ConfigureAwait(false));
                        if (result.Children.Any(x => ContainsStatus(x, TestResultStatus.已取消)))
                        {
                            result.Status = TestResultStatus.已取消;
                            result.Error = result.Children.First(x => ContainsStatus(x, TestResultStatus.已取消)).Error;
                            result.CompletedAt = DateTimeOffset.UtcNow;
                            progress?.Report(result);
                            return result;
                        }
                        if (result.Children.Any(x => ContainsStatus(x, TestResultStatus.执行错误)))
                            return Fail(result, result.Children.First(x => ContainsStatus(x, TestResultStatus.执行错误)).Error ?? "引用对象执行失败", progress);
                    }
                    finally { callStack.Remove(sequence.Id); }
                    result.Status = result.Children.Any(x => ContainsStatus(x, TestResultStatus.判定失败))
                        ? TestResultStatus.判定失败
                        : result.Children.Any(ContainsExpectation)
                            ? TestResultStatus.通过
                            : TestResultStatus.仅观察;
                    break;
                case TestFlowNodeKind.原子操作:
                    var operations = ResolveTestOperations(node);
                    if (operations is null)
                        return Fail(result, "测试步骤未绑定资源。", progress);
                    var referencedSequence = node.Operation is null ? ResolveReferencedSequence(node) : null;
                    if (referencedSequence is not null && !callStack.Add(referencedSequence.Id))
                        return Fail(result, $"检测到循环引用：{referencedSequence.Name}", progress);
                    try
                    {
                        result.Children.AddRange(await RunConfigurationStepsAsync(operations, callStack, cancellationToken, progress).ConfigureAwait(false));
                    }
                    finally
                    {
                        if (referencedSequence is not null)
                            callStack.Remove(referencedSequence.Id);
                    }
                    if (result.Children.Any(x => ContainsStatus(x, TestResultStatus.执行错误)))
                        return Fail(result, result.Children.First(x => ContainsStatus(x, TestResultStatus.执行错误)).Error ?? "测试步骤执行失败", progress);
                    if (result.Children.Any(x => ContainsStatus(x, TestResultStatus.已取消)))
                    {
                        result.Status = TestResultStatus.已取消;
                        result.Error = result.Children.First(x => ContainsStatus(x, TestResultStatus.已取消)).Error;
                        result.CompletedAt = DateTimeOffset.UtcNow;
                        progress?.Report(result);
                        return result;
                    }
                    result.Status = result.Children.Any(x => ContainsStatus(x, TestResultStatus.判定失败))
                        ? TestResultStatus.判定失败
                        : result.Children.Any(ContainsExpectation)
                            ? TestResultStatus.通过
                            : TestResultStatus.仅观察;
                    break;
                default:
                    return Fail(result, $"不支持的流程节点类型：{node.Kind}", progress);
            }
        }
        catch (OperationCanceledException)
        {
            result.Status = TestResultStatus.已取消;
            result.Error = "用户取消测试。";
        }
        catch (Exception ex)
        {
            result.Status = TestResultStatus.执行错误;
            result.Error = ex.Message;
        }

        result.CompletedAt = DateTimeOffset.UtcNow;
        progress?.Report(result);
        return result;
    }

    private IReadOnlyList<ConfigurationStep>? ResolveTestOperations(TestFlowNode node)
    {
        if (node.Operation is { } operation)
            return node.ParseAssertion is { } assertion
                ? [ResolveTestOperation(operation, assertion)]
                : [operation];

        var sequence = ResolveReferencedSequence(node);
        return sequence is null
            ? null
            : ApplyParseAssertion(sequence.Steps, node.ParseAssertion);
    }

    private IReadOnlyList<ConfigurationStep> ApplyParseAssertion(IReadOnlyList<ConfigurationStep> steps, ParseAssertion? assertion)
    {
        if (assertion is null || string.IsNullOrWhiteSpace(assertion.ParseResourceId))
            return steps;

        return steps.Select(step => ApplyParseAssertion(step, assertion)).ToList();
    }

    private ConfigurationStep ApplyParseAssertion(ConfigurationStep step, ParseAssertion assertion)
    {
        if (step.Id == assertion.ParseResourceId && step.Kind == ConfigurationStepKind.报文解析)
        {
            var resolved = CloneStep(step);
            resolved.Parse.Expectation = assertion.Expectation;
            resolved.Parse.TimeoutMs = Math.Max(0, assertion.TimeoutMs);
            return resolved;
        }

        if (step.Children.Count == 0)
            return step;

        var resolvedStep = CloneStep(step);
        resolvedStep.Children.Clear();
        foreach (var child in step.Children)
            resolvedStep.Children.Add(ApplyParseAssertion(child, assertion));
        return resolvedStep;
    }

    private ConfigurationStep ResolveTestOperation(ConfigurationStep operation, ParseAssertion assertion)
    {
        if (operation.Kind != ConfigurationStepKind.报文解析)
            return operation;

        // 直接内嵌解析步骤的定义参数来自 Operation，当前测试节点的期望和超时
        // 来自 ParseAssertion。两者合并到执行副本，不能因为没有 ParseResourceId
        // 就丢掉测试判定条件。
        var resolved = CloneStep(operation);
        resolved.Parse.Expectation = assertion.Expectation;
        resolved.Parse.TimeoutMs = Math.Max(0, assertion.TimeoutMs);
        return resolved;
    }

    private static ConfigurationStep CloneStep(ConfigurationStep step)
        => System.Text.Json.JsonSerializer.Deserialize<ConfigurationStep>(System.Text.Json.JsonSerializer.Serialize(step))
            ?? throw new InvalidOperationException("无法复制测试步骤资源。");

    private ConfigurationStep SnapshotStep(ConfigurationStep step)
        => _executionPlan?.StepsById.TryGetValue(step.Id, out var snapshot) == true
            ? snapshot
            : step;

    private async Task<TestNodeResult> RunFlowControlAsync(
        TestFlowNode node,
        TestFlowControlDefinition control,
        HashSet<string> callStack,
        ExecutionScope scope,
        CancellationToken cancellationToken,
        IProgress<TestNodeResult>? progress)
    {
        var result = new TestNodeResult { NodeId = node.Id, Name = node.Name, Status = TestResultStatus.运行中 };
        return control.Kind switch
        {
            TestFlowControlKind.Condition => await RunConditionControlAsync(node, control, result, callStack, scope, cancellationToken, progress).ConfigureAwait(false),
            TestFlowControlKind.WaitForVerdict => await RunWaitForVerdictAsync(node, control, result, callStack, scope, cancellationToken, progress).ConfigureAwait(false),
            TestFlowControlKind.ValueBranch => await RunValueBranchAsync(node, control, result, callStack, scope, cancellationToken, progress).ConfigureAwait(false),
            TestFlowControlKind.Repeat => await RunRepeatControlAsync(node, control, result, callStack, scope, cancellationToken, progress).ConfigureAwait(false),
            _ => Fail(result, $"不支持的流程控制类型：{control.Kind}。", progress)
        };
    }

    private async Task<TestNodeResult> RunConditionControlAsync(
        TestFlowNode node,
        TestFlowControlDefinition control,
        TestNodeResult result,
        HashSet<string> callStack,
        ExecutionScope scope,
        CancellationToken cancellationToken,
        IProgress<TestNodeResult>? progress)
    {
        if (!TryResolveVerdict(scope, control, out var producer, out var produced, out var error))
            return Fail(result, error!, progress);

        var selectedPath = produced!.Verdict == control.RequiredVerdict
            ? TestFlowPathKind.Then
            : TestFlowPathKind.Else;
        var path = node.Children.FirstOrDefault(x => x.PathKind == selectedPath);
        if (path is null)
        {
            if (selectedPath == TestFlowPathKind.Else)
            {
                result.CompletedAt = DateTimeOffset.UtcNow;
                result.Status = TestResultStatus.仅观察;
                progress?.Report(result);
                return result;
            }
            return Fail(result, "条件控制缺少 Then 路径。", progress);
        }

        result.SelectedPath = selectedPath;
        return await CompleteControlPathAsync(result, path, callStack, scope, cancellationToken, progress).ConfigureAwait(false);
    }

    private async Task<TestNodeResult> RunWaitForVerdictAsync(
        TestFlowNode node,
        TestFlowControlDefinition control,
        TestNodeResult result,
        HashSet<string> callStack,
        ExecutionScope scope,
        CancellationToken cancellationToken,
        IProgress<TestNodeResult>? progress)
    {
        if (!TryResolveVerdictProducer(scope, control, out var producer, out var error))
            return Fail(result, error!, progress);
        if (control.PeriodMs <= 0 || control.TimeoutMs <= 0)
            return Fail(result, "等待控制的周期和超时必须大于 0 毫秒。", progress);
        var hitPath = node.Children.FirstOrDefault(x => x.PathKind == TestFlowPathKind.Hit);
        var timeoutPath = node.Children.FirstOrDefault(x => x.PathKind == TestFlowPathKind.Timeout);
        if (hitPath is null || timeoutPath is null)
            return Fail(result, "等待控制必须同时存在 Hit 和 Timeout 路径。", progress);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(control.TimeoutMs);
        TestNodeResult? latest = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            latest = await RunReferencedParseOnceAsync(producer!, callStack, cancellationToken, progress).ConfigureAwait(false);
            scope.Publish(producer!, latest);
            var signal = latest.ProducedSignal;
            if (signal?.Verdict == control.RequiredVerdict)
            {
                result.SelectedPath = TestFlowPathKind.Hit;
                result.Children.Add(latest);
                return await CompleteControlPathAsync(result, hitPath, callStack, scope, cancellationToken, progress).ConfigureAwait(false);
            }

            if (signal?.Fault is { } fault && fault.Code is not SignalFaultCode.ReadTimeout and not SignalFaultCode.NoNewSample)
                return Fail(result, fault.Message, progress);
            if (latest.Status == TestResultStatus.执行错误 && signal?.Fault is null)
                return Fail(result, latest.Error ?? "等待控制重复执行判断步骤失败。", progress);
            if (DateTimeOffset.UtcNow >= deadline) break;
            await Task.Delay(TimeSpan.FromMilliseconds(control.PeriodMs), cancellationToken).ConfigureAwait(false);
        }

        result.SelectedPath = TestFlowPathKind.Timeout;
        if (latest is not null) result.Children.Add(latest);
        // With no explicit timeout handling, missing the required verdict is a real
        // failed assertion, not a neutral branch that could accidentally PASS.
        if (timeoutPath.Children.Count == 0)
        {
            result.HasExpectation = true;
            result.Expected = $"Verdict={control.RequiredVerdict} within {control.TimeoutMs}ms";
            result.Error = "WHEN 等待超时，未观察到目标 Verdict。";
            result.Status = TestResultStatus.判定失败;
            result.CompletedAt = DateTimeOffset.UtcNow;
            progress?.Report(result);
            return result;
        }
        return await CompleteControlPathAsync(result, timeoutPath, callStack, scope, cancellationToken, progress).ConfigureAwait(false);
    }

    private async Task<TestNodeResult> RunRepeatControlAsync(
        TestFlowNode node,
        TestFlowControlDefinition control,
        TestNodeResult result,
        HashSet<string> callStack,
        ExecutionScope scope,
        CancellationToken cancellationToken,
        IProgress<TestNodeResult>? progress)
    {
        if (control.RepeatCount is < 1 or > 100 || control.RepeatDelayMs is < 0 or > 60000)
            return Fail(result, "LOOP 参数无效：次数 1～100，间隔 0～60000ms。", progress);
        if (node.Children.Count != 1 || node.Children[0].PathKind != TestFlowPathKind.Body)
            return Fail(result, "LOOP 必须包含唯一 BODY 路径。", progress);

        result.SelectedPath = TestFlowPathKind.Body;
        var body = node.Children[0];
        var hasFailedExpectation = false;
        var hasExpectation = false;
        for (var iteration = 1; iteration <= control.RepeatCount; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var iterationResult = await RunNodeAsync(body, callStack, scope, cancellationToken, progress).ConfigureAwait(false);
            // Child result records the particular occurrence without modifying the persisted NodeId.
            iterationResult.Occurrence = iteration;
            result.Children.Add(iterationResult);
            if (ContainsStatus(iterationResult, TestResultStatus.已取消))
            {
                result.Status = TestResultStatus.已取消;
                result.Error = iterationResult.Error;
                break;
            }
            if (ContainsStatus(iterationResult, TestResultStatus.执行错误))
            {
                result.Status = TestResultStatus.执行错误;
                result.Error = iterationResult.Error;
                break;
            }
            hasFailedExpectation |= ContainsStatus(iterationResult, TestResultStatus.判定失败);
            hasExpectation |= ContainsExpectation(iterationResult);
            if (iteration < control.RepeatCount && control.RepeatDelayMs > 0)
                await Task.Delay(control.RepeatDelayMs, cancellationToken).ConfigureAwait(false);
        }
        if (result.Status == TestResultStatus.运行中)
            result.Status = hasFailedExpectation ? TestResultStatus.判定失败
                : hasExpectation ? TestResultStatus.通过 : TestResultStatus.仅观察;
        result.CompletedAt = DateTimeOffset.UtcNow;
        progress?.Report(result);
        return result;
    }

    private async Task<TestNodeResult> RunValueBranchAsync(
        TestFlowNode node,
        TestFlowControlDefinition control,
        TestNodeResult result,
        HashSet<string> callStack,
        ExecutionScope scope,
        CancellationToken cancellationToken,
        IProgress<TestNodeResult>? progress)
    {
        if (control.SignalDefinition is null)
            return Fail(result, "值分支未绑定通信数据库信号。", progress);
        var definition = FindSignalDefinition(_configuration.CommunicationDatabase, control.SignalDefinition);
        if (definition is null)
            return Fail(result, "值分支引用的通信数据库信号不存在。", progress);

        var (message, signal) = definition.Value;
        if (control.TimeoutMs is < 1 or > 60000)
            return Fail(result, "SWITCH 信号读取超时必须为 1～60000ms。", progress);
        var startedAt = DateTimeOffset.UtcNow;
        var deadline = startedAt.AddMilliseconds(control.TimeoutMs);
        var readDefinition = SignalReadDefinition.From(message, signal);
        SignalObservation? observation = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = _port.ReadSignal(readDefinition, startedAt);
            if (read.Fault is { } readFault && readFault.Code is not SignalFaultCode.NoNewSample and not SignalFaultCode.ReadTimeout)
            {
                result.ProducedSignal = new ProducedSignalResult(null, null, readFault);
                return Fail(result, readFault.Message, progress);
            }
            if (read.Observation is { } sample)
            {
                observation = sample;
                break;
            }
            await Task.Delay(_observationPollInterval, cancellationToken).ConfigureAwait(false);
        }
        if (observation is null)
        {
            var fault = new SignalFault(SignalFaultCode.ReadTimeout, $"SWITCH 在 {control.TimeoutMs}ms 内未收到新信号：{CanDefinitionDisplay.SignalPrimaryName(signal)}。");
            result.ProducedSignal = new ProducedSignalResult(null, null, fault);
            return Fail(result, fault.Message, progress);
        }

        var value = observation.Value;
        var parse = SignalReadDefinition.From(message, signal).ToConfigurationParameters();
        if (TryParseHexFrame(observation.RawFrame, out _) && !TryDecodeSignal(observation.RawFrame, parse, out value))
        {
            var fault = new SignalFault(SignalFaultCode.DecodeError, $"值分支无法解码信号：{CanDefinitionDisplay.SignalPrimaryName(signal)}。");
            result.ProducedSignal = ProducedSignalResult.FromObservation(observation).WithFault(fault.Code, fault.Message);
            return Fail(result, fault.Message, progress);
        }

        result.ActualValue = value;
        result.RawFrame = observation.RawFrame;
        result.ProducedSignal = ProducedSignalResult.FromObservation(observation with { Value = value });
        var path = node.Children.FirstOrDefault(x => x.PathKind == TestFlowPathKind.Case && MatchesCase(x, value, signal))
            ?? node.Children.FirstOrDefault(x => x.PathKind == TestFlowPathKind.Default);
        if (path is null)
            return Fail(result, $"值分支没有匹配的 Case，也没有 Default：{value}。", progress);

        result.SelectedPath = path.PathKind;
        return await CompleteControlPathAsync(result, path, callStack, scope, cancellationToken, progress).ConfigureAwait(false);
    }

    private async Task<TestNodeResult> CompleteControlPathAsync(
        TestNodeResult result,
        TestFlowNode path,
        HashSet<string> callStack,
        ExecutionScope scope,
        CancellationToken cancellationToken,
        IProgress<TestNodeResult>? progress)
    {
        var pathResult = await RunNodeAsync(path, callStack, scope, cancellationToken, progress).ConfigureAwait(false);
        result.Children.Add(pathResult);
        result.Status = ContainsStatus(pathResult, TestResultStatus.已取消)
            ? TestResultStatus.已取消
            : ContainsStatus(pathResult, TestResultStatus.执行错误)
                ? TestResultStatus.执行错误
                : ContainsStatus(pathResult, TestResultStatus.判定失败)
                    ? TestResultStatus.判定失败
                    : TestResultStatus.仅观察;
        result.Error = pathResult.Error;
        result.CompletedAt = DateTimeOffset.UtcNow;
        progress?.Report(result);
        return result;
    }

    private static bool TryResolveVerdict(
        ExecutionScope scope,
        TestFlowControlDefinition control,
        out TestNodeResult? producer,
        out ProducedSignalResult? signal,
        out string? error)
    {
        producer = null;
        signal = null;
        error = null;
        if (control.ResultReference is null ||
            string.IsNullOrWhiteSpace(control.ResultReference.ProducerNodeId) ||
            control.ResultReference.Channel != ResultChannel.Verdict)
        {
            error = "IF/WHEN 必须引用已有步骤的 Verdict。";
            return false;
        }
        if (!scope.TryGet(control.ResultReference.ProducerNodeId, out producer))
        {
            error = $"引用的结果步骤尚未在当前作用域执行：{control.ResultReference.ProducerNodeId}。";
            return false;
        }
        signal = producer.ProducedSignal;
        if (signal is null || signal.Verdict is null)
        {
            error = $"引用步骤没有可消费的 Verdict：{producer.Name}。";
            return false;
        }
        if (signal.Fault is { } fault)
        {
            error = $"引用步骤产生故障：{fault.Message}";
            return false;
        }
        return true;
    }

    private static bool TryResolveVerdictProducer(
        ExecutionScope scope,
        TestFlowControlDefinition control,
        out TestFlowNode? producer,
        out string? error)
    {
        producer = null;
        error = null;
        if (control.ResultReference is null ||
            string.IsNullOrWhiteSpace(control.ResultReference.ProducerNodeId) ||
            control.ResultReference.Channel != ResultChannel.Verdict)
        {
            error = "IF/WHEN 必须引用已有步骤的 Verdict。";
            return false;
        }
        if (!scope.TryGetNode(control.ResultReference.ProducerNodeId, out producer))
        {
            error = $"引用的结果步骤尚未在当前作用域执行：{control.ResultReference.ProducerNodeId}。";
            return false;
        }
        if (producer.Operation?.Kind != ConfigurationStepKind.报文解析)
        {
            error = "WHEN 只能重复执行已有的报文信号判断步骤。";
            return false;
        }
        var expectation = producer.ParseAssertion?.Expectation ?? producer.Operation.Parse.Expectation;
        if (string.IsNullOrWhiteSpace(expectation))
        {
            error = $"WHEN 引用步骤没有配置判断条件：{producer.Name}。";
            return false;
        }
        return true;
    }

    private async Task<TestNodeResult> RunReferencedParseOnceAsync(
        TestFlowNode producer,
        HashSet<string> callStack,
        CancellationToken cancellationToken,
        IProgress<TestNodeResult>? progress)
    {
        var expectation = producer.ParseAssertion?.Expectation ?? producer.Operation!.Parse.Expectation;
        var operation = producer.ParseAssertion is { } assertion
            ? ResolveTestOperation(producer.Operation!, assertion)
            : CloneStep(producer.Operation!);
        operation.Parse.Expectation = expectation;
        operation.Parse.TimeoutMs = 0;
        return await RunConfigurationStepAsync(operation, callStack, cancellationToken, progress).ConfigureAwait(false);
    }

    private static bool MatchesCase(TestFlowNode path, double value, CanSignalDefinition signal)
    {
        var expectedText = path.PathValue.Trim();
        if (double.TryParse(expectedText, NumberStyles.Float, CultureInfo.InvariantCulture, out var expected))
            return Math.Abs(value - expected) < 0.000001;
        if (signal.ValueTable.Count == 0) return false;

        var rawValue = Math.Abs(signal.Factor) < double.Epsilon
            ? value
            : (value - signal.Offset) / signal.Factor;
        return signal.ValueTable.Any(entry =>
            string.Equals(entry.Value?.Trim(), expectedText, StringComparison.Ordinal) &&
            Math.Abs(rawValue - entry.Key) < 0.000001);
    }

    private static (CanMessageDefinition Message, CanSignalDefinition Signal)? FindSignalDefinition(
        CanDatabase? database,
        SignalDefinitionReference reference)
    {
        var message = database?.Messages.FirstOrDefault(x => x.DefinitionId == reference.MessageDefinitionId);
        var signal = message?.Signals.FirstOrDefault(x => x.DefinitionId == reference.SignalDefinitionId);
        return message is null || signal is null ? null : (message, signal);
    }

    private sealed class ExecutionScope
    {
        private readonly Dictionary<string, TestNodeResult> _results = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TestFlowNode> _nodes = new(StringComparer.Ordinal);

        public void Publish(TestFlowNode node, TestNodeResult result)
        {
            _nodes[node.Id] = node;
            _results[node.Id] = result;
            if (node.Operation is not null)
            {
                _nodes[node.Operation.Id] = node;
                _results[node.Operation.Id] = result;
            }
        }

        public bool TryGet(string id, out TestNodeResult? result)
        {
            if (_results.TryGetValue(id, out var found))
            {
                result = found;
                return true;
            }
            result = null;
            return false;
        }

        public bool TryGetNode(string id, out TestFlowNode? node)
        {
            if (_nodes.TryGetValue(id, out var found))
            {
                node = found;
                return true;
            }
            node = null;
            return false;
        }
    }

    private ConfigurationSequence? ResolveReferencedSequence(TestFlowNode node)
        => _resourceRelations.Resolve(_configuration, node).Sequence;

    private static string DescribeMissingNodeReference(TestFlowNode node)
    {
        if (string.IsNullOrWhiteSpace(node.ResourceId) && string.IsNullOrWhiteSpace(node.SequenceId))
            return "引用节点未选择资源或序列。";
        var referenceId = !string.IsNullOrWhiteSpace(node.ResourceId) ? node.ResourceId : node.SequenceId;
        return $"引用对象不存在：节点“{node.Name}”，引用 ID={referenceId}";
    }

    private static ConfigurationStep? FindConfigurationStep(ConfigurationTreeItem item, string id)
    {
        if (item.Sequence is { } sequence)
        {
            foreach (var step in sequence.Steps)
            {
                var found = FindConfigurationStep(step, id);
                if (found is not null) return found;
            }
        }
        foreach (var child in item.Children)
        {
            var found = FindConfigurationStep(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static ConfigurationStep? FindConfigurationStep(ConfigurationStep step, string id)
    {
        if (step.Id == id) return step;
        foreach (var child in step.Children)
        {
            var found = FindConfigurationStep(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private async Task<TestNodeResult> RunConfigurationStepAsync(ConfigurationStep step, HashSet<string> callStack, CancellationToken cancellationToken, IProgress<TestNodeResult>? progress)
    {
        var assertionExpectation = step.Parse.Expectation;
        var assertionTimeout = step.Parse.TimeoutMs;
        step = SnapshotStep(step);
        // A resource/reference assertion belongs to the current test node, not
        // to the shared resource snapshot. Reapply it after canonical lookup.
        if (step.Kind == ConfigurationStepKind.报文解析 &&
            (!string.IsNullOrWhiteSpace(assertionExpectation) || assertionTimeout != 1000))
        {
            step.Parse.Expectation = assertionExpectation;
            step.Parse.TimeoutMs = Math.Max(0, assertionTimeout);
        }
        var result = new TestNodeResult { NodeId = step.Id, Name = step.Name, Status = TestResultStatus.运行中 };
        try
        {
            switch (step.Kind)
            {
                case ConfigurationStepKind.等待:
                    await Task.Delay(Math.Max(0, step.Wait.DurationMs), cancellationToken).ConfigureAwait(false);
                    result.Status = TestResultStatus.通过;
                    break;
                case ConfigurationStepKind.广播报文:
                var command = step.Broadcast.Mode switch
                    {
                        BroadcastMode.发送一次 => await _port.SendBroadcastAsync(step.Broadcast, cancellationToken).ConfigureAwait(false),
                        BroadcastMode.开始周期发送 => await _port.StartPeriodicBroadcastAsync([step.Broadcast], cancellationToken).ConfigureAwait(false),
                        BroadcastMode.停止周期发送 => await _port.StopPeriodicBroadcastAsync(cancellationToken).ConfigureAwait(false),
                        _ => HardwareCommandResult.Fail("不支持的广播模式。")
                    };
                    if (!command.Succeeded) return Fail(result, command.Error ?? "广播命令执行失败。", progress);
                    result.Status = TestResultStatus.通过;
                    break;
                case ConfigurationStepKind.电源控制:
                    var power = await _port.SetPowerAsync(step.Power, cancellationToken).ConfigureAwait(false);
                    if (!power.Succeeded) return Fail(result, power.Error ?? "电源控制执行失败。", progress);
                    result.Status = TestResultStatus.通过;
                    break;
                case ConfigurationStepKind.动作控制:
                    var action = await _port.SetActionAsync(step.Action, cancellationToken).ConfigureAwait(false);
                    if (!action.Succeeded) return Fail(result, action.Error ?? "动作控制执行失败。", progress);
                    result.Status = TestResultStatus.通过;
                    break;
                case ConfigurationStepKind.报文解析:
                    return await EvaluateParseAsync(step, result, cancellationToken, progress).ConfigureAwait(false);
                case ConfigurationStepKind.波形判定:
                    return await EvaluateWaveformAssertionAsync(step, result, cancellationToken, progress).ConfigureAwait(false);
                case ConfigurationStepKind.引用序列:
                    if (string.IsNullOrWhiteSpace(step.Reference.SequenceId))
                        return Fail(result, "引用步骤未选择引用序列。", progress);
                    var sequence = _configuration.FindSequence(step.Reference.SequenceId);
                    if (sequence is null) return Fail(result, $"引用序列不存在：{step.Reference.SequenceId}", progress);
                    if (!callStack.Add(sequence.Id)) return Fail(result, $"检测到循环引用：{sequence.Name}", progress);
                    try
                    {
                        result.Children.AddRange(await RunConfigurationStepsAsync(sequence.Steps, callStack, cancellationToken, progress).ConfigureAwait(false));
                        if (result.Children.Any(x => ContainsStatus(x, TestResultStatus.已取消)))
                        {
                            result.Status = TestResultStatus.已取消;
                            result.Error = result.Children.First(x => ContainsStatus(x, TestResultStatus.已取消)).Error;
                            result.CompletedAt = DateTimeOffset.UtcNow;
                            progress?.Report(result);
                            return result;
                        }
                        if (result.Children.Any(x => ContainsStatus(x, TestResultStatus.执行错误)))
                            return Fail(result, result.Children.First(x => ContainsStatus(x, TestResultStatus.执行错误)).Error ?? "序列执行失败", progress);
                    }
                    finally { callStack.Remove(sequence.Id); }
                    result.Status = result.Children.Any(x => ContainsStatus(x, TestResultStatus.判定失败))
                        ? TestResultStatus.判定失败
                        : result.Children.Any(ContainsExpectation) ? TestResultStatus.通过 : TestResultStatus.仅观察;
                    break;
                case ConfigurationStepKind.组合:
                    foreach (var child in step.Children)
                    {
                        var childResult = await RunConfigurationStepAsync(child, callStack, cancellationToken, progress).ConfigureAwait(false);
                        result.Children.Add(childResult);
                        if (ContainsStatus(childResult, TestResultStatus.已取消))
                        {
                            result.Status = TestResultStatus.已取消;
                            result.Error = childResult.Error;
                            result.CompletedAt = DateTimeOffset.UtcNow;
                            progress?.Report(result);
                            return result;
                        }
                        if (ContainsStatus(childResult, TestResultStatus.执行错误)) return Fail(result, childResult.Error ?? "组合执行失败", progress);
                    }
                    result.Status = result.Children.Any(x => ContainsStatus(x, TestResultStatus.判定失败))
                        ? TestResultStatus.判定失败
                        : result.Children.Any(ContainsExpectation) ? TestResultStatus.通过 : TestResultStatus.仅观察;
                    break;
                default:
                    return Fail(result, $"不支持的操作类型：{step.Kind}", progress);
            }
        }
        catch (OperationCanceledException)
        {
            result.Status = TestResultStatus.已取消;
            result.Error = "用户取消测试。";
        }
        catch (Exception ex)
        {
            result.Status = TestResultStatus.执行错误;
            result.Error = ex.Message;
        }
        result.CompletedAt = DateTimeOffset.UtcNow;
        progress?.Report(result);
        return result;
    }

    private async Task<List<TestNodeResult>> RunConfigurationStepsAsync(IReadOnlyList<ConfigurationStep> steps, HashSet<string> callStack, CancellationToken cancellationToken, IProgress<TestNodeResult>? progress)
    {
        var results = new List<TestNodeResult>();
        for (var index = 0; index < steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (steps[index].Kind == ConfigurationStepKind.广播报文 && steps[index].Broadcast.Mode == BroadcastMode.开始周期发送)
            {
                var group = new List<ConfigurationStep>();
                while (index < steps.Count && steps[index].Kind == ConfigurationStepKind.广播报文 && steps[index].Broadcast.Mode == BroadcastMode.开始周期发送)
                    group.Add(steps[index++]);
                var command = await _port.StartPeriodicBroadcastAsync(group.Select(x => SnapshotStep(x).Broadcast).ToArray(), cancellationToken).ConfigureAwait(false);
                foreach (var step in group)
                {
                    var item = new TestNodeResult { NodeId = step.Id, Name = step.Name, Status = command.Succeeded ? TestResultStatus.通过 : TestResultStatus.执行错误, Error = command.Error, CompletedAt = DateTimeOffset.UtcNow };
                    results.Add(item);
                    progress?.Report(item);
                }
                if (!command.Succeeded) break;
                index--;
                continue;
            }

            var result = await RunConfigurationStepAsync(steps[index], callStack, cancellationToken, progress).ConfigureAwait(false);
            results.Add(result);
            if (ContainsStatus(result, TestResultStatus.已取消)) break;
            if (ContainsStatus(result, TestResultStatus.执行错误)) break;
        }
        return results;
    }

    private async Task<TestNodeResult> EvaluateWaveformAssertionAsync(ConfigurationStep step, TestNodeResult result, CancellationToken cancellationToken, IProgress<TestNodeResult>? progress)
    {
        var assertion = step.WaveformAssertion;
        result.HasExpectation = assertion.ParticipatesInJudgement;
        result.Expected = DescribeWaveformExpectation(assertion);
        if (!assertion.ParticipatesInJudgement)
        {
            result.Status = TestResultStatus.仅观察;
            result.CompletedAt = DateTimeOffset.UtcNow;
            progress?.Report(result);
            return result;
        }

        var standardPointCount = assertion.StandardWaveform.Points.Count;
        if (standardPointCount == 0)
        {
            result.Status = TestResultStatus.判定失败;
            result.Error = "波形判定未配置标准波形。";
            result.CompletedAt = DateTimeOffset.UtcNow;
            progress?.Report(result);
            return result;
        }

        var windowStart = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(Math.Max(0, assertion.Rule.WindowStartMs));
        var windowEnd = windowStart + TimeSpan.FromMilliseconds(Math.Max(0, assertion.Rule.WindowDurationMs));
        var delay = windowEnd - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

        var samples = await _waveformSource.ReadSamplesAsync(assertion, windowStart, windowEnd, cancellationToken).ConfigureAwait(false);
        if (samples.Status == WaveformSampleReadStatus.SourceNotConfigured)
            return Fail(result, samples.Error ?? "波形判定采样源未配置。", progress);

        if (samples.Status != WaveformSampleReadStatus.Succeeded)
        {
            result.Status = TestResultStatus.判定失败;
            result.Error = samples.Error ?? "波形判定样本不足。";
            result.CompletedAt = DateTimeOffset.UtcNow;
            progress?.Report(result);
            return result;
        }

        var evaluation = _waveformEvaluator.Evaluate(assertion, samples.Samples);
        result.ActualValue = evaluation.CompareResult.MaxDeviation;
        result.Status = evaluation.Passed ? TestResultStatus.通过 : TestResultStatus.判定失败;
        result.Error = evaluation.Passed ? null : evaluation.Reason;
        result.CompletedAt = DateTimeOffset.UtcNow;
        progress?.Report(result);
        return result;
    }

    private async Task<TestNodeResult> EvaluateParseAsync(ConfigurationStep step, TestNodeResult result, CancellationToken cancellationToken, IProgress<TestNodeResult>? progress)
    {
        result.HasExpectation = !string.IsNullOrWhiteSpace(step.Parse.Expectation);
        result.Expected = step.Parse.Expectation;
        var notBefore = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(Math.Max(0, step.Parse.TimeoutMs));
        var deadline = notBefore + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = _port.ReadSignal(SignalReadDefinition.From(step.Parse), notBefore);
            if (read.Fault is { } readFault)
            {
                result.ProducedSignal = new ProducedSignalResult(null, null, readFault);
                return Fail(result, readFault.Message, progress);
            }

            if (read.Observation is { } observation)
            {
                // Motorola 帧从端口返回的原始数据按本次执行所用的最终参数复核，
                // 避免宿主沿用 Intel 位序。
                var actualValue = observation.Value;
                if (TryParseHexFrame(observation.RawFrame, out _))
                {
                    if (!TryDecodeSignal(observation.RawFrame, step.Parse, out var decoded))
                    {
                        result.ParseOutcome = ParseExecutionOutcome.DecodeInvalid;
                        result.ProducedSignal = ProducedSignalResult.FromObservation(observation)
                            .WithFault(SignalFaultCode.DecodeError, "收到目标报文但信号按最终定义解码失败。");
                        return Fail(result, $"收到目标报文但信号按最终定义解码失败：{DescribeParseTarget(step)}，StartByte={step.Parse.StartByte}，StartBit={step.Parse.StartBit}，BitLength={step.Parse.BitLength}。", progress);
                    }
                    // Existing adapters may already provide a decoded value for
                    // direct Intel steps. Database/Motorola steps must use the
                    // canonical raw-frame decode; direct Intel keeps adapter
                    // compatibility after validating the raw frame.
                    if (IsMotorola(step.Parse) || step.Parse.Source == ConfigurationSource.通信数据库)
                        actualValue = decoded;
                }
                result.ActualValue = actualValue;
                result.RawFrame = observation.RawFrame;
                // The port sample is transport data; expose the value after
                // the runner has applied the authoritative decode definition.
                result.ProducedSignal = ProducedSignalResult.FromObservation(observation with { Value = actualValue });
                if (!result.HasExpectation)
                {
                    result.Status = TestResultStatus.仅观察;
                    result.CompletedAt = DateTimeOffset.UtcNow;
                    progress?.Report(result);
                    return result;
                }

                if (!ExpectationEvaluator.TryEvaluate(actualValue, step.Parse.Expectation, out var matched, out var error))
                {
                    result.ProducedSignal = result.ProducedSignal!.WithFault(SignalFaultCode.EvaluationError, error ?? "期望表达式无效。");
                    return Fail(result, error ?? "期望表达式无效。", progress);
                }
                if (matched)
                {
                    result.ParseOutcome = ParseExecutionOutcome.ExpectationMet;
                    result.ProducedSignal = result.ProducedSignal!.WithVerdict(ResultVerdict.Passed);
                    result.Status = TestResultStatus.通过;
                    result.CompletedAt = DateTimeOffset.UtcNow;
                    progress?.Report(result);
                    return result;
                }

                // 超时表示“等待条件成立”的最长时间；收到不符合条件的新报文后继续等待。
                // 用该样本时间作为下一次查询下界，避免重复判定同一帧。
                notBefore = observation.Timestamp;
                // 读取端即使返回了旧缓存（例如设备暂时没有新帧），也不能
                // 让循环绕过截止时间。超时是“条件尚未成立”的最终边界，
                // 而不是只有收不到帧时才生效。
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    result.ParseOutcome = ParseExecutionOutcome.ValueMismatch;
                    result.ProducedSignal = result.ProducedSignal!.WithVerdict(ResultVerdict.Failed);
                    result.Status = TestResultStatus.判定失败;
                    result.Error = "超时未满足期望条件。";
                    result.CompletedAt = DateTimeOffset.UtcNow;
                    progress?.Report(result);
                    return result;
                }
                await Task.Delay(_observationPollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (DateTimeOffset.UtcNow >= deadline)
            {
                result.ParseOutcome = ParseExecutionOutcome.NoMatchingFrame;
                result.ProducedSignal = new ProducedSignalResult(
                    null,
                    null,
                    new SignalFault(SignalFaultCode.ReadTimeout, $"超时未收到新的 ECU 上报报文：{DescribeParseTarget(step)}。"));
                result.Status = result.HasExpectation ? TestResultStatus.判定失败 : TestResultStatus.仅观察;
                result.Error = $"超时未收到新的 ECU 上报报文：{DescribeParseTarget(step)}。";
                result.CompletedAt = DateTimeOffset.UtcNow;
                progress?.Report(result);
                return result;
            }
            await Task.Delay(_observationPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TestNodeResult Fail(TestNodeResult result, string error, IProgress<TestNodeResult>? progress)
    {
        result.Status = TestResultStatus.执行错误;
        result.Error = error;
        result.CompletedAt = DateTimeOffset.UtcNow;
        progress?.Report(result);
        return result;
    }

    private static bool ContainsExpectation(TestNodeResult result)
        => result.HasExpectation || result.Children.Any(ContainsExpectation);

    private static bool ContainsPassedExpectation(TestNodeResult result)
        => (result.HasExpectation && result.Status == TestResultStatus.通过) || result.Children.Any(ContainsPassedExpectation);

    private static int CountExpectations(TestNodeResult result)
        => (result.HasExpectation ? 1 : 0) + result.Children.Sum(CountExpectations);

    private static bool ContainsStatus(TestNodeResult result, TestResultStatus status)
        => result.Status == status || result.Children.Any(x => ContainsStatus(x, status));

    private static bool IsMotorola(ParseParameters parse)
        => parse.ByteOrder.Contains("Motorola", StringComparison.OrdinalIgnoreCase)
            || parse.ByteOrder.Contains("大", StringComparison.Ordinal);

    private static string DescribeParseTarget(ConfigurationStep step)
        => $"报文=0x{step.Parse.Id:X}{(step.Parse.Extended ? "（扩展帧）" : "（标准帧）")}，信号={step.Parse.SignalName}，DefinitionId={step.Parse.MessageDefinitionId}/{step.Parse.SignalDefinitionId}";

    private static string DescribeWaveformExpectation(WaveformAssertionParameters assertion)
    {
        var signal = !string.IsNullOrWhiteSpace(assertion.DisplayName)
            ? assertion.DisplayName
            : !string.IsNullOrWhiteSpace(assertion.SignalName)
                ? assertion.SignalName
                : assertion.SignalDefinitionId;
        var standard = string.IsNullOrWhiteSpace(assertion.StandardWaveform.Name)
            ? "未命名标准波形"
            : assertion.StandardWaveform.Name;
        return $"标准波形={standard}，信号={signal}，幅值容差={assertion.Rule.AbsoluteTolerance:0.###}，百分比容差={assertion.Rule.PercentTolerance:0.###}%";
    }

    private static bool TryParseHexFrame(string? rawFrame, out byte[] bytes)
    {
        bytes = [];
        var tokens = (rawFrame ?? string.Empty)
            .Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        var parsed = new List<byte>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                return false;
            parsed.Add(value);
        }
        bytes = parsed.ToArray();
        return true;
    }

    /// <summary>按解析步骤的最终配置从 CAN 原始数据解码信号。</summary>
    public static bool TryDecodeSignal(string? rawFrame, ParseParameters parse, out double value)
    {
        value = 0;
        if (parse is null || parse.StartByte < 0 || parse.StartBit is < 0 or > 7 || parse.BitLength is < 1 or > 32)
            return false;

        var bytes = (rawFrame ?? string.Empty)
            .Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => byte.TryParse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var item) ? item : (byte?)null)
            .ToArray();
        if (bytes.Any(x => x is null)) return false;

        var data = bytes.Select(x => x!.Value).ToArray();
        var start = parse.StartByte * 8 + parse.StartBit;
        uint raw = 0;
        if (IsMotorola(parse))
        {
            var position = start;
            for (var i = 0; i < parse.BitLength; i++)
            {
                if (!TryReadBit(data, position, out var bit)) return false;
                raw = (raw << 1) | (uint)bit;
                position = position % 8 == 0 ? position + 15 : position - 1;
            }
        }
        else
        {
            if (start + parse.BitLength > data.Length * 8) return false;
            for (var i = 0; i < parse.BitLength; i++)
            {
                if (TryReadBit(data, start + i, out var bit) && bit != 0) raw |= 1u << i;
            }
        }

        value = raw * parse.Factor + parse.Offset;
        return true;
    }

    private static bool TryReadBit(byte[] data, int position, out int bit)
    {
        bit = 0;
        var byteIndex = position / 8;
        if (position < 0 || byteIndex >= data.Length) return false;
        bit = (data[byteIndex] >> (position % 8)) & 1;
        return true;
    }
}

file sealed class UnavailableWaveformAssertionSource : IWaveformAssertionSource
{
    public static UnavailableWaveformAssertionSource Instance { get; } = new();

    public Task<WaveformSampleReadResult> ReadSamplesAsync(
        WaveformAssertionParameters assertion,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
        => Task.FromResult(WaveformSampleReadResult.SourceNotConfigured("波形判定采样源未配置。"));
}

public static class ExpectationEvaluator
{
    private static readonly Regex Range = new(@"^\s*([-+]?\d+(?:\.\d+)?)\s*\.\.\s*([-+]?\d+(?:\.\d+)?)\s*$", RegexOptions.Compiled);

    public static bool TryEvaluate(double actual, string expression, out bool matched, out string? error)
    {
        matched = false;
        error = null;
        expression = expression.Trim();
        if (expression.Length == 0) { error = "期望表达式为空。"; return false; }
        var range = Range.Match(expression);
        if (range.Success && double.TryParse(range.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var min) && double.TryParse(range.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
        {
            matched = actual >= Math.Min(min, max) && actual <= Math.Max(min, max);
            return true;
        }

        var match = Regex.Match(expression, @"^(==|=|!=|>=|<=|>|<)\s*([-+]?\d+(?:\.\d+)?)$");
        if (!match.Success || !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var expected))
        {
            error = $"无法解析期望表达式：{expression}";
            return false;
        }
        matched = match.Groups[1].Value switch
        {
            "=" or "==" => actual == expected,
            "!=" => actual != expected,
            ">" => actual > expected,
            "<" => actual < expected,
            ">=" => actual >= expected,
            "<=" => actual <= expected,
            _ => false
        };
        return true;
    }
}
