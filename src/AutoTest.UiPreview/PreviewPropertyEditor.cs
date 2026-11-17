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
        if (Key is "ms" or "duration" or "timeout" or "period" or "count"
            or "milliseconds" or "timeoutMs" or "durationMs" or "maxAgeMs")
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
    private bool _realCanEditor;

    public void Bind(DemoStep? step, Action<DemoStep, PreviewPropertyField> onChanged,
        bool realCanEditor = false)
    {
        _step = step;
        _onChanged = onChanged;
        _realCanEditor = realCanEditor;
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


        // The actual DBC selector renders message/signal editors in the same Inspector.
        // Never expose serialized editor payloads as two raw text boxes in real mode.
        if (_realCanEditor && step.Kind == "CanSend")
        {
            foreach (var pair in step.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
                if (pair.Key is not ("dbcMessage" or "signals" or "period"))
                    Text(pair.Key, pair.Key, pair.Value);
            return;
        }

        // Formal TEST step types use the exact canonical StepUiCatalog property keys.
        // Never offer hard-coded sample DBC messages or pretend a target Mapping exists.
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

            case "Wait": Text("等待时间（毫秒）", "milliseconds", "500"); break;
            case "SetVariable": Text("变量名称", "variable", "value"); Text("设置值", "value", "1"); break;
            case "If": Text("条件变量", "variable", "value"); Text("相等比较值", "equals", "1"); break;
            case "Loop": Text("循环次数", "count", "2"); break;
            case "Switch": Text("选择变量", "variable", ""); break;
            case "Case": Text("匹配值", "equals", ""); break;
            case "When":
                Text("条件变量", "variable", ""); Text("相等比较值", "equals", "");
                Text("超时（毫秒）", "timeoutMs", "1000"); break;
            case "CallSubroutine": Text("子程序引用", "target", ""); break;
            case "Write":
                Text("已映射操作对象", "target", "");
                Text("写入值", "value", "Pulse"); break;
            case "Read":
                Text("已映射目标", "target", "");
                Text("保存变量", "variable", "response");
                Choice("读取模式", "mode", ["Next", "Latest"]);
                Text("超时（毫秒）", "timeoutMs", "1000");
                Text("最新值年龄（毫秒）", "maxAgeMs", "500"); break;
            case "WaitUntil":
                Text("已映射目标", "target", "");
                Text("保存变量", "variable", "response");
                Text("比较运算符", "operator", "==");
                Text("期望值", "expected", "1");
                Text("超时（毫秒）", "timeoutMs", "1000"); break;
            case "Calculate":
                Text("保存变量", "variable", "value");
                Text("左操作数", "left", "$response");
                Text("运算符", "operator", "+");
                Text("右操作数", "right", "1"); break;
            case "Expect":
                Text("左操作数", "left", "$response");
                Text("比较运算符", "operator", "==");
                Text("右操作数", "right", "1"); break;
            case "WaveStart":
                Text("已映射信号", "target", "");
                Text("采集名称", "captureKey", "capture1");
                Text("最长时长（毫秒）", "durationMs", "3000"); break;
            case "WaveStop":
                Text("采集名称", "captureKey", "capture1");
                Text("样本数保存变量", "variable", "samples"); break;
            case "WaveAnalyze":
                Text("采集名称", "captureKey", "capture1");
                Text("结果保存变量", "variable", "result");
                Text("参考波形", "reference", "");
                Text("波形配置", "profile", "");
                Text("绝对容差", "tolerance", "0.05");
                Text("最小覆盖率", "coverage", "0.95"); break;
            case "CanSend":
                Text("DBC 报文标识", "dbcMessage", "");
                Text("CAN 信号设置", "signals", ""); break;
            case "CanWait":
                Text("CAN 目标", "dbcMessage", "");
                Text("超时（毫秒）", "timeoutMs", "1000"); break;
            case "UdsRequest":
                Text("UDS 服务", "service", "");
                Text("请求 HEX", "requestHex", ""); break;
            case "Flash": Text("烧录配置资源", "programmingProfile", ""); break;
            case "WaveCapture": Text("采集名称", "captureKey", ""); break;
            case "WaveCompare": Text("参考波形", "reference", ""); break;
            case "Sequence":
            case "TryCatch":
            case "Subroutine":
            case "Parallel": break;
            default: break;
        }
        // Preserve every property found in a real TEST file even when it has no
        // specialized editor yet. Unknown keys must never be silently discarded.
        var shown = Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in step.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
            if (!shown.Contains(pair.Key)) Text(pair.Key, pair.Key, pair.Value);
    }

    private void Changed(PreviewPropertyField field)
    {
        if (_step is not { } step) return;
        step.SetParam(field.Key, field.Value);
        _onChanged?.Invoke(step, field);
        // The view schedules a mode-dependent field rebuild after binding completes.
    }
}
