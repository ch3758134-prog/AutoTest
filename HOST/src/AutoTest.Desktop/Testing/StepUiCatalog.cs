using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

/// <summary>Chinese presentation metadata only. Structural contracts come from Testing.Authoring.</summary>
public sealed record StepUiChoice(string Type, string Label, string Summary, bool Runnable)
{
    public override string ToString() => Label;
}

public static class StepUiCatalog
{
    public static readonly IReadOnlyList<StepUiChoice> Choices =
    [
        new("Wait", "等待", "固定延时 0–60000 毫秒", true),
        new("If", "条件判断 IF / ELSE", "满足条件和否则两个子分支", true),
        new("Loop", "循环", "重复执行指定次数（0–1000）", true),
        new("Sequence", "步骤分组", "包含有顺序的子步骤", true),
        new("SetVariable", "设置变量", "设置测试程序变量", true),
        new("Write", "写入 / 外部激励", "操作映射对象：按键、IGN 或 DBC CAN 报文", true),
        new("Read", "读取信号", "读取 DBC 解析值保存到变量，读取本身不判定", true),
        new("WaitUntil", "等待响应条件", "持续观察信号，将条件是否成立写入变量", true),
        new("AssertSignal", "CAN 信号判定", "在规定时间内等待目标信号，满足判定才算通过", true),
        new("CanStopPeriodic", "停止周期 CAN", "停止当前独立用例或项目连续会话中的所有 ECU CAN 周期发送", true),
        new("Calculate", "计算", "用变量和字面值计算新的测试数据", true),
        new("Expect", "期望判定", "在指定位置用表达式作出测试判定", true),
        new("WaveStart", "开始波形记录", "开始记录所选的实时 DBC 信号", true),
        new("WaveStop", "结束波形记录", "结束采集并将样本数保存为数据", true),
        new("WaveAnalyze", "波形分析", "独立分析已采集波形，将分析结果保存为数据", true),
        new("Switch", "SWITCH 分支", "按已有有效变量匹配 CASE，默认分支可选；无数据阻断后续执行", true),
        new("Case", "CASE / 默认分支", "仅能添加在 SWITCH 内；比较值填 default 为默认分支", true),
        new("When", "WHEN 条件等待", "条件与超时、嵌套步骤；运行器尚未实现", false),
        new("TryCatch", "TRY / CATCH / FINALLY", "异常处理分支；运行器尚未实现", false),
        new("Subroutine", "子程序", "有名子步骤容器；运行器尚未实现", false),
        new("CallSubroutine", "调用子程序", "历史文档内部调用；运行器尚未实现", false),
        new("CallPublicFlow", "调用公共流程", "从当前工程选择固定修订的公共流程", false),
        new("CanSend", "CAN 发送", "需绑定正式 DBC / CAN 配置，执行未接线", false),
        new("CanWait", "CAN 接收等待", "需绑定 CAN 目标和超时，执行未接线", false),
        new("UdsRequest", "UDS 服务", "运行需实体诊断授权，执行未接线", false),
        new("WaveCapture", "波形采集", "记录服务尚未完成该步骤适配", false),
        new("WaveCompare", "波形判定", "对比已完成的真实波形与冻结的参考，产生有证据的 PASS/FAIL/无法判定", true)
    ];
    public static string Label(string type) =>
        Choices.FirstOrDefault(c => c.Type == type)?.Label ?? type;

    public static string SlotLabel(string slot) => slot switch
    {
        "then" => "条件成立（THEN）",
        "else" => "否则（ELSE）",
        "body" => "循环体 / 子步骤",
        "try" => "尝试执行",
        "catch" => "异常处理",
        "finally" => "清理",
        "cases" => "CASE 分派",
        "branches" => "分支",
        _ => slot
    };

    public static string PhaseLabel(TestPhase p) => p switch
    {
        TestPhase.Prepare => "准备",
        TestPhase.Test => "测试",
        TestPhase.Cleanup => "清理",
        _ => p.ToString()
    };

