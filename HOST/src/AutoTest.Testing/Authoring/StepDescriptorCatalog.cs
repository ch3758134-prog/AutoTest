using System.Collections.Immutable;
using System.Globalization;

namespace AutoTest.Testing.Authoring;

public enum StepExecutionCapability
{
    Interpreter,
    Hardware,
    Unsupported
}

public enum StepEditorKind
{
    Scalar,
    CanFrame,
    KeyAction,
    Ignition,
    SignalRead,
    SignalCondition,
    FlowCondition,
    Loop,
    Switch,
    WaveCapture,
    WaveAnalysis
}

public enum StepParameterKind
{
    Text,
    Identifier,
    Expression,
    Integer,
    Number,
    Choice,
    Mapping,
    Hex
}

public sealed record StepParameterDefinition(
    string Key,
    string Label,
    StepParameterKind Kind,
    string DefaultValue,
    bool Required = true,
    decimal? Minimum = null,
    decimal? Maximum = null,
    ImmutableArray<string> Choices = default);

public sealed record StepDescriptor(
    string Type,
    ImmutableArray<string> RequiredSlots,
    ImmutableArray<StepParameterDefinition> Parameters,
    StepExecutionCapability Execution,
    StepEditorKind EditorKind)
{
    public ImmutableArray<string> RequiredProperties => Parameters
        .Where(x => x.Required).Select(x => x.Key).ToImmutableArray();

    public bool ExecutableV1 => Execution != StepExecutionCapability.Unsupported;
}

/// <summary>
/// A user action is a projection of a real TEST node. Several actions may
/// intentionally create the same node type (CAN, KEY and IGN are all Write),
/// but each has its own editor and defaults.
/// </summary>
public sealed record StepActionDefinition(
    string Key,
    string StepType,
    string Category,
    string Title,
    string Description,
    string TargetCategory,
    string TargetProperty,
    StepEditorKind EditorKind,
    ImmutableDictionary<string, string> DefaultProperties,
    bool IsFrequent,
    bool ContextualOnly);

/// <summary>
/// The single authoritative TEST action/schema catalog. Authoring, validation,
/// palette projection and execution admission all read this definition.
/// Presentation layers may format it, but must not define parallel action lists.
/// </summary>
public sealed class StepDescriptorCatalog
{
    private readonly Dictionary<string, StepDescriptor> _types =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, StepActionDefinition> _actions =
        new(StringComparer.Ordinal);

