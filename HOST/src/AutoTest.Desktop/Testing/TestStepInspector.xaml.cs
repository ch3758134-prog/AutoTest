using System.Collections.ObjectModel;
using System.Globalization;
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
            "operator" when stepType is "Expect" or "WaitUntil" or "AssertSignal" &&
                value is "==" or "!=" or ">" or ">=" or "<" or "<=" =>
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
    public TestSignalInputField(DbcSignal signal, string value, string? displayName = null,
        string? description = null, IReadOnlyDictionary<long, string>? rawLabels = null,
        string? summary = null)
        : base(signal, displayName, description, rawLabels, value, summary) { }
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
            "选择 DBC 报文并填写信号值；也可发送原始帧。"),
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
            "创建真实 CASE Slot；当前运行器尚未实现，运行前阻断。"),
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
            "仅编辑配置引用，当前禁止真实刷写。"),
        new("wave-capture", "读取与观察", "波形采集（扩展步骤）", "WaveCapture", "",
            "编辑采集资源，运行前需要真实采集服务。"),
        new("wave-compare", "分析与判断", "波形判定（扩展步骤）", "WaveCompare", "",
            "编辑参考资源，真实比较须三态证据。")
    ];

    public static IReadOnlyList<TestActionOption> PaletteActions(bool caseOnly) =>
        Options.Where(option => caseOnly ? option.StepType == "Case" :
            // "CanSend" is an obsolete alternate authoring entry. A real
            // CAN sending operation already exists as Write + DBC/Raw CAN.
            // Keep it readable for old resources, but do not offer engineers
            // two misleading "send CAN" actions in the primary palette.
            option.StepType != "Case" && option.Key != "can-send").ToArray();

    private readonly StepNode? _editing;
     private readonly IReadOnlyList<MappingChoice> _catalog;
    private readonly IReadOnlyList<TestTargetMapping> _boundMappings;
    private bool _ready;
    private TestActionOption? _selected;
    private MappingChoice? _selectedTarget;
    private readonly List<MappingChoice> _allTargets = [];
    private string _targetRevisionWarning = "";
    private bool _loadingCanData;
    private bool _filteringTargets;
    private bool IsCanSend => _selected?.Key == "can";
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
        ErrorText.Text = "本步骤有未应用的属性。请先点击“应用属性”或“取消编辑”，再切换步骤。";

    private void MarkDraftChanged()
    {
        if (!_trackEdits) return;
        HasPendingEdits = true;
        ErrorText.Text = "属性已修改，尚未应用到测试程序。";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        HasPendingEdits = false;
        Cancelled?.Invoke();
    }

    public TestStepInspector(IReadOnlyList<MappingChoice> catalog,
        IReadOnlyList<TestTargetMapping> boundMappings, StepNode? editing = null,
        string? initialActionKey = null, bool caseOnly = false,
        IReadOnlyList<PublicFlowChoice>? publicFlows = null)
    {
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
        ConfirmButton.Content = "应用";
        var mapping = boundMappings.FirstOrDefault(m =>
            m.Name == editing.Properties.GetValueOrDefault("target", ""));
        var targetKind = mapping?.Kind;
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
                ? chosen.Title + " · 核对旧步骤参数" : chosen.Title;
            StepCode.Text = editing.Id.ToString()[^5..];
            ActionDescription.Text = chosen.Description;
            ActionDescription.Visibility = _convertedLegacy ||
                chosen.StepType is "CanSend" or "CanWait"
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
                if (ReferenceEquals(e.OriginalSource, TargetSearch)) return;
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
        // The imported source dictionary is not an operator-editable schema.
        // Keep its unrecognised keys in StepNode unchanged; expose only formal
        // TEST parameters that the UI can accurately explain to an engineer.
        var needsLegacyReview = StepUiCatalog.RequiresLegacyReview(node) ||
            _legacySourceType.Length > 0;
        ImportWarning.Visibility = needsLegacyReview ? Visibility.Visible : Visibility.Collapsed;
        ConvertImportedAssertionButton.Visibility = needsLegacyReview &&
            !_convertedLegacy && (node.Type is "Read" or "WaitUntil") &&
            node.Properties.TryGetValue("expectation", out var legacyExpected) &&
            !string.IsNullOrWhiteSpace(legacyExpected)
            ? Visibility.Visible : Visibility.Collapsed;
        if (needsLegacyReview)
            ImportWarning.Text = _convertedLegacy
                ? (_selected?.StepType == "AssertSignal"
                    ? "旧版 CAN 解析：核对目标 DBC 信号、比较值和超时后，点击应用属性，将转换为正式的信号判定。"
                    : _legacySourceType == "KeySet"
                    ? "旧版按键：核对 20 路 KEY 开关组合和持续时间。应用后将转为真实 KEY_SIM 操作，状态保持到下一次按键操作或测试清理。"
                    : "旧版 CAN 发送：核对报文、信号值和周期后应用。")
                : "此步骤来自旧测试文件，尚未验证，不能执行。" +
                  " 原始记录：" + node.Properties.GetValueOrDefault("canId", "未知报文") +
                  "；信号：" + node.Properties.GetValueOrDefault("displayName", "待核对") +
                  "；期望：" + node.Properties.GetValueOrDefault("expectation", "未提供") + "。";
        foreach (var item in node.Properties.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (action.StepType == "CallPublicFlow" &&
                item.Key is "flowAssetId" or "flowRevision" or "flowSha") continue;
            if (!StepUiCatalog.IsOperatorProperty(item.Key) ||
                (item.Key == "target" && !string.IsNullOrEmpty(action.TargetCategory)) ||
                (item.Key == "value" && action.StepType == "Write") ||
                (item.Key == "profile" && action.StepType == "WaveAnalyze")) continue;
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
    }

    private void ConvertImportedAssertion_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null || !StepUiCatalog.RequiresLegacyReview(_editing)) return;
        _convertedLegacy = true;
        _legacySourceType = _editing.Type;
        _selected = Options.First(x => x.StepType == "AssertSignal");
        TypeLabel.Text = "CAN 信号判定 · 旧文件重建";
        BuildParameters(_selected);
        BuildTargets(_selected);
        MarkDraftChanged();
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
        TargetPanel.Visibility = !IsCanSend && !string.IsNullOrEmpty(action.TargetCategory)
            ? Visibility.Visible : Visibility.Collapsed;
        RawCanPanel.Visibility = DbcCanPanel.Visibility =
            KeyPanel.Visibility = IgnitionPanel.Visibility = Visibility.Collapsed;
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
            _allTargets.Add(new MappingChoice("ECU CAN / 原始报文（无需 DBC）",
                new TestTargetMapping("原始 ECU CAN", TestTargetKind.RawCan, default, 0, "",
                    BusRole.DutCan, 0)));
        else if (action.TargetCategory == "button")
            _allTargets.Add(new MappingChoice("KEY CAN · 20 Key 组合",
                new TestTargetMapping("原始 KEY_SIM 20 Key", TestTargetKind.RawKey,
                    default, 0, "", BusRole.KeypadCan)));

        foreach (var mapping in _boundMappings.Where(Eligible))
            if (!_allTargets.Any(x => x.Mapping == mapping))
                _allTargets.Add(new MappingChoice("已绑定 / " + mapping.Name,
                    mapping));
        var oldTargetName = _editing?.Properties.GetValueOrDefault(
            action.TargetCategory == "wave" ? "profile" : "target", "");
        // A pinned mapping must be preferred over a catalog object with the same
        // display name but a newer asset revision.
        var originallyBound = _boundMappings.FirstOrDefault(x =>
            string.Equals(x.Name, oldTargetName, StringComparison.OrdinalIgnoreCase));
        ApplyTargetFilter("", originallyBound, oldTargetName);
        if (originallyBound is not null &&
            !_catalog.Any(x => x.Mapping == originallyBound) &&
            originallyBound.Kind is not (TestTargetKind.RawCan or TestTargetKind.RawKey or
                TestTargetKind.Ignition))
            _targetRevisionWarning = "  注意：原步骤绑定的资源版本不在当前 TCF/DBC 中，"
                + "软件不会自动换版本。请确认并显式重新选择有效对象。";
        if (!IsCanSend)
        {
            TargetHint.Text = _targetRevisionWarning.Trim();
            TargetHint.Visibility = TargetHint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        // No pointless resource picker for the single built-in IGN or direct
        // 20-Key action. Hardware/DBC-backed choices retain their picker.
        if (_allTargets.Count == 1 &&
            _allTargets[0].Mapping.Kind is TestTargetKind.RawKey or TestTargetKind.Ignition)
            TargetPanel.Visibility = Visibility.Collapsed;
    }

    private void TargetSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _filteringTargets || TargetPicker is null || _selected is null) return;
        ApplyTargetFilter((IsCanSend ? CanTargetSearch.Text : TargetSearch.Text).Trim(),
            _selectedTarget?.Mapping);
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
    }

    private void UpdateTarget()
    {
        var choice = ActiveTargetPicker.SelectedItem as MappingChoice;
        if (IsCanSend && choice is not null && _selectedTarget is { } previous &&
            choice.Mapping == previous.Mapping &&
            (choice.Message is null) == (previous.Message is null))
            return; // Search/refresh is not permission to overwrite unsaved editor fields.
        _selectedTarget = choice;
        RawCanPanel.Visibility = DbcCanPanel.Visibility = KeyPanel.Visibility =
            IgnitionPanel.Visibility = Visibility.Collapsed;
        SignalValues.Clear();
        if (IsCanSend)
        {
            CanDataCard.Visibility = Visibility.Collapsed;
            CanRebindButton.Visibility = Visibility.Collapsed;
            CanTargetHint.Text = AvailableTargets.Count == 0
                ? "当前没有可用报文。请检查搜索条件或导入 DBC；也可直接填写原始 CAN。"
                : "请选择一条 DBC 报文，或选择无 DBC 原始报文。";
            CanFramePreview.Text = "待选择报文";
            CanPreviewIssue.Text = "";
        }
        if (_selectedTarget is null) return;
        var mapping = _selectedTarget.Mapping;
        if (IsCanSend)
        {
            if (mapping.Kind == TestTargetKind.CanMessage && _selectedTarget.Message is null)
            {
                CanTargetHint.Text = "旧 DBC 版本绑定已失效。不能使用旧定义编辑或发送，请重新选择当前报文。";
                var matches = CurrentCanRebindCandidates();
                CanRebindButton.Visibility = matches.Length == 1
                    ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            CanTargetHint.Text = mapping.Kind == TestTargetKind.CanMessage
                ? "来自当前工程 DBC 的真实报文定义；修改信号后由现有编码器生成 CAN 数据。"
                : "无需 DBC。请填写 CAN ID、帧格式与数据；最终发送字节在下方预览。";
            CanDataCard.Visibility = Visibility.Visible;
        }
        if (mapping.Kind == TestTargetKind.RawCan)
        {
            RawCanPanel.Visibility = Visibility.Visible;
            FrameIdInput.Text = _legacySourceType == "CanSend" && _editing is { } old &&
                TryLegacyCanId(old, out var oldId)
                ? "0x" + oldId.ToString("X") :
                _editing is not null && mapping.Name ==
                _editing.Properties.GetValueOrDefault("target", "")
                ? "0x" + mapping.CanId.ToString("X") : "";
            ExtendedFrame.IsChecked = mapping.Extended;
            var previousRaw = _boundMappings.FirstOrDefault(x =>
                string.Equals(x.Name, _editing?.Properties.GetValueOrDefault("target", ""),
                    StringComparison.OrdinalIgnoreCase));
            var sameRaw = previousRaw?.Kind == TestTargetKind.RawCan &&
                previousRaw.Name == mapping.Name;
            var originalHex = _legacySourceType == "CanSend"
                ? _editing?.Properties.GetValueOrDefault("dataHex",
                    _editing.Properties.GetValueOrDefault("value", "")) ?? ""
                : sameRaw
                    ? _editing?.Properties.GetValueOrDefault("value", "") ?? ""
                    : "";
            RawCanDataInput.Text = originalHex.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase)
                ? originalHex[4..] : originalHex;
            UpdateCanFramePreview();
        }
        else if (mapping.Kind == TestTargetKind.CanMessage)
        {
            DbcCanPanel.Visibility = Visibility.Visible;
            var msg = _selectedTarget.Message;
            DbcMessageInfo.Text = msg is null
                ? "DBC 资源已变更或未加载。请重新选择当前工程的报文。"
                : (_selectedTarget.DisplayNames?.MessageLabel(msg) ?? msg.Name) +
                  " · 0x" + msg.ArbitrationId.ToString("X") +
                  " · DLC " + msg.Dlc + " · " + (msg.IsExtended ? "扩展帧" : "标准帧") +
                  (string.IsNullOrWhiteSpace(_selectedTarget.DisplayNames?.MessageSummary(msg.Id))
                      ? "" : " · " + _selectedTarget.DisplayNames!.MessageSummary(msg.Id));
            if (msg is not null)
            {
                var old = _editing?.Properties.GetValueOrDefault("value",
                    _editing.Properties.GetValueOrDefault("dataHex", "")) ?? "";
                var previousBinding = _boundMappings.FirstOrDefault(x =>
                    string.Equals(x.Name, _editing?.Properties.GetValueOrDefault("target", ""),
                        StringComparison.OrdinalIgnoreCase));
                if (_legacySourceType != "CanSend" && (previousBinding is null ||
                    previousBinding.Kind != TestTargetKind.CanMessage ||
                    previousBinding.Asset != mapping.Asset || previousBinding.CanId != mapping.CanId ||
                    previousBinding.Extended != mapping.Extended))
                    old = ""; // New/different CAN frames must not inherit a KEY "Pulse" value or unrelated frame bytes.
                var dict = new Dictionary<string, string>(StringComparer.Ordinal);
                if (old.Contains('='))
                    foreach (var segment in old.Split(';',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var pos = segment.IndexOf('=');
                        if (pos > 0) dict[segment[..pos].Trim()] = segment[(pos + 1)..].Trim();
                    }
                byte[]? originalBytes = old.Length == 0 ? new byte[msg.Dlc] : null;
                if (old.Length > 0 && !old.Contains('='))
                {
                    try
                    {
                        var hex = old.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase)
                            ? old[4..] : old;
                        var bytes = Convert.FromHexString(new string(hex.Where(
                            x => !char.IsWhiteSpace(x) && x != '-').ToArray()));
                        if (bytes.Length == msg.Dlc) originalBytes = bytes;
                    }
                    catch (FormatException) { /* Keep original HEX and require review. */ }
                }
                foreach (var signal in msg.Signals)
                {
                    var alias = _selectedTarget.DisplayNames?.SignalAlias(msg.Id, signal.Name);
                    var description = _selectedTarget.DisplayNames?.SignalDescription(msg.Id, signal.Name);
                    var labels = _selectedTarget.MessageValueLabels is not null &&
                        _selectedTarget.MessageValueLabels.TryGetValue(signal.Name, out var entries)
                            ? entries : null;
                    var current = dict.GetValueOrDefault(signal.Name, "");
                    if (current.Length == 0 && originalBytes is not null)
                    {
                        try
                        {
                            current = DbcSignalCodec.DecodePhysical(signal, originalBytes)
                                .ToString("G17", CultureInfo.InvariantCulture);
                        }
                        catch (Exception error) when (error is ArgumentException or
                            IndexOutOfRangeException or AutoTest.Projects.Project.ProjectDataException)
                        {
                            current = "";
                        }
                    }
                    var editor = new TestSignalInputField(signal, current,
                        string.IsNullOrWhiteSpace(alias) ? signal.Name : alias,
                        description, labels,
                        _selectedTarget.DisplayNames?.SignalSummary(msg.Id, signal.Name));
                    editor.PropertyChanged += (_, change) =>
                    {
                        if (change.PropertyName == nameof(TestSignalInputField.Value) &&
                            !_loadingCanData) UpdateCanFramePreview();
                    };
                    SignalValues.Add(editor);
                }
                _loadingCanData = true;
                try
                {
                    var rawOnly = msg.Signals.IsDefaultOrEmpty ||
                        msg.Signals.Any(signal => !string.IsNullOrEmpty(signal.Multiplexer));
                    DbcRawMode.IsChecked = rawOnly ||
                        old.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase) ||
                        (!old.Contains('=') && old.Length > 0);
                    DbcRawDataInput.Text = old.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase)
                        ? old[4..] : old.Length == 0
                            ? Convert.ToHexString(new byte[msg.Dlc]) :
                              DbcRawMode.IsChecked == true ? old : "";
                    SignalGrid.Visibility = DbcRawMode.IsChecked == true
                        ? Visibility.Collapsed : Visibility.Visible;
                    DbcRawDataInput.Visibility = DbcRawMode.IsChecked == true
                        ? Visibility.Visible : Visibility.Collapsed;
                }
                finally { _loadingCanData = false; }
                UpdateCanFramePreview();
            }
        }
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
                : operation is "Pulse" or "Hold" && mapping.Kind == TestTargetKind.RawKey &&
                parts.Length == 3 ? parts[2] :
                operation == "Pulse" && mapping.Kind == TestTargetKind.Button &&
                parts.Length == 2 ? parts[1] : "1000";
            UpdateKeyTimingVisibility();
        }
        else if (mapping.Kind == TestTargetKind.Signal &&
            _selected?.StepType == "AssertSignal" && _selectedTarget.ValueLabels is { Count: > 0 } labels)
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
        CanTimedStop.IsChecked = hasDuration;
        CanUntilStopped.IsChecked = !hasDuration;
        CanDurationInput.Text = hasDuration ? duration.ToString(CultureInfo.InvariantCulture) : "5000";
        UpdateCanSendingMode();
    }

    private void CanSendMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || CanPeriodicPanel is null) return;
        UpdateCanSendingMode();
    }

    private void CanStopMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || CanTimedDurationPanel is null) return;
        UpdateCanSendingMode();
    }

    private void UpdateCanSendingMode()
    {
        CanPeriodicPanel.Visibility = CanPeriodicMode.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        CanTimedDurationPanel.Visibility = CanPeriodicMode.IsChecked == true &&
            CanTimedStop.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
            ErrorText.Text = "当前 DBC 中没有唯一匹配的报文，请手动重新选择并核对 CAN ID。";
            return;
        }
        // This click, not an implicit same-name match, authorizes the new
        // revision to enter the TEST draft. Ctrl+S still saves the document.
        _filteringTargets = true;
        try { CanTargetSearch.Text = ""; }
        finally { _filteringTargets = false; }
        ApplyTargetFilter("", candidates[0].Mapping);
        ErrorText.Text = "";
        MarkDraftChanged();
    }

    private byte[] EncodeSelectedCanData(DbcMessage message, bool fromRaw)
    {
        if (fromRaw)
        {
            var bytes = ParseHex(DbcRawDataInput.Text, message.Dlc);
            if (bytes.Length != message.Dlc)
                throw new ArgumentException("发送字节数与 DBC DLC 不一致。");
            return bytes;
        }
        if (SignalValues.Any(x => string.IsNullOrWhiteSpace(x.Value)))
            throw new ArgumentException("还有未填写的信号值，请先完成或使用原始数据模式。");
        var specification = string.Join(";", SignalValues.Select(x =>
            x.Name + "=" + x.Value.Trim()));
        return DbcCanMessageEncoder.Encode(message, specification);
    }

    private void UpdateCanFramePreview()
    {
        if (!IsCanSend || _loadingCanData || CanFramePreview is null) return;
        var mapping = _selectedTarget?.Mapping;
        if (mapping is null) return;
        try
        {
            uint canId;
            bool extended;
            byte[] bytes;
            if (mapping.Kind == TestTargetKind.RawCan)
            {
                var idText = FrameIdInput.Text.Trim();
                if (idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    idText = idText[2..];
                if (!uint.TryParse(idText, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out canId))
                    throw new ArgumentException("请填写有效的十六进制 CAN ID。");
                extended = ExtendedFrame.IsChecked == true;
                if (canId > (extended ? 0x1FFFFFFFu : 0x7FFu))
                    throw new ArgumentException("CAN ID 超出帧格式范围。");
                bytes = ParseHex(RawCanDataInput.Text, 8);
            }
            else if (mapping.Kind == TestTargetKind.CanMessage &&
                _selectedTarget?.Message is { } message)
            {
                canId = message.ArbitrationId;
                extended = message.IsExtended;
                bytes = EncodeSelectedCanData(message, DbcRawMode.IsChecked == true);
            }
            else return;
            CanFramePreview.Text = "0x" + canId.ToString("X") +
                (extended ? " EXT" : " STD") + " · " + bytes.Length + "B · " +
                string.Join(" ", bytes.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));
            CanPreviewIssue.Text = "";
        }
        catch (Exception error) when (error is ArgumentException or FormatException or
                                      OverflowException or AutoTest.Projects.Project.ProjectDataException)
        {
            CanFramePreview.Text = "待完成";
            CanPreviewIssue.Text = error.Message;
        }
    }

    private void DbcRawMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _loadingCanData || SignalGrid is null) return;
        // Switching editing modes is an explicit user action; convert the
        // current payload so edits do not disappear silently.
        if (_selectedTarget?.Message is { } message)
        {
            try
            {
                if (DbcRawMode.IsChecked == true)
                {
                    var encoded = EncodeSelectedCanData(message, fromRaw: false);
                    DbcRawDataInput.Text = Convert.ToHexString(encoded);
                }
                else
                {
                    if (message.Signals.IsDefaultOrEmpty ||
                        message.Signals.Any(signal => !string.IsNullOrEmpty(signal.Multiplexer)))
                        throw new ArgumentException(
                            "此 DBC 报文没有可安全编码的完整普通信号集合，请使用原始数据字节。");
                    var original = ParseHex(DbcRawDataInput.Text, message.Dlc);
                    if (original.Length != message.Dlc)
                        throw new ArgumentException("原始数据字节数必须与 DBC DLC 相同。");
                    foreach (var field in SignalValues)
                        field.Value = DbcSignalCodec.DecodePhysical(field.Signal, original)
                            .ToString("G17", CultureInfo.InvariantCulture);
                }
                ErrorText.Text = "";
            }
            catch (Exception error) when (error is ArgumentException or FormatException or
                                          OverflowException or AutoTest.Projects.Project.ProjectDataException)
            {
                // A failed conversion must leave the edited source visible.
                _loadingCanData = true;
                DbcRawMode.IsChecked = DbcRawMode.IsChecked != true;
                _loadingCanData = false;
                ErrorText.Text = "切换报文编辑方式失败：" + error.Message;
                return;
            }
        }
        SignalGrid.Visibility = DbcRawMode.IsChecked == true
            ? Visibility.Collapsed : Visibility.Visible;
        DbcRawDataInput.Visibility = DbcRawMode.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateCanFramePreview();
    }

    private void KeyAction_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateKeyTimingVisibility();
    }

    private void UpdateKeyTimingVisibility()
    {
        var operation = (KeyActionInput.SelectedItem as ComboBoxItem)?.Tag as string;
        // New/direct KEY_SIM steps have only two regular inputs: 20 Key mask
        // and hold time. Keep the operation selector only for existing legacy
        // Pulse/Release actions, so opening/saving cannot silently change them.
        KeyActionInput.Visibility = _selectedTarget?.Mapping.Kind == TestTargetKind.RawKey &&
            operation == "Hold" ? Visibility.Collapsed : Visibility.Visible;
        KeyDurationPanel.Visibility = operation is "Pulse" or "Hold" &&
            _selectedTarget?.Mapping.Kind == TestTargetKind.RawKey || operation == "Pulse"
            ? Visibility.Visible : Visibility.Collapsed;
        if (operation == "Release" && _selectedTarget?.Mapping.Kind == TestTargetKind.RawKey)
            RawKeyMaskPanel.Visibility = Visibility.Collapsed;
        else if (_selectedTarget?.Mapping.Kind == TestTargetKind.RawKey)
            RawKeyMaskPanel.Visibility = Visibility.Visible;
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
            ErrorText.Text = "请选择有效的步骤操作类型，然后重试保存。";
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
                    var ext = ExtendedFrame.IsChecked == true;
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
                        throw new ArgumentException("所选 DBC 报文定义不可读取，请重新选择。");
                    string specification;
                    if (DbcRawMode.IsChecked == true)
                        specification = "HEX:" + Convert.ToHexString(
                            ParseHex(DbcRawDataInput.Text, message.Dlc));
                    else
                    {
                        if (SignalValues.Any(x => string.IsNullOrWhiteSpace(x.Value)))
                            throw new ArgumentException("按 DBC 编辑时必须填写所有信号的物理值；或切换为整帧 HEX。");
                        specification = string.Join(";", SignalValues.Select(x =>
                            x.Name + "=" + x.Value.Trim()));
                    }
                    _ = DbcCanMessageEncoder.Encode(message, specification);
                    node = node.WithProperty("value", specification);
                }
                else if (mapping?.Kind is TestTargetKind.Button or TestTargetKind.RawKey)
                {
                    var operation = (KeyActionInput.SelectedItem as ComboBoxItem)?.Tag as string
                        ?? throw new ArgumentException("请选择短按、保持或释放。");
                    if (mapping.Kind == TestTargetKind.RawKey)
                    {
                        // Empty 20-switch combination is the explicit all-off
                        // action, represented by the existing Release wire syntax.
                        var value = operation == "Release" ? "Release" :
                            operation + ":0x" + _selectedMask.ToString("X5") +
                            ((operation == "Pulse" || !string.IsNullOrWhiteSpace(KeyDurationInput.Text))
                                ? ":" + KeyDurationInput.Text.Trim() : "");
                        if (_legacySourceType == "KeySet" &&
                            string.IsNullOrWhiteSpace(KeyDurationInput.Text))
                            throw new ArgumentException("旧版按键步骤必须核对并填写保持时间。");
                        _ = AutoTest.Devices.Station.RawKeyAction.Parse(value);
                        node = node.WithProperty("value", value);
                    }
                    else
                    {
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
                ErrorText.Text = "步骤属性未应用。请检查测试程序状态栏中的错误，修改后重试。";
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
