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
    private bool _isCanSignal = true;
    public bool IsCanSignal { get => _isCanSignal; private set { _isCanSignal=value;Change();Change(nameof(IsOtherType)); } }
    public bool IsOtherType => !IsCanSignal;
    private string _otherHeading = "步骤配置";
    public string OtherHeading { get => _otherHeading; private set { _otherHeading=value;Change(); } }
    private string _otherLabel = "关键参数";
    public string OtherLabel { get => _otherLabel; private set { _otherLabel=value;Change(); } }
    private string _otherValue = "";
    public string OtherValue { get => _otherValue; private set { _otherValue=value;Change(); } }
    private string _otherGuidance = "";
    public string OtherGuidance { get => _otherGuidance; private set { _otherGuidance=value;Change(); } }
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
        Heading = step.Title;
        IsCanSignal = step.Title == "CAN 信号判定";
        OtherHeading = step.Kind is "Group" or "Flow" or "Branch" ? "流程结构" : "步骤配置";
        OtherLabel = step.Title switch
        {
            "日志记录" => "日志内容",
            "等待" or "延时" => "等待时间",
            "UDS 请求" => "UDS 请求负载",
            "波形判定" => "波形判定条件",
            "发送 CAN 报文" => "报文与数据",
            "模拟外部输入" => "按键组合及保持时间",
            "IF 条件" or "SWITCH" => "条件表达式",
            "ELSE" or "THEN" => "分支说明",
            _ => "参数摘要"
        };
        OtherValue = step.Summary;
        OtherGuidance = step.Kind switch
        {
            "Branch" => "此行是条件分支，不是 CAN 信号判定步骤。应在分支下选择实际步骤进行参数编辑。",
            "Flow" => "此行是流程控制容器，请检查分支关系和条件；完整流程参数编辑属于 UI-03。",
            "Group" => "此行是流程分组标题。选择其子步骤查看具体参数。",
            _ => "当前为 UI 高保真样本，具体参数的真实编辑与保存由 UI-03 的正式 TEST 模型接入。"
        };
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
        S(14,"测试","IF 条件","Signal: DoorSts = Open   ·   判断车门状态","Flow",1,"Warning","◇","#536982"),
        S(15,"测试","THEN","条件满足时执行","Branch",2,"","", "#536982"),
        S(16,"测试","CAN 信号判定","BCM.DoorSts = Open  （超时 1,000 ms）","Step",3,"Error","▥"),
        S(17,"测试","波形判定","CH1 电平：高 > 4.5 V，持续 ≥ 100 ms","Step",3,"Ok","∿"),
        S(18,"测试","ELSE","条件不满足时执行","Branch",2,"","", "#536982"),
        S(19,"测试","日志记录","车门未打开，记录当前状态","Step",3,"Ok","▤"),
        S(20,"测试","延时","2,000 ms","Step",1,"Ok","◷"),
        S(21,"测试","SWITCH","Signal: LightMode   ·   根据灯光模式执行检查","Flow",0,"","⌘","#536982"),
        S(22,"测试","CASE 0（关闭）","检查关闭状态","Branch",1,"","", "#536982"),
        S(23,"测试","CAN 信号判定","BCM.LightSts = Off","Step",2,"Ok","▥"),
        S(24,"测试","CASE 1（近光）","检查近光状态","Branch",1,"","", "#536982"),
        S(25,"测试","CAN 信号判定","BCM.LightSts = LowBeam","Step",2,"Warning","▥"),
        S(26,"测试","CASE 2（远光）","检查远光状态","Branch",1,"","", "#536982"),
        S(27,"测试","CAN 信号判定","BCM.LightSts = HighBeam","Step",2,"Ok","▥"),
        S(28,"测试","波形判定","CH3 波形通道：缺少有效参考资源","Step",1,"Error","∿"),
        S(29,"测试","TRY / CATCH","出现设备通信异常时进行处理","Flow",0,"","⌘","#536982"),
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


    // Secondary cases contain distinct short, populated design samples. They do not
    // claim any real ECU behavior and do not replace the 40-step TC-003 baseline.
    public static ObservableCollection<DemoStep> StepsForCase(string id)
    {
        if (id == "TC-003") return Steps();
        var specifics = new Dictionary<string, (string Name, string Summary)[]>
        {
            ["TC-001"] = [
                ("连接设备", "检查 USB 通道（演示）"),
                ("读取设备标识", "设备身份字段与版本摘要（演示）"),
                ("检查通信状态", "连接状态就绪（配置示意）"),
                ("记录身份信息", "保存演示设备信息到 UI 草稿")
            ],
            ["TC-002"] = [
                ("设置 IGN", "设置 IGN → ON（演示）"),
                ("等待电压稳定", "300 ms"),
                ("电源条件判定", "供电状态满足测试前置条件"),
                ("记录当前状态", "写入示例测量状态")
            ],
            ["TC-004"] = [
                ("配置 CAN 发送", "BCM_Ctrl，周期 100 ms（演示）"),
                ("发送 CAN 报文", "02 01 00 00 00 00 00 00"),
                ("等待", "500 ms"),
                ("停止周期发送", "停止演示周期任务")
            ],
            ["TC-005"] = [
                ("读取 CAN 信号", "BCM.Status.Counter → counter"),
                ("等待", "200 ms"),
                ("CAN 信号判定", "counter 变化符合表达式"),
                ("日志记录", "记录信号趋势（演示）")
            ],
            ["TC-006"] = [
                ("UDS 请求", "10 03 切换扩展诊断会话"),
                ("等待", "100 ms"),
                ("UDS 请求", "3E 00 TesterPresent"),
                ("期望判定", "服务响应满足演示预期")
            ],
            ["TC-007"] = [
                ("UDS 请求", "22 F1 90 读取示例标识"),
                ("解析响应", "提取演示标识字符串"),
                ("设置变量", "identifier → UI 变量"),
                ("日志记录", "打印演示读取结果")
            ],
            ["TC-008"] = [
                ("模拟外部输入", "20 Key 组合动作：驻车"),
                ("等待", "300 ms"),
                ("模拟外部输入", "恢复无操作"),
                ("CAN 信号判定", "演示状态响应检查")
            ],
            ["TC-009"] = [
                ("波形开始", "MotorCurrent · CH1"),
                ("等待", "500 ms"),
                ("波形停止", "停止演示采样"),
                ("日志记录", "记录参考采集摘要")
            ],
            ["TC-010"] = [
                ("选择波形参考", "Demo_CurrentReference（未连接）"),
                ("波形判定", "上升时间 ≤ 50 ms"),
                ("波形判定", "峰值幅度处于演示范围"),
                ("日志记录", "登记演示判定问题")
            ],
            ["TC-011"] = [
                ("TRY / CATCH", "诊断通信异常示例流程"),
                ("日志记录", "记录异常信息"),
                ("等待", "200 ms"),
                ("调用公共流程", "Demo_Recovery")
            ],
            ["TC-012"] = [
                ("停止周期发送", "停止所有示例发送任务"),
                ("设置 IGN", "IGN → OFF"),
                ("模拟外部输入", "恢复 20 Key 无操作"),
                ("日志记录", "清理结束")
            ]
        };
        if (!specifics.TryGetValue(id, out var named)) return [];
        var rows = new ObservableCollection<DemoStep>
        {
            S(1,"准备","加载测试配置","选择当前用例的 UI 样本配置",status:"Ok",icon:"▣"),
            S(2,"准备","等待","100 ms",status:"Ok",icon:"◷")
        };
        for (var i = 0; i < named.Length; i++)
        {
            var item = named[i];
            rows.Add(S(i+1,"测试",item.Name,item.Summary,status:id=="TC-010"&&i==1?"Warning":"Ok",
                icon:item.Name.Contains("CAN")?"▥":item.Name.Contains("波形")?"∿":"◈"));
        }
        rows.Add(S(1,"清理","恢复默认状态","停止预览样本相关操作",status:"Ok",icon:"■"));
        rows.Add(S(2,"清理","日志记录","结束 UI 样本用例",status:"Ok",icon:"▤"));
        return rows;
    }

    public static ObservableCollection<DemoIssue> Issues() =>
    [
        new() { Index=1,StepNumber=16,Severity="错误",Description="演示 DBC 信号“BCM.DoorSts”尚未建立有效映射。",Advice="检查演示 DBC 文件，或重新选择信号。" },
        new() { Index=2,StepNumber=28,Severity="错误",Description="波形判定的“CH3”未配置有效参考波形。",Advice="打开波形参考资源并配置有效的采集通道。" },
        new() { Index=3,StepNumber=25,Severity="警告",Description="信号“BCM.LightSts”的期望值与定义不完全一致。",Advice="核对枚举值 LowBeam，或采用数值比较。" }
    ];
}
