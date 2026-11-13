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
        new("Calculate", "计算", "用变量和字面值计算新的测试数据", true),
        new("Expect", "期望判定", "在指定位置用表达式作出测试判定", true),
        new("WaveStart", "开始波形记录", "开始记录所选的实时 DBC 信号", true),
        new("WaveStop", "结束波形记录", "结束采集并将样本数保存为数据", true),
        new("WaveAnalyze", "波形分析", "独立分析已采集波形，将分析结果保存为数据", true)
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
        _ => slot
    };

    public static string PhaseLabel(TestPhase p) => p switch
    {
        TestPhase.Prepare => "准备",
        TestPhase.Test => "测试",
        TestPhase.Cleanup => "清理",
        _ => p.ToString()
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
        "mode" => "读取模式（Latest / Next）",
        "maxAgeMs" => "最大数据年龄（毫秒）",
        "operator" => "运算符 / 比较符",
        "expected" => "目标值或变量",
        "left" => "左操作数（$变量 或数值）",
        "right" => "右操作数（$变量 或数值）",
        "captureKey" => "采集名称",
        "durationMs" => "最长记录时间（毫秒）",
        "reference" => "已建立的参考波形名称（非普通证据）",
        "profile" => "波形分析配置（可选，来自工程 Mapping）",
        "tolerance" => "比较绝对容差",
        "coverage" => "最低有效覆盖率（0-1）",
        _ => key
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
                "variable" => type == "Read" || type == "WaitUntil" ? "response" : "value",
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
        if (type == "WaveAnalyze")
            node = node.WithProperty("profile", "").WithProperty("reference", "");
        return node;
    }
}
