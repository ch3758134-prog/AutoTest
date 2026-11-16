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
    public bool IsCollapsed { get; }
    public string ExpandMark => Item.Kind is StepTreeItemKind.Phase or StepTreeItemKind.Slot ||
        Item.Children.Count > 0 ? (IsCollapsed ? "▸" : "▾") : "";
    public string Icon => Item.Step?.Type switch
    {
        "Write" or "CanSend" => "⇄",
        "Read" => "↧",
        "WaitUntil" or "CanWait" or "Wait" or "When" => "◷",
        "UdsRequest" => "◇",
        "Flash" => "⇧",
        "WaveStart" or "WaveCapture" => "〰",
        "WaveStop" or "WaveAnalyze" or "WaveCompare" => "≋",
        "If" or "Switch" or "Case" => "⑂",
        "Loop" => "⟳",
        "TryCatch" => "⚑",
        "Subroutine" or "CallSubroutine" => "↳",
        "SetVariable" or "Calculate" => "𝑥",
        "Expect" => "✓",
        _ => Item.Icon
    };

    public string PhaseKind => Item.Phase.ToString();
    public string Category => Item.Step?.Type switch
    {
        "Write" when Item.Step.Properties.GetValueOrDefault("target", "")
            .Contains("IGN", StringComparison.OrdinalIgnoreCase) => "Hardware",
        "Write" when Item.Step.Properties.GetValueOrDefault("target", "")
            .Contains("KEY", StringComparison.OrdinalIgnoreCase) => "Hardware",
        "Write" or "CanSend" or "CanWait" or "Read" or "WaitUntil" => "Can",
        "UdsRequest" or "Flash" => "Uds",
        "WaveStart" or "WaveStop" or "WaveAnalyze" or "WaveCapture" or "WaveCompare" => "Wave",
        "If" or "Switch" or "Case" or "When" or "Loop" or "TryCatch" or "Sequence" or
            "Subroutine" or "CallSubroutine" => "Flow",
        _ => "Neutral"
    };
    public bool IsSearchMatch { get; }
    public bool IsNewDraft { get; }
    public bool IsBreakpoint => BreakpointMark.Length > 0;
    public bool IsUnavailable => State == "执行未接线" || State == "未知类型";
    public bool IsQuickEditable => Item.Step?.Type is "Wait" or "Loop";
    public bool IsNotQuickEditable => !IsQuickEditable;
    public string OriginalQuickValue => Item.Step?.Type switch
    {
        "Wait" => Item.Step.Properties.GetValueOrDefault("milliseconds", ""),
        "Loop" => Item.Step.Properties.GetValueOrDefault("count", ""),
        _ => ""
    };
    public string QuickValue { get; set; } = "";
    public string BreakpointMark => Item.Step?.Properties.GetValueOrDefault("editor.breakpoint", "false")
        == "true" ? "●" : "";
    public string Name => IsPhase ? StepUiCatalog.PhaseLabel(Item.Phase) :
        Item.Step?.Name ?? Item.Label;
    public string RowCount => IsPhase ? Item.Children.Count + " 步" : "";
    public string Kind => Item.Step is { } n ? StepUiCatalog.Label(n.Type) : "";
    public string Summary { get; }
    public string Resource { get; }
    public string State { get; }
    public Thickness Indent => new(9 + Depth * 18, 0, 0, 0);
    public StepRowProjection(StepTreeItem item, int depth, bool collapsed = false,
        string? search = null, bool newDraft = false)
    {
        Item = item;
        IsCollapsed = collapsed;
        Depth = depth;
        Sequence = item.Kind == StepTreeItemKind.Step ? item.Index + 1 : 0;
        var step = item.Step;
        QuickValue = OriginalQuickValue;
        IsNewDraft = newDraft;
        IsSearchMatch = !string.IsNullOrWhiteSpace(search) &&
            (Name + " " + Kind + " " + item.Step?.Properties.GetValueOrDefault("target", "") +
             " " + string.Join(" ", item.Step?.Properties.Values ?? []))
                .Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
        if (step is null)
        {
            Summary = "";
            Resource = "";
            State = "";
            return;
        }
        string Prop(string key) => step.Properties.GetValueOrDefault(key, "");
        Summary = step.Type switch
        {
            "Wait" => Prop("milliseconds") + " ms",
            "Loop" => "重复 " + Prop("count") + " 次",
            "If" => "IF " + Prop("variable") + " = " + Prop("equals"),
            "When" => "等待条件 " + Prop("variable") + " = " + Prop("equals") +
                " · 超时 " + Prop("timeoutMs") + " ms",
            "Switch" => "按变量 " + Prop("variable") + " 分派",
            "Case" => "匹配值 " + Prop("equals"),
            "Write" => HumanWriteSummary(Prop("target"), Prop("value")),
            "CanSend" => "报文 " + (Prop("dbcMessage").Length > 0 ? Prop("dbcMessage") : "未选择") +
                " · CAN 发送（未接线）",
            "CanWait" => "等待报文 " + (Prop("dbcMessage").Length > 0 ? Prop("dbcMessage") : "未选择") +
                " · 超时 " + Prop("timeoutMs") + " ms（未接线）",
            "Read" => "读取 " + Prop("target") + " → " + Prop("variable") +
                " · " + (Prop("mode") == "Latest" ? "最新有效值" : "等待新数据"),
            "WaitUntil" => "信号 " + Prop("target") + " · 超时 " + Prop("timeoutMs") + " ms",
            "Expect" => Prop("left") + " " + Prop("operator") + " " + Prop("right"),
            "SetVariable" => Prop("variable") + " = " + Prop("value"),
            "Calculate" => "计算 " + Prop("variable") + " = " + Prop("left") +
                " " + Prop("operator") + " " + Prop("right"),
            "UdsRequest" => "UDS 服务 " + Prop("service") + "（执行待授权）",
            "WaveStart" or "WaveCapture" => "采集 " + Prop("captureKey") +
                " · " + Prop("durationMs") + " ms",
            "WaveStop" => "停止采集 " + Prop("captureKey"),
            "WaveAnalyze" or "WaveCompare" => "参考 " + (Prop("reference").Length > 0
                ? Prop("reference") : "未选择") + " · 波形判定",
            "Flash" => "烧录配置 " + Prop("programmingProfile") + "（禁止自动执行）",
            "TryCatch" => "TRY / CATCH / FINALLY",
            "Subroutine" => "子程序 " + step.Name,
            "CallSubroutine" => "调用 " + Prop("target"),
            "Sequence" => "按顺序执行子步骤",
            _ => StepUiCatalog.Label(step.Type) + " · 请在右侧查看参数"
        };
        if (Summary.Length > 150) Summary = Summary[..150] + "…";
        Resource = step.Properties.GetValueOrDefault("target",
            step.Properties.GetValueOrDefault("profile", "—"));
        var descriptor = StepDescriptorCatalog.Builtins().Lookup(step.Type);
        State = newDraft ? "待完善" : descriptor is null ? "未知类型" :
            descriptor.ExecutableV1 || step.Type is "Write" or "Read" or "WaitUntil" or
                "Calculate" or "Expect" or "WaveStart" or "WaveStop" or "WaveAnalyze"
                ? "待预检" : "执行未接线";
    }

    private static string HumanWriteSummary(string target, string value)
    {
        if (target.Contains("IGN", StringComparison.OrdinalIgnoreCase))
            return "IGN " + (value.Equals("ON", StringComparison.OrdinalIgnoreCase) ? "开启" :
                value.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? "关闭" : value);
        if (target.Contains("KEY", StringComparison.OrdinalIgnoreCase))
            return "按键 " + target + " · " + (value.Length > 0 ? value : "待配置");
        if (value.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
            return "原始 CAN 数据 · " + value.Length / 2 + " 字符";
        if (value.Contains('='))
        {
            var fields = value.Split(';', StringSplitOptions.RemoveEmptyEntries);
            return "DBC 报文 " + target + " · " + fields.Length + " 个信号";
        }
        return target.Length > 0 ? target + " · " + value : "未选择操作对象";
    }
}
