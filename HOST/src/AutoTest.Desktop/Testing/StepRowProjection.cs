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
    private readonly IReadOnlyList<TestTargetMapping> _mappings;
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
        "Write" => "⇄",
        "Read" => "↧",
        "WaitUntil" or "Wait" => "◷",
        "WaveStart" => "〰",
        "WaveStop" or "WaveAnalyze" or "WaveCompare" => "≋",
        "If" or "Switch" or "Case" => "⑂",
        "Loop" => "⟳",
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
        "Write" or "Read" or "WaitUntil" or "AssertSignal" or "CanStopPeriodic" => "Can",
        "WaveStart" or "WaveStop" or "WaveAnalyze" or "WaveCompare" => "Wave",
        "If" or "Switch" or "Case" or "Loop" or "Sequence" => "Flow",
        _ => "Neutral"
    };
    public bool IsSearchMatch { get; }
    public bool IsNewDraft { get; }
    public bool IsBreakpoint => BreakpointMark.Length > 0;
    public bool IsUnavailable => State is "暂不能运行" or "步骤类型不支持" or "未知类型";
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
            RaisePropertyChanged(nameof(ShowNormalSummary));
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
    public string AddBranchButtonText => Item.SlotName switch
    {
        "then" => "＋ 添加 THEN 步骤",
        "else" => "＋ 添加 ELSE 步骤",
        "body" => "＋ 添加循环内步骤",
        "cases" => "＋ 添加 CASE",
        _ => "＋ 添加子步骤"
    };
    public string Kind => Item.Step is { } n ? StepUiCatalog.Label(n, _mappings) : "";
    public string Summary { get; }
    public bool IsCanSendRow { get; }
    public bool ShowNormalSummary => ShowQuickSummary && !IsCanSendRow;
    public string CanFrameLine { get; } = "";
    public string CanDataLine { get; } = "";
    public string ResourceDisplay { get; }
    public string TypeLabel => Item.Step is { } node
        ? StepUiCatalog.Label(node, _mappings) : "";
    public string StateTone => State switch
    {
        "判定通过" => "Done",
        "超时" or "数据无效" or "设备错误" or "无数据" or "判定失败" or "步骤类型不支持" or "未知类型" => "Error",
        "待完善" or "暂不能运行" or "结果待核查" => "Warning",
        "已关闭" or "已跳过" or "条件不成立" or "已取消" => "Muted",
        _ => "Idle"
    };
    public string Resource { get; }
    public string State { get; }
    public Thickness Indent => new(9 + Depth * 18, 0, 0, 0);
    public StepRowProjection(StepTreeItem item, int depth, bool collapsed = false,
        string? search = null, bool newDraft = false,
        IReadOnlyList<TestTargetMapping>? mappings = null,
        RealStepFact? realFact = null, string? draftActionKey = null)
    {
        Item = item;
        _mappings = mappings ?? Array.Empty<TestTargetMapping>();
        _draftActionKey = draftActionKey;
        IsCollapsed = collapsed;
        Depth = depth;
        Sequence = item.Kind == StepTreeItemKind.Step ? item.Index + 1 : 0;
        var step = item.Step;
        var targetName = step?.Properties.GetValueOrDefault("target", "");
        var targetMapping = mappings?.FirstOrDefault(x =>
            string.Equals(x.Name, targetName, StringComparison.OrdinalIgnoreCase));
        _targetKind = targetMapping?.Kind;
        IsCanSendRow = step?.Type == "Write" &&
            (_targetKind is TestTargetKind.CanMessage or TestTargetKind.RawCan ||
             _draftActionKey == "can");
        QuickValue = OriginalQuickValue;
        IsNewDraft = newDraft;
        HasRealExecutionError = realFact?.Status is
            RealStepStatus.Timeout or RealStepStatus.InvalidData or RealStepStatus.DeviceError;
        StateDetail = realFact?.Detail ?? "";
        // Match the same fields as TestEditorViewModel.MatchesSearch so the
        // count, Enter navigation and yellow row highlight cannot disagree.
        IsSearchMatch = !string.IsNullOrWhiteSpace(search) && step is not null &&
            (step.Name + " " + StepUiCatalog.Label(step, _mappings) + " " +
             string.Join(" ", step.Properties
                 .Where(x => StepUiCatalog.IsOperatorProperty(x.Key))
                 .Select(x => StepUiCatalog.PropertyLabel(x.Key) + " " + x.Value)))
                .Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
        if (step is null)
        {
            Summary = "";
            Resource = "";
            ResourceDisplay = "";
            State = "";
            return;
        }
        string Prop(string key) => step.Properties.GetValueOrDefault(key, "");
        var displayTarget = _targetKind == TestTargetKind.Signal && !string.IsNullOrWhiteSpace(targetMapping?.SignalName)
            ? targetMapping!.SignalName : Prop("target");
        Summary = step.Type switch
        {
            "Wait" => Prop("milliseconds") + " ms",
            "Loop" => "重复 " + Prop("count") + " 次",
            "If" => "IF " + Prop("variable") + " " +
                Prop("operator") + " " + Prop("equals"),
                        "Switch" => "按变量 " + Prop("variable") + " 分派",
            "Case" => "匹配值 " + Prop("equals"),
            "Write" => HumanWriteSummary(Prop("target"), Prop("value"), _targetKind),
                        "Read" => "读取 " + displayTarget + " → " + Prop("variable") +
                " · " + (Prop("mode") == "Latest" ? "最新有效值" : "等待新数据"),
            "WaitUntil" => "等待 " + displayTarget + " " + Prop("operator") + " " +
                Prop("expected") + " · 超时 " + Prop("timeoutMs") + " ms",
            "CanStopPeriodic" => "停止本次测试的全部 ECU CAN 周期发送",
            "AssertSignal" => "判定 " + displayTarget + " " +
                Prop("operator") + " " + Prop("expected") + " · " +
                Prop("timeoutMs") + " 毫秒",
            "Expect" => Prop("left") + " " + Prop("operator") + " " + Prop("right"),
            "SetVariable" => Prop("variable") + " = " + Prop("value"),
            "Calculate" => "计算 " + Prop("variable") + " = " + Prop("left") +
                " " + Prop("operator") + " " + Prop("right"),
            "WaveStart" => "采集 " + Prop("captureKey") +
                " · " + Prop("durationMs") + " ms",
            "WaveStop" => "停止采集 " + Prop("captureKey"),
            "WaveAnalyze" or "WaveCompare" => "波形配置 " +
                (Prop("profile").Length > 0 ? Prop("profile") : "未选择") +
                " · 标准 " + Prop("reference"),
                        "Sequence" => "按顺序执行子步骤",
            _ => StepUiCatalog.Label(step, _mappings) + " · 打开属性查看参数"
        };
        if (step.Type == "If" &&
            (!step.Slots.TryGetValue("then", out var thenArm) ||
             !thenArm.Any(StepExecutionPolicy.IsEnabled)) &&
            (!step.Slots.TryGetValue("else", out var elseArm) ||
             !elseArm.Any(StepExecutionPolicy.IsEnabled)))
            Summary += " · THEN / ELSE 都没有动作，请添加分支步骤";
        if (step.Type == "If" && realFact is not null)
        {
            Summary += realFact.Status == RealStepStatus.NoData
                ? " · 无法判断，后续测试已阻断"
                : realFact.Status == RealStepStatus.Completed
                    ? " · 实际 " + realFact.Actual + " → " +
                      (realFact.Detail.Contains("→ THEN", StringComparison.Ordinal) ||
                       realFact.Detail.Contains("Branch: then", StringComparison.Ordinal)
                          ? "THEN" : "ELSE")
                    : " · " + realFact.Status;
        }
        if (step.Type == "Write" &&
            int.TryParse(Prop("periodMs"), out var interval) && interval > 0)
        {
            Summary += " · 周期 " + interval + " ms";
            if (int.TryParse(Prop("durationMs"), out var duration) && duration > 0)
                Summary += " · " + duration + " ms 后自动停止";
            else Summary += " · 直到停止或测试清理";
        }
        if (IsCanSendRow)
        {
            var value = Prop("value");
            var mode = "单次发送";
            if (int.TryParse(Prop("periodMs"), out var periodMs) && periodMs > 0)
            {
                mode = "周期 " + periodMs + " ms";
                mode += int.TryParse(Prop("durationMs"), out var durationMs) && durationMs > 0
                    ? " · " + durationMs + " ms 后停止" : " · 直到停止 / 测试清理";
            }
            var frame = targetMapping is null ? "CAN ID 待配置" :
                "0x" + targetMapping.CanId.ToString("X") +
                (targetMapping.Extended ? " · 扩展帧" : " · 标准帧");
            if (value.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
            {
                var hex = string.Concat(value[4..].Where(ch => !char.IsWhiteSpace(ch)));
                if (hex.Length <= 16 && hex.Length % 2 == 0 && hex.All(Uri.IsHexDigit))
                {
                    var bytes = Convert.FromHexString(hex);
                    CanDataLine = bytes.Length == 0 ? "DATA：无数据 (DLC 0)" :
                        "DATA  " + string.Join(" ", bytes.Select(b => b.ToString("X2")));
                    frame += " · DLC " + bytes.Length;
                }
                else CanDataLine = "DATA 格式无效，请核对属性";
            }
            else CanDataLine = value.Contains('=') ? "信号赋值配置 · 打开属性核对实际 DATA" : "DATA 待配置";
            CanFrameLine = frame + " · " + mode;
            Summary = CanFrameLine + " · " + CanDataLine;
        }
        if (!IsCanSendRow && Summary.Length > 150) Summary = Summary[..150] + "…";
        Resource = step.Properties.GetValueOrDefault("target",
            step.Properties.GetValueOrDefault("profile", "—"));
        ResourceDisplay = _targetKind switch
        {
            TestTargetKind.RawKey => "20 Key",
            TestTargetKind.Button => "按键动作",
            TestTargetKind.Ignition => "IGN",
            TestTargetKind.RawCan => "原始 CAN",
            TestTargetKind.CanMessage => "DBC 辅助",
            TestTargetKind.Signal => targetMapping?.SignalName ?? Resource,
            TestTargetKind.WaveProfile => "波形配置",
            _ => Resource
        };
        var descriptor = StepDescriptorCatalog.Builtins().Lookup(step.Type);
        State = newDraft ? "待完善" :
            !StepExecutionPolicy.IsEnabled(step) && realFact is null ? "已关闭" :
            step.Type == "If" && realFact is null &&
            (!step.Slots.TryGetValue("then", out var thenSteps) ||
             !thenSteps.Any(StepExecutionPolicy.IsEnabled)) &&
            (!step.Slots.TryGetValue("else", out var elseSteps) ||
             !elseSteps.Any(StepExecutionPolicy.IsEnabled)) ? "待完善" :
            realFact is not null ? realFact.Status switch
            {
                RealStepStatus.Completed => step.Type is "AssertSignal" or "Expect" or "WaveCompare" or "WaitUntil"
                    ? "判定通过" : "执行成功",
                RealStepStatus.Skipped => "已跳过",
                RealStepStatus.ConditionFalse => step.Type is "AssertSignal" or "Expect" or "WaveCompare" or "WaitUntil"
                    ? "判定失败" : "条件不成立",
                RealStepStatus.NoData => "无数据",
                RealStepStatus.Timeout => "超时",
                RealStepStatus.InvalidData => "数据无效",
                RealStepStatus.DeviceError => "设备错误",
                RealStepStatus.Cancelled => "已取消",
                _ => "结果待核查"
            } : descriptor is null ? "未知类型" :
            descriptor.Execution == StepExecutionCapability.Unsupported
                ? "暂不能运行" : "未执行";
    }

    private static string HumanWriteSummary(string target, string value, TestTargetKind? kind)
    {
        if (kind == TestTargetKind.Ignition ||
            target.Contains("IGN", StringComparison.OrdinalIgnoreCase))
            return "IGN " + (value.Equals("ON", StringComparison.OrdinalIgnoreCase) ? "开启" :
                value.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? "关闭" : value);
        if (kind is TestTargetKind.Button or TestTargetKind.RawKey)
        {
            var parts = value.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && parts[0].Equals("Wait", StringComparison.OrdinalIgnoreCase))
                return "纯等待 · " + (parts.Length > 1 ? parts[1] : "?") + " ms";
            if (value.Equals("Release", StringComparison.OrdinalIgnoreCase))
                return "KEY · 全部断开";
            if (parts.Length > 0 && parts[0] == "Hold" && parts.Length > 1)
            {
                var combination = FriendlyKeyMask(parts[1]);
                var duration = parts.Length > 2 ? parts[2] + " ms" : "持续";
                return combination == "全部断开"
                    ? "KEY 全部断开 · " + duration
                    : combination + " · 保持 · " + duration;
            }
            return "按键 " + target + " · " +
                (value.Length > 0 ? value : "待配置");
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

    private static string FriendlyKeyMask(string code)
    {
        if (!code.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !uint.TryParse(code[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var mask) ||
            mask > 0xFFFFF)
            return "KEY 组合待校验";
        if (mask == 0) return "全部断开";
        return string.Join(" + ", Enumerable.Range(1, 20)
            .Where(i => (mask & (1u << (i - 1))) != 0)
            .Select(i => "K" + i.ToString("00")));
    }

}