    // Only formal TEST authoring parameters are displayed in the step inspector.
    // Unknown keys remain as data, but never create special step categories or
    // additional execution gates. Step validity comes from the TEST contracts.
    public static bool IsOperatorProperty(string key) => key switch
    {
        "milliseconds" or "count" or "variable" or "equals" or "value" or
        "timeoutMs" or "target" or "mode" or "maxAgeMs" or "operator" or
        "expected" or "left" or "right" or "captureKey" or "durationMs" or
        "reference" or "dbcMessage" or "signals" or "requestHex" or
        "service" or "profile" or "tolerance" or
        "coverage" or "periodMs" => true,
        _ => false
    };


    public static string PropertyLabel(string key) => key switch
    {
        "milliseconds" => "等待时间（毫秒）",
        "count" => "循环次数",
        "variable" => "变量名称",
        "equals" => "比较值",
        "value" => "变量值",
        "timeoutMs" => "超时（毫秒）",
        "target" => "已映射操作 / DBC 信号",
        "mode" => "信号读取方式",
        "maxAgeMs" => "数据有效时间（毫秒）",
        "operator" => "比较或计算方式",
        "expected" => "目标值或变量",
        "left" => "左操作数（$变量 或数值）",
        "right" => "右操作数（$变量 或数值）",
        "captureKey" => "采集名称",
        "durationMs" => "周期持续时长（ms；0＝直到显式停止或运行作用域结束）",
        "periodMs" => "CAN 发送周期（毫秒；0 表示单次）",
        "reference" => "已建立的参考波形名称（非普通证据）",
        "dbcMessage" => "CAN 报文",
        "signals" => "CAN 信号条件",
        "requestHex" => "UDS 请求数据（HEX）",
        "service" => "UDS 服务标识",
        "profile" => "已审核波形判定配置（必选，来自工程 Mapping）",
        "tolerance" => "比较绝对容差",
        "coverage" => "最低有效覆盖率（0-1）",
        _ => "内部参数"
    };

    public static StepNode NewNode(string type, string defaultTarget = "")
    {
        var descriptor = StepDescriptorCatalog.Builtins().Lookup(type)
            ?? throw new ArgumentException("未知步骤类型：" + type);
        if (!Choices.Any(x => x.Type == type))
            throw new ArgumentException("该步骤类型尚未开放创建。");
        var node = StepNode.Create(type, Label(type));
        foreach (var slot in descriptor.RequiredSlots)
            node = node.WithSlot(slot);
        foreach (var property in descriptor.RequiredProperties)
        {
            var value = property switch
            {
                "milliseconds" => "500",
                "count" => "2",
                "variable" => type == "Read" || type == "WaitUntil" ? "response" :
                    type == "WaveCompare" ? "waveVerdict" : "value",
                "equals" => "1",
                "value" => type == "Write" ? "Pulse" : "1",
                "timeoutMs" => "1000",
                "maxAgeMs" => "500",
                "mode" => "Next",
                "target" => defaultTarget,
                "captureKey" => "capture1",
                "durationMs" => "3000",
                "reference" => "",
                "tolerance" => "0.05",
                "coverage" => "0.95",
                "operator" => type == "Calculate" ? "+" : "==",
                "expected" => "1",
                "left" => "$response",
                "right" => "1",
                _ => "1"
            };
            node = node.WithProperty(property, value);
        }
        // Optional operator preserves equality semantics for existing saved IF nodes.
        if (type == "If") node = node.WithProperty("operator", "==");
        if (type is "WaveAnalyze" or "WaveCompare")
            node = node.WithProperty("profile", "").WithProperty("reference", "");
        // Editing schemas are deliberately not executable; invalid/absent resource
        // references must be reported at preflight, not silently defaulted.
        node = type switch
        {
            "Switch" => node.WithProperty("variable", "value"),
            "AssertSignal" => node.WithProperty("operator", "==")
                .WithProperty("expected", "").WithProperty("timeoutMs", "5000"),
            "When" => node.WithProperty("variable", "").WithProperty("equals", "")
                .WithProperty("timeoutMs", "1000"),
            "CanSend" => node.WithProperty("dbcMessage", "").WithProperty("signals", ""),
            "CanWait" => node.WithProperty("dbcMessage", "").WithProperty("timeoutMs", "1000"),
            "UdsRequest" => node.WithProperty("service", "").WithProperty("requestHex", ""),
            "WaveCapture" => node.WithProperty("captureKey", ""),
            "WaveCompare" => node.WithProperty("reference", "")
                .WithProperty("profile", ""),
            _ => node
        };
        return node;
    }
}
