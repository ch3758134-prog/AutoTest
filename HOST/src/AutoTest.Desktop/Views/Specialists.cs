using AutoTest.Desktop.Core;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AutoTest.Desktop.Views;

/// <summary>
/// Independent, stateful UI workspaces. The controls implement authoring and navigation,
/// not transport, instrument acquisition, real report generation, or physical ACK.
/// </summary>
public static class Specialists
{
    public static UIElement Dbc(UiStore state)
    {
        var messages=new ListBox{ItemsSource=state.DbcMessages,DisplayMemberPath=nameof(UiDbcMessage.Name),
            Margin=new Thickness(12,8,12,10),BorderThickness=new Thickness(0)};
        var signals=new DockPanel{Margin=new Thickness(12)};
        var detail=new StackPanel{Margin=new Thickness(12)};
        var editor=UiKit.Table(Array.Empty<UiDbcSignal>(),
            ("信号名",nameof(UiDbcSignal.Name),170,true),
            ("中文别名",nameof(UiDbcSignal.Alias),180,true),
            ("起始 bit",nameof(UiDbcSignal.StartBit),80,true),
            ("长度",nameof(UiDbcSignal.Length),70,true),
            ("精度",nameof(UiDbcSignal.Factor),80,true),
            ("偏移",nameof(UiDbcSignal.Offset),80,true),
            ("单位",nameof(UiDbcSignal.Unit),75,true));
        signals.Children.Add(editor);
        var validation=UiKit.Note("请先创建报文并添加信号。",12);
        DockPanel.SetDock(validation,Dock.Bottom);signals.Children.Add(validation);
        var left=UiKit.Vertical(UiKit.Actions(
            UiKit.Button("＋ 报文",(_,_)=>{
                var m=new UiDbcMessage();state.DbcMessages.Add(m);messages.SelectedItem=m;state.Changed("DBC 新增报文");
            },true),
            UiKit.Button("删除",(_,_)=>{
                if(messages.SelectedItem is UiDbcMessage m){state.DbcMessages.Remove(m);state.Changed("DBC 删除报文");}
            })),messages);
        left.Margin=new Thickness(8);
        var top=UiKit.Actions(
            UiKit.Button("＋ 信号",(_,_)=>{
                if(messages.SelectedItem is not UiDbcMessage m)return;
                m.Signals.Add(new UiDbcSignal{Name="NewSignal"+(m.Signals.Count+1)});
                state.Changed("DBC 添加信号");Validate(m);
            },true),
            UiKit.Button("删除信号",(_,_)=>{
                if(messages.SelectedItem is UiDbcMessage m&&editor.SelectedItem is UiDbcSignal s){
                    m.Signals.Remove(s);state.Changed("DBC 删除信号");Validate(m);}
            }),
            UiKit.Button("检查位重叠",(_,_)=>{if(messages.SelectedItem is UiDbcMessage m)Validate(m);}));
        DockPanel.SetDock(top,Dock.Top);signals.Children.Add(top);
        editor.CellEditEnding+=(_,_)=>{
            state.Changed("修改 DBC 信号");
            if(messages.SelectedItem is UiDbcMessage m)
                editor.Dispatcher.BeginInvoke(new Action(()=>Validate(m)));
        };
        messages.SelectionChanged+=(_,_)=>{
            if(messages.SelectedItem is not UiDbcMessage m){editor.ItemsSource=null;detail.Children.Clear();return;}
            editor.ItemsSource=m.Signals;
            detail.Children.Clear();
            detail.Children.Add(UiKit.Heading("报文详情",14));
            detail.Children.Add(UiKit.Field("名称",UiKit.BoundText(m,nameof(UiDbcMessage.Name))));
            detail.Children.Add(UiKit.Field("CAN ID（十六进制）",UiKit.BoundText(m,nameof(UiDbcMessage.CanId))));
            detail.Children.Add(UiKit.Field("数据长度 DLC",UiKit.BoundText(m,nameof(UiDbcMessage.Dlc))));
            detail.Children.Add(UiKit.Heading("信号位布局",14,new Thickness(0,17,0,10)));
            var bits=new UniformGrid{Columns=8,Rows=8};
            for(var n=0;n<64;n++)
            {
                var bit=n;
                var tile=new Border{BorderBrush=UiKit.Line,BorderThickness=new Thickness(0.5),
                    Background=(n/8)%2==0?Brushes.White:UiKit.Canvas,
                    Padding=new Thickness(2),MinHeight=29};
                tile.Child=new TextBlock{Text=n.ToString(),FontSize=10,
                    HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                bits.Children.Add(tile);
            }
            detail.Children.Add(bits);
            detail.Children.Add(UiKit.Note("位图用于查看结构；位占用与合法性由当前输入计算。没有从真实 DBC 文件读取数据。",11,
                new Thickness(0,10,0,0)));
            Validate(m);
        };
        void Validate(UiDbcMessage m)
        {
            var errors=new List<string>();
            if(!UiStore.IsCanId(m.CanId))errors.Add("CAN ID 无效");
            if(m.Dlc<0||m.Dlc>8)errors.Add("DLC 应为 0–8");
            var used=new HashSet<int>();
            foreach(var s in m.Signals)
            {
                if(s.StartBit<0||s.Length<1||s.StartBit+s.Length>m.Dlc*8){errors.Add(s.Name+": 位范围越界");continue;}
                for(var bit=s.StartBit;bit<s.StartBit+s.Length;bit++)
                    if(!used.Add(bit)){errors.Add(s.Name+": 与其他信号重叠");break;}
            }
            validation.Text=errors.Count==0?"当前输入未发现位范围冲突（仅 UI 层基础检查）":
                string.Join("；",errors.Distinct());
            validation.Foreground=errors.Count==0?UiKit.Green:Brushes.IndianRed;
        }
        return UiKit.Page("DBC 报文与信号","专业密集编辑：报文列表 / 可直接编辑的信号表 / 报文与位布局详情。无真实文件导入或保存接线。",
            UiKit.ThreeColumns(UiKit.Side("报文",left),UiKit.Side("信号编辑",signals),
                UiKit.Side("报文属性与位图",UiKit.Scroll(detail)),245,325));
    }
    public static UIElement Can(UiStore state)
    {
        var messages=state.CanMessages;
        var grid=UiKit.Table(messages,("报文名称",nameof(UiCanMessage.Name),120,true),
            ("CAN ID",nameof(UiCanMessage.Id),120,true),
            ("数据 HEX",nameof(UiCanMessage.Data),0,true),
            ("周期 ms",nameof(UiCanMessage.PeriodMs),90,true));
        grid.Columns.Insert(0,new DataGridCheckBoxColumn
        {
            Header="计划启用",
            Binding=new Binding(nameof(UiCanMessage.Enabled)){Mode=BindingMode.TwoWay},
            Width=76
        });
        var toggle=new CheckBox{Content="当前选中报文采用周期发送（仅 UI 计划）",Margin=new Thickness(12)};
        toggle.Checked+=(_,_)=>{if(grid.SelectedItem is UiCanMessage m){m.Periodic=true;state.Changed("修改 CAN 发送模式");}};
        toggle.Unchecked+=(_,_)=>{if(grid.SelectedItem is UiCanMessage m){m.Periodic=false;state.Changed("修改 CAN 发送模式");}};
        grid.SelectionChanged+=(_,_)=>{
            if(grid.SelectedItem is UiCanMessage m)toggle.IsChecked=m.Periodic;
        };
        var issue=UiKit.Note("编辑报文不会执行发送。真实 CAN RX/TX 与设备 ACK 尚未接线。",12);
        var rows=new DockPanel();
        var head=UiKit.Vertical(UiKit.Actions(
            UiKit.Button("＋ 添加报文",(_,_)=>{
                var m=new UiCanMessage();messages.Add(m);grid.SelectedItem=m;state.Changed("添加待发报文");
            },true),
            UiKit.Button("从 DBC 选择",(_,_)=>{
                if(state.DbcMessages.Count==0)
                {issue.Text="当前 UI 工作会话没有 DBC 报文。先在 DBC 工具创建报文。";return;}
                var menu=new ContextMenu();
                foreach(var dbc in state.DbcMessages)
                {
                    var item=new MenuItem{Header=dbc.Name+" · "+dbc.CanId};
                    item.Click+=(_,_)=>{var msg=new UiCanMessage{Name=dbc.Name,Id=dbc.CanId};
                        messages.Add(msg);state.Changed("从 DBC 选取报文");grid.SelectedItem=msg;};
                    menu.Items.Add(item);
                }
                menu.IsOpen=true;
            }),
            UiKit.Button("复制",(_,_)=>{
                if(grid.SelectedItem is UiCanMessage m){var copy=new UiCanMessage{Name=m.Name+" - 副本",Id=m.Id,
                    Data=m.Data,Enabled=m.Enabled,Periodic=m.Periodic,PeriodMs=m.PeriodMs};
                    messages.Add(copy);grid.SelectedItem=copy;state.Changed("复制待发报文");}
            }),
            UiKit.Button("移除",(_,_)=>{if(grid.SelectedItem is UiCanMessage m){messages.Remove(m);state.Changed("移除报文");}}),
            new Button{Content="▷ 实体发送",IsEnabled=false,ToolTip="USB/CAN 未接入；只允许编辑发送计划。"}),
            toggle,issue);
        DockPanel.SetDock(head,Dock.Top);rows.Children.Add(head);
        rows.Children.Add(grid);
        grid.CellEditEnding+=(_,_)=>{
            state.Changed("编辑 CAN 待发字段");
            if(grid.SelectedItem is UiCanMessage m&&!UiStore.IsCanId(m.Id))
                issue.Text="CAN ID 格式或范围无效，请修正后再使用此配置。";
        };
        var keyMini=UiKit.Vertical(UiKit.Note("CAN 工具内的外部输入快捷控制，仅编辑状态；台架专业工作区提供完整 20 Key 和动作编排。",12));
        var mini=new UniformGrid{Columns=4};
        for(var i=0;i<20;i++)
        {
            var n=i;
            var k=new CheckBox{Margin=new Thickness(6,8,0,8)};
            k.SetBinding(ContentControl.ContentProperty,new Binding(nameof(UiKeySwitch.Label))
                {Source=state.KeySwitches[i]});
            k.SetBinding(ToggleButton.IsCheckedProperty,new Binding(nameof(UiKeySwitch.Closed))
                {Source=state.KeySwitches[i],Mode=BindingMode.TwoWay});
            k.Checked+=(_,_)=>state.SetKeyState(n,true);
            k.Unchecked+=(_,_)=>state.SetKeyState(n,false);
            mini.Children.Add(k);
        }
        keyMini.Children.Add(mini);
        var log=UiKit.Vertical(UiKit.Heading("CAN 监视",15),UiKit.Note("状态：未连接硬件",12),
            UiKit.Empty("尚无真实 CAN 帧","不展示生成的 RX/TX 报文。接入真实总线之后才显示真实时间戳、方向、报文及解析。"));
        return UiKit.Page("CAN 发送与监视","左侧多报文编辑、中部输入条件、右侧实时观察。编辑单次/周期计划；实体发送暂不提供。",
            UiKit.ThreeColumns(UiKit.Side("多报文发送计划",rows),
                UiKit.Side("外部条件快捷操作",UiKit.Scroll(keyMini)),
                UiKit.Side("总线监视",UiKit.Scroll(log)),475,290));
    }
    private static FrameworkElement KeyGrid(UiStore state)
    {
        var panel=new UniformGrid{Columns=5};
        for(var i=0;i<20;i++)
        {
            var n=i;
            var button=new ToggleButton{Height=40,Margin=new Thickness(4),
                ToolTip="KEY_SIM 内部电阻网络开关 "+(i+1)+"；右键重命名。仅改变 UI 会话状态。"};
            button.SetBinding(ContentControl.ContentProperty,new Binding(nameof(UiKeySwitch.Label))
                {Source=state.KeySwitches[i]});
            button.SetBinding(ToggleButton.IsCheckedProperty,new Binding(nameof(UiKeySwitch.Closed))
                {Source=state.KeySwitches[i],Mode=BindingMode.TwoWay});
            button.Checked+=(_,_)=>state.SetKeyState(n,true);
            button.Unchecked+=(_,_)=>state.SetKeyState(n,false);
            var context=new ContextMenu();
            var rename=new MenuItem{Header="重命名当前开关（UI 标签）"};
            rename.Click+=(_,_)=>{
                var name=UiKit.AskText(Application.Current.MainWindow,"修改 KEY_SIM 标签",
                    "K"+(n+1).ToString("00")+" 的显示名称",state.KeySwitches[n].Label);
                if(name is null)return;
                state.KeySwitches[n].Label=name;
                state.Changed("重命名 KEY_SIM 开关");
            };
            context.Items.Add(rename);
            button.ContextMenu=context;
            panel.Children.Add(button);
        }
        return panel;
    }
    public static UIElement Bench(UiStore state)
    {
        var left=new StackPanel{Margin=new Thickness(14)};
        left.Children.Add(UiKit.Heading("工位外部条件",15));
        left.Children.Add(UiKit.Note("KEY_SIM 的 20 Key 是电阻网络组合开关，形成模拟电压输出；不可直接等同 20 个独立功能按键。"));
        left.Children.Add(KeyGrid(state));
        var ign=new CheckBox{Content="IGN 条件（UI 计划）",Margin=new Thickness(4,14,4,9),IsChecked=state.IgnOn};
        ign.Checked+=(_,_)=>{state.IgnOn=true;state.Changed("设置 IGN 计划");};
        ign.Unchecked+=(_,_)=>{state.IgnOn=false;state.Changed("清除 IGN 计划");};
        var fixture=new CheckBox{Content="夹具压紧条件（UI 计划）",Margin=new Thickness(4,0,4,10),IsChecked=state.FixtureEngaged};
        fixture.Checked+=(_,_)=>{state.FixtureEngaged=true;state.Changed("设置夹具计划");};
        fixture.Unchecked+=(_,_)=>{state.FixtureEngaged=false;state.Changed("清除夹具计划");};
        left.Children.Add(ign);left.Children.Add(fixture);
        left.Children.Add(UiKit.Note("上方是计划输入，不会控制主控板、继电器或夹具。"));
        left.Children.Add(new Button{Content="发送当前外部输入",IsEnabled=false,
            ToolTip="实体外部输入需要真实 DEVICE 授权；纯 UI 阶段不会产生物理输出。"});
        var actions=new DockPanel{Margin=new Thickness(12)};
        var table=UiKit.Table(state.KeyActions,("动作名称",nameof(UiKeyAction.Name),0,true),
            ("保持 ms",nameof(UiKeyAction.HoldMs),90,true),("开关组合",nameof(UiKeyAction.Summary),195,false));
        var top=UiKit.Vertical(UiKit.Heading("外部输入动作编排",15),
            UiKit.Actions(
                UiKit.Button("＋ 记录当前组合",(_,_)=>{
                    var item=new UiKeyAction{Name="组合动作 "+(state.KeyActions.Count+1),HoldMs=state.HoldMs};
                    foreach(var i in Enumerable.Range(0,20).Where(i=>state.KeyStates[i]))item.ClosedKeys.Add(i+1);
                    state.KeyActions.Add(item);table.SelectedItem=item;state.Changed("记录输入动作");
                },true),
                UiKit.Button("删除",(_,_)=>{if(table.SelectedItem is UiKeyAction x){
                    state.KeyActions.Remove(x);state.Changed("删除输入动作");}}),
                UiKit.Button("上移",(_,_)=>Move(-1)),
                UiKit.Button("下移",(_,_)=>Move(1))),
            UiKit.Note("操作仅记录输入组合与保持时间。动作未写入 USB，也没有模拟的 ECU 反馈。"));
        void Move(int offset)
        {
            if(table.SelectedItem is not UiKeyAction x)return;
            var i=state.KeyActions.IndexOf(x);var j=i+offset;
            if(j<0||j>=state.KeyActions.Count)return;
            state.KeyActions.Move(i,j);state.Changed("调整动作顺序");
        }
        DockPanel.SetDock(top,Dock.Top);actions.Children.Add(top);actions.Children.Add(table);
        var evidence=UiKit.Vertical(UiKit.Heading("DUT 观察",15),
            UiKit.Note("USB 状态：未接入 · 实际 CAN：无 · 物理输出：未知",12),
            UiKit.Empty("没有来自 ECU 的观察事实","连接与操作合同属于后续真实集成；本版不伪造 ACK、机械状态或 PASS。"));
        evidence.Margin=new Thickness(13);
        return UiKit.Page("综合手动台架","综合外部激励与 DUT 观察的专业工作空间；不是 KEY 优先或 EPB 专用窗口。",
            UiKit.ThreeColumns(UiKit.Side("组合输入",UiKit.Scroll(left)),UiKit.Side("动作序列",actions),
                UiKit.Side("响应及状态",UiKit.Scroll(evidence)),345,300));
    }
    public static UIElement Inputs(UiStore state)
    {
        var duration=UiKit.Text(state.HoldMs.ToString());
        var detail=UiKit.Vertical(UiKit.Heading("20 Key 组合编辑",15),
            UiKit.Note("每个 K 是 KEY_SIM 电阻开关。下方选择可任意组合且保持，不映射成 20 个功能按钮。"),
            KeyGrid(state),
            UiKit.Field("保持时长（毫秒）",duration));
        duration.TextChanged+=(_,_)=>{
            if(int.TryParse(duration.Text,out var n)&&n>=0&&n<=600000)
                state.HoldMs=n;
        };
        var create=UiKit.Button("保存当前组合作为动作",(_,_)=>{
            if(!int.TryParse(duration.Text,out var ms)||ms<0){state.Status="保持时间无效";return;}
            var x=new UiKeyAction{Name="输入动作 "+(state.KeyActions.Count+1),HoldMs=ms};
            foreach(var i in Enumerable.Range(0,20).Where(i=>state.KeyStates[i]))x.ClosedKeys.Add(i+1);
            state.KeyActions.Add(x);state.Changed("新建输入组合");
        },true);
        detail.Children.Add(create);
        detail.Margin=new Thickness(16);
        var list=UiKit.Table(state.KeyActions,("名称",nameof(UiKeyAction.Name),0,true),
            ("保持 ms",nameof(UiKeyAction.HoldMs),100,true),("开关组合",nameof(UiKeyAction.Summary),200,false));
        var right=new DockPanel{Margin=new Thickness(12)};
        var top=UiKit.Actions(UiKit.Button("复制动作",(_,_)=>{
            if(list.SelectedItem is not UiKeyAction item)return;
            var copy=new UiKeyAction{Name=item.Name+" - 副本",HoldMs=item.HoldMs};
            foreach(var key in item.ClosedKeys)copy.ClosedKeys.Add(key);
            state.KeyActions.Add(copy);state.Changed("复制动作");
        }),UiKit.Button("删除动作",(_,_)=>{
            if(list.SelectedItem is UiKeyAction item){state.KeyActions.Remove(item);state.Changed("删除动作");}
        }));
        DockPanel.SetDock(top,Dock.Top);right.Children.Add(top);right.Children.Add(list);
        return UiKit.Page("输入条件与动作配置","独立编辑输入组合、动作名称及保持时间；供未来测试步骤和手动台架共同引用。",
            UiKit.Split(UiKit.Side("组合编辑",UiKit.Scroll(detail)),UiKit.Side("动作列表",right),490));
    }
    public static UIElement Uds(UiStore state)
    {
        var grid=UiKit.Table(state.UdsRequests,("请求名称",nameof(UiUdsRequest.Name),0,true),
            ("SID",nameof(UiUdsRequest.Service),70,true),
            ("Payload HEX",nameof(UiUdsRequest.Payload),170,true),
            ("预期响应",nameof(UiUdsRequest.Expected),115,true));
        var result=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=UiKit.Muted,Margin=new Thickness(12)};
        var services=new ComboBox{ItemsSource=new[]{"0x10 会话控制","0x11 ECU 复位","0x22 读 DID","0x27 安全访问","0x2E 写 DID",
            "0x31 例程控制","0x34 请求下载","0x36 传输数据","0x37 退出传输","0x3E Tester Present"},SelectedIndex=2};
        var left=UiKit.Vertical(UiKit.Heading("请求序列",15),UiKit.Actions(
            UiKit.Button("＋ 请求",(_,_)=>{
                var sid=(services.SelectedItem as string??"0x22")[..4];
                var x=new UiUdsRequest{Service=sid};state.UdsRequests.Add(x);grid.SelectedItem=x;
                state.Changed("增加 UDS 请求");
            },true),
            UiKit.Button("复制",(_,_)=>{
                if(grid.SelectedItem is UiUdsRequest x){var copy=new UiUdsRequest{Name=x.Name+" - 副本",Service=x.Service,
                    Payload=x.Payload,Expected=x.Expected};state.UdsRequests.Add(copy);state.Changed("复制诊断请求");}
            }),
            UiKit.Button("移除",(_,_)=>{
                if(grid.SelectedItem is UiUdsRequest x){state.UdsRequests.Remove(x);state.Changed("移除诊断请求");}
            }),
            new Button{Content="▷ 实体发送",IsEnabled=false,ToolTip="UDS/ISO-TP 与设备未接入，无法发送。"}),
            UiKit.Field("服务模板",services),
            UiKit.Note("请求序列为可编辑 UI 内容，尚不进行 ISO-TP 发送或会话切换。"));
        left.Margin=new Thickness(12);
        var central=new DockPanel{Margin=new Thickness(12)};
        var title=UiKit.Vertical(UiKit.Heading("请求参数表",15),UiKit.Note("双击单元格可直接修改参数，既不触发实体发送，也不会产生 NRC 或正响应。"));
        DockPanel.SetDock(title,Dock.Top);central.Children.Add(title);central.Children.Add(grid);
        var trace=UiKit.Vertical(UiKit.Heading("响应关联与时间线",15),
            UiKit.Note("状态：未连接 ECU；没有诊断响应。",12),result,
            UiKit.Empty("没有真实响应","服务请求与响应必须由实际 UDS/ISO-TP 服务关联；不能用 UI 操作生成假的 ECU 响应。"));
        trace.Margin=new Thickness(12);
        grid.SelectionChanged+=(_,_)=>result.Text=grid.SelectedItem is UiUdsRequest x?
            "当前请求："+x.Name+" · "+x.Service+"；尚无发送或响应。":"尚未选择请求。";
        grid.CellEditEnding+=(_,_)=>state.Changed("编辑 UDS 请求参数");
        return UiKit.Page("UDS 诊断工作区","服务模板、可编辑请求序列与响应观察分区，面向持续调试而非单次弹窗。",
            UiKit.ThreeColumns(UiKit.Side("诊断服务",left),UiKit.Side("请求序列编辑",central),
                UiKit.Side("诊断响应",UiKit.Scroll(trace)),260,320));
    }
    public static UIElement Flash(UiStore state)
    {
        var filename=UiKit.BoundText(state.FlashProfile,nameof(UiFlashProfile.ImagePath),true);
        var profile=UiKit.BoundText(state.FlashProfile,nameof(UiFlashProfile.Name));
        var request=UiKit.BoundText(state.FlashProfile,nameof(UiFlashProfile.AppRequest));
        var response=UiKit.BoundText(state.FlashProfile,nameof(UiFlashProfile.AppResponse));
        var bootRequest=UiKit.BoundText(state.FlashProfile,nameof(UiFlashProfile.BootRequest));
        var bootResponse=UiKit.BoundText(state.FlashProfile,nameof(UiFlashProfile.BootResponse));
        var status=UiKit.Note("尚未选择镜像。配置输入仅存在于当前 UI 工作会话。");
        var input=UiKit.Vertical(
            UiKit.Heading("镜像与烧录配置",15),
            UiKit.Field("配置名称",profile),
            UiKit.Field("固件文件",filename),
            UiKit.Button("选择 HEX / S19 文件",(_,_)=>{
                var picker=new OpenFileDialog{Filter="固件文件|*.hex;*.s19;*.s28;*.s37|所有文件|*.*"};
                if(picker.ShowDialog()!=true)return;
                state.FlashProfile.ImagePath=picker.FileName;state.Changed("选择烧录文件");
            }),
            UiKit.Field("应用诊断请求 ID",request),UiKit.Field("应用诊断响应 ID",response),
            UiKit.Field("Boot 请求 ID",bootRequest),UiKit.Field("Boot 响应 ID",bootResponse),status);
        input.Margin=new Thickness(15);
        var stages=new ListBox{ItemsSource=new[]{
            "01  配置与文件检查","02  进入编程会话","03  安全访问","04  请求下载",
            "05  数据块传输","06  传输结束","07  校验与例程","08  ECU 复位","09  烧录收尾"
        },Margin=new Thickness(14,5,14,14)};
        var check=UiKit.Note("状态：未进行 UI 配置检查。");
        var center=UiKit.Vertical(UiKit.Heading("烧录过程",15),
            UiKit.Note("以下是阶段结构，不是已发生的 UDS 服务与进度。"),
            UiKit.Actions(UiKit.Button("检查填写内容",(_,_)=>{
                var issues=new List<string>();
                if(filename.Text.Length==0)issues.Add("未选择镜像");
                foreach(var (label,field) in new[]{("应用请求",request),("应用响应",response),
                    ("Boot 请求",bootRequest),("Boot 响应",bootResponse)})
                    if(!UiStore.IsCanId(field.Text))issues.Add(label+" ID 无效");
                check.Text=issues.Count==0?"UI 配置字段检查通过；尚未进行真实烧录预检。":
                    string.Join("；",issues);
                state.Changed("检查烧录 UI 配置");
            },true)),check,
            UiKit.Heading("正式烧录阶段",14,new Thickness(0,15,0,5)),
            new Button{Content="▷ 开始实体烧录",IsEnabled=false,
                ToolTip="需要真实 FlashLab、工位独占许可与严格的编程预检。"},
            stages);
        center.Margin=new Thickness(13);
        var evidence=UiKit.Vertical(UiKit.Heading("进度与审计",15),
            UiKit.Note("未接入实体 FlashLab：进度未知、确认字节 0（未执行）、不存在 ECU 复位反馈。"),
            UiKit.Empty("无烧录执行记录","只有真实服务返回阶段、ACK、字节计数和结果时才显示执行进度。"));
        evidence.Margin=new Thickness(13);
        return UiKit.Page("ECU 烧录工具","将配置、可控操作、阶段过程与证据分离。UI 配置检查不等于真实 ECU 烧录准备完成。",
            UiKit.ThreeColumns(UiKit.Side("烧录配置",UiKit.Scroll(input)),UiKit.Side("阶段计划",UiKit.Scroll(center)),
                UiKit.Side("执行与审计",UiKit.Scroll(evidence)),380,295));
    }
    public static UIElement Wave(UiStore state)
    {
        var signals=UiKit.Table(state.WaveSignals,("显示信号",nameof(UiWaveSignal.Name),0,true),
            ("数据源",nameof(UiWaveSignal.Source),115,true));
        var manual=UiKit.Text();
        manual.ToolTip="输入逗号分隔的数字，例如 1,2,3；这些仅是用户手工绘制数据，不是 ECU 采样";
        signals.SelectionChanged+=(_,_)=>
        {
            manual.Text=signals.SelectedItem is UiWaveSignal selected?selected.Points:"";
        };
        manual.LostKeyboardFocus+=(_,_)=>
        {
            if(signals.SelectedItem is UiWaveSignal selected)
            {selected.Points=manual.Text;state.Changed("修改手工绘图数据");}
        };
        var add=UiKit.Actions(
            UiKit.Button("＋ 信号",(_,_)=>{
                var x=new UiWaveSignal{Name="信号 "+(state.WaveSignals.Count+1)};
                state.WaveSignals.Add(x);state.Changed("增加波形观察信号");
            },true),
            UiKit.Button("移除",(_,_)=>{
                if(signals.SelectedItem is UiWaveSignal x){state.WaveSignals.Remove(x);state.Changed("移除波形观察信号");}
            }));
        var left=new DockPanel{Margin=new Thickness(12)};
        DockPanel.SetDock(add,Dock.Top);left.Children.Add(add);left.Children.Add(signals);
        var chart=new Canvas{Background=Brushes.White,MinHeight=280,ClipToBounds=true};
        var chartHost=new Grid{Background=Brushes.White,Margin=new Thickness(12)};
        chartHost.Children.Add(chart);
        var cursor=UiKit.Note("游标：未选择。没有真实采样值。");
        var zoom=new Slider{Minimum=1,Maximum=20,Value=5,Margin=new Thickness(10),TickFrequency=1,IsSnapToTickEnabled=true};
        var zoomLabel=UiKit.Note("视图缩放：5×（仅坐标网格）");
        zoom.ValueChanged+=(_,_)=>{zoomLabel.Text="视图缩放："+Math.Round(zoom.Value)+"×（仅坐标网格）";};
        void Draw()
        {
            chart.Children.Clear();
            if(chart.ActualWidth<80||chart.ActualHeight<80)return;
            var width=chart.ActualWidth;var height=chart.ActualHeight;
            for(var i=0;i<=10;i++)
            {
                var v=i*width/10;
                var x=new Line{X1=v,X2=v,Y1=0,Y2=height,Stroke=UiKit.Line,StrokeThickness=.7};
                chart.Children.Add(x);
            }
            for(var i=0;i<=8;i++)
            {
                var v=i*height/8;
                var x=new Line{X1=0,X2=width,Y1=v,Y2=v,Stroke=UiKit.Line,StrokeThickness=.7};
                chart.Children.Add(x);
            }
            var warning=new TextBlock{Text="未接真实采样：仅显示用户手工填写的曲线",
                Foreground=UiKit.Muted,TextAlignment=TextAlignment.Center,FontSize=13};
            Canvas.SetLeft(warning,Math.Max(8,(width-320)/2));Canvas.SetTop(warning,Math.Max(8,(height-48)/2));
            chart.Children.Add(warning);
            var visible=state.WaveSignals.Where(s=>s.Visible && s.Points.Length>0).Take(4).ToList();
            var colors=new[]{Brushes.SteelBlue,Brushes.DarkOrange,Brushes.MediumSeaGreen,Brushes.MediumPurple};
            for(var i=0;i<visible.Count;i++)
            {
                var numbers=visible[i].Points.Split(new[]{',','，',' ',';'},StringSplitOptions.RemoveEmptyEntries)
                    .Select(x=>double.TryParse(x,System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,out var n)?(double?)n:null).ToArray();
                if(numbers.Length<2||numbers.Any(x=>x is null))continue;
                var lo=numbers.Min(x=>x!.Value);
                var hi=numbers.Max(x=>x!.Value);
                var span=Math.Max(hi-lo,0.01);
                var line=new Polyline{Stroke=colors[i],StrokeThickness=1.8};
                for(var j=0;j<numbers.Length;j++)
                {
                    var x=15+j*(width-30)/(numbers.Length-1);
                    var y=height-15-(numbers[j]!.Value-lo)/span*(height-45);
                    line.Points.Add(new Point(x,y));
                }
                chart.Children.Add(line);
            }
        }
        chart.SizeChanged+=(_,_)=>Draw();
        manual.TextChanged+=(_,_)=>{ if(signals.SelectedItem is UiWaveSignal selected)
            {selected.Points=manual.Text;Draw();} };
        state.WaveSignals.CollectionChanged+=(_,_)=>Draw();
        chart.MouseLeftButtonDown+=(_,e)=>
        {
            var p=e.GetPosition(chart);
            cursor.Text="游标 A（UI 画布位置）：x="+p.X.ToString("F0")+" px，y="+p.Y.ToString("F0")+" px；不是采样时间/测量值。";
            state.Changed("移动波形 UI 游标");
        };
        var main=new DockPanel{Margin=new Thickness(12)};
        var chartToolbar=UiKit.Vertical(UiKit.Heading("波形画布与时间轴",15),
            UiKit.Note("滚动/缩放/游标是 UI 交互；没有真实采样数据，不绘制合成的电压或电流曲线。"),
            UiKit.Field("视图缩放",zoom),zoomLabel,cursor,
            new Button{Content="开始实时采集",IsEnabled=false,
                ToolTip="真实 CAN 信号采样未接线。手工数值可以编辑并绘制。"});
        DockPanel.SetDock(chartToolbar,Dock.Top);main.Children.Add(chartToolbar);
        main.Children.Add(chartHost);
        var reference=UiKit.Vertical(UiKit.Heading("参考与判定",15),
            UiKit.Note("参考和容差需在后续绑定真实采样。此处只设计选择和编辑位置。"),
            UiKit.Field("参考名称",UiKit.BoundText(state.WaveReference,nameof(UiWaveReference.Name))),
            UiKit.Field("判定容差",UiKit.BoundText(state.WaveReference,nameof(UiWaveReference.Tolerance))),
            UiKit.Field("有效覆盖率",UiKit.BoundText(state.WaveReference,nameof(UiWaveReference.Coverage))),
            UiKit.Empty("尚无参考数据","不能在缺少来源与采样质量证据时显示通过或失败。"));
        reference.Margin=new Thickness(12);
        return UiKit.Page("波形分析","曲线画布主导的专业交互：信号管理 / 绘图与游标 / 参考分析。不是通用表单界面。",
            UiKit.ThreeColumns(UiKit.Side("信号列表",left),UiKit.Side("波形画布",main),
                UiKit.Side("参考与分析",UiKit.Scroll(reference)),248,290));
    }
    public static UIElement Scan(UiStore state)
    {
        var serial=UiKit.BoundText(state.ProductionDraft,nameof(UiProductionDraft.Serial));
        var pattern=UiKit.Text("长度 6–64；允许数字/字母/-/_");
        pattern.IsReadOnly=true;
        var status=UiKit.Note("等待扫码输入。没有连接到正式编号表、台架或实际执行。");
        var batch=UiKit.BoundText(state.ProductionDraft,nameof(UiProductionDraft.Station));
        var entry=UiKit.Vertical(UiKit.Heading("本次检测对象",17),
            UiKit.Field("扫码枪 / 编号输入",serial),UiKit.Field("编号规则（UI 提示）",pattern),
            UiKit.Actions(UiKit.Button("检查编号格式",(_,_)=>{
                var id=serial.Text.Trim();
                var valid=id.Length is >=6 and <=64 && id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
                status.Text=valid?"格式检查通过；匹配工程和测试配置需后续真实接线。":"编号格式不符合当前 UI 示例规则。";
                status.Foreground=valid?UiKit.Green:Brushes.IndianRed;
                state.Changed("校验扫码编号格式");
            },true),UiKit.Button("清空",(_,_)=>{serial.Clear();status.Text="等待扫码输入。";})),
            status,UiKit.Field("工位标签",batch),
            new Button{Content="▷ 开始正式生产检测",IsEnabled=false,
                ToolTip="需要真实编号匹配、工位许可、TEST 和 RECORD 后端。"});
        entry.Margin=new Thickness(20);
        var steps=UiKit.Vertical(UiKit.Heading("受控检测任务",16),
            UiKit.Note("1 识别编号 → 2 查找真实匹配 → 3 工位预检 → 4 真实执行 → 5 归档报告"),
            UiKit.Heading("当前尚未形成真实匹配",15,new Thickness(0,15,0,4)),
            UiKit.Empty("生产执行尚未接线","扫码格式验证不是 ECU 结果；不产生虚构 PASS、计数或已完成记录。"));
        steps.Margin=new Thickness(18);
        return UiKit.Page("生产扫码与自动检测","生产入口聚焦编号、任务和结果，不要求操作员进入复杂的工程编辑界面。",
            UiKit.Split(UiKit.Side("识别与输入",UiKit.Scroll(entry)),UiKit.Side("执行与结果",UiKit.Scroll(steps)),430));
    }
    public static UIElement Records(UiStore state)
    {
        var search=UiKit.Text();
        var filter=new ComboBox{ItemsSource=new[]{"全部记录","通过","失败","错误","中止"},SelectedIndex=0};
        var listing=new DataGrid{AutoGenerateColumns=false,ItemsSource=Array.Empty<string>()};
        foreach(var h in new[]{"时间","编号","测试项目","运行状态","报告"})
            listing.Columns.Add(new DataGridTextColumn{Header=h,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        var left=new DockPanel{Margin=new Thickness(12)};
        var top=UiKit.Vertical(UiKit.Heading("历史筛选",15),UiKit.Field("按编号 / 任务搜索",search),
            UiKit.Field("运行状态",filter),UiKit.Note("真实运行记录尚未接线；筛选操作不生成历史数据。"));
        DockPanel.SetDock(top,Dock.Top);left.Children.Add(top);left.Children.Add(listing);
        var detail=UiKit.Vertical(UiKit.Heading("运行详情和证据",16),
            new Button{Content="导出正式报告",IsEnabled=false,ToolTip="没有真实 RunRecord 和报告文件。"},
            UiKit.Empty("当前没有真实运行记录","后续通过 RunId、StepOccurrence 与附件关联浏览证据；不会根据 UI 草稿推断运行通过。"));
        detail.Margin=new Thickness(17);
        return UiKit.Page("报告与历史","历史索引、可筛选记录、运行快照、步骤证据和报告浏览的独立工作环境。",
            UiKit.Split(UiKit.Side("运行记录",left),UiKit.Side("报告和关联证据",UiKit.Scroll(detail)),550));
    }
    public static UIElement Device(UiStore state)
    {
        var left=UiKit.Vertical(UiKit.Heading("当前工位",16),
            new Button{Content="连接主控",IsEnabled=false,ToolTip="当前纯 UI 产品不拥有 USB 主控连接。"},
            UiKit.Note("主控连接：未接入（纯 UI）"),UiKit.Note("主控身份：未知"),
            UiKit.Note("ECU CAN：未连接"),UiKit.Note("KEY_SIM CAN：未连接"),
            UiKit.Note("硬件请求 / ACK：无真实事实"),
            UiKit.Heading("物理状态与输出",15,new Thickness(0,20,0,8)),
            UiKit.Note("IGN：未知"),UiKit.Note("夹具：未知"),
            UiKit.Note("KEY_SIM：未观察到物理反馈"),
            UiKit.Note("所有实体操作均须在后续正式硬件集成阶段授权，当前只提供信息结构。"));
        left.Margin=new Thickness(20);
        var notes=UiKit.Vertical(UiKit.Heading("诊断与异常恢复",16),
            UiKit.Note("工位身份、连接世代、操作请求、主控 ACK、ECU 响应、测量与结论是不同的事实层。"),
            UiKit.Empty("没有真实设备诊断事件","没有 USB 连接、请求、超时、CAN 帧或 ECU 响应；不要推测设备已经就绪。"));
        notes.Margin=new Thickness(19);
        return UiKit.Page("设备和工位诊断","设备身份、通路、工位状态与故障诊断独立于正常的离线编辑工作。",
            UiKit.Split(UiKit.Side("连接与状态",UiKit.Scroll(left)),UiKit.Side("诊断时间线",UiKit.Scroll(notes)),420));
    }
}
