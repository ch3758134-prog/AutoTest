using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoTest.TestEditorPreview;

public enum NodeRole { Phase, Branch, Container, Step }
public enum FieldKind { Text, PositiveNumber, RequiredResource, HexBytes, Expression }

public abstract class Notifying : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string property = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

public sealed class EditorField : Notifying
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public FieldKind Kind { get; set; }
    public string Hint { get; set; } = "";
    private string _text = "";
    public string Text
    {
        get => _text;
        set { if (_text == value) return; _text = value; Notify(); Notify(nameof(Error)); Notify(nameof(IsInvalid)); }
    }
    [JsonIgnore] public string Error => Kind switch
    {
        FieldKind.PositiveNumber when !int.TryParse(Text, out var n) || n < 0 => "请输入不小于 0 的整数。",
        FieldKind.RequiredResource when string.IsNullOrWhiteSpace(Text) => "尚未选择有效资源。",
        FieldKind.HexBytes when string.IsNullOrWhiteSpace(Text) => "请输入十六进制请求。",
        FieldKind.HexBytes when !IsHex(Text) => "请输入以空格分隔的完整十六进制字节。",
        FieldKind.Expression when string.IsNullOrWhiteSpace(Text) => "请填写判断表达式。",
        _ => ""
    };
    [JsonIgnore] public bool IsInvalid => Error.Length > 0;
    private static bool IsHex(string text)
    {
        var normalized = text.Replace(" ", "").Replace("-", "").Replace(",", "");
        if (normalized.Length == 0 || normalized.Length % 2 != 0) return false;
        return normalized.All(ch => Uri.IsHexDigit(ch));
    }
}

public sealed class EditorNode : Notifying
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Code { get; set; } = "";
    public NodeRole Role { get; set; } = NodeRole.Step;
    public string Kind { get; set; } = "等待";
    private string _title = "";
    public string Title { get => _title; set { _title = value; Notify(); Notify(nameof(DisplayTitle)); } }
    public string Summary { get; set; } = "";
    public bool IsExpanded { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public bool Breakpoint { get; set; }
    public ObservableCollection<EditorField> Fields { get; set; } = [];
    public ObservableCollection<EditorNode> Children { get; set; } = [];
    [JsonIgnore] public EditorNode? Parent { get; set; }
    [JsonIgnore] public bool IsMovable => Role is NodeRole.Step or NodeRole.Container;
    [JsonIgnore] public int LocalIssues => Fields.Count(f => f.IsInvalid);
    [JsonIgnore] public int DescendantIssues => LocalIssues + Children.Sum(c => c.DescendantIssues);
    [JsonIgnore] public bool HasSelectedChild { get; set; }
    [JsonIgnore] public string DisplayTitle => Role switch
    {
        NodeRole.Phase => Title,
        NodeRole.Branch => Title,
        _ => (string.IsNullOrEmpty(Code) ? "" : Code + "  ") + Title
    };
    public EditorField? Field(string key) => Fields.FirstOrDefault(f => f.Key == key);
    public EditorNode? Find(string id)
    {
        if (Id == id) return this;
        foreach (var child in Children)
        {
            var found = child.Find(id);
            if (found is not null) return found;
        }
        return null;
    }
    public void RepairParents(EditorNode? parent = null)
    {
        Parent = parent;
        foreach (var child in Children) child.RepairParents(this);
    }
    public EditorNode CloneWithFreshIds()
    {
        var value = JsonSerializer.Deserialize<EditorNode>(JsonSerializer.Serialize(this))!;
        void Renew(EditorNode root)
        {
            root.Id = Guid.NewGuid().ToString("N");
            root.Code = ""; // Design identity is newly created, not a duplicate formal StepId.
            foreach (var child in root.Children) Renew(child);
        }
        Renew(value);
        value.RepairParents();
        return value;
    }
}

