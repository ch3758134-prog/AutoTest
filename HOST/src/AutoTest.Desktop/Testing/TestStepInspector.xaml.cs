using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoTest.Specialists.Wave;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Configuration;
using AutoTest.Desktop.Dbc;
using AutoTest.Projects.Dbc;
using AutoTest.Testing.Authoring;
using Prism.Mvvm;

namespace AutoTest.Desktop.Testing;

public sealed record TestActionOption(string Key, string Category, string Title, string StepType,
    string TargetCategory, string Description)
{
    public string DisplayName => Category + "  /  " + Title;
    public string Group => Key switch
    {
        "can" or "can-send" or "can-wait" or "stop-can" or "read" or "wait-signal" or "assert-signal" or "expect"
            => "CAN 与数据",
        "key" or "ign" => "工位操作",
        "uds" => "UDS 服务",
        "wave-start" or "wave-stop" or "wave-analyze" or "wave-capture" or "wave-compare"
            => "波形分析",
        "flash" => "UDS 服务",
        _ => "流程与计算"
    };
}

public sealed record TestStepFieldChoice(string Value, string Label);

public sealed record PublicFlowChoice(Guid AssetId, int Revision, string Sha256, string Name)
{
    public string Label => Name + " · 修订 " + Revision;
}

public sealed class TestStepInputField : BindableBase
{
    private string _value;
    public TestStepInputField(string key, string value, string? label = null,
        string stepType = "", IReadOnlyList<TestStepFieldChoice>? customChoices = null)
    {
        Key = key;
        Label = label ?? StepUiCatalog.PropertyLabel(key);
        _value = value;
        Choices = customChoices ?? (key switch
        {
            "mode" when value is "Latest" or "Next" =>
            [
                new("Latest", "读取最近一次收到的信号"),
                new("Next", "等待下一次收到的信号")
            ],
            "operator" when stepType == "Calculate" &&
                value is "+" or "-" or "*" or "/" =>
            [
                new("+", "加"),
                new("-", "减"),
                new("*", "乘"),
                new("/", "除")
            ],
            "operator" when (stepType is "If" or "Expect" or "WaitUntil" or "AssertSignal") &&
                (value is "==" or "!=" or ">" or ">=" or "<" or "<=") =>
            [
                new("==", "等于"),
                new("!=", "不等于"),
                new(">", "大于"),
                new(">=", "大于等于"),
                new("<", "小于"),
                new("<=", "小于等于")
            ],
            _ => []
        });
    }
    public string Key { get; }
    public string Label { get; }
    public IReadOnlyList<TestStepFieldChoice> Choices { get; }
    public bool HasChoices => Choices.Count > 0;
    public string Value { get => _value; set => SetProperty(ref _value, value ?? ""); }
}
public sealed class TestSignalInputField : DbcEditableSignal
{
    public bool IsEditable { get; }
    public TestSignalInputField(DbcSignal signal, string value, string? displayName = null,
        string? description = null, IReadOnlyDictionary<long, string>? rawLabels = null,
        string? summary = null, bool isEditable = true)
        : base(signal, displayName, description, rawLabels, value, summary)
        => IsEditable = isEditable;
}

public sealed record StepCompositionResult(StepNode Step, TestTargetMapping? Mapping);

/// <summary>
/// A single user-facing action-to-step transaction. Selection and editing are offline only;
/// the physical execution fence remains exclusively in RealTestWorkbenchService.
/// </summary>
public partial class TestStepInspector : UserControl
{
    private static readonly IReadOnlyList<TestActionOption> Options =
    [
        new("can", "外部操作", "发送 CAN 报文", "Write", "can",
            "直接编辑完整 CAN ID 与 DATA；选择 DBC 定义后，可用信号辅助解析和修改 DATA。"),
        new("key", "外部操作", "按键动作", "Write", "button",
            "选择 KEY 组合与保持时间。"),
        new("ign", "外部操作", "切换 IGN", "Write", "ign", "切换固定台架的 IGN 输出。"),
        new("stop-can", "外部操作", "停止全部周期 CAN", "CanStopPeriodic", "",
            "停止本次测试启动的 ECU CAN 周期发送。"),
        new("read", "读取与观察", "读取 CAN 信号", "Read", "signal",
            "通过已导入的 DBC 选择信号，将实际读取值保存到变量，不自动判定。"),
        new("wait-signal", "读取与观察", "等待信号条件", "WaitUntil", "signal",
            "持续观察指定 DBC 信号直到满足条件或超时。"),
        new("assert-signal", "分析与判断", "CAN 信号判定", "AssertSignal", "signal",
            "选择 DBC 信号及目标值，规定时间内达到条件判定通过；超时且未匹配判定失败，完全无有效数据则无法判定。"),
        new("wave-start", "读取与观察", "开始波形采集", "WaveStart", "signal",
            "从 ECU CAN 采集波形信号。"),
        new("wave-stop", "读取与观察", "结束波形采集", "WaveStop", "",
            "停止指定采集，保存测量证据。"),
        new("wave-analyze", "分析与判断", "分析波形", "WaveAnalyze", "wave",
            "选择已配置波形分析方案或参考，输出数据供测试判断步骤使用。"),
        new("expect", "分析与判断", "期望判定", "Expect", "",
            "比较测量值、变量或分析结果，显式作出测试结论。"),
        new("calculate", "分析与判断", "计算变量", "Calculate", "",
            "使用实际测量与变量构建计算表达式。"),
        new("variable", "分析与判断", "设置变量", "SetVariable", "",
            "设置测试用例的数据变量。"),
        new("if", "流程控制", "条件分支 IF / ELSE", "If", "",
            "根据已获得的变量值选择条件分支。"),
        new("loop", "流程控制", "循环", "Loop", "", "有界次数循环，循环体可以继续添加步骤。"),
        new("sequence", "流程控制", "步骤分组", "Sequence", "", "按照顺序执行子步骤。"),
        new("wait", "流程控制", "固定等待", "Wait", "", "固定等待指定毫秒数。"),
        new("switch", "流程控制", "SWITCH / CASE", "Switch", "",
            "按已有有效变量选择唯一 CASE；支持 default，未知数据阻断正常测试；无隐式贯穿。"),
        new("case", "流程控制", "CASE 分支", "Case", "",
            "在 SWITCH 子分支中填写匹配值并加入步骤。"),
        new("when", "流程控制", "WHEN 等待条件", "When", "",
            "条件等待与超时，运行前阻断。"),
        new("try", "流程控制", "TRY / CATCH / FINALLY", "TryCatch", "",
            "异常处理树结构可编辑；执行未接线。"),
        new("subroutine", "流程控制", "子程序", "Subroutine", "",
            "具名子程序容器，执行未接线。"),
        new("call-public-flow", "流程控制", "调用公共流程", "CallPublicFlow", "",
            "从当前工程选择已保存的公共流程，引用固定修订；不自动跟随更新。"),
        new("call", "流程控制", "调用子程序", "CallSubroutine", "",
            "必须明确填写子程序引用，执行未接线。"),
        new("can-send", "外部操作", "旧版 CAN 发送（待转换）", "CanSend", "",
            "原步骤只保留旧配置，尚不能发送到真实 ECU。请在中央步骤列表添加“发送 CAN 报文”，重新选择 DBC 报文并配置每个信号。"),
        new("can-wait", "读取与观察", "旧版 CAN 等待（待转换）", "CanWait", "",
            "旧等待节点尚不能参与真实执行。请使用“CAN 信号判定”或“等待信号条件”重新选择正式 DBC 信号。"),
        new("uds", "外部操作", "UDS 服务", "UdsRequest", "",
            "编辑服务和请求，实体诊断执行必须独立授权。"),
        new("flash", "外部操作", "Flash 烧录", "Flash", "",
            "选择经核对的配置及固件。真正运行需要连接设备、冻结资源指纹、独立授权和 APP 诊断恢复确认。"),
        new("wave-capture", "读取与观察", "波形采集（扩展步骤）", "WaveCapture", "",
            "编辑采集资源，运行前需要真实采集服务。"),
        new("wave-compare", "分析与判断", "波形判定", "WaveCompare", "wave",
            "比较本次已采集的真实 DBC 波形与冻结参考；PASS / FAIL / 无法判定均保留证据。")
    ];

    public static IReadOnlyList<TestActionOption> PaletteActions(bool caseOnly) =>
        Options.Where(option => caseOnly ? option.StepType == "Case" :
            // "CanSend" is an obsolete alternate authoring entry. A real
            // CAN sending operation already exists as Write + DBC/Raw CAN.
            // Keep it readable for old resources, but do not offer engineers
            // two misleading "send CAN" actions in the primary palette.
            option.StepType != "Case" && option.Key != "can-send").ToArray();

    private readonly StepNode? _editing;
    private readonly IReadOnlyList<TestStepFieldChoice> _ifVariables;
     private readonly IReadOnlyList<MappingChoice> _catalog;
    private readonly IReadOnlyList<TestTargetMapping> _boundMappings;
    private bool _ready;
    private TestActionOption? _selected;
    private MappingChoice? _selectedTarget;
    private readonly List<MappingChoice> _allTargets = [];
    private string _targetRevisionWarning = "";
    private bool _loadingCanData;
    private bool _explicitRawCan;
    private uint? _rawFormatId;
    private bool _rawFormatExtended;
    private string _canSignalError = "";
    private bool _filteringTargets;
    private bool IsCanSend => _selected?.Key == "can";
    private bool IsDbcSignal => _selected?.TargetCategory == "signal";
    public double PreferredEditorWidth => IsCanSend ? 970 : IsDbcSignal || _selected?.TargetCategory == "wave" ? 880 : 620;
    public double PreferredEditorHeight => IsCanSend ? 600 : IsDbcSignal || _selected?.TargetCategory == "wave" ? 620 : 500;
    private ComboBox ActiveTargetPicker => IsCanSend ? CanTargetPicker : TargetPicker;
    private uint _selectedMask;
    private readonly CheckBox[] _rawKeys = new CheckBox[20];
    private bool _updatingRawKeys;
    private bool _convertedLegacy;
    private string _legacySourceType = "";


     public ObservableCollection<MappingChoice> AvailableTargets { get; } = new();
    public ObservableCollection<TestStepInputField> ParameterFields { get; } = new();
    public ObservableCollection<PublicFlowChoice> AvailableFlowChoices { get; } = new();
    public ObservableCollection<TestSignalInputField> SignalValues { get; } = new();
     public StepCompositionResult? Result { get; private set; }
    public Func<StepCompositionResult, bool>? ApplyRequested { get; set; }
    public Func<string>? ApplyFailureReason { get; set; }
    public Action<string>? StepActionRequested { get; set; }
    public event Action? Cancelled;
    /// <summary>
    /// Raised only after the canonical TEST transaction was accepted and the
    /// Inspector dirty flag was cleared, not during the applying callback.
    /// </summary>
    public event Action? AppliedSuccessfully;
    public bool HasPendingEdits { get; private set; }
    private bool _trackEdits;

