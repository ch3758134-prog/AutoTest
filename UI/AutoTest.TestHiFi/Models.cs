using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace AutoTest.TestHiFi;

// Presentation-only data. These names and status icons reproduce the reference design,
// not any confirmed CAN/DBC test execution.
public abstract class ViewState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Change([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DemoCase
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string State { get; init; } = "Idle";
    public string StateSymbol => State switch { "Ok" => "●", "Issue" => "●", _ => "○" };
    public string StateColor => State switch
    {
        "Ok" => "#168B43", "Issue" => "#D9363E", _ => "#8796AD"
    };
}

public sealed class DemoStep
{
    public int Number { get; init; }
    public string Phase { get; init; } = "测试";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Kind { get; init; } = "Step";
    public int Depth { get; init; }
    public string Icon { get; init; } = "◈";
    public string IconColor { get; init; } = "#1769D2";
    public string Status { get; init; } = "";
    public string StatusText => Status switch
    {
        "Ok" => "✓", "Warning" => "▲", "Error" => "!",
        _ => ""
    };
    public string StatusColor => Status switch
    {
        "Ok" => "#138B40", "Warning" => "#B87A00", "Error" => "#DA3543",
        _ => "#76849A"
    };
    public Thickness Indent => new(4 + Depth * 22, 0, 0, 0);
    public bool HasIssue => Status is "Warning" or "Error";
    public string Chevron => Kind is "Group" or "Flow" ? "⌄" : Kind == "Branch" ? "└" : "";
    public bool IsBranch => Kind == "Branch";
    public string BranchLabel => IsBranch ? Title : "";
    public string RowName => IsBranch ? "" : Title;
}

public sealed class DemoIssue
{
    public int Index { get; init; }
    public int StepNumber { get; init; }
    public string Severity { get; init; } = "错误";
    public string Description { get; init; } = "";
    public string Advice { get; init; } = "";
    public string Symbol => Severity == "错误" ? "!" : "▲";
    public string Color => Severity == "错误" ? "#D9363E" : "#B87A00";
    public string Location => "步骤 " + StepNumber;
}

public sealed class PreviewInspector : ViewState
{
    private string _heading = "CAN 信号判定";
    private string _description = "读取 CAN 信号并判断其值是否满足预期条件。";
    private string _name = "车门状态判定";
    private string _channel = "CAN1";
    private string _signal = "BCM.DoorSts";
    private string _dbc = "演示 BCM.dbc";
    private string _operator = "等于 (＝)";
    private string _expected = "Open (1)";
    private string _timeout = "1,000";
    private string _sampleInterval = "10 ms";
    private string _warning = "演示问题：该信号尚未在当前 DBC 中确认。";
    private bool _stopOnTimeout = true;
    private bool _recordValue;
    private bool _ignoreInvalid;
    public string Heading { get => _heading; set { _heading = value; Change(); } }
    public string Description { get => _description; set { _description = value; Change(); } }
    public string Name { get => _name; set { _name = value; Change(); } }
    public string Channel { get => _channel; set { _channel = value; Change(); } }
    public string Signal { get => _signal; set { _signal = value; Change(); } }
    public string Dbc { get => _dbc; set { _dbc = value; Change(); } }
    public string Operator { get => _operator; set { _operator = value; Change(); } }
    public string Expected { get => _expected; set { _expected = value; Change(); } }
    public string Timeout { get => _timeout; set { _timeout = value; Change(); } }
    public string SampleInterval { get => _sampleInterval; set { _sampleInterval = value; Change(); } }
    public string Warning { get => _warning; set { _warning = value; Change(); } }
    public bool StopOnTimeout { get => _stopOnTimeout; set { _stopOnTimeout = value; Change(); } }
    public bool RecordValue { get => _recordValue; set { _recordValue = value; Change(); } }
    public bool IgnoreInvalid { get => _ignoreInvalid; set { _ignoreInvalid = value; Change(); } }
    public void Show(DemoStep step)
    {
        Heading = step.Title is "IF 条件" or "SWITCH" ? step.Title : step.Title;
        Description = step.Title switch
        {
            "CAN 信号判定" => "读取 CAN 信号并判断其值是否满足预期条件。",
            "波形判定" => "依据参考波形检查采集数据与预期特征。",
            "发送 CAN 报文" => "配置发送报文、发送周期和数据内容。",
            "等待" or "延时" => "等待指定时长或状态条件。",
            "IF 条件" => "按表达式进入对应的流程分支。",
            "SWITCH" => "按表达式选择一个 CASE 分支。",
            _ => "当前步骤的参数预览，仅用于高保真界面检查。"
        };
        Name = step.Title switch
        {
            "CAN 信号判定" => "车门状态判定",
            "波形判定" => "制动电流波形判定",
            "发送 CAN 报文" => "模式控制报文",
            _ => step.Title
        };
        Channel = step.Title == "波形判定" ? "CH1" : "CAN1";
        Signal = step.Title switch
        {
            "波形判定" => "MotorCurrent",
            "发送 CAN 报文" => "BCM_Ctrl (0x3A0)",
            _ => "BCM.DoorSts"
        };
        Dbc = step.Title == "波形判定" ? "演示参考波形库" : "演示 BCM.dbc";
        Operator = step.Title == "波形判定" ? "小于等于 (≤)" : "等于 (＝)";
        Expected = step.Title == "波形判定" ? "50 ms" : "Open (1)";
        Timeout = step.Title == "等待" ? "500" : "1,000";
        SampleInterval = step.Title == "波形判定" ? "1 ms" : "10 ms";
        Warning = step.Status switch
        {
            "Error" => "演示错误：当前资源引用未完成，需进行配置。",
            "Warning" => "演示警告：期望值与当前资源定义存在差异。",
            _ => "当前为设计样本，尚未建立真实设备或资源校验。"
        };
    }
}

public static class DemoContent
{
    public static ObservableCollection<DemoCase> Cases() =>
    [
        new() { Id="TC-001", Name="工位连接与身份", State="Ok" },
        new() { Id="TC-002", Name="供电条件检查", State="Ok" },
        new() { Id="TC-003", Name="外部输入与状态响应", State="Ok" },
        new() { Id="TC-004", Name="CAN 周期发送验证" },
        new() { Id="TC-005", Name="CAN 信号变化判定" },
        new() { Id="TC-006", Name="UDS 诊断会话" },
        new() { Id="TC-007", Name="诊断参数读取" },
        new() { Id="TC-008", Name="输入动作时序" },
        new() { Id="TC-009", Name="波形参考采集" },
        new() { Id="TC-010", Name="波形判定", State="Issue" },
        new() { Id="TC-011", Name="异常路径恢复" },
        new() { Id="TC-012", Name="清理与结束条件" }
    ];

    private static DemoStep S(int no,string phase,string title,string summary,string kind="Step",int depth=0,string status="Ok",string icon="◈",string iconColor="#1769D2") =>
        new() { Number=no,Phase=phase,Title=title,Summary=summary,Kind=kind,Depth=depth,Status=status,Icon=icon,IconColor=iconColor };
    public static ObservableCollection<DemoStep> Steps() =>
    [
        // Displayed case TC-003: 6 prepare, 29 test and 5 cleanup, matching reference-density.
        S(1,"准备","连接设备","查找 USB 主控板并读取连接状态",status:"Ok",icon:"▣"),
        S(2,"准备","设置 IGN","IGN → ON（演示配置）",status:"Ok",icon:"◉"),
        S(3,"准备","等待","100 ms",status:"Ok",icon:"◷"),
        S(4,"准备","UDS 请求","会话请求 10 03（演示）",status:"Ok",icon:"⇄"),
        S(5,"准备","模拟外部输入","20 Key → 默认状态",status:"Ok",icon:"⌘"),
        S(6,"准备","日志记录","准备阶段完成",status:"Ok",icon:"▤"),
        S(10,"测试","实验条件设置","设置电源、模式与初始状态","Group",0,"Ok","▣"),
        S(11,"测试","循环","执行 2 次","Flow",0,"Ok","⟳"),
        S(12,"测试","发送 CAN 报文","BCM_Ctrl (0x3A0)  Data: 02 01 00 00 00 00 00 00","Step",1,"Ok","✉"),
        S(13,"测试","等待","500 ms","Step",1,"Ok","◷"),
        S(14,"测试","IF 条件","Signal: DoorSts = Open   ·   判断车门状态","Flow",1,"Warning","◇","#7B55D8"),
        S(15,"测试","THEN","条件满足时执行","Branch",2,"","", "#7B55D8"),
        S(16,"测试","CAN 信号判定","BCM.DoorSts = Open  （超时 1,000 ms）","Step",3,"Error","▥"),
        S(17,"测试","波形判定","CH1 电平：高 > 4.5 V，持续 ≥ 100 ms","Step",3,"Ok","∿"),
        S(18,"测试","ELSE","条件不满足时执行","Branch",2,"","", "#7B55D8"),
        S(19,"测试","日志记录","车门未打开，记录当前状态","Step",3,"Ok","▤"),
        S(20,"测试","延时","2,000 ms","Step",1,"Ok","◷"),
        S(21,"测试","SWITCH","Signal: LightMode   ·   根据灯光模式执行检查","Flow",0,"","⌘","#7B55D8"),
        S(22,"测试","CASE 0（关闭）","检查关闭状态","Branch",1,"","", "#7B55D8"),
        S(23,"测试","CAN 信号判定","BCM.LightSts = Off","Step",2,"Ok","▥"),
        S(24,"测试","CASE 1（近光）","检查近光状态","Branch",1,"","", "#7B55D8"),
        S(25,"测试","CAN 信号判定","BCM.LightSts = LowBeam","Step",2,"Warning","▥"),
        S(26,"测试","CASE 2（远光）","检查远光状态","Branch",1,"","", "#7B55D8"),
        S(27,"测试","CAN 信号判定","BCM.LightSts = HighBeam","Step",2,"Ok","▥"),
        S(28,"测试","波形判定","CH3 波形通道：缺少有效参考资源","Step",1,"Error","∿"),
        S(29,"测试","TRY / CATCH","出现设备通信异常时进行处理","Flow",0,"","⌘","#7B55D8"),
        S(30,"测试","UDS 请求","19 02 FF 读取故障信息","Step",1,"Ok","⇄"),
        S(31,"测试","WHEN","等待故障信息响应 ≤ 2,000 ms","Flow",1,"Ok","◷"),
        S(32,"测试","日志记录","记录故障状态与响应摘要","Step",2,"Ok","▤"),
        S(33,"测试","调用公共流程","恢复输入状态 · Demo_Recovery","Step",0,"Ok","↗"),
        S(34,"测试","设置变量","responseSeen = true","Step",0,"Ok","ƒ"),
        S(35,"测试","期望判定","responseSeen == true","Step",0,"Ok","✓"),
        S(36,"测试","停止周期发送","BCM_Ctrl（演示）","Step",0,"Ok","■"),
        S(37,"测试","日志记录","测试阶段结束","Step",0,"Ok","▤"),
        S(38,"测试","等待","100 ms","Step",0,"Ok","◷"),
        S(1,"清理","停止周期发送","停止当前例程中的全部周期任务",status:"Ok",icon:"■"),
        S(2,"清理","模拟外部输入","恢复到无操作",status:"Ok",icon:"⌘"),
        S(3,"清理","设置 IGN","IGN → OFF",status:"Ok",icon:"◉"),
        S(4,"清理","等待","100 ms",status:"Ok",icon:"◷"),
        S(5,"清理","日志记录","关闭当前样本测试用例",status:"Ok",icon:"▤")
    ];

    public static ObservableCollection<DemoIssue> Issues() =>
    [
        new() { Index=1,StepNumber=16,Severity="错误",Description="演示 DBC 信号“BCM.DoorSts”尚未建立有效映射。",Advice="检查演示 DBC 文件，或重新选择信号。" },
        new() { Index=2,StepNumber=28,Severity="错误",Description="波形判定的“CH3”未配置有效参考波形。",Advice="打开波形参考资源并配置有效的采集通道。" },
        new() { Index=3,StepNumber=25,Severity="警告",Description="信号“BCM.LightSts”的期望值与定义不完全一致。",Advice="核对枚举值 LowBeam，或采用数值比较。" }
    ];
}