public sealed class EditorCase : Notifying
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _name = "";
    public string Name { get => _name; set { _name = value; Notify(); } }
    private bool _runSelected;
    public bool RunSelected { get => _runSelected; set { _runSelected = value; Notify(); } }
    public ObservableCollection<EditorNode> Phases { get; set; } = [];
    public EditorNode? Find(string id) => Phases.Select(p => p.Find(id)).FirstOrDefault(p => p is not null);
    public void RepairParents() { foreach (var phase in Phases) phase.RepairParents(); }
    public int CountSteps => Phases.Sum(x => Count(x));
    private static int Count(EditorNode node) => (node.IsMovable ? 1 : 0) + node.Children.Sum(Count);
}

public sealed class EditorDocument
{
    public int FormatVersion { get; set; } = 1;
    public string Title { get; set; } = "TEST 操作与视觉研究工作区";
    public ObservableCollection<EditorCase> Cases { get; set; } = [];
    public string ActiveCaseId { get; set; } = "";
    public string ActiveNodeId { get; set; } = "";
    public bool IsStudyData { get; set; } = true;
    public void RepairParents() { foreach (var c in Cases) c.RepairParents(); }
    public string Serialize() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public static EditorDocument Load(string data)
    {
        var doc = JsonSerializer.Deserialize<EditorDocument>(data) ?? throw new InvalidDataException("文件没有有效内容。");
        if (doc.FormatVersion != 1 || !doc.IsStudyData)
            throw new InvalidDataException("这不是受支持的 V02-04 UI 研究草稿。");
        if (doc.Cases.Count > 150) throw new InvalidDataException("此原型只支持不超过 150 个 UI 用例。");
        doc.RepairParents();
        return doc;
    }
}

