using System.Windows;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// One display row per formal StepNode, branch, or phase. No second TEST document,
/// no independent IDs, and no persisted UI-only execution facts.
/// </summary>
public sealed class StepRowProjection
{
    public StepTreeItem Item { get; }
    public int Depth { get; }
    public int Sequence { get; }
    public string SequenceText => Sequence == 0 ? "" : Sequence.ToString("D2");
    public bool IsPhase => Item.Kind == StepTreeItemKind.Phase;
    public bool IsSlot => Item.Kind == StepTreeItemKind.Slot;
    public bool IsStep => Item.Kind == StepTreeItemKind.Step;
    public string Icon => Item.Icon;
    public string BreakpointMark => Item.Step?.Properties.GetValueOrDefault("editor.breakpoint", "false")
        == "true" ? "●" : "";
    public string Name => Item.Step?.Name ?? Item.Label;
    public string Kind => Item.Step is { } n ? StepUiCatalog.Label(n.Type) : "";
    public string Summary { get; }
    public string Resource { get; }
    public string State { get; }
    public Thickness Indent => new(9 + Depth * 18, 0, 0, 0);
    public StepRowProjection(StepTreeItem item, int depth)
    {
        Item = item;
        Depth = depth;
        Sequence = item.Kind == StepTreeItemKind.Step ? item.Index + 1 : 0;
        var step = item.Step;
        if (step is null)
        {
            Summary = "";
            Resource = "";
            State = "";
            return;
        }
        Summary = step.Type switch
        {
            "Wait" => step.Properties.GetValueOrDefault("milliseconds", "") + " ms",
            "Loop" => "重复 " + step.Properties.GetValueOrDefault("count", "") + " 次",
            "If" or "When" => step.Properties.GetValueOrDefault("variable", "") + " = " +
                step.Properties.GetValueOrDefault("equals", ""),
            "Write" => step.Properties.GetValueOrDefault("value", ""),
            "Read" => "→ " + step.Properties.GetValueOrDefault("variable", ""),
            "Expect" => step.Properties.GetValueOrDefault("left", "") + " " +
                step.Properties.GetValueOrDefault("operator", "") + " " +
                step.Properties.GetValueOrDefault("right", ""),
            _ => string.Join(" · ", step.Properties
                .Where(x => x.Key is not ("target" or "profile"))
                .Take(3).Select(x => x.Key + ": " + x.Value))
        };
        if (Summary.Length > 150) Summary = Summary[..150] + "…";
        Resource = step.Properties.GetValueOrDefault("target",
            step.Properties.GetValueOrDefault("profile", "—"));
        var descriptor = StepDescriptorCatalog.Builtins().Lookup(step.Type);
        State = descriptor is null ? "未知类型" :
            descriptor.ExecutableV1 || step.Type is "Write" or "Read" or "WaitUntil" or
                "Calculate" or "Expect" or "WaveStart" or "WaveStop" or "WaveAnalyze"
                ? "待预检" : "执行未接线";
    }
}
