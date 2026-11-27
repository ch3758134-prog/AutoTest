using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

/// <summary>Chinese presentation projection of the canonical TEST action catalog.</summary>
public sealed record StepUiChoice(string Type, string Label, string Summary, bool Runnable)
{
    public override string ToString() => Label;
}

public static class StepUiCatalog
{
    private static readonly StepDescriptorCatalog Catalog = StepDescriptorCatalog.Builtins();

    public static readonly IReadOnlyList<StepUiChoice> Choices = Catalog.Actions
        .Select(action => new StepUiChoice(action.StepType, action.Title,
            action.Description,
            Catalog.Lookup(action.StepType)?.Execution != StepExecutionCapability.Unsupported))
        .ToArray();

    public static string Label(string type) =>
        Catalog.Actions.FirstOrDefault(x => x.StepType == type)?.Title ??
        Catalog.Lookup(type)?.Type ?? type;

    public static string Label(StepNode node, IReadOnlyList<TestTargetMapping>? mappings = null)
    {
        if (node.Type != "Write") return Label(node.Type);
        var targetName = node.Properties.GetValueOrDefault("target", "");
        var target = mappings?.FirstOrDefault(x =>
            string.Equals(x.Name, targetName, StringComparison.OrdinalIgnoreCase));
        return target?.Kind switch
        {
            TestTargetKind.RawKey or TestTargetKind.Button => "按键动作",
            TestTargetKind.Ignition => "切换 IGN",
            TestTargetKind.RawCan or TestTargetKind.CanMessage => "发送 CAN 报文",
            _ => "工位动作"
        };
    }

    public static string SlotLabel(string slot) => slot switch
    {
        "then" => "条件成立",
        "else" => "否则",
        "body" => "循环体 / 子步骤",
        "try" => "尝试执行",
        "catch" => "异常处理",
        "finally" => "最后执行",
        "cases" => "CASE 分支",
        "branches" => "分支",
        _ => slot
    };

    public static string PhaseLabel(TestPhase phase) => phase switch
    {
        TestPhase.Prepare => "准备",
        TestPhase.Test => "测试",
        TestPhase.Cleanup => "清理",
        _ => phase.ToString()
    };

    public static bool IsOperatorProperty(string key) =>
        Catalog.Enumerate().Any(descriptor =>
            descriptor.Parameters.Any(parameter => parameter.Key == key));

    public static string PropertyLabel(string key) =>
        Catalog.Enumerate().SelectMany(x => x.Parameters)
            .FirstOrDefault(x => x.Key == key)?.Label ?? key;

    public static StepNode NewNode(string type, string defaultTarget = "") =>
        Catalog.CreateNodeForType(type, defaultTarget);
}