public static class StepCatalog
{
    public static readonly string[] Types =
    [
        "等待", "日志记录", "设置变量", "计算", "期望判定",
        "CAN 单次发送", "CAN 周期发送", "停止周期发送", "CAN 信号读取", "CAN 信号判定",
        "UDS 请求", "调用公共流程", "模拟外部输入", "IGN 控制", "夹具控制",
        "波形开始", "波形停止", "波形判定",
        "IF / ELSE", "WHEN", "SWITCH", "循环 LOOP", "TRY / CATCH"
    ];
    private static EditorField F(string key, string label, FieldKind kind = FieldKind.Text, string value = "", string hint = "") =>
        new() { Key = key, Label = label, Kind = kind, Text = value, Hint = hint };
    public static EditorNode New(string type, string code = "", string summary = "")
    {
        var container = type is "IF / ELSE" or "WHEN" or "SWITCH" or "循环 LOOP" or "TRY / CATCH";
        var node = new EditorNode { Role = container ? NodeRole.Container : NodeRole.Step,
            Code = code, Kind = type, Title = type, Summary = summary };
        node.Fields = type switch
        {
            "等待" => [F("ms", "等待时间（ms）", FieldKind.PositiveNumber, "300")],
            "CAN 信号判定" =>
            [
                F("signal", "DBC 信号 · 必填", FieldKind.RequiredResource, "Demo_Status.Ready", "从已导入的 DBC 资源选择；本 UI 只使用研究数据。"),
                F("operator", "比较关系", FieldKind.Text, "=="),
                F("expected", "期望值", FieldKind.Text, "1"),
                F("timeout", "最长等待时间（ms）", FieldKind.PositiveNumber, "2000")
            ],
            "CAN 信号读取" => [F("signal","DBC 信号", FieldKind.RequiredResource,"Demo_Status.Counter"), F("variable","写入变量",FieldKind.Text,"counter")],
            "CAN 单次发送" => [F("message","DBC 报文",FieldKind.RequiredResource,"Demo_Command"),F("bytes","数据 HEX",FieldKind.HexBytes,"00 01")],
            "CAN 周期发送" => [F("message","DBC 报文",FieldKind.RequiredResource,"Demo_Alive"),F("period","发送周期（ms）",FieldKind.PositiveNumber,"100"),F("bytes","数据 HEX",FieldKind.HexBytes,"00 00")],
            "停止周期发送" => [F("task","周期任务",FieldKind.RequiredResource,"Demo_Alive")],
            "UDS 请求" => [F("request","请求 HEX",FieldKind.HexBytes,"22 F1 90"), F("target","目标标识",FieldKind.Text,"DUT")],
            "调用公共流程" => [F("resource","公共流程引用",FieldKind.RequiredResource,"Demo_BeforeTest"),F("revision","固定修订",FieldKind.RequiredResource,"R01")],
            "模拟外部输入" => [F("action","外部输入动作",FieldKind.RequiredResource,"Demo_InputAction"),F("hold","保持时间（ms）",FieldKind.PositiveNumber,"300")],
            "波形判定" => [F("reference","参考波形",FieldKind.RequiredResource,"Demo_Reference"),F("tolerance","允许偏差",FieldKind.Text,"0.05")],
            "波形开始" or "波形停止" => [F("channel","波形信号",FieldKind.RequiredResource,"Demo_MotorCurrent")],
            "IF / ELSE" or "WHEN" or "SWITCH" => [F("condition","条件表达式",FieldKind.Expression,"Demo_Status.Mode == 2")],
            "循环 LOOP" => [F("count","循环次数",FieldKind.PositiveNumber,"3"),F("variable","循环变量",FieldKind.Text,"i")],
            "TRY / CATCH" => [F("reason","异常处理说明",FieldKind.Text,"设备通信异常")],
            "IGN 控制" => [F("value","目标状态",FieldKind.Text,"OFF")],
            "夹具控制" => [F("value","目标动作",FieldKind.Text,"压紧")],
            "设置变量" or "计算" => [F("variable","变量名",FieldKind.Text,"retryCount"),F("expression","值或表达式",FieldKind.Text,"0")],
            "期望判定" => [F("expression","比较条件",FieldKind.Expression,"readySeen == true")],
            "日志记录" => [F("message","日志内容",FieldKind.Text,"测试事件")],
            _ => [F("note","步骤说明")]
        };
        var branches = type switch
        {
            "IF / ELSE" => new[] {"THEN","ELSE"},
            "WHEN" => new[] {"满足条件","超时分支"},
            "SWITCH" => new[] {"CASE 0","CASE 1","DEFAULT"},
            "循环 LOOP" => new[] {"BODY"},
            "TRY / CATCH" => new[] {"TRY","CATCH","FINALLY"},
            _ => []
        };
        foreach (var label in branches)
        {
            var child = new EditorNode { Role = NodeRole.Branch, Kind = "分支",
                Title = label, Summary = type };
            child.Parent = node;
            node.Children.Add(child);
        }
        return node;
    }
    public static EditorNode Phase(string title) => new()
    {
        Role = NodeRole.Phase, Kind = "阶段", Title = title
    };
}

public sealed class StepRow : Notifying
{
    public required EditorNode Node { get; init; }
    public int Depth { get; init; }
    public Thickness Indent => new(Depth * 20 + 9, 0, 0, 0);
    public string Arrow => Node.Children.Count == 0 ? "" : Node.IsExpanded ? "▾" : "▸";
    public string Title => Node.DisplayTitle;
    public string Summary => Node.Summary;
    public string Category => Node.Role switch
    {
        NodeRole.Phase => "阶段",
        NodeRole.Branch => "分支",
        NodeRole.Container => "流程",
        _ => "步骤"
    };
    public bool IsStructural => Node.Role != NodeRole.Step;
    public bool HasIssue => Node.DescendantIssues > 0;
    public string IssueText => Node.DescendantIssues > 0 ? "⚠ " + Node.DescendantIssues : "";
    public string StateText =>
        Node.DescendantIssues > 0 ? "需修正" :
        !Node.IsEnabled ? "已禁用" : Node.Breakpoint ? "● 断点" : "";
    public void Update() { Notify(nameof(Arrow)); Notify(nameof(Summary)); Notify(nameof(HasIssue)); Notify(nameof(IssueText)); Notify(nameof(StateText)); }
}

public sealed class EditorProblem
{
    public required string NodeId { get; init; }
    public required string FieldKey { get; init; }
    public required string NodeName { get; init; }
    public required string Message { get; init; }
    public string Display => NodeName + "  ·  " + Message;
}
