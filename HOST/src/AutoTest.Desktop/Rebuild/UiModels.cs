using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoTest.Desktop.Rebuild;

/// <summary>These are ONLY UI study documents, not TEST/PROJECT schemas or physical facts.</summary>
public sealed class UiStudy
{
    public int SchemaVersion { get; set; } = 1;
    public string ProjectName { get; set; } = "未命名工作区";
    public List<UiTestCase> TestCases { get; set; } = [];
    public List<UiPublicFlow> PublicFlows { get; set; } = [];
    public List<UiDbcMessage> DbcMessages { get; set; } = [];
    public List<UiCanMessage> CanMessages { get; set; } = [];
    public List<UiUdsRequest> UdsRequests { get; set; } = [];
    public List<UiKeySequence> KeySequences { get; set; } = [];
    public List<UiInputName> InputNames { get; set; } = [];
    public string SelectedFirmware { get; set; } = "";
    public string FlashProfile { get; set; } = "";
    public string SelectedWaveSignal { get; set; } = "";
    public string ScanInput { get; set; } = "";
}

public sealed class UiTestCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新建测试用例";
    public bool RunSelected { get; set; } = true;
    public bool DefaultEnabled { get; set; } = true;
    public List<UiStep> Prepare { get; set; } = [];
    public List<UiStep> Test { get; set; } = [];
    public List<UiStep> Cleanup { get; set; } = [];
    public IEnumerable<UiStep> AllSteps() => Walk(Prepare).Concat(Walk(Test)).Concat(Walk(Cleanup));
    public static IEnumerable<UiStep> Walk(IEnumerable<UiStep> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Walk(node.Then)) yield return child;
            foreach (var child in Walk(node.Else)) yield return child;
            foreach (var child in Walk(node.Body)) yield return child;
        }
    }
}

public sealed class UiPublicFlow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新建公共流程";
    public List<UiStep> Steps { get; set; } = [];
}

public sealed class UiStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "Wait";
    public string Name { get; set; } = "等待";
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Properties { get; set; } = new();
    public List<UiStep> Then { get; set; } = [];
    public List<UiStep> Else { get; set; } = [];
    public List<UiStep> Body { get; set; } = [];

    public UiStep CloneFresh()
    {
        var copy = JsonSerializer.Deserialize<UiStep>(JsonSerializer.Serialize(this))!;
        void Renew(UiStep item)
        {
            item.Id = Guid.NewGuid();
            foreach (var child in item.Then.Concat(item.Else).Concat(item.Body)) Renew(child);
        }
        Renew(copy);
        return copy;
    }
}

public sealed class UiDbcMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New_Message";
    public string CanId { get; set; } = "0x100";
    public int Length { get; set; } = 8;
    public int CycleMs { get; set; }
    public List<UiDbcSignal> Signals { get; set; } = [];
}
public sealed class UiDbcSignal
{
    public string Name { get; set; } = "New_Signal";
    public string Alias { get; set; } = "";
    public int StartBit { get; set; }
    public int Length { get; set; } = 8;
    public string ByteOrder { get; set; } = "Intel";
    public double Factor { get; set; } = 1;
    public double Offset { get; set; }
    public string Unit { get; set; } = "";
    public string Comment { get; set; } = "";
}
public sealed class UiCanMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "新建报文";
    public string CanId { get; set; } = "0x100";
    public string HexData { get; set; } = "00 00 00 00 00 00 00 00";
    public bool Extended { get; set; }
    public int CycleMs { get; set; }
    public string DbcName { get; set; } = "";
}
public sealed class UiUdsRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "新建请求";
    public string Service { get; set; } = "0x22";
    public string RequestHex { get; set; } = "22 F1 90";
    public string ExpectedResponse { get; set; } = "";
    public int TimeoutMs { get; set; } = 1000;
}
public sealed class UiKeySequence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "按键动作";
    public string MaskHex { get; set; } = "00000";
    public int HoldMs { get; set; } = 500;
}
public sealed class UiInputName
{
    public int Index { get; set; }
    public string DisplayName { get; set; } = "";
    public string Channel { get; set; } = "";
}

