using System.Windows;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Runtime;
using Prism.Mvvm;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// One display row per formal StepNode, branch, or phase. No second TEST document,
/// no independent IDs, and no persisted UI-only execution facts.
/// </summary>
public sealed class StepRowProjection : BindableBase
{
    public StepTreeItem Item { get; }
    private readonly TestTargetKind? _targetKind;
    private readonly string? _draftActionKey;
    public int Depth { get; }
    public int Sequence { get; }
    public string SequenceText => Sequence == 0 ? "" : Sequence.ToString("D2");
    public bool IsPhase => Item.Kind == StepTreeItemKind.Phase;
    // Presentation-only empty state: no additional StepNode or TEST collection.
    public bool IsEmptyPhase => IsPhase && Item.Children.Count == 0;
    public bool IsSlot => Item.Kind == StepTreeItemKind.Slot;
    public bool IsStep => Item.Kind == StepTreeItemKind.Step;
    public bool IsCollapsed { get; }
    public string ExpandMark => Item.Kind is StepTreeItemKind.Phase or StepTreeItemKind.Slot ||
        Item.Children.Count > 0 ? (IsCollapsed ? "▸" : "▾") : "";
    public string Icon => Item.Step?.Type switch
    {
        "Write" when _draftActionKey == "ign" => "◈",
        "Write" when _draftActionKey == "key" => "⌁",
        "Write" when _targetKind == TestTargetKind.Ignition => "◈",
        "Write" when _targetKind is TestTargetKind.Button or TestTargetKind.RawKey => "⌁",
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
        "Write" when _draftActionKey is "ign" or "key" => "Hardware",
        "Write" when _targetKind is TestTargetKind.Ignition or
            TestTargetKind.Button or TestTargetKind.RawKey => "Hardware",
        "Write" or "CanSend" or "CanWait" or "CanStopPeriodic" or "Read" or "WaitUntil" or "AssertSignal" => "Can",
        "UdsRequest" or "Flash" => "Uds",
        "WaveStart" or "WaveStop" or "WaveAnalyze" or "WaveCapture" or "WaveCompare" => "Wave",
        "If" or "Switch" or "Case" or "When" or "Loop" or "TryCatch" or "Sequence" or
            "Subroutine" or "CallSubroutine" => "Flow",
        _ => "Neutral"
    };
    public bool IsSearchMatch { get; }
    public bool IsNewDraft { get; }
    public bool IsBreakpoint => BreakpointMark.Length > 0;
    public bool IsUnavailable => State is "暂不能运行" or "旧步骤待转换" or "未知类型" or "旧文件待核对";
    public bool HasRealExecutionError { get; }
    public string StateDetail { get; }
    public bool IsQuickEditable =>
        !IsNewDraft && Item.Step?.Type is ("Wait" or "Loop");
    public bool IsNotQuickEditable => !IsQuickEditable;
    private bool _inlineEditing;
    public bool IsInlineEditing
    {
        get => _inlineEditing;
        set
        {
            if (!SetProperty(ref _inlineEditing, value)) return;
            RaisePropertyChanged(nameof(ShowQuickSummary));
            RaisePropertyChanged(nameof(ShowInlineEditor));
        }
    }
    public bool ShowQuickSummary => !IsInlineEditing;
    public bool ShowInlineEditor => IsInlineEditing && IsQuickEditable;
    public void CancelQuickEdit()
    {
        QuickValue = OriginalQuickValue;
        IsInlineEditing = false;
    }
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
        string? search = null, bool newDraft = false,
        IReadOnlyList<TestTargetMapping>? mappings = null,
        RealStepFact? realFact = null, string? draftActionKey = null)
    {
        Item = item;
        _draftActionKey = draftActionKey;
        IsCollapsed = collapsed;
        Depth = depth;
        Sequence = item.Kind == StepTreeItemKind.Step ? item.Index + 1 : 0;
        var step = item.Step;
        var targetName = step?.Properties.GetValueOrDefault("target", "");
        _targetKind = mappings?.FirstOrDefault(x =>
            string.Equals(x.Name, targetName, StringComparison.OrdinalIgnoreCase))?.Kind;
        QuickValue = OriginalQuickValue;
        IsNewDraft = newDraft;
        HasRealExecutionError = realFact?.Status is
            RealStepStatus.Timeout or RealStepStatus.InvalidData or RealStepStatus.DeviceError;
        StateDetail = realFact?.Detail ?? "";
        // Match the same fields as TestEditorViewModel.MatchesSearch so the
        // count, Enter navigation and yellow row highlight cannot disagree.
        IsSearchMatch = !string.IsNullOrWhiteSpace(search) && step is not null &&
            (step.Name + " " + StepUiCatalog.Label(step.Type) + " " +
             string.Join(" ", step.Properties
                 .Where(x => StepUiCatalog.IsOperatorProperty(x.Key))
                 .Select(x => StepUiCatalog.PropertyLabel(x.Key) + " " + x.Value)))
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
            "Write" => HumanWriteSummary(Prop("target"), Prop("value"), _targetKind),
            "CanSend" => "待转换的 CAN 发送 · " +
                (Prop("dbcMessage").Length > 0 ? Prop("dbcMessage") : step.Name),
            "CanWait" => "待转换的 CAN 等待 · " +
                (Prop("dbcMessage").Length > 0 ? Prop("dbcMessage") : step.Name),
            "Read" => "读取 " + Prop("target") + " → " + Prop("variable") +
                " · " + (Prop("mode") == "Latest" ? "最新有效值" : "等待新数据"),
            "WaitUntil" => "信号 " + Prop("target") + " · 超时 " + Prop("timeoutMs") + " ms",
            "CanStopPeriodic" => "停止本次测试的全部 ECU CAN 周期发送",
            "AssertSignal" => "判定 " + Prop("target") + " " +
                Prop("operator") + " " + Prop("expected") + " · " +
                Prop("timeoutMs") + " 毫秒",
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
        if (step.Type == "Write" &&
            int.TryParse(Prop("periodMs"), out var interval) && interval > 0)
        {
            Summary += " · 周期 " + interval + " ms";
            if (int.TryParse(Prop("durationMs"), out var duration) && duration > 0)
                Summary += " · " + duration + " ms 后自动停止";
            else Summary += " · 直到停止或测试清理";
        }
        if (Summary.Length > 150) Summary = Summary[..150] + "…";
        Resource = step.Properties.GetValueOrDefault("target",
            step.Properties.GetValueOrDefault("profile", "—"));
        var descriptor = StepDescriptorCatalog.Builtins().Lookup(step.Type);
        State = newDraft ? "待完善" :
            !StepExecutionPolicy.IsEnabled(step) && realFact is null ? "已关闭" :
            step.Type is "KeySet" or "CanSend" or "CanWait" && realFact is null
                ? "旧步骤待转换" :
            StepUiCatalog.RequiresLegacyReview(step) && realFact is null ? "旧文件待核对" :
            realFact is not null ? realFact.Status switch
            {
                RealStepStatus.Completed => "已执行",
                RealStepStatus.Skipped => "已跳过",
                RealStepStatus.ConditionFalse => "条件不成立",
                RealStepStatus.NoData => "无数据",
                RealStepStatus.Timeout => "超时",
                RealStepStatus.InvalidData => "数据无效",
                RealStepStatus.DeviceError => "设备错误",
                RealStepStatus.Cancelled => "已取消",
                _ => "结果待核查"
            } : descriptor is null ? "未知类型" :
            descriptor.ExecutableV1 || step.Type is "Write" or "Read" or "WaitUntil" or "AssertSignal" or "CanStopPeriodic" or
                "Calculate" or "Expect" or "WaveStart" or "WaveStop" or "WaveAnalyze"
                ? "待预检" : "暂不能运行";
    }

    private static string HumanWriteSummary(string target, string value, TestTargetKind? kind)
    {
        if (kind == TestTargetKind.Ignition || target.Contains("IGN", StringComparison.OrdinalIgnoreCase))
            return "IGN " + (value.Equals("ON", StringComparison.OrdinalIgnoreCase) ? "开启" :
                value.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? "关闭" : value);
        if (kind is TestTargetKind.Button or TestTargetKind.RawKey ||
            target.Contains("KEY", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Equals("Release", StringComparison.OrdinalIgnoreCase))
                return "松开 KEY 开关 · " + target;
            var segments = value.Split(':', StringSplitOptions.TrimEntries);
            if (segments.Length > 0 && segments[0] is "Hold" or "Pulse")
                return (segments[0] == "Hold" ? "保持按键组合" : "短按按键组合") +
                    " · " + target + (segments.Length > 1 ? " · " + segments[1] : "") +
                    (segments.Length > 2 ? " · " + segments[2] + " ms" : "");
            return "按键 " + target + " · " + (value.Length > 0 ? value : "待配置");
        }
        if (value.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
            return "CAN " + target + " · DATA " +
                string.Join(" ", Enumerable.Range(0, Math.Max(0, (value.Length - 4) / 2))
                    .Select(i => value.Substring(4 + i * 2, 2)));
        if (value.Contains('='))
        {
            var fields = value.Split(';', StringSplitOptions.RemoveEmptyEntries);
            return "CAN " + target + " · 已配置 " + fields.Length + " 个信号";
        }
        return target.Length > 0 ? target + " · " + value : "未选择操作对象";
    }
}
