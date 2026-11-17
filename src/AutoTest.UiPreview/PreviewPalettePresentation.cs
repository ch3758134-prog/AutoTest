namespace AutoTest.UiPreview;

/// <summary>
/// Frozen G3 light IDE palette ordering and visual categories; these names are
/// presentation only. Formal StepUiCatalog owns the real authoring contracts.
/// </summary>
public static class PreviewPalettePresentation
{
    public static readonly IReadOnlyList<string> Groups =
    [
        "CAN 与数据", "工位操作", "UDS 服务", "波形分析", "流程与计算"
    ];
    public static PaletteKind Describe(string type, string label) => type switch
    {
        "CanSend" => New(type, label, "CAN 与数据", "⇄"),
        "CanWait" => New(type, label, "CAN 与数据", "◷"),
        "Read" => New(type, label, "CAN 与数据", "↧"),
        "WaitUntil" => New(type, label, "CAN 与数据", "◉"),
        "Expect" => New(type, label, "CAN 与数据", "✓"),
        "Write" => New(type, label, "工位操作", "◈"),
        "UdsRequest" => New(type, label, "UDS 服务", "◇"),
        "Flash" => New(type, label, "UDS 服务", "◇"),
        "WaveStart" or "WaveCapture" => New(type, label, "波形分析", "〰"),
        "WaveStop" => New(type, label, "波形分析", "◼"),
        "WaveAnalyze" or "WaveCompare" => New(type, label, "波形分析", "≋"),
        "Wait" => New(type, label, "流程与计算", "◷"),
        "SetVariable" => New(type, label, "流程与计算", "𝑥"),
        "If" => New(type, label, "流程与计算", "⑂"),
        "Switch" => New(type, label, "流程与计算", "⋈"),
        "When" => New(type, label, "流程与计算", "◉"),
        "Loop" => New(type, label, "流程与计算", "⟳"),
        "TryCatch" => New(type, label, "流程与计算", "⚑"),
        "Subroutine" => New(type, label, "流程与计算", "▤"),
        "CallSubroutine" => New(type, label, "流程与计算", "↳"),
        "Sequence" => New(type, label, "流程与计算", "▤"),
        "Calculate" => New(type, label, "流程与计算", "𝑥"),
        "Case" => New(type, label, "流程与计算", "⋈"),
        _ => New(type, label, "流程与计算", "◇")
    };

    // CASE nodes are offered only inside a SWITCH/cases branch by the WPF view;
    // adding them directly to the Prepare/Test/Cleanup phase is invalid.


    public static string Accent(string group) => group switch
    {
        "CAN 与数据" => "#4186CC",
        "工位操作" => "#D59138",
        "UDS 服务" => "#9069C3",
        "波形分析" => "#3CAA8E",
        _ => "#8E6ABD"
    };
    private static PaletteKind New(string type, string label, string group, string icon)
        => new(type, label, group, icon);
}