public static class UiStepTypes
{
    public static readonly (string Type, string Label, string Note, string[] Fields)[] All =
    [
        ("Wait","等待","固定时间等待",["milliseconds"]),
        ("If","IF / ELSE 条件","条件成立与否则分支",["condition"]),
        ("Switch","SWITCH 分派","按变量进行条件分派",["variable"]),
        ("When","WHEN 等待条件","带超时的条件等待",["condition","timeoutMs"]),
        ("Loop","循环","重复执行子步骤",["count"]),
        ("Sequence","步骤组","有序子步骤容器",[]),
        ("SetVariable","设置变量","写入测试变量",["variable","value"]),
        ("Calculate","计算","计算表达式",["variable","expression"]),
        ("AssertSignal","CAN 信号判定","选择 DBC 信号并设置判定条件",["dbcMessage","signal","operator","expected","timeoutMs"]),
        ("CanSend","发送 CAN","绑定报文与周期",["dbcMessage","canId","dataHex","periodMs"]),
        ("CanWait","等待 CAN","等待目标报文",["dbcMessage","timeoutMs"]),
        ("KeySet","设置输入条件","选择按键组合和保持时间",["keyMask","holdMs"]),
        ("Ignition","IGN 条件","工位 IGN 操作",["state","holdMs"]),
        ("Fixture","夹具动作","夹具控制",["state"]),
        ("UdsRequest","UDS 请求","服务与请求体",["service","requestHex"]),
        ("Flash","ECU 烧录","引用烧录配置",["profile"]),
        ("WaveCapture","波形采集","指定采集配置",["capture"]),
        ("WaveCompare","波形判定","指定参考与容差",["reference","tolerance"]),
        ("CallPublicFlow","调用公共流程","固定引用目标流程",["flow"])
    ];

    public static string Label(string type) => All.FirstOrDefault(x => x.Type == type).Label ?? type;
    public static string[] Fields(string type) => All.FirstOrDefault(x => x.Type == type).Fields ?? [];
    public static string FieldName(string key) => key switch
    {
        "milliseconds" => "等待时间 (ms)", "condition" => "条件表达式",
        "timeoutMs" => "超时时间 (ms)", "count" => "循环次数",
        "variable" => "变量", "value" => "值", "expression" => "表达式",
        "dbcMessage" => "DBC 报文", "signal" => "信号", "operator" => "关系运算符",
        "expected" => "期望值", "canId" => "报文 ID", "dataHex" => "数据 (HEX)",
        "periodMs" => "周期 (ms，0 为单次)", "keyMask" => "20 路开关组合 (HEX)",
        "holdMs" => "保持时间 (ms)", "state" => "目标状态",
        "service" => "UDS 服务", "requestHex" => "请求数据 (HEX)",
        "profile" => "烧录配置", "capture" => "采集配置",
        "reference" => "参考波形", "tolerance" => "容差", "flow" => "公共流程",
        _ => key
    };
    public static UiStep New(string type)
    {
        var node = new UiStep { Type = type, Name = Label(type) };
        foreach (var field in Fields(type)) node.Properties[field] = "";
        if (type == "Wait") node.Properties["milliseconds"] = "500";
        if (type == "Loop") node.Properties["count"] = "2";
        if (type == "CanSend") node.Properties["periodMs"] = "0";
        if (type == "KeySet") node.Properties["holdMs"] = "500";
        return node;
    }
}

public sealed class UiStudyStore
{
    public string PathOnDisk { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTest", "UI-Rebuild-Study", "workspace.json");

    public UiStudy Current { get; private set; }
    public UiStudyStore()
    {
        Current = LoadFile(PathOnDisk) ?? new UiStudy();
    }
    public void Replace(UiStudy next) { Current = next; Save(); }
    public void Save()
    {
        var path = PathOnDisk;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(temp, path, true);
    }
    public void Export(string target) => File.WriteAllText(target, JsonSerializer.Serialize(Current, JsonOptions));
    public void Import(string source)
    {
        var parsed = LoadFile(source) ?? throw new InvalidDataException("无法读取 UI 草稿。");
        if (parsed.SchemaVersion != 1) throw new InvalidDataException("不支持的 UI 草稿版本。");
        Replace(parsed);
    }
    private static UiStudy? LoadFile(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<UiStudy>(File.ReadAllText(path)) : null; }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        { return null; }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

/// <summary>Pure UI edit history. There is no TEST execution or PROJECT saving under this type.</summary>
public sealed class UiEditHistory<T> where T : class
{
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Record(T before)
    {
        _undo.Push(JsonSerializer.Serialize(before));
        _redo.Clear();
    }
    public T Undo(T current)
    {
        if (_undo.Count == 0) return current;
        _redo.Push(JsonSerializer.Serialize(current));
        return JsonSerializer.Deserialize<T>(_undo.Pop())!;
    }
    public T Redo(T current)
    {
        if (_redo.Count == 0) return current;
        _undo.Push(JsonSerializer.Serialize(current));
        return JsonSerializer.Deserialize<T>(_redo.Pop())!;
    }
}