    public void Register(StepDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Type) ||
            !Enum.IsDefined(descriptor.Execution) ||
            !Enum.IsDefined(descriptor.EditorKind))
            throw new ArgumentException("Invalid TEST step descriptor.", nameof(descriptor));
        if (!_types.TryAdd(descriptor.Type, descriptor))
            throw new InvalidOperationException($"Duplicate step type: {descriptor.Type}");
    }

    public void RegisterAction(StepActionDefinition action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_types.ContainsKey(action.StepType))
            throw new InvalidOperationException(
                $"Action {action.Key} refers to unknown TEST node {action.StepType}.");
        if (string.IsNullOrWhiteSpace(action.Key) ||
            string.IsNullOrWhiteSpace(action.Title) ||
            string.IsNullOrWhiteSpace(action.Category))
            throw new ArgumentException("Invalid TEST action definition.", nameof(action));
        if (_types[action.StepType].Execution == StepExecutionCapability.Unsupported)
            throw new InvalidOperationException(
                $"An unsupported TEST node cannot be offered as an authoring action: {action.StepType}.");
        if (!_actions.TryAdd(action.Key, action))
            throw new InvalidOperationException($"Duplicate TEST action key: {action.Key}");
    }

    public StepDescriptor? Lookup(string type) => _types.GetValueOrDefault(type);
    public IReadOnlyCollection<StepDescriptor> Enumerate() => _types.Values.ToArray();
    public IReadOnlyList<StepActionDefinition> Actions => _actions.Values.ToArray();
    public StepActionDefinition? FindAction(string key) => _actions.GetValueOrDefault(key);

    public StepNode CreateNodeForAction(string actionKey, string? defaultTarget = null)
    {
        var action = FindAction(actionKey) ??
            throw new ArgumentException("未知或尚未开放的 TEST 操作：" + actionKey, nameof(actionKey));
        var descriptor = Lookup(action.StepType) ??
            throw new InvalidOperationException("TEST 操作定义缺少节点结构：" + action.StepType);
        var node = StepNode.Create(action.StepType, action.Title);
        foreach (var slot in descriptor.RequiredSlots)
            node = node.WithSlot(slot);
        foreach (var parameter in descriptor.Parameters)
            node = node.WithProperty(parameter.Key, parameter.DefaultValue);
        if (!string.IsNullOrWhiteSpace(defaultTarget) &&
            !string.IsNullOrWhiteSpace(action.TargetProperty))
            node = node.WithProperty(action.TargetProperty, defaultTarget);
        foreach (var property in action.DefaultProperties)
            node = node.WithProperty(property.Key, property.Value);
        return node;
    }

    public StepNode CreateNodeForType(string type, string? defaultTarget = null)
    {
        var action = _actions.Values.FirstOrDefault(x => x.StepType == type) ??
            throw new ArgumentException("该 TEST 节点没有可创建的用户操作：" + type, nameof(type));
        return CreateNodeForAction(action.Key, defaultTarget);
    }

    public ImmutableArray<string> ValidateParameters(
        StepNode node, string location, bool allowIncompleteSteps = false)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        var descriptor = Lookup(node.Type);
        if (descriptor is null) return errors.ToImmutable();

        foreach (var parameter in descriptor.Parameters)
        {
            if (!node.Properties.TryGetValue(parameter.Key, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                if (parameter.Required && !allowIncompleteSteps)
                    errors.Add($"{location}: 缺少{parameter.Label}");
                continue;
            }

            switch (parameter.Kind)
            {
                case StepParameterKind.Integer:
                    if (!int.TryParse(value, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var integer))
                        errors.Add($"{location}: {parameter.Label}必须是整数");
                    else if (parameter.Minimum is { } min && integer < min ||
                             parameter.Maximum is { } max && integer > max)
                        errors.Add($"{location}: {parameter.Label}超出允许范围");
                    break;
                case StepParameterKind.Number:
                    if (!decimal.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var number))
                        errors.Add($"{location}: {parameter.Label}必须是有效数字");
                    else if (parameter.Minimum is { } min && number < min ||
                             parameter.Maximum is { } max && number > max)
                        errors.Add($"{location}: {parameter.Label}超出允许范围");
                    break;
                case StepParameterKind.Choice:
                    if (!parameter.Choices.IsDefaultOrEmpty &&
                        !parameter.Choices.Contains(value, StringComparer.Ordinal))
                        errors.Add($"{location}: {parameter.Label}不是有效选项");
                    break;
            }
        }
        return errors.ToImmutable();
    }

    public static StepDescriptorCatalog Builtins()
    {
        var catalog = new StepDescriptorCatalog();
        var comparisons = ImmutableArray.Create("==", "!=", ">", ">=", "<", "<=");
        var arithmetic = ImmutableArray.Create("+", "-", "*", "/");
        var readModes = ImmutableArray.Create("Latest", "Next");

        static StepParameterDefinition P(string key, string label,
            StepParameterKind kind, string value, bool required = true,
            decimal? min = null, decimal? max = null,
            ImmutableArray<string> choices = default) =>
            new(key, label, kind, value, required, min, max, choices);

        void Node(string type, StepExecutionCapability execution,
            StepEditorKind editor, string[]? slots = null,
            params StepParameterDefinition[] parameters) =>
            catalog.Register(new StepDescriptor(type,
                (slots ?? []).ToImmutableArray(), parameters.ToImmutableArray(),
                execution, editor));

        static ImmutableDictionary<string, string> D(
            params (string Key, string Value)[] values) =>
            values.ToImmutableDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        void Action(string key, string type, string category, string title,
            string description, string targetCategory, string targetProperty,
            StepEditorKind editor, bool frequent = false, bool contextual = false,
            params (string Key, string Value)[] defaults) =>
            catalog.RegisterAction(new StepActionDefinition(key, type, category,
                title, description, targetCategory, targetProperty, editor,
                D(defaults), frequent, contextual));

        // Structural and calculation nodes.
        Node("Sequence", StepExecutionCapability.Interpreter, StepEditorKind.Scalar,
            ["body"]);
        Node("If", StepExecutionCapability.Interpreter, StepEditorKind.FlowCondition,
            ["then", "else"],
            P("variable", "条件来源变量", StepParameterKind.Identifier, "value"),
            P("operator", "比较方式", StepParameterKind.Choice, "==", choices: comparisons),
            P("equals", "比较值", StepParameterKind.Expression, "1"));
        Node("Switch", StepExecutionCapability.Interpreter, StepEditorKind.Switch,
            ["cases"], P("variable", "分支来源变量", StepParameterKind.Identifier, "value"));
        Node("Case", StepExecutionCapability.Interpreter, StepEditorKind.Switch,
            ["body"], P("equals", "匹配值", StepParameterKind.Expression, ""));
        Node("Loop", StepExecutionCapability.Interpreter, StepEditorKind.Loop,
            ["body"], P("count", "重复次数", StepParameterKind.Integer, "2", min: 0, max: 1000));
        Node("Wait", StepExecutionCapability.Interpreter, StepEditorKind.Scalar,
            null, P("milliseconds", "等待时间（ms）", StepParameterKind.Integer,
                "500", min: 0, max: 60000));
        Node("SetVariable", StepExecutionCapability.Interpreter, StepEditorKind.Scalar,
            null, P("variable", "变量名称", StepParameterKind.Identifier, "value"),
            P("value", "变量值", StepParameterKind.Expression, "1"));
        Node("Calculate", StepExecutionCapability.Interpreter, StepEditorKind.Scalar,
            null, P("variable", "结果变量", StepParameterKind.Identifier, "result"),
            P("left", "左操作数", StepParameterKind.Expression, "$response"),
            P("operator", "运算方式", StepParameterKind.Choice, "+", choices: arithmetic),
            P("right", "右操作数", StepParameterKind.Expression, "1"));
        Node("Expect", StepExecutionCapability.Interpreter, StepEditorKind.Scalar,
            null, P("left", "实际值", StepParameterKind.Expression, "$response"),
            P("operator", "比较方式", StepParameterKind.Choice, "==", choices: comparisons),
            P("right", "期望值", StepParameterKind.Expression, "1"));

        // Physical and observed TEST nodes. They use the same real ports in
        // engineering debug and production; no second execution capability.
        Node("Write", StepExecutionCapability.Hardware, StepEditorKind.CanFrame,
            null, P("target", "操作对象", StepParameterKind.Mapping, ""),
            P("value", "动作或报文数据", StepParameterKind.Expression, "Pulse"),
            P("periodMs", "CAN 周期（ms）", StepParameterKind.Integer, "0",
                required: false, min: 0, max: 600000),
            P("durationMs", "周期持续时间（ms）", StepParameterKind.Integer, "0",
                required: false, min: 0, max: 1800000));
        Node("Read", StepExecutionCapability.Hardware, StepEditorKind.SignalRead,
            null, P("target", "DBC 信号", StepParameterKind.Mapping, ""),
            P("variable", "保存到变量", StepParameterKind.Identifier, "response"),
            P("mode", "读取方式", StepParameterKind.Choice, "Next", choices: readModes),
            P("timeoutMs", "读取超时（ms）", StepParameterKind.Integer, "1000", min: 1, max: 600000),
            P("maxAgeMs", "最大数据年龄（ms）", StepParameterKind.Integer, "500", min: 0, max: 600000));
        Node("WaitUntil", StepExecutionCapability.Hardware, StepEditorKind.SignalCondition,
            null, P("target", "DBC 信号", StepParameterKind.Mapping, ""),
            P("variable", "保存到变量", StepParameterKind.Identifier, "response"),
            P("operator", "比较方式", StepParameterKind.Choice, "==", choices: comparisons),
            P("expected", "目标值", StepParameterKind.Expression, "1"),
            P("timeoutMs", "最大等待时间（ms）", StepParameterKind.Integer, "1000", min: 1, max: 600000));
        Node("AssertSignal", StepExecutionCapability.Hardware, StepEditorKind.SignalCondition,
            null, P("target", "DBC 信号", StepParameterKind.Mapping, ""),
            P("operator", "比较方式", StepParameterKind.Choice, "==", choices: comparisons),
            P("expected", "期望值", StepParameterKind.Expression, ""),
            P("timeoutMs", "判定时间窗（ms）", StepParameterKind.Integer, "5000", min: 1, max: 600000));
        Node("CanStopPeriodic", StepExecutionCapability.Hardware, StepEditorKind.Scalar);
        Node("WaveStart", StepExecutionCapability.Hardware, StepEditorKind.WaveCapture,
            null, P("target", "采集信号", StepParameterKind.Mapping, ""),
            P("captureKey", "采集组", StepParameterKind.Identifier, "capture1"),
            P("durationMs", "最长采集时间（ms）", StepParameterKind.Integer,
                "3000", min: 0, max: 3600000));
        Node("WaveStop", StepExecutionCapability.Hardware, StepEditorKind.WaveCapture,
            null, P("captureKey", "采集组", StepParameterKind.Identifier, "capture1"),
            P("variable", "采集结果变量", StepParameterKind.Identifier, "waveSamples"));
        Node("WaveAnalyze", StepExecutionCapability.Hardware, StepEditorKind.WaveAnalysis,
            null, P("captureKey", "采集组", StepParameterKind.Identifier, "capture1"),
            P("variable", "分析结果变量", StepParameterKind.Identifier, "waveResult"),
            P("profile", "波形判定配置", StepParameterKind.Mapping, ""));
        Node("WaveCompare", StepExecutionCapability.Hardware, StepEditorKind.WaveAnalysis,
            null, P("captureKey", "采集组", StepParameterKind.Identifier, "capture1"),
            P("variable", "判定结果变量", StepParameterKind.Identifier, "waveVerdict"),
            P("profile", "波形判定配置", StepParameterKind.Mapping, ""));

        // Only executable operations appear in the authoring palette. The
        // contextual CASE action is exposed only while editing a SWITCH.
        Action("can", "Write", "CAN 与工位", "发送 CAN 报文",
            "以完整 CAN ID、DLC 和 DATA 为核心；DBC 只提供可选的报文/信号辅助。",
            "can", "target", StepEditorKind.CanFrame, true, false,
            ("value", "HEX:0000000000000000"), ("periodMs", "0"), ("durationMs", "0"));
        Action("stop-can", "CanStopPeriodic", "CAN 与工位", "停止全部周期 CAN",
            "停止当前 TEST 项目运行作用域内的全部 ECU CAN 周期发送。",
            "", "", StepEditorKind.Scalar, true);
        Action("key", "Write", "CAN 与工位", "按键动作",
            "选择 20 Key 组合并设置保持；保持时间结束后最后组合继续保持。",
            "button", "target", StepEditorKind.KeyAction, true, false,
            ("value", "Pulse"));
        Action("ign", "Write", "CAN 与工位", "切换 IGN",
            "切换固定工位 IGN 输出状态。",
            "ign", "target", StepEditorKind.Ignition, true, false,
            ("value", "ON"));
        Action("read", "Read", "读取与观察", "读取 CAN 信号",
            "从当前工程 DBC 读取真实信号值并保存到变量，不自动判定。",
            "signal", "target", StepEditorKind.SignalRead, true);
        Action("wait-signal", "WaitUntil", "读取与观察", "等待信号条件",
            "持续观察真实 DBC 信号，直到满足条件或达到超时。",
            "signal", "target", StepEditorKind.SignalCondition);
        Action("assert-signal", "AssertSignal", "分析与判断", "CAN 信号判定",
            "在指定时间窗内评价 ECU 信号，产生 PASS、FAIL 或无法判定。",
            "signal", "target", StepEditorKind.SignalCondition, true);
        Action("expect", "Expect", "分析与判断", "期望判定",
            "比较已取得的真实测量值或变量，显式产生测试结论。",
            "", "", StepEditorKind.Scalar);
        Action("calculate", "Calculate", "分析与判断", "计算变量",
            "根据已有测量值和变量执行数值计算。",
            "", "", StepEditorKind.Scalar);
        Action("variable", "SetVariable", "分析与判断", "设置变量",
            "为后续条件、计算和判定设置明确的数据变量。",
            "", "", StepEditorKind.Scalar);
        Action("if", "If", "流程控制", "条件分支 IF / ELSE",
            "根据有效数据选择成立或否则分支；无数据不进入 ELSE。",
            "", "", StepEditorKind.FlowCondition, true);
        Action("switch", "Switch", "流程控制", "SWITCH 分支",
            "按有效变量选择唯一 CASE；默认分支可选，不会隐式贯穿。",
            "", "", StepEditorKind.Switch);
        Action("loop", "Loop", "流程控制", "重复指定次数",
            "按限定次数执行循环体。",
            "", "", StepEditorKind.Loop, true);
        Action("sequence", "Sequence", "流程控制", "步骤分组",
            "将一组相关步骤折叠为有序分组。",
            "", "", StepEditorKind.Scalar);
        Action("wait", "Wait", "流程控制", "固定等待",
            "只等待指定时间，不隐式改变 CAN、KEY 或 IGN 状态。",
            "", "", StepEditorKind.Scalar, true);
        Action("case", "Case", "流程控制", "添加 CASE 分支",
            "仅在 SWITCH 内添加；每个 CASE 匹配一个明确值。",
            "", "", StepEditorKind.Switch, false, true);
        Action("wave-start", "WaveStart", "波形", "开始波形采集",
            "开始采集所选 DBC 信号，保留同次采集的时间和质量信息。",
            "signal", "target", StepEditorKind.WaveCapture, true);
        Action("wave-stop", "WaveStop", "波形", "结束波形采集",
            "结束同次采集并保存真实样本证据。",
            "", "", StepEditorKind.WaveCapture);
        Action("wave-analyze", "WaveAnalyze", "波形", "分析波形",
            "使用工程内波形判定配置分析已完成的真实采集。",
            "wave", "profile", StepEditorKind.WaveAnalysis, true);
        Action("wave-compare", "WaveCompare", "波形", "波形判定",
            "将本次真实采集与工程标准比较，产生有证据的 PASS / FAIL / 无法判定。",
            "wave", "profile", StepEditorKind.WaveAnalysis, true);

        return catalog;
    }
}