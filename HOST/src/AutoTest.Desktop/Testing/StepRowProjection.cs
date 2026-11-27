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
        "Flash" => "Flash",
        "UdsRequest" => "Uds",
        "WaveStart" or "WaveStop" or "WaveAnalyze" or "WaveCapture" or "WaveCompare" => "Wave",
        "If" or "Switch" or "Case" or "When" or "Loop" or "TryCatch" or "Sequence" or
            "Subroutine" or "CallSubroutine" => "Flow",
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
    public string TypeLabel => Item.Step?.Type switch
    {
        "Write" when _targetKind == TestTargetKind.RawKey || _targetKind == TestTargetKind.Button || _draftActionKey == "key" => "按键",
        "Write" when _targetKind == TestTargetKind.Ignition || _draftActionKey == "ign" => "IGN",
        "Write" or "CanSend" => "CAN 发送",
        "AssertSignal" => "信号判定",
        "Read" => "信号读取",
        "WaitUntil" => "信号等待",
        "Flash" => "烧录",
        "UdsRequest" => "UDS",
        _ => Kind
    };
    public string StateTone => State switch
    {
        "已执行" => "Done",
        "超时" or "数据无效" or "设备错误" or "无数据" or "步骤类型不支持" or "未知类型" => "Error",
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
            (step.Name + " " + StepUiCatalog.Label(step, _mappings) + " "
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
                (Prop("operator").Length == 0 ? "==" : Prop("operator")) +
                " " + Prop("equals"),
            "When" => "等待条件 " + Prop("variable") + " = " + Prop("equals") +
                " · 超时 " + Prop("timeoutMs") + " ms",
            "Switch" => "按变量 " + Prop("variable") + " 分派",
            "Case" => "匹配值 " + Prop("equals"),
            "Write" => HumanWriteSummary(Prop("target"), Prop("value"), _targetKind),
            "CanSend" => "待转换的 CAN 发送 · " +
                (Prop("dbcMessage").Length > 0 ? Prop("dbcMessage") : step.Name),
            "CanWait" => "待转换的 CAN 等待 · " +
                (Prop("dbcMessage").Length > 0 ? Prop("dbcMessage") : step.Name),
            "Read" => "读取 " + displayTarget + " → " + Prop("variable") +
                " · " + (Prop("mode") == "Latest" ? "最新有效值" : "等待新数据"),
            "WaitUntil" => "信号 " + displayTarget + " · 超时 " + Prop("timeoutMs") + " ms",
            "CanStopPeriodic" => "停止本次测试的全部 ECU CAN 周期发送",
            "AssertSignal" => "判定 " + displayTarget + " " +
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
            _ => StepUiCatalog.Label(step.Type) + " · 打开属性查看参数"
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
            step.Type is "KeySet" or "CanSend" or "CanWait" && realFact is null
                ? "步骤类型不支持" :
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
                ? "未执行" : "暂不能运行";
    }

    private static string HumanWriteSummary(string target, string value, TestTargetKind? kind)
    {
        if (kind == TestTargetKind.Ignition ||
            target.Contains("IGN", StringComparison.OrdinalIgnoreCase))
            return "IGN " + (value.Equals("ON", StringComparison.OrdinalIgnoreCase) ? "开启" :
                value.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? "关闭" : value);
        if (kind is TestTargetKind.Button or TestTargetKind.RawKey ||
            target.Contains("KEY", StringComparison.OrdinalIgnoreCase))
        {
            var parts = value.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && parts[0].Equals("Wait", StringComparison.OrdinalIgnoreCase))
                return "纯等待 · " + (parts.Length > 1 ? parts[1] : "?") + " ms";
            if (value.Equals("Release", StringComparison.OrdinalIgnoreCase))
                return "KEY · 全部断开";
            if (parts.Length > 0 && parts[0] is "Hold" or "Pulse" && parts.Length > 1)
            {
                var combination = kind == TestTargetKind.RawKey
                    ? FriendlyKeyMask(parts[1]) : target;
                var duration = parts.Length > 2 ? parts[2] + " ms" : "持续";
                if (parts[0] == "Pulse")
                    return combination + " · 短按 " + duration;
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