    public void FocusName()
    {
        NameInput.Focus();
        NameInput.SelectAll();
    }

    public void ShowPendingWarning() =>
        ErrorText.Text = "请完成当前字段；有效修改将自动保存，输入有误时会保留本窗口。";

    private void MarkDraftChanged()
    {
        if (!_trackEdits) return;
        HasPendingEdits = true;
        ErrorText.Text = "";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        HasPendingEdits = false;
        Cancelled?.Invoke();
    }

    public TestStepInspector(IReadOnlyList<MappingChoice> catalog,
        IReadOnlyList<TestTargetMapping> boundMappings, StepNode? editing = null,
        string? initialActionKey = null, bool caseOnly = false,
        IReadOnlyList<PublicFlowChoice>? publicFlows = null,
        IReadOnlyList<TestStepFieldChoice>? ifVariables = null)
    {
        _ifVariables = ifVariables ?? [];
         _catalog = catalog;
        _boundMappings = boundMappings;
        _editing = editing;
        InitializeComponent();
        DataContext = this;
        BuildRawKeyGrid();
        _ready = true;
        // A STEP Inspector is never the place to create a new node.
        // The only creation path is ActionPalette -> transaction.Insert -> StepId.
        if (editing is null)
            throw new ArgumentNullException(nameof(editing),
                "先从中央步骤列表选择操作类型，不能在 Inspector 内直接新增。");
        Heading.Text = "编辑步骤";
        StepEnabledInput.IsChecked = StepExecutionPolicy.IsEnabled(editing);
        StepActionsPanel.Visibility = Visibility.Collapsed;
        ConfirmButton.Content = "完成";
        var mapping = boundMappings.FirstOrDefault(m =>
            m.Name == editing.Properties.GetValueOrDefault("target", ""));
        var targetKind = mapping?.Kind;
        // An applied raw mapping remains raw when reopening its inspector.
        _explicitRawCan = mapping?.Kind == TestTargetKind.RawCan &&
            mapping.Name.StartsWith("RAW-ECU-CAN-", StringComparison.OrdinalIgnoreCase);
        _legacySourceType = editing.Type is "KeySet" or "CanSend" or "CanWait"
            ? editing.Type : "";
        _convertedLegacy = _legacySourceType.Length > 0;
        var chosen = _legacySourceType switch
        {
            "KeySet" => Options.First(x => x.Key == "key"),
            "CanSend" => Options.First(x => x.Key == "can"),
            "CanWait" => Options.First(x => x.Key == "assert-signal"),
            _ => null
        };
        chosen ??= Options.FirstOrDefault(a => a.Key == initialActionKey &&
            a.StepType == editing.Type) ??
            Options.FirstOrDefault(a => a.StepType == editing.Type &&
            (editing.Type != "Write" || a.TargetCategory switch
            {
                "button" => targetKind is TestTargetKind.Button or TestTargetKind.RawKey,
                "ign" => targetKind == TestTargetKind.Ignition,
                "can" => targetKind is TestTargetKind.CanMessage or TestTargetKind.RawCan,
                _ => false
            })) ?? Options.FirstOrDefault(a => a.StepType == editing.Type);
        if (chosen is null)
        {
            TypeLabel.Text = editing.Type == "KeySet"
                ? "旧版按键步骤（待转换）" : "暂不支持的步骤";
            ActionDescription.Visibility = Visibility.Visible;
            ActionDescription.Text = editing.Type == "KeySet"
                ? "这条旧按键动作没有接入真实按键执行流程。请使用“添加步骤 → 按键动作”重新选择 20 路电阻开关和动作方式；保留此旧步骤不会让它真正下发命令。"
                : "此旧步骤当前只能保留原始数据，尚不能参与真实测试。请换用已支持的正式操作步骤。";
            NameInput.Text = editing.Name;
            StepCode.Text = editing.Id.ToString()[^5..];
            ConfirmButton.IsEnabled = false;
            ErrorText.Text = "此步骤尚不支持编辑为可执行操作；原始数据已保留。";
        }
        else
        {
            _selected = chosen;
            TypeIcon.Text = chosen.StepType switch
            {
                "Write" or "CanSend" or "Read" or "CanWait" or "AssertSignal" => "⇄",
                "UdsRequest" or "Flash" => "◇",
                "WaveStart" or "WaveStop" or "WaveAnalyze" or "WaveCompare" => "〰",
                "If" or "Switch" or "Case" or "TryCatch" => "⑂",
                "Loop" => "⟳",
                "Wait" or "When" => "◷",
                _ => "▣"
            };
            TypeLabel.Text = _convertedLegacy
                ? chosen.Title + " · 转换为正式步骤" : chosen.Title;
            StepCode.Text = editing.Id.ToString()[^5..];
            ActionDescription.Text = chosen.Description;
            if (chosen.StepType == "If")
                ActionDescription.Text = "IF 只选择执行分支，不负责判定 ECU PASS/FAIL。变量必须由前面的读取、设置或计算步骤产生；数据无效时不进入 ELSE。";
            ActionDescription.Visibility = _convertedLegacy ||
                chosen.StepType is "If" or "CanSend" or "CanWait"
                ? Visibility.Visible : Visibility.Collapsed;
            TypeLabel.ToolTip = chosen.Description;
            NameInput.Text = editing.Name;
            BuildParameters(chosen);
            BuildTargets(chosen);
            BuildPublicFlowPicker(chosen, editing, publicFlows ?? []);
        }
        AddHandler(TextBox.TextChangedEvent,
            new TextChangedEventHandler((sender, e) =>
            {
                if (ReferenceEquals(e.OriginalSource, TargetSearch) ||
                    ReferenceEquals(e.OriginalSource, CanSignalSearch)) return;
                MarkDraftChanged();
                if (IsCanSend && !_loadingCanData && CanDataCard is not null)
                    UpdateCanFramePreview();
            }), true);
        AddHandler(Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler((sender, e) =>
            {
                MarkDraftChanged();
                if (IsCanSend && !_loadingCanData && CanDataCard is not null)
                    UpdateCanFramePreview();
            }), true);
        AddHandler(ToggleButton.CheckedEvent,
            new RoutedEventHandler((sender, e) => MarkDraftChanged()), true);
        AddHandler(ToggleButton.UncheckedEvent,
            new RoutedEventHandler((sender, e) => MarkDraftChanged()), true);
        Loaded += (_, _) =>
        {
            _trackEdits = true;
        };
    }

    private void BuildParameters(TestActionOption action)
    {
        ParameterFields.Clear();
        var node = _editing ?? throw new InvalidOperationException("步骤编辑器需要真实 StepId。");
        // Ordinary TEST steps are judged by their formal parameters and resource
        // mappings, not by metadata recording their source. Only genuinely
        // unsupported StepNode types need an explicit conversion path.
        var convertingUnsupportedType = _legacySourceType.Length > 0;
        ImportWarning.Visibility = convertingUnsupportedType
            ? Visibility.Visible : Visibility.Collapsed;
        if (convertingUnsupportedType)
            ImportWarning.Text = _legacySourceType switch
            {
                "CanWait" => "CanWait 当前不能直接运行。请确认 DBC 信号、期望值和超时；应用后转换为正式 CAN 信号判定。",
                "KeySet" => "KeySet 当前不能直接运行。请确认 20 路 KEY 开关和保持时间；应用后转换为正式 KEY_SIM 操作。",
                "CanSend" => "CanSend 当前不能直接运行。请确认 CAN ID、数据与周期；应用后转换为正式 CAN 发送。",
                _ => "当前步骤类型无法执行，请转换为正式操作步骤。"
            };
        foreach (var item in node.Properties.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (action.StepType == "CallPublicFlow" &&
                item.Key is "flowAssetId" or "flowRevision" or "flowSha") continue;
            if (!StepUiCatalog.IsOperatorProperty(item.Key) ||
                (item.Key == "target" && !string.IsNullOrEmpty(action.TargetCategory)) ||
                (item.Key == "value" && action.StepType == "Write") ||
                (item.Key == "profile" && (action.StepType == "WaveAnalyze" ||
                    action.StepType == "WaveCompare"))) continue;
            var label = item.Key == "target" && action.StepType == "CallSubroutine"
                ? "要调用的子程序" : null;
            ParameterFields.Add(new TestStepInputField(item.Key, item.Value, label, action.StepType));
        }
        if (_convertedLegacy && action.StepType == "AssertSignal")
        {
            string expected = node.Properties.GetValueOrDefault("expectation", "").Trim();
            var operators = new[] { ">=", "<=", "!=", "==", ">", "<", "=" };
            var selected = operators.FirstOrDefault(expected.StartsWith);
            var compare = selected is null ? "==" : selected == "=" ? "==" : selected;
            if (selected is not null) expected = expected[selected.Length..].Trim();
            if (expected.Length == 0) expected = "";
            var timeout = node.Properties.GetValueOrDefault("timeoutMs",
                node.Properties.GetValueOrDefault("durationMs",
                    node.Properties.GetValueOrDefault("conditionTimeoutMs", "5000")));
            ParameterFields.Clear();
            ParameterFields.Add(new TestStepInputField("operator", compare, stepType: "AssertSignal"));
            ParameterFields.Add(new TestStepInputField("expected", expected, stepType: "AssertSignal"));
            ParameterFields.Add(new TestStepInputField("timeoutMs", timeout, stepType: "AssertSignal"));
        }
        else
        {
            if (_legacySourceType is "KeySet" or "CanSend")
                ParameterFields.Clear();
            // Imported steps often lack the formally required parameters.
            // Render them so the engineer can correct the TEST resource, not a raw JSON field.
            var descriptor = StepDescriptorCatalog.Builtins().Lookup(action.StepType);
            if (descriptor is not null)
                foreach (var key in descriptor.RequiredProperties)
                {
                    if (action.StepType == "CallPublicFlow" &&
                        key is "flowAssetId" or "flowRevision" or "flowSha") continue;
                    if (key == "target" && !string.IsNullOrEmpty(action.TargetCategory)) continue;
                    if (key == "value" && action.StepType == "Write") continue;
                    if (ParameterFields.Any(f => f.Key == key)) continue;
                    var defaultValue = StepUiCatalog.NewNode(action.StepType)
                        .Properties.GetValueOrDefault(key, "");
                    ParameterFields.Add(new TestStepInputField(key, defaultValue,
                        stepType: action.StepType));
                }
            // CAN sending has a dedicated, task-oriented editor. Keep the
            // canonical periodMs/durationMs properties but never expose them
            // as opaque generic fields in the narrow inspector.
            if (action.Key == "can")
                ParameterFields.Clear();
        }
        if (action.StepType == "If")
        {
            // A conditional has ONE recognizable left-to-right expression.
            // Display only variables proven available before this StepId;
            // retain an invalid saved value visibly so it can be corrected.
            var variable = node.Properties.GetValueOrDefault("variable", "");
            var choices = _ifVariables.ToList();
            if (!string.IsNullOrWhiteSpace(variable) &&
                !choices.Any(x => string.Equals(x.Value, variable,
                    StringComparison.OrdinalIgnoreCase)))
                choices.Add(new TestStepFieldChoice(variable,
                    variable + "（未找到可保证的上游来源，运行前将阻断）"));
            ParameterFields.Clear();
            ParameterFields.Add(new TestStepInputField("variable", variable,
                "条件变量（由前面步骤产生）", "If", choices));
            ParameterFields.Add(new TestStepInputField("operator",
                node.Properties.GetValueOrDefault("operator", "=="),
                "比较方式", "If"));
            ParameterFields.Add(new TestStepInputField("equals",
                node.Properties.GetValueOrDefault("equals", ""),
                "比较值（常量或 $其他变量）", "If"));
        }
        else if (action.StepType == "Switch")
        {
            var variable = node.Properties.GetValueOrDefault("variable", "");
            var choices = _ifVariables.ToList();
            if (!string.IsNullOrWhiteSpace(variable) &&
                !choices.Any(x => string.Equals(x.Value, variable,
                    StringComparison.OrdinalIgnoreCase)))
                choices.Add(new TestStepFieldChoice(variable,
                    variable + "（目前没有可保证的上游来源，运行前阻断）"));
            ParameterFields.Clear();
            ParameterFields.Add(new TestStepInputField("variable", variable,
                "条件来源（必须是前面真实产生的变量）", "Switch", choices));
        }
        else if (action.StepType == "Case")
        {
            ParameterFields.Clear();
            ParameterFields.Add(new TestStepInputField("equals",
                node.Properties.GetValueOrDefault("equals", ""),
                "匹配值（填写 default 即默认分支，其他值大小写不敏感）",
                "Case"));
        }
    }


