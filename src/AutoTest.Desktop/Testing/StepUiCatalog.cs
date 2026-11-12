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
        new("Assert", "条件断言", "比较测试程序变量", true),
        new("When", "等待条件", "等待变量满足条件（仅编辑，运行能力未接入）", false)
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
        _ => key
    };

    public static StepNode NewNode(string type)
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
                "variable" => "value",
                "equals" => "1",
                "value" => "1",
                "timeoutMs" => "1000",
                _ => "1"
            };
            node = node.WithProperty(property, value);
        }
        return node;
    }
}
