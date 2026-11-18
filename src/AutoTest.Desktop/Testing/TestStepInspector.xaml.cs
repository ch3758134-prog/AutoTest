using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Configuration;
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
        "can" or "can-send" or "can-wait" or "read" or "wait-signal" or "assert-signal" or "expect"
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

public sealed class TestStepInputField : BindableBase
{
    private string _value;
    public TestStepInputField(string key, string value, string? label = null,
        string stepType = "")
    {
        Key = key;
        Label = label ?? StepUiCatalog.PropertyLabel(key);
        _value = value;
        Choices = key switch
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
        };
    }
    public string Key { get; }
    public string Label { get; }
    public IReadOnlyList<TestStepFieldChoice> Choices { get; }
    public bool HasChoices => Choices.Count > 0;
    public string Value { get => _value; set => SetProperty(ref _value, value ?? ""); }
}
public sealed class TestSignalInputField : BindableBase
{
    private string _value;
    public TestSignalInputField(DbcSignal signal, string value, string? displayName = null)
    {
        Name = signal.Name; DisplayName = displayName ?? signal.Name; Unit = signal.Unit;
        Range = signal.Minimum.ToString("G", CultureInfo.InvariantCulture) +
            " ～ " + signal.Maximum.ToString("G", CultureInfo.InvariantCulture);
        _value = value;
    }
    public string Name { get; }
    public string DisplayName { get; }
    public string Unit { get; }
    public string Range { get; }
    public string Value { get => _value; set => SetProperty(ref _value, value ?? ""); }
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
            "选 DBC 报文后逐个编辑物理信号；没有 DBC 可直接填写 CAN1 原始帧。"),
        new("key", "外部操作", "按键动作", "Write", "button",
            "可使用已配置功能按键，或直接操作 KEY_SIM 的 20 个电阻开关。"),
        new("ign", "外部操作", "切换 IGN", "Write", "ign", "切换固定台架的 IGN 输出。"),
        new("read", "读取与观察", "读取 CAN 信号", "Read", "signal",
            "通过已导入的 DBC 选择信号，将实际读取值保存到变量，不自动判定。"),
        new("wait-signal", "读取与观察", "等待信号条件", "WaitUntil", "signal",
            "持续观察指定 DBC 信号直到满足条件或超时。"),
        new("assert-signal", "分析与判断", "CAN 信号判定", "AssertSignal", "signal",
            "选择 DBC 信号及目标值，规定时间内达到条件判定通过；超时且未匹配判定失败，完全无有效数据则无法判定。"),
        new("wave-start", "读取与观察", "开始波形采集", "WaveStart", "signal",
            "从共享 CAN1/DBC 信号流采集真实时序数据。"),
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
        new("call", "流程控制", "调用子程序", "CallSubroutine", "",
            "必须明确填写子程序引用，执行未接线。"),
        new("can-send", "外部操作", "CAN 发送（扩展步骤）", "CanSend", "",
            "编辑占位字段，运行前必须正式绑定 DBC / CAN 资源。"),
        new("can-wait", "读取与观察", "等待 CAN 报文（执行未接线）", "CanWait", "",
            "编辑目标报文和超时，未接线时不能运行。"),
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
    private string _targetInstruction = "";
    private string _targetRevisionWarning = "";
    private uint _selectedMask;
    private readonly CheckBox[] _rawKeys = new CheckBox[20];
    private bool _updatingRawKeys;
    private bool _convertedLegacy;


     public ObservableCollection<MappingChoice> AvailableTargets { get; } = new();
    public ObservableCollection<TestStepInputField> ParameterFields { get; } = new();
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
        string? initialActionKey = null, bool caseOnly = false)
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
        Heading.Text = "步骤属性";
        StepActionsPanel.Visibility = Visibility.Visible;
        ConfirmButton.Content = "应用属性";
        var mapping = boundMappings.FirstOrDefault(m =>
            m.Name == editing.Properties.GetValueOrDefault("target", ""));
        var targetKind = mapping?.Kind;
        var chosen = Options.FirstOrDefault(a => a.Key == initialActionKey &&
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
            TypeLabel.Text = "暂不支持的步骤类型";
            NameInput.Text = editing.Name;
            StepCode.Text = editing.Id.ToString()[^5..];
            ConfirmButton.IsEnabled = false;
            ErrorText.Text = "此 TEST 步骤类型没有已批准的编辑器，原始内容保持不变。";
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
            TypeLabel.Text = chosen.Title;
            StepCode.Text = editing.Id.ToString()[^5..];
            ActionDescription.Text = chosen.Description;
            TypeLabel.ToolTip = chosen.Description;
            NameInput.Text = editing.Name;
            BuildParameters(chosen);
            BuildTargets(chosen);
        }
        AddHandler(TextBox.TextChangedEvent,
            new TextChangedEventHandler((sender, e) =>
            {
                if (ReferenceEquals(e.OriginalSource, TargetSearch)) return;
                MarkDraftChanged();
            }), true);
        AddHandler(Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler((sender, e) => MarkDraftChanged()), true);
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
        var needsLegacyReview = StepUiCatalog.RequiresLegacyReview(node);
        ImportWarning.Visibility = needsLegacyReview ? Visibility.Visible : Visibility.Collapsed;
        ConvertImportedAssertionButton.Visibility = needsLegacyReview &&
            !_convertedLegacy && (node.Type is "Read" or "WaitUntil") &&
            node.Properties.TryGetValue("expectation", out var legacyExpected) &&
            !string.IsNullOrWhiteSpace(legacyExpected)
            ? Visibility.Visible : Visibility.Collapsed;
        if (needsLegacyReview)
            ImportWarning.Text = _convertedLegacy
                ? "正在重建旧版 CAN 解析判定：选择同 CAN ID 的 DBC 信号、核对期望值和超时后应用。旧版启用标记不会自动继承；本次重建完成后，新判定将参与执行。"
                : "此步骤来自旧测试文件，尚未验证，不能执行。" +
                  " 原始记录：" + node.Properties.GetValueOrDefault("canId", "未知报文") +
                  "；信号：" + node.Properties.GetValueOrDefault("displayName", "待核对") +
                  "；期望：" + node.Properties.GetValueOrDefault("expectation", "未提供") + "。";
        foreach (var item in node.Properties.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
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
                node.Properties.GetValueOrDefault("conditionTimeoutMs", "5000"));
            ParameterFields.Clear();
            ParameterFields.Add(new TestStepInputField("operator", compare, stepType: "AssertSignal"));
            ParameterFields.Add(new TestStepInputField("expected", expected, stepType: "AssertSignal"));
            ParameterFields.Add(new TestStepInputField("timeoutMs", timeout, stepType: "AssertSignal"));
        }
        else
        {
            // Imported steps often lack the formally required parameters.
            // Render them so the engineer can correct the TEST resource, not a raw JSON field.
            var descriptor = StepDescriptorCatalog.Builtins().Lookup(action.StepType);
            if (descriptor is not null)
                foreach (var key in descriptor.RequiredProperties)
                {
                    if (key == "target" && !string.IsNullOrEmpty(action.TargetCategory)) continue;
                    if (key == "value" && action.StepType == "Write") continue;
                    if (ParameterFields.Any(f => f.Key == key)) continue;
                    var defaultValue = StepUiCatalog.NewNode(action.StepType)
                        .Properties.GetValueOrDefault(key, "");
                    ParameterFields.Add(new TestStepInputField(key, defaultValue,
                        stepType: action.StepType));
                }
        }
    }

    private void ConvertImportedAssertion_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null || !StepUiCatalog.RequiresLegacyReview(_editing)) return;
        _convertedLegacy = true;
        _selected = Options.First(x => x.StepType == "AssertSignal");
        TypeLabel.Text = "CAN 信号判定 · 旧文件重建";
        BuildParameters(_selected);
        BuildTargets(_selected);
        MarkDraftChanged();
    }

    private void BuildTargets(TestActionOption action)
    {
        _allTargets.Clear();
        AvailableTargets.Clear();
        _selectedTarget = null;
        _targetRevisionWarning = "";
        TargetSearch.Text = "";
        TargetPanel.Visibility = string.IsNullOrEmpty(action.TargetCategory)
            ? Visibility.Collapsed : Visibility.Visible;
        RawCanPanel.Visibility = DbcCanPanel.Visibility =
            KeyPanel.Visibility = IgnitionPanel.Visibility = Visibility.Collapsed;
        if (TargetPanel.Visibility == Visibility.Collapsed) return;

        bool Eligible(TestTargetMapping mapping) => action.TargetCategory switch
        {
            "can" => mapping.Kind is TestTargetKind.CanMessage or TestTargetKind.RawCan,
            "button" => mapping.Kind is TestTargetKind.Button or TestTargetKind.RawKey,
            "ign" => mapping.Kind == TestTargetKind.Ignition,
            "signal" => mapping.Kind == TestTargetKind.Signal,
            "wave" => mapping.Kind == TestTargetKind.WaveProfile,
            _ => false
        };
        foreach (var item in _catalog.Where(x => Eligible(x.Mapping))) _allTargets.Add(item);
        if (action.TargetCategory == "can")
            _allTargets.Add(new MappingChoice("CAN1 / 原始报文（无需 DBC）",
                new TestTargetMapping("原始 CAN1", TestTargetKind.RawCan, default, 0, "",
                    BusRole.DutCan, 0)));
        else if (action.TargetCategory == "button")
            _allTargets.Add(new MappingChoice("KEY_SIM / 直接操作 20 Key（高级）",
                new TestTargetMapping("原始 KEY_SIM 20 Key", TestTargetKind.RawKey,
                    default, 0, "", BusRole.KeypadCan)));

        foreach (var mapping in _boundMappings.Where(Eligible))
            if (!_allTargets.Any(x => x.Mapping == mapping))
                _allTargets.Add(new MappingChoice("已绑定 / " + mapping.Name,
                    mapping));
        _targetInstruction = action.TargetCategory switch
        {
            "can" => "选 DBC 报文可直接编辑所有信号；没有 DBC 可选原始 CAN1 报文。发送只会在明确运行授权后发生。",
            "button" => "推荐在 ECU 输入配置中先定义功能按键；也可以直接选择底层 20 Key 操作。",
            "signal" => "需要 DBC 信号。若列表为空，请先在工程资源中导入 DBC，再创建该步骤。",
            "wave" => "分析方案是可选项；若不选，请在下方填写已建立的命名参考。",
            "ign" => "当前固定台架的 IGN 控制。配置步骤不会立即操作硬件。",
            _ => ""
        };
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
        TargetHint.Text = _targetInstruction + _targetRevisionWarning;
    }

    private void TargetSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || TargetPicker is null || _selected is null) return;
        ApplyTargetFilter(TargetSearch.Text.Trim(), _selectedTarget?.Mapping);
    }

    private void ApplyTargetFilter(string query, TestTargetMapping? preferredMapping,
        string? preferredName = null)
    {
        AvailableTargets.Clear();
        foreach (var item in _allTargets.Where(x =>
            x.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            x.Mapping.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            AvailableTargets.Add(item);
        var preferred = preferredMapping is null ? null :
            AvailableTargets.FirstOrDefault(x => x.Mapping == preferredMapping);
        preferred ??= AvailableTargets.FirstOrDefault(x => x.Mapping.Name == preferredName);
        // Avoid silently binding the first DBC signal/message when a new
        // STEP has not explicitly chosen its operation object.
        // A unique IGN / raw-only option may be preselected for convenience.
        if (_convertedLegacy && _editing is { } imported)
        {
            var rawId = imported.Properties.GetValueOrDefault("canId", "").Trim();
            if (rawId.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) rawId = rawId[2..];
            if (uint.TryParse(rawId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            {
                var matches = AvailableTargets.Where(x =>
                    x.Mapping.Kind == TestTargetKind.Signal && x.Mapping.CanId == id).ToArray();
                preferred = matches.Length == 1 ? matches[0] : null;
            }
            else preferred = null;
        }
        TargetPicker.SelectedItem = preferred ??
            (_convertedLegacy ? null : AvailableTargets.Count == 1 ? AvailableTargets[0] : null);
        UpdateTarget();
        TargetHint.Text = AvailableTargets.Count == 0
            ? "没有匹配的对象。请调整搜索词，或先在工程中导入相应 DBC / 功能配置。"
            : _targetInstruction + _targetRevisionWarning;
    }

    private void TargetPicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateTarget();
    }

    private void UpdateTarget()
    {
        _selectedTarget = TargetPicker.SelectedItem as MappingChoice;
        RawCanPanel.Visibility = DbcCanPanel.Visibility = KeyPanel.Visibility =
            IgnitionPanel.Visibility = Visibility.Collapsed;
        SignalValues.Clear();
        if (_selectedTarget is null) return;
        var mapping = _selectedTarget.Mapping;
        if (mapping.Kind == TestTargetKind.RawCan)
        {
            RawCanPanel.Visibility = Visibility.Visible;
            FrameIdInput.Text = _editing is not null && mapping.Name ==
                _editing.Properties.GetValueOrDefault("target", "")
                ? "0x" + mapping.CanId.ToString("X") : "";
            ExtendedFrame.IsChecked = mapping.Extended;
            RawCanDataInput.Text = (_editing?.Properties.GetValueOrDefault("value", "") ?? "")
                .Replace("HEX:", "", StringComparison.OrdinalIgnoreCase);
        }
        else if (mapping.Kind == TestTargetKind.CanMessage)
        {
            DbcCanPanel.Visibility = Visibility.Visible;
            var msg = _selectedTarget.Message;
            DbcMessageInfo.Text = msg is null
                ? "DBC 资源已变更或未加载。请重新选择当前工程的报文。"
                : (_selectedTarget.DisplayNames?.MessageLabel(msg) ?? msg.Name) +
                  " · 0x" + msg.ArbitrationId.ToString("X") +
                  " · DLC " + msg.Dlc + " · " + (msg.IsExtended ? "扩展帧" : "标准帧");
            if (msg is not null)
            {
                var old = _editing?.Properties.GetValueOrDefault("value", "") ?? "";
                var dict = new Dictionary<string, string>(StringComparer.Ordinal);
                if (old.Contains('='))
                    foreach (var segment in old.Split(';',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var pos = segment.IndexOf('=');
                        if (pos > 0) dict[segment[..pos].Trim()] = segment[(pos + 1)..].Trim();
                    }
                foreach (var signal in msg.Signals)
                    SignalValues.Add(new TestSignalInputField(signal,
                        dict.GetValueOrDefault(signal.Name, ""),
                        _selectedTarget.DisplayNames?.SignalLabel(msg, signal)));
                DbcRawMode.IsChecked = old.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase) ||
                    (!old.Contains('=') && old.Length > 0);
                DbcRawDataInput.Text = old.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase)
                    ? old[4..] : DbcRawMode.IsChecked == true ? old : "";
            }
        }
        else if (mapping.Kind is TestTargetKind.Button or TestTargetKind.RawKey)
        {
            KeyPanel.Visibility = Visibility.Visible;
            RawKeyMaskPanel.Visibility = mapping.Kind == TestTargetKind.RawKey
                ? Visibility.Visible : Visibility.Collapsed;
            var old = _editing?.Properties.GetValueOrDefault("value", "") ?? "";
            var parts = old.Split(':', StringSplitOptions.TrimEntries);
            var operation = parts[0] is "Hold" or "Release" ? parts[0] : "Pulse";
            KeyActionInput.SelectedItem = KeyActionInput.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(x => (string?)x.Tag == operation);
            var selectedMask = mapping.Kind == TestTargetKind.RawKey && parts.Length > 1 &&
                parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(parts[1][2..], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var mask) ? mask : 0;
            SetRawKeyMask(selectedMask);
            KeyDurationInput.Text = operation == "Pulse" && mapping.Kind == TestTargetKind.RawKey &&
                parts.Length == 3 ? parts[2] :
                operation == "Pulse" && mapping.Kind == TestTargetKind.Button &&
                parts.Length == 2 ? parts[1] : "";
            UpdateKeyTimingVisibility();
        }
        else if (mapping.Kind == TestTargetKind.Ignition)
        {
            IgnitionPanel.Visibility = Visibility.Visible;
            var old = _editing?.Properties.GetValueOrDefault("value", "ON") ?? "ON";
            IgnitionState.SelectedIndex = old.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
    }

    private void DbcRawMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || SignalGrid is null) return;
        SignalGrid.Visibility = DbcRawMode.IsChecked == true
            ? Visibility.Collapsed : Visibility.Visible;
        DbcRawDataInput.Visibility = DbcRawMode.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void KeyAction_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateKeyTimingVisibility();
    }

    private void UpdateKeyTimingVisibility()
    {
        var operation = (KeyActionInput.SelectedItem as ComboBoxItem)?.Tag as string;
        KeyDurationPanel.Visibility = operation == "Pulse"
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
                Type = _convertedLegacy ? "AssertSignal" : _selected.StepType
            };
            foreach (var field in ParameterFields)
                node = node.WithProperty(field.Key, field.Value.Trim());

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
                    mapping = mapping with { Name = "RAW-CAN1-0x" + id.ToString("X") +
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
                        var value = operation == "Release" ? "Release" :
                            operation + ":0x" + _selectedMask.ToString("X5") +
                            (operation == "Pulse" ? ":" + KeyDurationInput.Text.Trim() : "");
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
            if (node.Type == "AssertSignal" && mapping?.Kind != TestTargetKind.Signal)
                throw new ArgumentException("CAN 信号判定必须选择 DBC 信号。");
            if (_convertedLegacy)
            {
                if (mapping is null || _editing is null)
                    throw new ArgumentException("先选择与旧报文对应的 DBC 信号。");
                var rawId = _editing.Properties.GetValueOrDefault("canId", "").Trim();
                if (rawId.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) rawId = rawId[2..];
                if (!uint.TryParse(rawId, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out var originalId) || mapping.CanId != originalId)
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
