using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace AutoTest.UiPreview;

/// <summary>Editable, UI-only property field. This type does not reference any device or TEST service.</summary>
public sealed class PreviewPropertyField : INotifyPropertyChanged
{
    private readonly Action<PreviewPropertyField> _changed;
    private string _value;
    private string _error = "";

    public PreviewPropertyField(string label, string key, string value, string[]? options,
        bool rebuildOnChange, Action<PreviewPropertyField> changed)
    {
        Label = label;
        Key = key;
        _value = value;
        Options = options ?? [];
        IsChoice = options is not null;
        RebuildOnChange = rebuildOnChange;
        _changed = changed;
        Validate();
    }

    public string Label { get; }
    public string Key { get; }
    public string[] Options { get; }
    public bool IsChoice { get; }
    public bool IsText => !IsChoice;
    public bool RebuildOnChange { get; }
    public string Error { get => _error; private set { if (_error != value) { _error = value; Notify(); Notify(nameof(HasError)); } } }
    public bool HasError => Error.Length != 0;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Notify();
            Validate();
            _changed(this);
        }
    }

    private void Validate()
    {
        // A preview-only syntax check, not ECU range validation.
        if (Key is "ms" or "duration" or "timeout" or "period" or "count")
        {
            if (!int.TryParse(_value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                || n < 0 || (Key == "count" && n == 0))
                Error = Key == "count" ? "请输入大于 0 的整数" : "请输入非负整数（ms）";
            else Error = "";
        }
        else Error = "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>
/// Maps the selected step into a typed field collection for the right-side inspector.
/// Only selected-step properties are edited; sample options are not real DBC/UDS resources.
/// </summary>
public sealed class PreviewPropertyEditor
{
    public ObservableCollection<PreviewPropertyField> Fields { get; } = new();
    private DemoStep? _step;
    private Action<DemoStep, PreviewPropertyField>? _onChanged;

    public void Bind(DemoStep? step, Action<DemoStep, PreviewPropertyField> onChanged)
    {
        _step = step;
        _onChanged = onChanged;
        Rebuild();
    }

    public void Rebuild()
    {
        Fields.Clear();
        var step = _step;
        if (step is null) return;

        void Text(string label, string key, string fallback) =>
            Fields.Add(new PreviewPropertyField(label, key, step.Param(key, fallback), null, false, Changed));
        void Choice(string label, string key, string[] options, bool rebuild = false) =>
            Fields.Add(new PreviewPropertyField(label, key, step.Param(key, options[0]), options, rebuild, Changed));

        switch (step.Kind)
        {
            case "ign": Choice("请求输出", "state", ["ON", "OFF"]); break;
            case "sol": Choice("夹具请求", "state", ["CLAMP", "OPEN"]); break;
            case "can":
                Choice("编辑方式", "mode", ["DBC 报文及信号", "原始 CAN 帧"], true);
                if (step.Param("mode", "DBC 报文及信号") == "原始 CAN 帧")
                {
                    Text("CAN ID", "id", "0x100");
                    Text("数据（Hex）", "data", "00 00 00 00 00 00 00 00");
                }
                else
                {
                    Choice("DBC 报文", "message", ["未选择报文", "Debug_702 · 0x702", "EPB_VCU_Status · 0xCFE2750"]);
                    Text("KeyState（物理值）", "signal1", "0");
                    Text("key.Release_out（物理值）", "signal2", "1");
                }
                Text("发送周期（ms；0 为单次）", "period", "0");
                break;
            case "canwait":
                Choice("目标 CAN 报文", "message", ["未选择报文", "Debug_702 · 0x702", "EPB_VCU_Status · 0xCFE2750"]);
                Text("超时时间（ms）", "timeout", "1000"); break;
            case "input": Choice("按键动作", "button", ["未选择按键动作", "驻车按键", "释放按键"]); break;
            case "read": Text("DBC 信号名称", "signal", "Signal_A"); Text("保存到变量", "variable", "$actual"); break;
            case "expect": Text("判定表达式", "expression", "$actual >= 0"); break;
            case "if": Text("布尔条件", "condition", "$actual > 2"); break;
            case "when": Text("布尔条件", "condition", "$actual > 2"); Text("等待超时（ms）", "timeout", "1000"); break;
            case "loop": Text("重复次数", "count", "3"); break;
            case "wait": Text("等待时长（ms）", "ms", "200"); break;
            case "switch": Text("选择表达式", "expression", "$state"); break;
            case "wave": Text("采集信号", "signal", "Signal_A"); Text("窗口时长（ms）", "duration", "1200"); break;
            case "wavecheck": Text("参考波形", "reference", "参考记录01"); break;
            case "uds": Choice("UDS 服务配置", "job", ["未选择", "读取标识信息", "读取故障码"]); break;
            case "var": Text("变量名称", "variable", "$value"); Text("表达式", "expression", "0"); break;
            case "try": Choice("异常处理方式", "onError", ["停止并记录", "继续并记录", "转入异常分支"]); break;
            case "sub": Text("子程序引用", "target", ""); break;
            default: Text("备注或表达式", "text", ""); break;
        }
    }

    private void Changed(PreviewPropertyField field)
    {
        if (_step is not { } step) return;
        step.SetParam(field.Key, field.Value);
        _onChanged?.Invoke(step, field);
        // The view schedules a mode-dependent field rebuild after binding completes.
    }
}
