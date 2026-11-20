using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AutoTest.Desktop.Core;

// Pure UI authoring objects. These types are NOT TEST/PROJECT models or a second executable DSL.
// They exist only to exercise complete UI interactions before the approved backend integration.
public abstract class Notify : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Change<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        return true;
    }
    public void Touch([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public sealed class UiResource : Notify
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _name = "";
    public string Name { get => _name; set => Change(ref _name, value); }
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
}
public sealed class UiStep : Notify
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _name = "";
    private bool _enabled = true;
    private bool _breakpoint;
    public string Name { get => _name; set => Change(ref _name, value); }
    public string Type { get; set; } = "Wait";
    public bool Breakpoint { get => _breakpoint; set => Change(ref _breakpoint, value); }
    public string Phase { get; set; } = "测试";
    public string Branch { get; set; } = "";
    public bool Enabled { get => _enabled; set => Change(ref _enabled, value); }
    public ObservableCollection<UiStepProperty> Parameters { get; set; } = [];
    public ObservableCollection<UiStep> Children { get; set; } = [];
    public string Label => $"{Type} · {Name}";
}
public sealed class UiStepProperty : Notify
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    private string _value = "";
    public string Value { get => _value; set => Change(ref _value, value); }
}
public sealed class UiTestCase : Notify
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _name = "未命名测试用例";
    private bool _selectedForRun = true;
    public string Name { get => _name; set => Change(ref _name, value); }
    public bool SelectedForRun { get => _selectedForRun; set => Change(ref _selectedForRun, value); }
    public ObservableCollection<UiStep> Prepare { get; set; } = [];
    public ObservableCollection<UiStep> Test { get; set; } = [];
    public ObservableCollection<UiStep> Cleanup { get; set; } = [];
}
public sealed class UiTestProject : Notify
{
    private string _name = "测试项目";
    public string Name { get => _name; set => Change(ref _name, value); }
    public ObservableCollection<UiTestCase> Cases { get; set; } = [];
}
public sealed class UiCanMessage : Notify
{
    private string _name = "新报文", _id = "0x000", _data = "00 00 00 00 00 00 00 00";
    private bool _periodic, _enabled = true;
    private int _period = 100;
    public string Name { get => _name; set => Change(ref _name, value); }
    public string Id { get => _id; set => Change(ref _id, value); }
    public string Data { get => _data; set => Change(ref _data, value); }
    public bool Enabled { get => _enabled; set => Change(ref _enabled, value); }
    public bool Periodic { get => _periodic; set => Change(ref _periodic, value); }
    public int PeriodMs { get => _period; set => Change(ref _period, value); }
}
public sealed class UiDbcSignal : Notify
{
    private string _name = "Signal", _alias = "", _unit = "";
    private int _startBit, _length = 8;
    private double _factor = 1, _offset;
    public string Name { get => _name; set => Change(ref _name, value); }
    public string Alias { get => _alias; set => Change(ref _alias, value); }
    public string Unit { get => _unit; set => Change(ref _unit, value); }
    public int StartBit { get => _startBit; set => Change(ref _startBit, value); }
    public int Length { get => _length; set => Change(ref _length, value); }
    public double Factor { get => _factor; set => Change(ref _factor, value); }
    public double Offset { get => _offset; set => Change(ref _offset, value); }
}
public sealed class UiDbcMessage : Notify
{
    private string _name = "New_Message", _canId = "0x100";
    private int _dlc = 8;
    public string Name { get => _name; set => Change(ref _name, value); }
    public string CanId { get => _canId; set => Change(ref _canId, value); }
    public int Dlc { get => _dlc; set => Change(ref _dlc, value); }
    public ObservableCollection<UiDbcSignal> Signals { get; set; } = [];
}
public sealed class UiUdsRequest : Notify
{
    private string _name = "新诊断请求", _service = "0x22", _payload = "", _expected = "正响应";
    public string Name { get => _name; set => Change(ref _name, value); }
    public string Service { get => _service; set => Change(ref _service, value); }
    public string Payload { get => _payload; set => Change(ref _payload, value); }
    public string Expected { get => _expected; set => Change(ref _expected, value); }
}
public sealed class UiKeySwitch : Notify
{
    public int Number { get; set; }
    private string _label = "";
    private bool _closed;
    public string Label { get => _label; set => Change(ref _label, value); }
    public bool Closed { get => _closed; set => Change(ref _closed, value); }
}
public sealed class UiKeyAction : Notify
{
    private string _name = "按键动作";
    private int _holdMs = 500;
    public string Name { get => _name; set => Change(ref _name, value); }
    public int HoldMs { get => _holdMs; set => Change(ref _holdMs, value); }
    public ObservableCollection<int> ClosedKeys { get; set; } = [];
    public string Summary => ClosedKeys.Count == 0 ? "20 Key 全释放" :
        string.Join(", ", ClosedKeys.Order()) + " 闭合";
}
public sealed class UiWaveSignal : Notify
{
    private string _name = "新信号";
    private bool _visible = true;
    private string _source = "UI 手工数据";
    private string _points = "";
    public string Name { get => _name; set => Change(ref _name, value); }
    public bool Visible { get => _visible; set => Change(ref _visible, value); }
    public string Source { get => _source; set => Change(ref _source, value); }
    public string Points { get => _points; set => Change(ref _points, value); }
}
public sealed class UiInspectorBuffer
{
    public string Name { get; set; } = "";
    public Dictionary<string,string> Values { get; set; } = new(StringComparer.Ordinal);
}
public sealed class UiFlashProfile : Notify
{
    private string _name="新建烧录配置", _imagePath="", _appRequest="18DA50F1",
        _appResponse="18DAF150", _bootRequest="18DA50F1", _bootResponse="18DAF150";
    public string Name { get=>_name; set=>Change(ref _name,value); }
    public string ImagePath { get=>_imagePath; set=>Change(ref _imagePath,value); }
    public string AppRequest { get=>_appRequest; set=>Change(ref _appRequest,value); }
    public string AppResponse { get=>_appResponse; set=>Change(ref _appResponse,value); }
    public string BootRequest { get=>_bootRequest; set=>Change(ref _bootRequest,value); }
    public string BootResponse { get=>_bootResponse; set=>Change(ref _bootResponse,value); }
}
public sealed class UiProductionDraft : Notify
{
    private string _serial="", _station="当前工位";
    public string Serial { get=>_serial; set=>Change(ref _serial,value); }
    public string Station { get=>_station; set=>Change(ref _station,value); }
}
public sealed class UiWaveReference : Notify
{
    private string _name="", _tolerance="0.05", _coverage="0.95";
    public string Name { get=>_name; set=>Change(ref _name,value); }
    public string Tolerance { get=>_tolerance; set=>Change(ref _tolerance,value); }
    public string Coverage { get=>_coverage; set=>Change(ref _coverage,value); }
}
public sealed class UiStore : Notify
{
    private string _workspace = "未命名工作环境";
    private string _status = "纯 UI 工作会话 · 未连接实体设备";
    private bool _dirty;
    public string Workspace { get => _workspace; set { if (Change(ref _workspace, value)) Dirty = true; } }
    public string Status { get => _status; set => Change(ref _status, value); }
    public bool Dirty { get => _dirty; set => Change(ref _dirty, value); }
    public ObservableCollection<UiResource> Resources { get; } = [];
    public ObservableCollection<UiTestProject> Projects { get; } = [];
    public ObservableCollection<UiTestCase> Cases { get; } = [];
    public ObservableCollection<UiStep> PublicFlow { get; } = [];
    public ObservableCollection<UiCanMessage> CanMessages { get; } = [];
    public ObservableCollection<UiDbcMessage> DbcMessages { get; } = [];
    public ObservableCollection<UiUdsRequest> UdsRequests { get; } = [];
    public ObservableCollection<UiKeyAction> KeyActions { get; } = [];
    public ObservableCollection<UiWaveSignal> WaveSignals { get; } = [];
    public UiFlashProfile FlashProfile { get; } = new();
    public UiProductionDraft ProductionDraft { get; } = new();
    public UiWaveReference WaveReference { get; } = new();
    public Dictionary<string,UiInspectorBuffer> InspectorBuffers { get; } = new(StringComparer.Ordinal);
    public ObservableCollection<string> OperatorNotes { get; } = [];
    public bool[] KeyStates { get; } = new bool[20];
    public ObservableCollection<UiKeySwitch> KeySwitches { get; } =
        new(Enumerable.Range(1, 20).Select(i => new UiKeySwitch
        { Number = i, Label = "K" + i.ToString("00") }));
    public void SetKeyState(int zeroBased, bool closed)
    {
        if (zeroBased is < 0 or >= 20) return;
        KeyStates[zeroBased] = closed;
        KeySwitches[zeroBased].Closed = closed;
        Changed((closed ? "勾选" : "释放") + " KEY_SIM " + KeySwitches[zeroBased].Label);
    }
    public int HoldMs { get; set; } = 500;
    public bool IgnOn { get; set; }
    public bool FixtureEngaged { get; set; }
    public UiStore()
    {
        // Empty-by-default: no synthetic DUT, CAN trace, measurement or PASS record.
        var current = new UiTestCase();
        Cases.Add(current);
        var project = new UiTestProject();
        project.Cases.Add(current);
        Projects.Add(project);
        FlashProfile.PropertyChanged += (_, _) => Changed("修改烧录配置 UI");
        ProductionDraft.PropertyChanged += (_, _) => Changed("修改生产输入 UI");
        WaveReference.PropertyChanged += (_, _) => Changed("修改参考配置 UI");
    }
    public void Changed(string action)
    {
        Dirty = true;
        Status = "UI 编辑：" + action + " · 不触发实体设备";
    }
    public UiTestCase NewCase()
    {
        var c = new UiTestCase { Name = "新建测试用例" };
        Cases.Add(c);
        Projects[0].Cases.Add(c);
        Resources.Add(new UiResource { Name = c.Name, Kind = "测试用例" });
        Changed("新建测试用例");
        return c;
    }
    public UiStep NewStep(string type, string phase)
    {
        var s = new UiStep { Type = type, Phase = phase, Name = type switch
        {
            "Wait" => "等待", "If" => "条件判断", "Switch" => "多路选择",
            "Loop" => "循环", "Write" => "输出操作", "Read" => "读取信号",
            "AssertSignal" => "信号判定", "CanSend" => "发送 CAN", "UdsRequest" => "诊断请求",
            "Flash" => "ECU 烧录", "WaveCompare" => "波形判定",
            "CallPublicFlow" => "调用公共流程", _ => type
        } };
        (string Key, string Label, string Value)[] defaults = type switch
        {
            "Wait" => new[]{("milliseconds","等待时间 ms","500")},
            "If" or "Switch" or "When" => [("condition","条件表达式",""),("timeoutMs","超时 ms","1000")],
            "Loop" => [("count","循环次数","2")],
            "Write" => [("target","操作对象",""),("value","目标值","")],
            "Read" => [("target","DBC/数据源",""),("variable","保存变量","response")],
            "AssertSignal" => [("signal","DBC 信号",""),("expected","期望值",""),("timeoutMs","超时 ms","5000")],
            "CanSend" => [("message","DBC 报文/ID",""),("data","数据/信号",""),("periodMs","周期 ms；0=单次","0")],
            "UdsRequest" => [("service","服务标识","0x22"),("request","请求负载","")],
            "Flash" => [("profile","烧录配置","")],
            "WaveCompare" => [("reference","参考波形",""),("tolerance","容差","")],
            "CallPublicFlow" => [("flow","公共流程","")],
            _ => [("value","参数","")]
        };
        foreach (var (key, label, value) in defaults)
            s.Parameters.Add(new UiStepProperty { Key = key, Label = label, Value = value });
        return s;
    }
    public static UiStep CloneStep(UiStep step)
    {
        var copy = new UiStep { Name = step.Name + " - 副本", Type = step.Type, Phase = step.Phase, Branch = step.Branch, Enabled = step.Enabled, Breakpoint = step.Breakpoint };
        foreach (var p in step.Parameters)
            copy.Parameters.Add(new UiStepProperty { Key = p.Key, Label = p.Label, Value = p.Value });
        foreach (var c in step.Children) copy.Children.Add(CloneStep(c));
        return copy;
    }
    public static bool IsCanId(string input) =>
        TryHex(input, out var n) && n <= 0x1FFFFFFFu;
    public static bool TryHex(string input, out uint value)
    {
        var s = input.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return uint.TryParse(s, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }
    // A local UI-session snapshot; explicitly NOT a PROJECT or TEST resource.
    private sealed record Session(string Kind, string Workspace, List<UiTestCase> Cases,
        List<UiCanMessage> Can, List<UiDbcMessage> Dbc, List<UiUdsRequest> Uds,
        List<UiKeyAction> Keys, List<UiWaveSignal> Wave, List<UiStep> PublicFlow,
        List<UiResource> Resources, List<UiKeySwitch> Switches, bool[] KeyStates, int HoldMs, bool IgnOn,
        bool FixtureEngaged, UiFlashProfile FlashProfile, UiProductionDraft ProductionDraft, UiWaveReference WaveReference,
        Dictionary<string,UiInspectorBuffer> InspectorBuffers);
    public void SaveUiSession(string path)
    {
        var data = new Session("AutoTest.PURE_UI_SESSION.v1", Workspace, Cases.ToList(),
            CanMessages.ToList(), DbcMessages.ToList(), UdsRequests.ToList(),
            KeyActions.ToList(), WaveSignals.ToList(), PublicFlow.ToList(), Resources.ToList(),
            KeySwitches.ToList(), KeyStates.ToArray(), HoldMs, IgnOn, FixtureEngaged,
            FlashProfile, ProductionDraft, WaveReference, InspectorBuffers);
        var serialized = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, serialized);
        Dirty = false;
        Status = "UI 操作会话已保存（非正式 AutoTest 工程）：" + Path.GetFileName(path);
    }
    public void OpenUiSession(string path)
    {
        var data = JsonSerializer.Deserialize<Session>(File.ReadAllText(path));
        if (data?.Kind != "AutoTest.PURE_UI_SESSION.v1") throw new InvalidDataException("不是合法的 UI 会话。");
        Cases.Clear(); CanMessages.Clear(); DbcMessages.Clear(); UdsRequests.Clear();
        KeyActions.Clear(); WaveSignals.Clear(); Projects.Clear(); Resources.Clear(); PublicFlow.Clear();
        foreach (var x in data.Cases) Cases.Add(x);
        if (Cases.Count == 0) Cases.Add(new UiTestCase());
        var p = new UiTestProject();
        foreach (var x in Cases) p.Cases.Add(x);
        Projects.Add(p);
        foreach (var x in data.Can) CanMessages.Add(x);
        foreach (var x in data.Dbc) DbcMessages.Add(x);
        foreach (var x in data.Uds) UdsRequests.Add(x);
        foreach (var x in data.Keys) KeyActions.Add(x);
        foreach (var x in data.Wave) WaveSignals.Add(x);
        foreach (var x in data.PublicFlow ?? []) PublicFlow.Add(x);
        foreach (var x in data.Resources ?? []) Resources.Add(x);
        for (var i = 0; i < KeyStates.Length; i++)
        {
            KeyStates[i] = i < (data.KeyStates?.Length ?? 0) && data.KeyStates[i];
            KeySwitches[i].Closed = KeyStates[i];
            if (i < (data.Switches?.Count ?? 0) && !string.IsNullOrWhiteSpace(data.Switches[i].Label))
                KeySwitches[i].Label = data.Switches[i].Label;
        }
        HoldMs = data.HoldMs;
        IgnOn = data.IgnOn;
        FixtureEngaged = data.FixtureEngaged;
        if (data.FlashProfile is { } flash)
        {
            FlashProfile.Name=flash.Name;FlashProfile.ImagePath=flash.ImagePath;
            FlashProfile.AppRequest=flash.AppRequest;FlashProfile.AppResponse=flash.AppResponse;
            FlashProfile.BootRequest=flash.BootRequest;FlashProfile.BootResponse=flash.BootResponse;
        }
        if (data.ProductionDraft is { } scan)
        { ProductionDraft.Serial=scan.Serial;ProductionDraft.Station=scan.Station; }
        if (data.WaveReference is { } wave)
        { WaveReference.Name=wave.Name;WaveReference.Tolerance=wave.Tolerance;WaveReference.Coverage=wave.Coverage; }
        InspectorBuffers.Clear();
        foreach(var item in data.InspectorBuffers ?? new Dictionary<string,UiInspectorBuffer>())
            InspectorBuffers[item.Key]=item.Value;
        _workspace = data.Workspace;
        Touch(nameof(Workspace));
        Dirty = false;
        Status = "UI 会话已恢复（不代表真实工程已加载）";
    }
}