    private static bool TryLegacyMask(StepNode old, out uint mask)
    {
        foreach (var key in new[] { "outputMask", "keyMask", "mask", "legacy.outputMask", "legacy.keyMask" })
        {
            var text = old.Properties.GetValueOrDefault(key, "").Trim();
            if (text.Length == 0) continue;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask))
                return mask <= 0xFFFFF;
            if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out mask))
                return mask <= 0xFFFFF;
        }
        mask = 0;
        return false;
    }

    private static bool TryLegacyCanId(StepNode old, out uint id)
    {
        var keys = new[] { "canId", "frameId", "id", "legacy.canId", "messageRef" };
        foreach (var key in keys)
        {
            var text = old.Properties.GetValueOrDefault(key, "").Trim();
            if (text.Length == 0) continue;
            var match = System.Text.RegularExpressions.Regex.Match(text,
                @"0[xX][0-9a-fA-F]+");
            if (match.Success &&
                uint.TryParse(match.Value[2..], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out id))
                return id <= 0x1FFFFFFF;
            if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                return id <= 0x1FFFFFFF;
        }
        id = 0;
        return false;
    }

    private void BuildPublicFlowPicker(TestActionOption action, StepNode node,
        IReadOnlyList<PublicFlowChoice> publicFlows)
    {
        PublicFlowPanel.Visibility = action.StepType == "CallPublicFlow"
            ? Visibility.Visible : Visibility.Collapsed;
        AvailableFlowChoices.Clear();
        if (action.StepType != "CallPublicFlow") return;
        foreach (var flow in publicFlows.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            AvailableFlowChoices.Add(flow);
        var idText = node.Properties.GetValueOrDefault("flowAssetId", "");
        var revisionText = node.Properties.GetValueOrDefault("flowRevision", "");
        var sha = node.Properties.GetValueOrDefault("flowSha", "");
        PublicFlowChoice? pinned = null;
        if (Guid.TryParse(idText, out var guid) && int.TryParse(revisionText, out var revision))
            pinned = AvailableFlowChoices.FirstOrDefault(f => f.AssetId == guid &&
                f.Revision == revision && string.Equals(f.Sha256, sha,
                    StringComparison.OrdinalIgnoreCase));
        PublicFlowPicker.SelectedItem = pinned;
        PublicFlowReferenceStatus.Text = pinned is not null || idText.Length == 0
            ? "固定当前选中的公共流程修订；后续更新需显式重新选择。"
            : "STALE_REFERENCE：原流程引用未匹配当前修订，不能自动替换。";
    }

    private void BuildTargets(TestActionOption action)
    {
        _allTargets.Clear();
        AvailableTargets.Clear();
        _selectedTarget = null;
        _targetRevisionWarning = "";
        TargetSearch.Text = "";
        CanTargetSearch.Text = "";
        CanSendPanel.Visibility = IsCanSend ? Visibility.Visible : Visibility.Collapsed;
        var split = IsDbcSignal || action.TargetCategory == "wave";
        ObjectParameterLeft.Width = new GridLength(split ? 0.48 : 1, GridUnitType.Star);
        ObjectParameterGap.Width = new GridLength(split ? 18 : 0);
        ObjectParameterRight.Width = new GridLength(split ? 0.52 : 0, GridUnitType.Star);
        Grid.SetColumn(ParameterList, split ? 2 : 0);
        Grid.SetRow(ParameterList, split ? 0 : 1);
        TargetPanel.Visibility = !IsCanSend && !string.IsNullOrEmpty(action.TargetCategory)
            ? Visibility.Visible : Visibility.Collapsed;
        DbcSignalPickerPanel.Visibility = IsDbcSignal ? Visibility.Visible : Visibility.Collapsed;
        LegacyTargetPanel.Visibility = IsDbcSignal ? Visibility.Collapsed : Visibility.Visible;
        DbcCanPanel.Visibility = KeyPanel.Visibility =
            IgnitionPanel.Visibility = Visibility.Collapsed;
        if (IsCanSend) ConfigureCanSending();
        if (TargetPanel.Visibility == Visibility.Collapsed && !IsCanSend) return;

        bool Eligible(TestTargetMapping mapping) => action.TargetCategory switch
        {
            "can" => mapping.Kind is TestTargetKind.CanMessage or TestTargetKind.RawCan,
            // Normal authoring uses the direct 20-Key action; retain legacy
            // Button target only while editing that exact existing reference.
            "button" => mapping.Kind == TestTargetKind.RawKey ||
                mapping.Kind == TestTargetKind.Button && _editing is { } oldStep &&
                string.Equals(oldStep.Properties.GetValueOrDefault("target", ""), mapping.Name,
                    StringComparison.OrdinalIgnoreCase),
            "ign" => mapping.Kind == TestTargetKind.Ignition,
            "signal" => mapping.Kind == TestTargetKind.Signal,
            "wave" => mapping.Kind == TestTargetKind.WaveProfile,
            _ => false
        };
        foreach (var item in _catalog.Where(x => Eligible(x.Mapping))) _allTargets.Add(item);
        if (action.TargetCategory == "ign")
            _allTargets.Add(new MappingChoice("台架 / IGN",
                new TestTargetMapping("IGN", TestTargetKind.Ignition,
                    default, 0, "")));
        if (action.TargetCategory == "can")
            _allTargets.Add(new MappingChoice("CAN 字节直编 · DBC 辅助可选",
                new TestTargetMapping("原始 ECU CAN", TestTargetKind.RawCan, default, 0, "",
                    BusRole.DutCan, 0)));
        else if (action.TargetCategory == "button")
            _allTargets.Add(new MappingChoice("KEY CAN · 20 Key 组合",
                new TestTargetMapping("原始 KEY_SIM 20 Key", TestTargetKind.RawKey,
                    default, 0, "", BusRole.KeypadCan)));

        foreach (var mapping in _boundMappings.Where(Eligible))
            if (!_allTargets.Any(x => x.Mapping == mapping))
                _allTargets.Add(new MappingChoice(mapping.Kind switch
                {
                    TestTargetKind.RawKey => "20 Key 按键组合",
                    TestTargetKind.Ignition => "IGN 电源",
                    TestTargetKind.RawCan => "原始 CAN 报文 · 0x" + mapping.CanId.ToString("X"),
                    TestTargetKind.Signal => "当前信号 · " + mapping.SignalName,
                    TestTargetKind.CanMessage => "当前报文 · 0x" + mapping.CanId.ToString("X"),
                    TestTargetKind.Button => "当前按键动作 · " + mapping.Name,
                    _ => mapping.Name
                }, mapping));
        var oldTargetName = _editing?.Properties.GetValueOrDefault(
            action.TargetCategory == "wave" ? "profile" : "target", "");
        // A pinned mapping must be preferred over a catalog object with the same
        // display name but a newer asset revision.
        var originallyBound = _boundMappings.FirstOrDefault(x =>
            string.Equals(x.Name, oldTargetName, StringComparison.OrdinalIgnoreCase));
        // Keep the exact pinned revision while authoring. A new DBC revision
        // is never adopted merely because the CAN ID matches: bit layout,
        // scaling and enum values may have changed. The operator must choose
        // a new result or use the explicit rebind action.
        ApplyTargetFilter("", originallyBound, oldTargetName);
        if (originallyBound is not null &&
            !_catalog.Any(x => x.Mapping == originallyBound) &&
            originallyBound.Kind is not (TestTargetKind.RawCan or TestTargetKind.RawKey or
                TestTargetKind.Ignition))
            _targetRevisionWarning = "请核对当前工程中的报文、信号或波形资源。";
        if (!IsCanSend)
        {
            TargetHint.Text = _targetRevisionWarning.Trim();
            TargetHint.Visibility = TargetHint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        // No pointless resource picker for the single built-in IGN or direct
        // 20-Key action. Hardware/DBC-backed choices retain their picker.
        if (_allTargets.Count > 0 &&
            _allTargets.All(x => x.Mapping.Kind == TestTargetKind.RawKey) ||
            _allTargets.Count > 0 &&
            _allTargets.All(x => x.Mapping.Kind == TestTargetKind.Ignition))
            TargetPanel.Visibility = Visibility.Collapsed;
    }

    private void TargetSearch_Changed(object sender, TextChangedEventArgs e)
    {
        // DBC pickers search their own popup results and never mutate the draft
        // just because a query changed. This legacy path remains for KEY/IGN/wave.
        if (!_ready || _filteringTargets || _selected is null ||
            IsCanSend || IsDbcSignal) return;
        ApplyTargetFilter(TargetSearch.Text.Trim(), _selectedTarget?.Mapping);
    }

    private void DbcReference_ChoiceCommitted(object? sender, DbcReferenceChosenEventArgs e)
    {
        if (!_ready || _filteringTargets || (!IsCanSend && !IsDbcSignal)) return;
        var match = AvailableTargets.FirstOrDefault(x => x.Mapping == e.Choice.Mapping);
        if (match is null)
        {
            ErrorText.Text = "所选 DBC 已不在当前工程资源列表；请刷新工程后重新选择。";
            RefreshDbcPickers();
            return;
        }
        if (IsCanSend && match.Mapping.Kind == TestTargetKind.RawCan)
        {
            ClearCanDbcAssistance();
            return;
        }
        if (_selectedTarget?.Mapping == match.Mapping) return;
        _filteringTargets = true;
        try { ActiveTargetPicker.SelectedItem = match; }
        finally { _filteringTargets = false; }
        UpdateTarget();
        MarkDraftChanged();
    }

    private void RefreshDbcPickers()
    {
        if (IsCanSend)
            CanDbcPicker.SetChoices(_allTargets.Where(x => x.Mapping.Kind == TestTargetKind.CanMessage),
                _selectedTarget?.Mapping.Kind == TestTargetKind.CanMessage ? _selectedTarget : null);
        else if (IsDbcSignal)
            SignalDbcPicker.SetChoices(_allTargets, _selectedTarget);
    }

    private void ApplyTargetFilter(string query, TestTargetMapping? preferredMapping,
        string? preferredName = null)
    {
        var matches = _allTargets.Where(x =>
            x.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            x.Mapping.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        // Keep the currently edited CAN message as a pinned choice while
        // filtering. Typing a search must not destroy unsaved signal values.
        if (IsCanSend && _selectedTarget is { } pinned &&
            !matches.Any(x => x.Mapping == pinned.Mapping))
            matches.Insert(0, pinned);
        _filteringTargets = true;
        try
        {
            AvailableTargets.Clear();
            foreach (var item in matches) AvailableTargets.Add(item);
        }
        finally { _filteringTargets = false; }
        var preferred = preferredMapping is null ? null :
            AvailableTargets.FirstOrDefault(x => x.Mapping == preferredMapping);
        preferred ??= AvailableTargets.FirstOrDefault(x => x.Mapping.Name == preferredName);
        if (preferred is null && IsCanSend && string.IsNullOrWhiteSpace(preferredName))
            preferred = AvailableTargets.FirstOrDefault(x =>
                x.Mapping.Kind == TestTargetKind.RawCan);
        // For a new KEY step, prefer direct 20-switch control; legacy
        // asset-backed button mappings remain available to existing files.
        if (preferred is null && _selected?.TargetCategory == "button" &&
            string.IsNullOrWhiteSpace(preferredName) && query.Length == 0)
            preferred = AvailableTargets.FirstOrDefault(x =>
                x.Mapping.Kind == TestTargetKind.RawKey);
        // Avoid silently binding the first DBC signal/message when a new
        // STEP has not explicitly chosen its operation object.
        // A unique IGN / raw-only option may be preselected for convenience.
        if (_convertedLegacy && _editing is { } imported)
        {
            var rawId = imported.Properties.GetValueOrDefault("canId", "").Trim();
            if (rawId.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) rawId = rawId[2..];
            if (uint.TryParse(rawId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            {
                var legacySignalMatches = AvailableTargets.Where(x =>
                    x.Mapping.Kind == TestTargetKind.Signal && x.Mapping.CanId == id).ToArray();
                preferred = legacySignalMatches.Length == 1 ? legacySignalMatches[0] : null;
            }
            else preferred = null;
        }
        if (_legacySourceType == "KeySet")
            preferred = AvailableTargets.FirstOrDefault(x => x.Mapping.Kind == TestTargetKind.RawKey);
        else if (_legacySourceType == "CanSend" && _editing is { } oldCan)
        {
            var refName = oldCan.Properties.GetValueOrDefault("messageRef", "");
            if (TryLegacyCanId(oldCan, out var frame))
            {
                var found = AvailableTargets.Where(x =>
                    x.Mapping.Kind == TestTargetKind.CanMessage && x.Mapping.CanId == frame).ToArray();
                if (found.Length == 1) preferred = found[0];
                else preferred = AvailableTargets.FirstOrDefault(x =>
                    x.Mapping.Kind == TestTargetKind.RawCan);
            }
            else if (refName.Length > 0)
                preferred = AvailableTargets.FirstOrDefault(x =>
                    x.Mapping.Kind == TestTargetKind.CanMessage &&
                    (string.Equals(x.Message?.Name, refName, StringComparison.OrdinalIgnoreCase) ||
                     x.Label.Contains(refName, StringComparison.OrdinalIgnoreCase)));
        }
        else if (_convertedLegacy && _selected?.StepType == "AssertSignal")
        {
            // Prefer exact old signal identity when a frame contains several signals.
            if (_editing is { } old && TryLegacyCanId(old, out var frameId))
            {
                var signalName = old.Properties.GetValueOrDefault("signalName",
                    old.Properties.GetValueOrDefault("databaseSignalName", ""));
                var legacyAssertionMatches = AvailableTargets.Where(x =>
                    x.Mapping.Kind == TestTargetKind.Signal && x.Mapping.CanId == frameId &&
                    (signalName.Length == 0 ||
                     string.Equals(x.Mapping.SignalName, signalName,
                         StringComparison.OrdinalIgnoreCase))).ToArray();
                preferred = legacyAssertionMatches.Length == 1 ? legacyAssertionMatches[0] : null;
            }
        }
        _filteringTargets = true;
        try
        {
            ActiveTargetPicker.SelectedItem = preferred ??
                (_convertedLegacy ? null : AvailableTargets.Count == 1 ? AvailableTargets[0] : null);
        }
        finally { _filteringTargets = false; }
        UpdateTarget();
        RefreshDbcPickers();
        if (!IsCanSend)
        {
            TargetHint.Text = AvailableTargets.Count == 0
                ? "没有可用对象：请检查搜索内容或导入 DBC。"
                : _targetRevisionWarning.Trim();
            TargetHint.Visibility = TargetHint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void TargetPicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _filteringTargets) return;
        UpdateTarget();
        RefreshDbcPickers();
    }

    private void UpdateTarget()
    {
        var selected = ActiveTargetPicker.SelectedItem as MappingChoice;
        if (IsCanSend && selected is not null && _selectedTarget is { } prior &&
            selected.Mapping == prior.Mapping &&
            (selected.Message is null) == (prior.Message is null))
            return;
        var previousTarget = _selectedTarget;
        var enteredBytes = IsCanSend ? RawCanDataInput.Text : "";
        var enteredId = IsCanSend ? FrameIdInput.Text : "";
        var enteredExtended = IsCanSend && GetCanFrameExtended();
        _selectedTarget = selected;
        RefreshTargetDetail(selected);
        if (IsCanSend) CanSignalSearch.Clear();
        DbcCanPanel.Visibility = KeyPanel.Visibility =
            IgnitionPanel.Visibility = Visibility.Collapsed;
        SignalValues.Clear();
        if (IsCanSend)
        {
            _canSignalError = "";
            CanRebindButton.Visibility = Visibility.Collapsed;
            CanTargetHint.Text = "CAN ID 与 DATA 始终可以直接编辑；输入 ID 后自动寻找匹配的 DBC 报文。";
            CanPreviewIssue.Text = "";
            CanFramePreview.Text = "填写 CAN ID 和 DATA";
            _loadingCanData = true;
            try
            {
                FrameIdInput.IsReadOnly = false;
                FrameIdInput.Text = "";
                RawCanDataInput.Text = "";
                CanFrameFormatLabel.Text = "等待 CAN ID";
                CanDlcInput.Text = "8";
            }
            finally { _loadingCanData = false; }
        }
        if (selected is null) return;
        var mapping = selected.Mapping;
        if (IsCanSend && mapping.Kind is (TestTargetKind.CanMessage or TestTargetKind.RawCan))
        {
            var definition = selected.Message;
            var isDbc = mapping.Kind == TestTargetKind.CanMessage;
            if (isDbc) { _explicitRawCan = false; _rawFormatId = null; }
            var savedTarget = _editing?.Properties.GetValueOrDefault("target", "") ?? "";
            var sameSaved = string.Equals(mapping.Name, savedTarget, StringComparison.OrdinalIgnoreCase);
            var stored = sameSaved
                ? _editing?.Properties.GetValueOrDefault("value", "") ?? "" : "";
            if (_legacySourceType == "CanSend" && sameSaved && _editing is { } legacy)
                stored = legacy.Properties.GetValueOrDefault("dataHex", stored);
            var bytesText = "";
            var warning = "";

            if (previousTarget is not null &&
                mapping.Kind == TestTargetKind.RawCan)
            {
                // Explicitly removing the DBC must not throw away the actual bytes.
                bytesText = enteredBytes;
            }
            else if (previousTarget is { } compatible &&
                definition is not null)
            {
                // A raw CAN ID entered by the engineer may resolve to a DBC
                // definition on focus loss. Keep exactly those existing bytes
                // if their ID, frame format and DLC match; do not reset to 00.
                var previousIdText = enteredId.Trim();
                if (previousIdText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    previousIdText = previousIdText[2..];
                var sameFrame = compatible.Mapping.Kind == TestTargetKind.RawCan
                    ? uint.TryParse(previousIdText, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var rawId) &&
                        rawId == mapping.CanId
                    : compatible.Mapping.CanId == mapping.CanId &&
                        compatible.Mapping.Extended == mapping.Extended;
                if (sameFrame)
                {
                    try
                    {
                        var preserved = ParseHex(enteredBytes, 8);
                        if (preserved.Length == definition.Dlc)
                            bytesText = FormatCanBytes(preserved);
                    }
                    catch (ArgumentException) { /* Incomplete bytes still need explicit review. */ }
                }
            }
            if (bytesText.Length == 0 && stored.Length > 0)
            {
                try
                {
                    byte[] loaded;
                    if (stored.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
                        loaded = ParseHex(stored[4..], 8);
                    else if (stored.Contains('=') && definition is not null)
                        loaded = DbcCanMessageEncoder.Encode(definition, stored);
                    else if (stored.All(ch => char.IsAsciiHexDigit(ch) ||
                                                 char.IsWhiteSpace(ch) || ch == '-'))
                        loaded = ParseHex(stored.Replace("-", ""), 8);
                    else throw new ArgumentException("不是有效的 HEX 或 DBC 信号赋值。");
                    if (definition is not null && loaded.Length != definition.Dlc)
                        throw new ArgumentException("字节长度与当前 DBC 不一致。");
                    bytesText = FormatCanBytes(loaded);
                }
                catch (Exception error) when (error is ArgumentException or
                    FormatException or OverflowException or
                    AutoTest.Projects.Project.ProjectDataException)
                {
                    warning = "旧 DATA 未能解析，已按当前报文长度填充 00，请核对后应用。";
                }
            }
            // A CAN frame is immediately editable: new frames default to
            // 8 zero bytes; a DBC frame defaults to exactly its declared DLC.
            // Never require the operator to populate every byte manually.
            if (bytesText.Length == 0)
                bytesText = FormatCanBytes(new byte[definition?.Dlc ?? 8]);
            _loadingCanData = true;
            try
            {
                FrameIdInput.Text = mapping.Kind == TestTargetKind.RawCan && !sameSaved
                    ? previousTarget is not null ? enteredId : ""
                    : "0x" + mapping.CanId.ToString("X");
                FrameIdInput.IsReadOnly = isDbc;
                RawCanDataInput.Text = bytesText;
                CanDlcInput.Text = (definition?.Dlc ?? 8)
                    .ToString(CultureInfo.InvariantCulture);
                UpdateCanFrameFormatLabel();
            }
            finally { _loadingCanData = false; }
            if (isDbc)
            {
                if (definition is null)
                {
                    CanTargetHint.Text = "原 DBC 版本失效；重新关联有效定义，或主动选择无 DBC 编辑。";
                    var candidates = CurrentCanRebindCandidates();
                    CanRebindButton.Visibility = candidates.Length == 1 ?
                        Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    DbcCanPanel.Visibility = Visibility.Visible;
                    DbcMessageInfo.Text = (selected.DisplayNames?.MessageLabel(definition) ??
                        definition.Name) + " · 0x" + definition.ArbitrationId.ToString("X") +
                        " · DLC " + definition.Dlc;
                    var safeToEdit = !HasOverlappingCanSignals(definition) &&
                        !definition.Signals.Any(sig => !string.IsNullOrEmpty(sig.Multiplexer));
                    CanTargetHint.Text = safeToEdit
                        ? "已关联 " + (selected.DisplayNames?.MessageLabel(definition) ??
                            definition.Name) + " · 修改信号直接更新 DATA"
                        : "DBC 含复用或重叠信号，仅解析查看；DATA 仍可直接编辑。";
                    foreach (var signal in definition.Signals)
                    {
                        var alias = selected.DisplayNames?.SignalAlias(definition.Id, signal.Name);
                        var labels = selected.MessageValueLabels is not null &&
                            selected.MessageValueLabels.TryGetValue(signal.Name, out var valueLabels)
                            ? valueLabels : null;
                        var field = new TestSignalInputField(signal, "",
                            string.IsNullOrWhiteSpace(alias) ? signal.Name : alias,
                            selected.DisplayNames?.SignalDescription(definition.Id, signal.Name),
                            labels, selected.DisplayNames?.SignalSummary(definition.Id, signal.Name),
                            safeToEdit);
                        field.PropertyChanged += (_, e) =>
                        {
                            if (e.PropertyName == nameof(TestSignalInputField.Value) &&
                                !_loadingCanData) PatchCanSignal(field);
                        };
                        SignalValues.Add(field);
                    }
                    SynchronizeCanSignals();
                }
            }
            else CanTargetHint.Text = "DATA 可直接编辑；需要 DBC 辅助时，选择对应报文。";
            UpdateCanFramePreview();
            if (warning.Length > 0) CanPreviewIssue.Text = warning;
            return;
        }
        // Non-CAN editors use their existing inputs and semantics.
        else if (mapping.Kind is TestTargetKind.Button or TestTargetKind.RawKey)
        {
            KeyPanel.Visibility = Visibility.Visible;
            RawKeyMaskPanel.Visibility = mapping.Kind == TestTargetKind.RawKey
                ? Visibility.Visible : Visibility.Collapsed;
            var old = _editing?.Properties.GetValueOrDefault("value", "") ?? "";
            var parts = old.Split(':', StringSplitOptions.TrimEntries);
            var operation = _legacySourceType == "KeySet" ? "Hold" :
                parts[0] is "Hold" or "Release" or "Pulse" ? parts[0] :
                mapping.Kind == TestTargetKind.RawKey ? "Hold" : "Pulse";
            KeyActionInput.SelectedItem = KeyActionInput.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(x => (string?)x.Tag == operation);
            var selectedMask = mapping.Kind == TestTargetKind.RawKey && parts.Length > 1 &&
                parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(parts[1][2..], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var mask) ? mask : 0;
            if (_legacySourceType == "KeySet" && _editing is { } oldKey &&
                TryLegacyMask(oldKey, out var legacyMask))
                selectedMask = legacyMask;
            SetRawKeyMask(selectedMask);
            KeyDurationInput.Text = _legacySourceType == "KeySet" && _editing is { } oldDuration
                ? oldDuration.Properties.GetValueOrDefault("holdMs",
                    oldDuration.Properties.GetValueOrDefault("durationMs", ""))
                : operation == "Wait" && mapping.Kind == TestTargetKind.RawKey &&
                parts.Length == 2 ? parts[1] :
                operation is "Pulse" or "Hold" && mapping.Kind == TestTargetKind.RawKey &&
                parts.Length == 3 ? parts[2] :
                operation == "Pulse" && mapping.Kind == TestTargetKind.Button &&
                parts.Length == 2 ? parts[1] : "1000";
            UpdateKeyTimingVisibility();
        }
        else if (mapping.Kind == TestTargetKind.Signal &&
            _selected?.StepType == "AssertSignal" && _selectedTarget?.ValueLabels is { Count: > 0 } labels)
        {
            var index = Array.FindIndex(ParameterFields.ToArray(), x => x.Key == "expected");
            if (index >= 0)
            {
                var current = ParameterFields[index].Value.Trim();
                if (!double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out _) &&
                    !current.StartsWith("$", StringComparison.Ordinal))
                {
                    var matching = labels.Where(x =>
                        string.Equals(x.Value.Trim(), current, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matching.Length == 1)
                        current = matching[0].Key.ToString(CultureInfo.InvariantCulture);
                }
                var choices = labels.OrderBy(x => x.Key).Select(x =>
                    new TestStepFieldChoice(x.Key.ToString(CultureInfo.InvariantCulture),
                        x.Value + "（" + x.Key + "）")).ToArray();
                ParameterFields[index] = new TestStepInputField("expected", current, "期望信号状态",
                    "AssertSignal", choices);
            }
        }
        else if (mapping.Kind == TestTargetKind.Ignition)
        {
            IgnitionPanel.Visibility = Visibility.Visible;
            var old = _editing?.Properties.GetValueOrDefault("value", "ON") ?? "ON";
            IgnitionState.SelectedIndex = old.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
    }

    private int _targetPreviewVersion;

    private void RefreshTargetDetail(MappingChoice? choice)
    {
        var request = ++_targetPreviewVersion;
        TargetDetailCard.Visibility = Visibility.Collapsed;
        WaveReferenceView.Visibility = Visibility.Collapsed;
        WaveReferenceCanvas.Children.Clear();
        WaveReferenceStatus.Text = "";
        if (choice is null) return;
        if (choice.Mapping.Kind == TestTargetKind.Signal)
        {
            TargetDetailCard.Visibility = Visibility.Visible;
            var signal = choice.Message?.Signals.FirstOrDefault(s => s.Name == choice.Mapping.SignalName);
            TargetDetail.Text = "CAN ID  0x" + choice.Mapping.CanId.ToString("X") +
                (choice.Mapping.Extended ? " · 扩展帧" : " · 标准帧") +
                "\n信号  " + choice.Mapping.SignalName +
                "\nDBC 修订  " + choice.Mapping.AssetRevision +
                (signal is null ? "\n原始信号定义未找到，请重新选择有效资源。" :
                    "\n物理范围  " + signal.Minimum.ToString("G", CultureInfo.InvariantCulture) +
                    " ～ " + signal.Maximum.ToString("G", CultureInfo.InvariantCulture) + " " + signal.Unit +
                    "\n位长  " + signal.BitLength + (signal.Intel ? " · Intel" : " · Motorola"));
            return;
        }
        if (choice.Mapping.Kind != TestTargetKind.WaveProfile) return;
        TargetDetailCard.Visibility = Visibility.Visible;
        if (choice.WaveSettings is not { } settings || choice.WaveProjectRoot is null)
        {
            TargetDetail.Text = "波形配置版本未能解析。请重新选择有效的工程资源。";
            return;
        }
        var cfg = settings.Comparison;
        TargetDetail.Text = "信号  " + settings.SignalName + " " + settings.Unit +
            " · 0x" + settings.CanId.ToString("X") +
            "\n参考  " + (string.IsNullOrWhiteSpace(settings.ReferenceName) ? "未指定" : settings.ReferenceName) +
            "\n绝对容差  " + cfg.AbsoluteTolerance.ToString("G") + " " + settings.Unit +
            "   相对容差  " + cfg.RelativeTolerance.ToString("P1") +
            "\n覆盖率 ≥ " + cfg.MinimumCoverage.ToString("P1") +
            "   通过比例 ≥ " + cfg.MinimumPassRatio.ToString("P1") +
            "\n最大时间偏移  " + (cfg.MaximumShiftSeconds * 1000).ToString("G") + " ms" +
            "\n最大连续越界  " + (cfg.MaximumOutOfToleranceSeconds * 1000).ToString("G") + " ms" +
            "\n配置修订  " + choice.Mapping.AssetRevision +
            "\n判定参数由所引用的工程波形配置维护；此处仅核对。";
        if (string.IsNullOrWhiteSpace(settings.ReferenceName) || !settings.DbcAssetId.HasValue)
        {
            WaveReferenceStatus.Text = "尚无已配置参考波形。";
            return;
        }
        WaveReferenceStatus.Text = "正在读取真实参考波形…";
        _ = LoadWavePreviewAsync(choice, request);
    }

    private async Task LoadWavePreviewAsync(MappingChoice choice, int request)
    {
        try
        {
            var settings = choice.WaveSettings!;
            var points = await Task.Run(() =>
            {
                var catalog = new WaveReferenceCatalog(System.IO.Path.Combine(choice.WaveProjectRoot!,
                    "wave-records", "references"));
                var matches = catalog.List().Where(x =>
                    x.Name == settings.ReferenceName &&
                    x.Signal.DbcAsset.Id.Value == settings.DbcAssetId &&
                    x.Signal.DbcAsset.Revision.Value == settings.DbcAssetRevision &&
                    string.Equals(x.Signal.DbcAsset.Fingerprint.Sha256, settings.DbcAssetFingerprint,
                        StringComparison.OrdinalIgnoreCase) && x.Signal.Role == settings.Bus &&
                    x.Signal.FrameId == settings.CanId && x.Signal.SignalName == settings.SignalName &&
                    x.Signal.Unit == settings.Unit).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException("参考名称与当前信号修订没有唯一匹配。");
                var reference = catalog.Load(matches[0].Reference);
                var data = WaveChartAnalysis.FromSeries(reference.Series);
                if (data.Length < 2 || data[^1].Seconds <= data[0].Seconds)
                    throw new InvalidDataException("参考波形没有足够的有效时间序列。");
                return WaveChartAnalysis.InWindow(data, data[0].Seconds, data[^1].Seconds, 400);
            });
            if (request != _targetPreviewVersion || !ReferenceEquals(choice, _selectedTarget)) return;
            var t0 = points[0].Seconds;
            var t1 = points[^1].Seconds;
            var ymin = points.Min(p => p.Value);
            var ymax = points.Max(p => p.Value);
            var span = ymax - ymin;
            if (span <= 0) span = Math.Max(1, Math.Abs(ymin) * 0.02);
            double X(double t) => 43 + 305 * (t - t0) / (t1 - t0);
            double Y(double v) => 147 - 124 * (v - ymin) / span;
            void Label(string label, double x, double y)
            {
                var text = new TextBlock { Text = label, FontSize = 10, Foreground = Brushes.SlateGray };
                Canvas.SetLeft(text, x); Canvas.SetTop(text, y);
                WaveReferenceCanvas.Children.Add(text);
            }
            void Line(double x1, double y1, double x2, double y2)
            {
                WaveReferenceCanvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2,
                    Y2 = y2, Stroke = Brushes.LightSlateGray, StrokeThickness = 0.5 });
            }
            for (var i = 0; i <= 4; i++)
                Line(43, 23 + i * 31, 348, 23 + i * 31);
            Line(43, 23, 43, 147);
            Label(settings.Unit, 4, 4);
            Label(ymax.ToString("G4"), 3, 23);
            Label(ymin.ToString("G4"), 3, 137);
            Label((t0 * 1000).ToString("G4"), 43, 153);
            Label((t1 * 1000).ToString("G4") + " ms", 278, 153);
            var curve = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(52, 120, 214)),
                StrokeThickness = 1.6 };
            foreach (var point in points) curve.Points.Add(new Point(X(point.Seconds), Y(point.Value)));
            WaveReferenceCanvas.Children.Add(curve);
            WaveReferenceView.Visibility = Visibility.Visible;
            WaveReferenceStatus.Text = "已校验参考资源 · 真实样本预览（显示降采样，不用于判定）";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (request == _targetPreviewVersion && ReferenceEquals(choice, _selectedTarget))
                WaveReferenceStatus.Text = "参考波形无法预览：" + error.Message;
        }
    }

    private void ConfigureCanSending()
    {
        var periodText = _editing?.Properties.GetValueOrDefault("periodMs", "0") ?? "0";
        var durationText = _editing?.Properties.GetValueOrDefault("durationMs", "0") ?? "0";
        var hasPeriod = int.TryParse(periodText, NumberStyles.None,
            CultureInfo.InvariantCulture, out var period) && period > 0;
        var hasDuration = int.TryParse(durationText, NumberStyles.None,
            CultureInfo.InvariantCulture, out var duration) && duration > 0;
        CanOnceMode.IsChecked = !hasPeriod;
        CanPeriodicMode.IsChecked = hasPeriod;
        CanPeriodInput.Text = hasPeriod ? period.ToString(CultureInfo.InvariantCulture) : "100";
        // "0" explicitly means until CanStopPeriodic or owning scope ends.
        // Never silently change a new vehicle-environment sender to 5 seconds.
        CanTimedStop.IsChecked = hasDuration;
        CanUntilStopped.IsChecked = !hasDuration;
        CanDurationInput.Text = hasDuration ? duration.ToString(CultureInfo.InvariantCulture) : "5000";
        UpdateCanSendingMode();
    }

    private void CanSendMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || CanPeriodicPanel is null || CanPeriodField is null || CanTimedDurationPanel is null) return;
        UpdateCanSendingMode();
    }

    private void CanStopMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || CanTimedDurationPanel is null || CanPeriodField is null || CanPeriodicPanel is null) return;
        UpdateCanSendingMode();
    }

    private void UpdateCanSendingMode()
    {
        CanPeriodicPanel.Visibility = CanPeriodField.Visibility = CanPeriodicMode.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        CanTimedDurationPanel.Visibility = CanPeriodicMode.IsChecked == true &&
            CanTimedStop.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (CanPeriodicMeaning is not null)
            CanPeriodicMeaning.Text = CanTimedStop.IsChecked == true
                ? "启动时立即发送第一帧；后台继续按设定周期发送。到达指定时长后自动停止；后续测试步骤不会等待它。"
                : "启动时立即发送第一帧；跨同一项目的用例持续发送，直至“停止全部周期 CAN”、STOP 或整组结束。单独运行一步/用例时随其独立作用域结束。";
    }

    private MappingChoice[] CurrentCanRebindCandidates()
    {
        var original = _selectedTarget?.Mapping;
        if (original is null || original.Kind != TestTargetKind.CanMessage) return [];
        return _catalog.Where(x =>
            x.Mapping.Kind == TestTargetKind.CanMessage &&
            x.Mapping.Asset == original.Asset &&
            x.Mapping.CanId == original.CanId &&
            x.Mapping.Extended == original.Extended &&
            x.Message is not null).ToArray();
    }

    private void CanRebind_Click(object sender, RoutedEventArgs e)
    {
        if (!IsCanSend) return;
        var candidates = CurrentCanRebindCandidates();
        if (candidates.Length != 1)
        {
            ErrorText.Text = "当前 DBC 中没有唯一匹配的报文，请打开资源选择器核对报文。";
            CanDbcPicker.OpenWithSearch();
            return;
        }
        // A deliberate action is still required to update a pinned revision.
        ApplyTargetFilter("", candidates[0].Mapping);
        ErrorText.Text = "";
        MarkDraftChanged();
    }

    private void ClearCanDbc_Click(object sender, RoutedEventArgs e) => ClearCanDbcAssistance();

    private void ClearCanDbcAssistance()
    {
        if (!IsCanSend || _selectedTarget is null) return;
        var idText = FrameIdInput.Text;
        var data = RawCanDataInput.Text;
        var extended = GetCanFrameExtended();
        var hex = idText.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        _rawFormatId = uint.TryParse(hex, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var id) ? id : null;
        _rawFormatExtended = extended;
        _explicitRawCan = true;
        var raw = _allTargets.FirstOrDefault(x =>
            x.Mapping.Kind == TestTargetKind.RawCan && x.Mapping.Name == "原始 ECU CAN");
        if (raw is null) return;
        _filteringTargets = true;
        try { ActiveTargetPicker.SelectedItem = raw; }
        finally { _filteringTargets = false; }
        UpdateTarget();
        // Keep even incomplete draft bytes; applying still validates them.
        _loadingCanData = true;
        try
        {
            FrameIdInput.IsReadOnly = false;
            FrameIdInput.Text = idText;
            RawCanDataInput.Text = data;
            DbcCanPanel.Visibility = Visibility.Collapsed;
            CanRebindButton.Visibility = Visibility.Collapsed;
            _canSignalError = "";
            UpdateCanFrameFormatLabel();
        }
        finally { _loadingCanData = false; }
        CanTargetHint.Text = "已清除 DBC 辅助；CAN ID、DATA、帧格式及发送参数保持不变。可重新选择报文。";
        RefreshDbcPickers();
        UpdateCanFramePreview();
        MarkDraftChanged();
    }

    private static string FormatCanBytes(byte[] data) =>
        string.Join(" ", data.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));

    private static bool HasOverlappingCanSignals(DbcMessage message)
    {
        var bits = new HashSet<int>();
        foreach (var signal in message.Signals)
            foreach (var bit in DbcSignalCodec.BitPositions(signal))
                if (!bits.Add(bit)) return true;
        return false;
    }

    private bool GetCanFrameExtended()
    {
        var mapping = _selectedTarget?.Mapping;
        if (mapping?.Kind == TestTargetKind.CanMessage)
            return mapping.Extended; // DBC is authoritative, including low-ID extended frames.

        var idText = FrameIdInput.Text.Trim();
        if (idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            idText = idText[2..];
        if (!uint.TryParse(idText, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var id)) return false;

        if (_rawFormatId == id) return _rawFormatExtended;

        // Preserve an explicitly saved raw frame format when editing that same ID.
        if (mapping?.Kind == TestTargetKind.RawCan &&
            mapping.Name.StartsWith("RAW-ECU-CAN-", StringComparison.OrdinalIgnoreCase) &&
            mapping.CanId == id)
            return mapping.Extended;

        // For an unbound raw frame, the 29-bit range identifies extended frames.
        // IDs within 11 bits default to standard; the numeric ID alone cannot
        // distinguish a deliberately low-numbered extended frame.
        return id > 0x7FFu;
    }

    private void UpdateCanFrameFormatLabel()
    {
        if (CanFrameFormatLabel is null) return;
        var mapping = _selectedTarget?.Mapping;
        if (mapping?.Kind == TestTargetKind.CanMessage)
        {
            CanFrameFormatLabel.Text = mapping.Extended
                ? "扩展帧 · DBC 定义" : "标准帧 · DBC 定义";
            return;
        }
        var idText = FrameIdInput.Text.Trim();
        if (idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            idText = idText[2..];
        if (!uint.TryParse(idText, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var id))
        {
            CanFrameFormatLabel.Text = "等待 CAN ID";
            return;
        }
        var extended = GetCanFrameExtended();
        CanFrameFormatLabel.Text = extended
            ? "扩展帧 · 自动识别" : "标准帧 · 默认";
    }

    private void CanId_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || !IsCanSend || _loadingCanData) return;
        UpdateCanFrameFormatLabel();
        UpdateCanFramePreview();
    }

    private void CanId_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!IsCanSend || _loadingCanData || _explicitRawCan ||
            _selectedTarget?.Mapping.Kind != TestTargetKind.RawCan) return;
        var idText = FrameIdInput.Text.Trim();
        if (idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            idText = idText[2..];
        if (!uint.TryParse(idText, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var id))
        {
            UpdateCanFrameFormatLabel();
            return;
        }

        // Match by numeric CAN ID first. A DBC definition supplies the IDE bit,
        // so a low numeric ID can still correctly resolve to an extended frame.
        var found = _catalog.Where(x =>
            x.Mapping.Kind == TestTargetKind.CanMessage && x.Message is not null &&
            x.Mapping.CanId == id).ToArray();
        if (found.Length == 1)
        {
            // Automatic assistance must not replace entered DATA to fit a DBC.
            try
            {
                if (ParseHex(RawCanDataInput.Text, 8).Length == found[0].Message!.Dlc)
                {
                    ApplyTargetFilter("", found[0].Mapping);
                    return;
                }
            }
            catch (ArgumentException) { }
            CanTargetHint.Text = "找到对应 DBC，但 DATA 尚未与其 DLC 一致；已保留原始字节，可主动选择辅助。";
        }
        if (found.Length > 1)
        {
            // Multiple definitions exist for the same numeric ID (perhaps
            // different frame formats). Ask the user rather than guessing.
            CanDbcPicker.OpenWithSearch("0x" + id.ToString("X", CultureInfo.InvariantCulture));
            CanTargetHint.Text = "该 CAN ID 匹配多个 DBC 报文，请在候选中确认目标。";
            return;
        }
        UpdateCanFrameFormatLabel();
        UpdateCanFramePreview();
    }

    private void CanSignalSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (SignalGrid is null || CanSignalSearch is null) return;
        var query = CanSignalSearch.Text.Trim();
        SignalGrid.Items.Filter = item => query.Length == 0 ||
            item is TestSignalInputField signal &&
            (signal.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             signal.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             signal.Definition.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void CanData_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || !IsCanSend || _loadingCanData) return;
        _canSignalError = "";
        SynchronizeCanSignals();
        UpdateCanFramePreview();
    }

    private void SynchronizeCanSignals()
    {
        if (!IsCanSend || _loadingCanData || _selectedTarget?.Message is not { } message)
            return;
        byte[]? data = null;
        try
        {
            var candidate = ParseHex(RawCanDataInput.Text, 8);
            if (candidate.Length == message.Dlc) data = candidate;
        }
        catch (ArgumentException) { /* An unfinished HEX input is not an ECU value. */ }
        _loadingCanData = true;
        try
        {
            foreach (var item in SignalValues)
            {
                var display = "";
                if (data is not null)
                {
                    try
                    {
                        display = DbcSignalCodec.DecodePhysical(item.Signal, data)
                            .ToString("G12", CultureInfo.InvariantCulture);
                    }
                    catch (Exception ex) when (ex is ArgumentException or
                        IndexOutOfRangeException or AutoTest.Projects.Project.ProjectDataException)
                    { /* Unsupported decoding must not fabricate a value. */ }
                }
                item.Value = display;
            }
        }
        finally { _loadingCanData = false; }
    }

    private void PatchCanSignal(TestSignalInputField item)
    {
        if (!IsCanSend || _loadingCanData || !item.IsEditable ||
            _selectedTarget?.Message is not { } message) return;
        try
        {
            if (!double.TryParse(item.Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var physical) ||
                !double.IsFinite(physical))
                throw new ArgumentException("必须填写有效的信号物理值。");
            if (physical < item.Signal.Minimum || physical > item.Signal.Maximum)
                throw new ArgumentException("超过 DBC 允许的物理值范围。");
            var bytes = ParseHex(RawCanDataInput.Text, 8);
            if (bytes.Length != message.Dlc)
                throw new ArgumentException("请先填写完整 " + message.Dlc +
                    " 字节 DATA，再通过 DBC 修改信号。");
            DbcSignalCodec.EncodePhysical(item.Signal, physical, bytes);
            _canSignalError = "";
            _loadingCanData = true;
            try { RawCanDataInput.Text = FormatCanBytes(bytes); }
            finally { _loadingCanData = false; }
            SynchronizeCanSignals();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or
            OverflowException or AutoTest.Projects.Project.ProjectDataException)
        {
            _canSignalError = item.DisplayName + "：" + ex.Message;
        }
        UpdateCanFramePreview();
    }

    private void UpdateCanFramePreview()
    {
        if (!IsCanSend || _loadingCanData || CanFramePreview is null) return;
        try
        {
            var idText = FrameIdInput.Text.Trim();
            if (idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                idText = idText[2..];
            if (!uint.TryParse(idText, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var id))
                throw new ArgumentException("需要填写有效的十六进制 CAN ID。");
            var extended = GetCanFrameExtended();
            if (id > (extended ? 0x1FFFFFFFu : 0x7FFu))
                throw new ArgumentException("CAN ID 不符合帧格式。");
            var bytes = ParseHex(RawCanDataInput.Text, 8);
            var definition = _selectedTarget?.Message;
            if (definition is not null && bytes.Length != definition.Dlc)
                throw new ArgumentException("当前 DATA 为 " + bytes.Length +
                    " 字节，DBC 定义为 " + definition.Dlc + " 字节。");
            if (_selectedTarget?.Mapping.Kind == TestTargetKind.CanMessage &&
                (definition is null || id != _selectedTarget.Mapping.CanId ||
                 extended != _selectedTarget.Mapping.Extended))
                throw new ArgumentException("DBC 旧绑定失效，或 CAN ID 与定义不一致。");
            CanDlcInput.Text = (definition?.Dlc ?? bytes.Length)
                .ToString(CultureInfo.InvariantCulture);
            CanFramePreview.Text = (extended ? "扩展帧" : "标准帧") +
                " · " + bytes.Length + " 字节 · 数据完整";
            CanPreviewIssue.Text = _canSignalError;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or
            OverflowException or AutoTest.Projects.Project.ProjectDataException)
        {
            if (_selectedTarget?.Message is null) CanDlcInput.Text = "—";
            CanFramePreview.Text = "报文待完成";
            CanPreviewIssue.Text = _canSignalError.Length > 0
                ? _canSignalError : ex.Message;
        }
    }

    private void KeyAction_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateKeyTimingVisibility();
    }

    private void UpdateKeyTimingVisibility()
    {
        var operation = (KeyActionInput.SelectedItem as ComboBoxItem)?.Tag as string;
        var raw = _selectedTarget?.Mapping.Kind == TestTargetKind.RawKey;
        // A KEY_SIM hold is explicitly persistent; operators must be able to
        // switch to a no-write Wait or an intentional all-release without
        // manufacturing a zero mask as an implicit wait.
        KeyActionInput.Visibility = Visibility.Visible;
        // Pulse is a legacy press-and-release command, not the frozen
        // persistent KEY_SIM semantics. Do not offer it for a new 20-Key step.
        var legacyPulse = raw && _editing?.Properties.GetValueOrDefault("value", "")
            .StartsWith("Pulse:", StringComparison.OrdinalIgnoreCase) == true;
        foreach (var item in KeyActionInput.Items.OfType<ComboBoxItem>())
        {
            if ((string?)item.Tag == "Wait") item.IsEnabled = raw;
            if ((string?)item.Tag == "Pulse")
                item.Visibility = raw && !legacyPulse ? Visibility.Collapsed : Visibility.Visible;
        }
        KeyDurationLabel.Text = operation == "Wait" ? "纯等待时间（ms）" : "保持时间（ms）";
        KeyDurationPanel.Visibility =
            ((operation is "Pulse" or "Hold" or "Wait") && raw) ||
            operation == "Pulse"
                ? Visibility.Visible : Visibility.Collapsed;
        RawKeyMaskPanel.Visibility = raw &&
            (operation is not ("Release" or "Wait"))
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildRawKeyGrid()
    {
        for (var i = 0; i < _rawKeys.Length; i++)
        {
            var item = new CheckBox
            {
                Content = "Key " + (i + 1).ToString("D2", CultureInfo.InvariantCulture),
                Margin = new Thickness(3, 6, 3, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            item.Checked += (_, _) => UpdateRawKeyMaskFromControls();
            item.Unchecked += (_, _) => UpdateRawKeyMaskFromControls();
            _rawKeys[i] = item;
            RawKeyGrid.Children.Add(item);
        }
        SetRawKeyMask(0);
    }

    private void SetRawKeyMask(uint mask)
    {
        _updatingRawKeys = true;
        try
        {
            _selectedMask = mask & 0xFFFFF;
            for (var i = 0; i < _rawKeys.Length; i++)
                _rawKeys[i].IsChecked = (_selectedMask & (1u << i)) != 0;
            RawKeyMaskInput.Text = "0x" + _selectedMask.ToString("X5", CultureInfo.InvariantCulture);
        }
        finally { _updatingRawKeys = false; }
    }

    private void UpdateRawKeyMaskFromControls()
    {
        if (_updatingRawKeys) return;
        uint mask = 0;
        for (var i = 0; i < _rawKeys.Length; i++)
            if (_rawKeys[i].IsChecked == true) mask |= 1u << i;
        _selectedMask = mask;
        RawKeyMaskInput.Text = "0x" + mask.ToString("X5", CultureInfo.InvariantCulture);
    }

    private void ClearRawKeys_Click(object sender, RoutedEventArgs e) => SetRawKeyMask(0);

    private void StepAction_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: string action })
            StepActionRequested?.Invoke(action);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => TryApplyChanges();

    /// <summary>
    /// Apply the inspector draft to the canonical StepEditTransaction. Returns false
    /// without discarding inputs when a required field or target is invalid.
    /// </summary>
    public bool TryApplyChanges()
    {
        if (_selected is null)
        {
            ErrorText.Text = "请选择有效的步骤操作类型。";
            return false;
        }
        try
        {
            var name = NameInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
                throw new ArgumentException("请填写步骤名称，不超过 120 字符。");
            var node = (_editing ?? StepUiCatalog.NewNode(_selected.StepType)) with
            {
                Name = name,
                Type = _selected.StepType
            };
            foreach (var field in ParameterFields)
                node = node.WithProperty(field.Key, field.Value.Trim());
            if (IsCanSend)
            {
                if (_canSignalError.Length > 0)
                    throw new ArgumentException("DBC 信号修改未应用：" + _canSignalError);
                var periodic = CanPeriodicMode.IsChecked == true;
                var period = 0;
                if (periodic && (!int.TryParse(CanPeriodInput.Text.Trim(),
                        NumberStyles.None, CultureInfo.InvariantCulture, out period) ||
                    period is < 20 or > 600000))
                    throw new ArgumentException("周期必须是 20～600000 毫秒的整数。");
                var duration = 0;
                if (periodic && CanTimedStop.IsChecked == true &&
                    (!int.TryParse(CanDurationInput.Text.Trim(), NumberStyles.None,
                        CultureInfo.InvariantCulture, out duration) ||
                     duration is < 1 or > 1800000))
                    throw new ArgumentException("持续时间必须是 1～1800000 毫秒的整数。");
                node = node.WithProperty("periodMs", period.ToString(CultureInfo.InvariantCulture))
                    .WithProperty("durationMs", duration.ToString(CultureInfo.InvariantCulture));
            }
            node = node.WithProperty("enabled",
                StepEnabledInput.IsChecked == true ? "true" : "false");

            TestTargetMapping? mapping = null;
            if (!string.IsNullOrEmpty(_selected.TargetCategory))
            {
                if (_selectedTarget is null &&
                    _selected.TargetCategory != "wave")
                    throw new ArgumentException("还没有可用操作对象。请导入 DBC 或创建 ECU 输入配置。");
                mapping = _selectedTarget?.Mapping;
                if (mapping?.Kind == TestTargetKind.RawCan)
                {
                    var hex = FrameIdInput.Text.Trim();
                    if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
                    if (!uint.TryParse(hex, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var id))
                        throw new ArgumentException("请填写合法 CAN ID（十六进制）。");
                    var ext = GetCanFrameExtended();
                    if (id > (ext ? 0x1FFFFFFFu : 0x7FFu))
                        throw new ArgumentException("CAN ID 超出所选标准/扩展帧范围。");
                    var bytes = ParseHex(RawCanDataInput.Text, 8);
                    mapping = mapping with { Name = "RAW-ECU-CAN-0x" + id.ToString("X") +
                        (ext ? "-EXT" : "-STD"), CanId = id, Extended = ext };
                    node = node.WithProperty("value", "HEX:" + Convert.ToHexString(bytes));
                }
                else if (mapping?.Kind == TestTargetKind.CanMessage)
                {
                    var message = _selectedTarget?.Message ??
                        throw new ArgumentException("DBC 资源失效，必须重新关联。");
                    var data = ParseHex(RawCanDataInput.Text, 8);
                    if (data.Length != message.Dlc)
                        throw new ArgumentException("DATA 必须填写 DBC 规定的 " +
                            message.Dlc + " 字节。");
                    var payload = "HEX:" + Convert.ToHexString(data);
                    // DBC is a helper: execution persists the exact visible bytes.
                    _ = DbcCanMessageEncoder.Encode(message, payload);
                    node = node.WithProperty("value", payload);
                }
                else if (mapping?.Kind is TestTargetKind.Button or TestTargetKind.RawKey)
                {
                    var operation = (KeyActionInput.SelectedItem as ComboBoxItem)?.Tag as string
                        ?? throw new ArgumentException("请选择保持、释放、纯等待或短按。");
                    if (mapping.Kind == TestTargetKind.RawKey)
                    {
                        if (operation is "Hold" or "Wait")
                        {
                            if (!int.TryParse(KeyDurationInput.Text.Trim(), NumberStyles.None,
                                CultureInfo.InvariantCulture, out var keyMs) || keyMs is < 1 or > 600000)
                                throw new ArgumentException("保持/纯等待时间须为 1～600000 毫秒，计时结束不会自动释放 KEY。");
                        }
                        // Empty 20-switch combination is the explicit all-off
                        // action, represented by the existing Release wire syntax.
                        if (operation == "Hold" && _selectedMask == 0)
                            throw new ArgumentException(
                                "零组合会请求全部断开。仅计时请选择“纯等待”，断开请选择“明确全部断开 KEY”。");
                        var value = operation == "Release" ? "Release" :
                            operation == "Wait"
                                ? "Wait:" + KeyDurationInput.Text.Trim()
                                : operation + ":0x" + _selectedMask.ToString("X5") +
                                    ((operation == "Pulse" ||
                                      !string.IsNullOrWhiteSpace(KeyDurationInput.Text))
                                        ? ":" + KeyDurationInput.Text.Trim() : "");
                        if (_legacySourceType == "KeySet" &&
                            string.IsNullOrWhiteSpace(KeyDurationInput.Text))
                            throw new ArgumentException("旧版按键步骤必须核对并填写保持时间。");
                        _ = AutoTest.Devices.Station.RawKeyAction.Parse(value);
                        node = node.WithProperty("value", value);
                    }
                    else
                    {
                        if (operation == "Wait")
                            throw new ArgumentException("功能按键请使用单独的“等待”步骤；只有原始 KEY_SIM 支持纯等待。");
                        var suffix = operation == "Pulse" &&
                            !string.IsNullOrWhiteSpace(KeyDurationInput.Text)
                            ? ":" + ParseDuration(KeyDurationInput.Text).ToString(CultureInfo.InvariantCulture)
                            : "";
                        node = node.WithProperty("value", operation + suffix);
                    }
                }
                else if (mapping?.Kind == TestTargetKind.Ignition)
                    node = node.WithProperty("value",
                        (IgnitionState.SelectedItem as ComboBoxItem)?.Tag as string ?? "ON");

                if (_selected.TargetCategory == "wave")
                {
                    node = node.WithProperty("profile", mapping?.Name ?? "");
                    if (mapping is null && string.IsNullOrWhiteSpace(
                        node.Properties.GetValueOrDefault("reference", "")))
                        throw new ArgumentException("波形分析需要选择已配置方案，或填写命名参考波形。");
                }
                else if (mapping is not null)
                    node = node.WithProperty("target", mapping.Name);
            }
            if (node.Type == "CallPublicFlow")
            {
                var flow = PublicFlowPicker.SelectedItem as PublicFlowChoice
                    ?? throw new ArgumentException("请选择工程中已保存的公共流程；失效引用须显式更新。");
                node = node.WithProperty("flowAssetId", flow.AssetId.ToString("D"))
                    .WithProperty("flowRevision", flow.Revision.ToString(CultureInfo.InvariantCulture))
                    .WithProperty("flowSha", flow.Sha256);
            }
            if (node.Type == "AssertSignal" && mapping?.Kind != TestTargetKind.Signal)
                throw new ArgumentException("CAN 信号判定必须选择 DBC 信号。");
            if (node.Type == "AssertSignal")
            {
                var expectation = node.Properties.GetValueOrDefault("expected", "");
                if (!double.TryParse(expectation, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _) &&
                    !expectation.StartsWith("$", StringComparison.Ordinal))
                    throw new ArgumentException("期望状态必须是 DBC 枚举值对应的数值，或已定义的数值变量。请核对原始中文状态说明。");
            }
            if (_convertedLegacy && node.Type == "AssertSignal")
            {
                if (mapping is null || _editing is null)
                    throw new ArgumentException("先选择与旧报文对应的 DBC 信号。");
                if (TryLegacyCanId(_editing, out var originalId) &&
                    mapping.CanId != originalId)
                    throw new ArgumentException("所选 DBC 信号的 CAN ID 与旧文件不一致，请先核对。");
                var sourceMessage = _selectedTarget?.Message;
                var sourceSignal = sourceMessage?.Signals.FirstOrDefault(x =>
                    x.Name == mapping.SignalName);
                if (sourceSignal is null)
                    throw new ArgumentException("未找到当前 DBC 中的原始信号定义。");
                if (_editing.Properties.TryGetValue("bitLength", out var bits) &&
                    int.TryParse(bits, out var originalBits) &&
                    sourceSignal.BitLength != originalBits)
                    throw new ArgumentException("旧文件位长与当前 DBC 不一致，不能直接确认重建。");
                if (_editing.Properties.TryGetValue("bitOrder", out var order) &&
                    !string.IsNullOrWhiteSpace(order) &&
                    ((order.Equals("Intel", StringComparison.OrdinalIgnoreCase) && !sourceSignal.Intel) ||
                     (order.Equals("Motorola", StringComparison.OrdinalIgnoreCase) && sourceSignal.Intel)))
                    throw new ArgumentException("旧文件字节序与当前 DBC 不一致，不能直接确认重建。");
                node = node.WithProperty("legacy.importStatus", "REBOUND_VALIDATED")
                    .WithProperty("legacy.reboundTarget", mapping.Name);
            }
            if (_convertedLegacy && node.Type == "Write")
            {
                if (_legacySourceType == "CanSend" && _editing is { } sourceCan &&
                    TryLegacyCanId(sourceCan, out var oldFrame) &&
                    mapping?.CanId != oldFrame)
                    throw new ArgumentException("当前所选 CAN 报文与旧文件的 CAN ID 不符，请核对后重新选择。");
                if (_legacySourceType == "CanSend")
                {
                    var periodText = node.Properties.GetValueOrDefault("periodMs", "0");
                    var durationText = node.Properties.GetValueOrDefault("durationMs", "0");
                    if (!int.TryParse(periodText, NumberStyles.None,
                            CultureInfo.InvariantCulture, out var period) ||
                        period != 0 && period is < 20 or > 600000 ||
                        !int.TryParse(durationText, NumberStyles.None,
                            CultureInfo.InvariantCulture, out var duration) ||
                        duration is < 0 or > 1800000)
                        throw new ArgumentException("CAN 周期须为 0（单次）或 20～600000ms；持续时间须为 0～1800000ms。");
                }
                node = node.WithProperty("legacy.importStatus", "REBOUND_VALIDATED")
                    .WithProperty("legacy.reboundTarget", mapping?.Name ?? "");
            }
            if (node.Type == "Write" && mapping is null)
                throw new ArgumentException("请选择操作对象。");
            foreach (var required in StepDescriptorCatalog.Builtins().Lookup(node.Type)!
                .RequiredProperties)
                if (string.IsNullOrWhiteSpace(node.Properties.GetValueOrDefault(required, "")))
                    throw new ArgumentException("请填写必要参数：" + StepUiCatalog.PropertyLabel(required));

            mapping?.Validate();
            Result = new StepCompositionResult(node, mapping);
            if (ApplyRequested?.Invoke(Result) != true)
            {
                HasPendingEdits = true;
                ErrorText.Text = "无法保存此步骤：" + (ApplyFailureReason?.Invoke() ?? "测试程序拒绝了本次修改。");
                return false;
            }
            HasPendingEdits = false;
            AppliedSuccessfully?.Invoke();
            return true;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or
                                      OverflowException or AutoTest.Projects.Project.ProjectDataException)
        {
            HasPendingEdits = true;
            ErrorText.Text = "参数未完成：" + error.Message;
            return false;
        }
    }

    private static int ParseDuration(string text)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) ||
            ms is < 1 or > 60000)
            throw new ArgumentException("短按时间必须在 1～60000 毫秒内。");
        return ms;
    }

    private static byte[] ParseHex(string input, int maximum)
    {
        var compact = string.Concat(input.Where(c => !char.IsWhiteSpace(c)));
        if (compact.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase)) compact = compact[4..];
        byte[] bytes;
        try { bytes = Convert.FromHexString(compact); }
        catch (FormatException ex) { throw new ArgumentException("报文数据必须是完整的十六进制字节。", ex); }
        if (bytes.Length > maximum)
            throw new ArgumentException("报文数据超出允许的 " + maximum + " 字节。");
        return bytes;
    }
}
