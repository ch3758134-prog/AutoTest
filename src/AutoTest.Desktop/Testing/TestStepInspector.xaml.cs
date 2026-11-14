using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
}

public sealed class TestStepInputField : BindableBase
{
    private string _value;
    public TestStepInputField(string key, string value)
    { Key = key; Label = StepUiCatalog.PropertyLabel(key); _value = value; }
    public string Key { get; }
    public string Label { get; }
    public string Value { get => _value; set => SetProperty(ref _value, value ?? ""); }
}
public sealed class TestSignalInputField : BindableBase
{
    private string _value;
    public TestSignalInputField(DbcSignal signal, string value)
    {
        Name = signal.Name; Unit = signal.Unit;
        Range = signal.Minimum.ToString("G", CultureInfo.InvariantCulture) +
            " ～ " + signal.Maximum.ToString("G", CultureInfo.InvariantCulture);
        _value = value;
    }
    public string Name { get; }
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
        new("key", "外部操作", "模拟 ECU 按键", "Write", "button",
            "可使用已配置功能按键，或直接操作 KEY_SIM 的 20 个电阻开关。"),
        new("ign", "外部操作", "切换 IGN", "Write", "ign", "切换固定台架的 IGN 输出。"),
        new("read", "读取与观察", "读取 CAN 信号", "Read", "signal",
            "通过已导入的 DBC 选择信号，将实际读取值保存到变量，不自动判定。"),
        new("wait-signal", "读取与观察", "等待信号条件", "WaitUntil", "signal",
            "持续观察指定 DBC 信号直到满足条件或超时。"),
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
        new("wait", "流程控制", "固定等待", "Wait", "", "固定等待指定毫秒数。")
    ];

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

    public ObservableCollection<TestActionOption> FilteredActions { get; } = new();
    public ObservableCollection<MappingChoice> AvailableTargets { get; } = new();
    public ObservableCollection<TestStepInputField> ParameterFields { get; } = new();
    public ObservableCollection<TestSignalInputField> SignalValues { get; } = new();
    public TestActionOption? SelectedAction { get; set; }
    public StepCompositionResult? Result { get; private set; }
    public event Action<StepCompositionResult>? Applied;
    public event Action? Cancelled;

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancelled?.Invoke();

    public TestStepInspector(IReadOnlyList<MappingChoice> catalog,
        IReadOnlyList<TestTargetMapping> boundMappings, StepNode? editing = null,
        string? initialActionKey = null)
    {
        _catalog = catalog;
        _boundMappings = boundMappings;
        _editing = editing;
        InitializeComponent();
        DataContext = this;
        _ready = true;
        Heading.Text = editing is null ? "新步骤 · 属性配置" : "步骤属性 · 直接编辑";
        ActionSelectorPanel.Visibility = editing is null ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.Content = editing is null ? "插入步骤" : "应用属性";
        ActionSearch.IsEnabled = editing is null;
        RefreshActionList("");
        if (editing is not null)
        {
            var mapping = boundMappings.FirstOrDefault(m =>
                m.Name == editing.Properties.GetValueOrDefault("target", ""));
            var targetKind = mapping?.Kind;
            var chosen = Options.FirstOrDefault(a => a.StepType == editing.Type &&
                (editing.Type != "Write" || a.TargetCategory switch
                {
                    "button" => targetKind is TestTargetKind.Button or TestTargetKind.RawKey,
                    "ign" => targetKind == TestTargetKind.Ignition,
                    "can" => targetKind is TestTargetKind.CanMessage or TestTargetKind.RawCan,
                    _ => false
                })) ?? Options.FirstOrDefault(a => a.StepType == editing.Type);
            if (chosen is not null) ActionList.SelectedItem = chosen;
        }
        else if (FilteredActions.Count > 0)
            ActionList.SelectedItem = FilteredActions.FirstOrDefault(a => a.Key == initialActionKey)
                ?? FilteredActions[0];
        Loaded += (_, _) =>
        {
            if (editing is null && initialActionKey is null) ActionSearch.Focus();
            else NameInput.Focus();
        };
    }

    private void RefreshActionList(string query)
    {
        var previous = ActionList.SelectedItem as TestActionOption;
        FilteredActions.Clear();
        var filtered = _editing is null ? Options :
            Options.Where(a => a.StepType == _editing.Type);
        foreach (var action in filtered.Where(a =>
            a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            a.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
            FilteredActions.Add(action);
        ActionList.SelectedItem = FilteredActions.FirstOrDefault(x => x == previous);
        if (ActionList.SelectedItem is null && FilteredActions.Count > 0)
            ActionList.SelectedIndex = 0;
    }

    private void ActionSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || ActionList is null) return;
        RefreshActionList(ActionSearch.Text.Trim());
    }

    private void ActionList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ActionList.SelectedItem is not TestActionOption option) return;
        _selected = option;
        ActionDescription.Text = option.Description;
        NameInput.Text = _editing?.Name ?? option.Title;
        BuildParameters(option);
        BuildTargets(option);
    }

    private void BuildParameters(TestActionOption action)
    {
        ParameterFields.Clear();
        var node = _editing ?? StepUiCatalog.NewNode(action.StepType);
        foreach (var item in node.Properties.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (item.Key == "target" || (item.Key == "value" && action.StepType == "Write") ||
                (item.Key == "profile" && action.StepType == "WaveAnalyze")) continue;
            ParameterFields.Add(new TestStepInputField(item.Key, item.Value));
        }
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
        TargetPicker.SelectedItem = preferred ?? AvailableTargets.FirstOrDefault();
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
                : msg.Name + " · 0x" + msg.ArbitrationId.ToString("X") +
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
                        dict.GetValueOrDefault(signal.Name, "")));
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
            _selectedMask = mapping.Kind == TestTargetKind.RawKey && parts.Length > 1 &&
                parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(parts[1][2..], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var mask) ? mask : 0;
            RawKeyMaskInput.Text = "0x" + _selectedMask.ToString("X5");
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

    private void PickRawKeys_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new KeyMaskPickerDialog(_selectedMask, "直接选择 KEY_SIM 电阻开关");
        dialog.Owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        if (dialog.ShowDialog() != true) return;
        _selectedMask = dialog.SelectedMask;
        RawKeyMaskInput.Text = "0x" + _selectedMask.ToString("X5");
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try
        {
            var name = NameInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
                throw new ArgumentException("请填写步骤名称，不超过 120 字符。");
            var node = (_editing ?? StepUiCatalog.NewNode(_selected.StepType)) with { Name = name };
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
            if (node.Type == "Write" && mapping is null)
                throw new ArgumentException("请选择操作对象。");
            foreach (var required in StepDescriptorCatalog.Builtins().Lookup(node.Type)!
                .RequiredProperties)
                if (string.IsNullOrWhiteSpace(node.Properties.GetValueOrDefault(required, "")))
                    throw new ArgumentException("请填写必要参数：" + StepUiCatalog.PropertyLabel(required));

            mapping?.Validate();
            Result = new StepCompositionResult(node, mapping);
            if (Result is { } completed) Applied?.Invoke(completed);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or
                                      OverflowException or AutoTest.Projects.Project.ProjectDataException)
        {
            ErrorText.Text = "参数未完成：" + error.Message;
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
