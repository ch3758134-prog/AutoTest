using AutoTest.Desktop.Core;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop.Views;

public static class Workspaces
{
    public static UIElement Build(string key,UiStore state,MainWindow app)
    {
        if(key.StartsWith("test:",StringComparison.Ordinal))
        {
            var id=key["test:".Length..];
            var test=state.Cases.FirstOrDefault(x=>x.Id==id)??state.Cases[0];
            return new StepAuthoringView(state,test);
        }
        return key switch
        {
            "home"=>Home(state,app),
            "test-project"=>TestProjects(state,app),
            "run"=>RunAndDebug(state,app),
            "flows"=>PublicFlows(state),
            "resources"=>Resources(state,app),
            "tools"=>Tools(state,app),
            "dbc"=>Specialists.Dbc(state),
            "can"=>Specialists.Can(state),
            "bench"=>Specialists.Bench(state),
            "inputs"=>Specialists.Inputs(state),
            "uds"=>Specialists.Uds(state),
            "flash"=>Specialists.Flash(state),
            "wave"=>Specialists.Wave(state),
            "scan"=>Specialists.Scan(state),
            "records"=>Specialists.Records(state),
            "device"=>Specialists.Device(state),
            _=>Home(state,app)
        };
    }
    private static UIElement Home(UiStore state,MainWindow app)
    {
        var space=new StackPanel{Margin=new Thickness(0,5,0,15)};
        space.Children.Add(UiKit.Heading("继续你的工作",22));
        space.Children.Add(UiKit.Note("当前为纯 UI 产品体验版本。创建、编辑、浏览和导航均可操作；所有硬件动作与真实业务接线尚未启用。",13,new Thickness(0,0,0,24)));
        var row=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,15)};
        void Card(string title,string note,string action,Action callback)
        {
            var group=UiKit.Vertical(UiKit.Heading(title,17),UiKit.Note(note,12,new Thickness(0,0,0,17)),
                UiKit.Button(action,(_,_)=>callback(),true));
            var border=UiKit.Card(group,new Thickness(0,0,12,0));
            group.Margin=new Thickness(20);
            row.Children.Add(border);
        }
        Card("自动测试","管理测试用例、编辑流程、选择本次运行范围。","打开测试项目",
            ()=>app.Open("test-project","测试项目"));
        Card("编辑测试程序","直接进入步骤编辑，不要求先创建真实工程。","进入编辑器",
            ()=>app.Open("test:"+state.Cases[0].Id,state.Cases[0].Name));
        Card("专业工具","在各自的工作环境中处理 CAN、DBC、UDS、波形等任务。","打开工具中心",
            ()=>app.Open("tools","专业工具"));
        space.Children.Add(row);
        space.Children.Add(UiKit.Heading("当前工作区",17,new Thickness(0,16,0,9)));
        var section=UiKit.Vertical(
            UiKit.Note("工作环境名称可随时修改。该名称仅用于本次 UI 体验，不生成真实 ECU 工程。",12),
            UiKit.Field("会话名称",UiKit.BoundText(state,nameof(UiStore.Workspace))),
            UiKit.Heading("持续工作",15,new Thickness(0,15,0,8)),
            UiKit.Note("左侧是资源和任务入口；中央页签支持同时打开多个文档；专业工具可用独立副窗口，不挤占当前编辑器。",12),
            UiKit.Note("快捷键：Ctrl+N 新建用例，Ctrl+O 恢复 UI 会话，Ctrl+S 保存 UI 会话，Ctrl+Tab 切换文档。",12));
        section.Margin=new Thickness(20);
        space.Children.Add(UiKit.Card(section));
        return UiKit.Scroll(UiKit.Page("工作台","从真实任务开始，而不是从模块清单开始。",space));
    }
    private static UIElement Tools(UiStore state,MainWindow app)
    {
        var host=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var body=new WrapPanel{Margin=new Thickness(3,5,0,0)};
        foreach(var (key,name,desc) in new (string,string,string)[]
        {
            ("dbc","DBC 数据库","报文、信号、中文别名、位定义与参数编辑"),
            ("can","CAN 发送与监视","待发列表、周期与单次配置、日志观察"),
            ("bench","综合手动台架","20 Key 组合、保持时间、工位条件与观察"),
            ("inputs","输入配置","按键组合、动作编排及持续时间"),
            ("uds","UDS 诊断","服务请求、参数编辑与请求队列"),
            ("flash","ECU 烧录","配置、文件、阶段计划和风险信息"),
            ("wave","波形分析","信号与曲线视图、参考、游标"),
            ("scan","生产扫码","编号识别、任务配置和操作路径"),
            ("records","报告与历史","索引、筛选、详情与证据导航"),
            ("device","设备诊断","设备身份、工位通路、状态与诊断")
        })
        {
            var content=UiKit.Vertical(
                UiKit.Heading(name,16),
                UiKit.Note(desc,12,new Thickness(0,0,0,16)),
                UiKit.Actions(UiKit.Button("工作区页签",(_,_)=>app.Open(key,name)),
                    UiKit.Button("独立工具窗口",(_,_)=>app.OpenTool(key,name))));
            content.Margin=new Thickness(16);
            var card=UiKit.Card(content,new Thickness(0,0,12,12));
            card.Width=286;card.MinHeight=165;
            body.Children.Add(card);
        }
        host.Content=body;
        return UiKit.Page("专业工具中心","工具有自己的操作环境；打开第二工具不会取代正在编辑的测试程序。",host);
    }
    private static UIElement TestProjects(UiStore state,MainWindow app)
    {
        var list=new DataGrid{ItemsSource=state.Cases,CanUserAddRows=false,AutoGenerateColumns=false,
            AlternatingRowBackground=new SolidColorBrush(Color.FromRgb(248,250,252)),
            SelectionMode=DataGridSelectionMode.Single};
        list.Columns.Add(new DataGridCheckBoxColumn{Header="本次选择",Binding=new Binding(nameof(UiTestCase.SelectedForRun)){Mode=BindingMode.TwoWay},Width=95});
        list.Columns.Add(new DataGridTextColumn{Header="用例名称",Binding=new Binding(nameof(UiTestCase.Name)){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},
            Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        list.Columns.Add(new DataGridTextColumn{Header="用例 ID（UI）",Binding=new Binding(nameof(UiTestCase.Id)),IsReadOnly=true,Width=200});
        list.MouseDoubleClick+=(_,_)=>{if(list.SelectedItem is UiTestCase c) app.Open("test:"+c.Id,c.Name);};
        var actions=UiKit.Actions(
            UiKit.Button("＋ 新建用例",(_,_)=>{var c=state.NewCase();list.SelectedItem=c;app.Open("test:"+c.Id,c.Name);},true),
            UiKit.Button("编辑选中用例",(_,_)=>{if(list.SelectedItem is UiTestCase c)app.Open("test:"+c.Id,c.Name);}),
            UiKit.Button("上移",(_,_)=>Move(-1)),
            UiKit.Button("下移",(_,_)=>Move(1)),
            UiKit.Button("复制",(_,_)=>Duplicate()),
            UiKit.Button("删除",(_,_)=>Delete()));
        void Move(int offset)
        {
            if(list.SelectedItem is not UiTestCase item)return;
            var i=state.Cases.IndexOf(item);var target=i+offset;
            if(target<0||target>=state.Cases.Count)return;
            state.Cases.Move(i,target);state.Changed("调整用例顺序");
        }
        void Duplicate()
        {
            if(list.SelectedItem is not UiTestCase c)return;
            var clone=new UiTestCase{Name=c.Name+" - 副本",SelectedForRun=c.SelectedForRun};
            foreach(var n in c.Prepare)clone.Prepare.Add(UiStore.CloneStep(n));
            foreach(var n in c.Test)clone.Test.Add(UiStore.CloneStep(n));
            foreach(var n in c.Cleanup)clone.Cleanup.Add(UiStore.CloneStep(n));
            state.Cases.Add(clone);state.Projects[0].Cases.Add(clone);
            state.Changed("复制测试用例");list.SelectedItem=clone;
        }
        void Delete()
        {
            if(list.SelectedItem is not UiTestCase c)return;
            if(state.Cases.Count<=1){state.Status="至少保留一个 UI 编辑会话中的测试用例。";return;}
            if(!UiKit.Confirm(app,"确认从 UI 会话中删除选中用例？"))return;
            state.Cases.Remove(c);state.Projects[0].Cases.Remove(c);state.Changed("删除用例");
        }
        list.CellEditEnding+=(_,_)=>state.Changed("编辑测试项目");
        var stack=new DockPanel{Margin=new Thickness(0,5,0,0)};
        DockPanel.SetDock(actions,Dock.Top);stack.Children.Add(actions);
        var lower=UiKit.Vertical(UiKit.Heading("本次运行范围",14),
            UiKit.Note("勾选仅调整 UI 的本次计划，不会启动 ECU、修改真实测试程序或生成 PASS。",12));
        DockPanel.SetDock(lower,Dock.Bottom);lower.Margin=new Thickness(10,12,10,14);
        stack.Children.Add(lower);stack.Children.Add(list);
        return UiKit.Page("测试项目与用例","有序组织、选择、排序和编辑。双击用例直接打开连续编辑器。",stack);
    }
    private static UIElement RunAndDebug(UiStore state,MainWindow app)
    {
        var selected=UiKit.Table(state.Cases,("测试用例",nameof(UiTestCase.Name),0,false));
        selected.Columns.Insert(0,new DataGridCheckBoxColumn
        {
            Header="本次运行",Binding=new Binding(nameof(UiTestCase.SelectedForRun))
            {Mode=BindingMode.TwoWay},Width=94
        });
        var preflight=UiKit.Note("尚未进行结构检查。实体工位预检、设备授权和执行器未连接。");
        var current=UiKit.Note("本次运行：尚未开始。不存在 RunId、步骤事实或 PASS。");
        var plan=new DockPanel{Margin=new Thickness(12)};
        var planTop=UiKit.Vertical(
            UiKit.Heading("本次用例选择",15),
            UiKit.Note("本次选择独立于测试用例持久启用状态。勾选不会启动设备。"),
            UiKit.Actions(
                UiKit.Button("检查 UI 计划结构",(_,_)=>
                {
                    var cases=state.Cases.Where(c=>c.SelectedForRun).ToList();
                    var count=cases.Sum(c=>Walk(c.Prepare).Count()+Walk(c.Test).Count()+Walk(c.Cleanup).Count());
                    var problems=new List<string>();
                    if(cases.Count==0)problems.Add("尚未选择用例");
                    if(count==0)problems.Add("所选用例没有步骤");
                    preflight.Text=problems.Count==0
                        ?$"UI 结构检查：已选 {cases.Count} 个用例，共 {count} 步；未执行实体预检。"
                        :"UI 结构检查发现问题："+string.Join("、",problems);
                    state.Changed("检查本次测试 UI 计划");
                },true),
                UiKit.Button("编辑选中用例",(_,_)=>
                {
                    if(selected.SelectedItem is UiTestCase c)app.Open("test:"+c.Id,c.Name);
                })),
            preflight);
        DockPanel.SetDock(planTop,Dock.Top);plan.Children.Add(planTop);plan.Children.Add(selected);
        var steps=new ListBox{Margin=new Thickness(8,0,8,8)};
        var stepInfo=UiKit.Note("选择用例查看步骤定义。断点属于 UI 编辑标记，不能启动单步执行。");
        selected.SelectionChanged+=(_,_)=>
        {
            if(selected.SelectedItem is not UiTestCase test)return;
            steps.ItemsSource=Walk(test.Prepare).Select(x=>"准备 · "+(x.Breakpoint?"● 断点 · ":"")+x.Name)
                .Concat(Walk(test.Test).Select(x=>"测试 · "+(x.Breakpoint?"● 断点 · ":"")+x.Name))
                .Concat(Walk(test.Cleanup).Select(x=>"清理 · "+(x.Breakpoint?"● 断点 · ":"")+x.Name)).ToArray();
            stepInfo.Text="当前查看："+test.Name+"。此处是步骤定义，不是已执行事实。";
        };
        var editor=UiKit.Vertical(UiKit.Heading("步骤与调试目标",15),stepInfo,steps);
        editor.Margin=new Thickness(12);
        var execution=UiKit.Vertical(
            UiKit.Heading("运行控制",15),
            UiKit.Note("工程师执行视图强调当前运行范围、工位状态和错误证据，不能以编辑草稿代替正式 Run。"),
            UiKit.Heading("工位状态：未接入",14,new Thickness(0,14,0,7)),
            UiKit.Note("设备身份：未知 · 工位许可：无 · 当前执行：未开始"),
            UiKit.Heading("实体运行与单步",14,new Thickness(0,16,0,7)),
            new TextBlock{Text="开始运行 / 单步 / 运行到此 / 停止：等待真实 TEST 与 DEVICE 接线。",
                Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,16)},
            current,
            UiKit.Heading("步骤事实与证据",14,new Thickness(0,15,0,5)),
            UiKit.Empty("没有执行时间线",
                "执行事实、断点命中、异常、测量和报告只能来自真实后端；当前不能生成虚构记录。"));
        execution.Margin=new Thickness(12);
        return UiKit.Page("自动测试准备、运行与调试",
            "同一测试任务的运行工作空间，选测计划、步骤定义、工位状态和真实执行事实分层。",
            UiKit.ThreeColumns(UiKit.Side("运行选择",plan),
                UiKit.Side("步骤定义",UiKit.Scroll(editor)),
                UiKit.Side("运行控制与证据",UiKit.Scroll(execution)),350,365));
    }
    private static IEnumerable<UiStep> Walk(IEnumerable<UiStep> source)
    {
        foreach(var item in source)
        {
            yield return item;
            foreach(var child in Walk(item.Children))yield return child;
        }
    }

    private static UIElement PublicFlows(UiStore state)
    {
        var draft=new UiTestCase{Name="公共流程（单一连续步骤树）",Test=state.PublicFlow};
        return new StepAuthoringView(state,draft,true);
    }
    private static UIElement Resources(UiStore state,MainWindow app)
    {
        var resources=new ObservableCollection<UiResource>();
        void Sync()
        {
            resources.Clear();
            foreach(var c in state.Cases)resources.Add(new UiResource{Id=c.Id,Name=c.Name,Kind="测试用例",Description="TEST 编辑会话对象"});
            foreach(var m in state.DbcMessages)resources.Add(new UiResource{Name=m.Name,Kind="DBC 报文",Description=m.CanId});
            foreach(var m in state.CanMessages)resources.Add(new UiResource{Name=m.Name,Kind="CAN 待发",Description=m.Id});
            foreach(var r in state.Resources)resources.Add(r);
        }
        Sync();
        var grid=UiKit.Table(resources,("资源名称",nameof(UiResource.Name),0,true),
            ("类型",nameof(UiResource.Kind),145,false),("说明",nameof(UiResource.Description),220,true));
        var filter=UiKit.Text();
        filter.ToolTip="输入资源名称筛选";
        filter.TextChanged+=(_,_)=>grid.Items.Filter=x=>
            x is UiResource r && r.Name.Contains(filter.Text,StringComparison.OrdinalIgnoreCase);
        grid.MouseDoubleClick+=(_,_)=>
        {
            if(grid.SelectedItem is not UiResource r)return;
            var target=r.Kind switch{"测试用例"=>"test:"+r.Id,"DBC 报文"=>"dbc","CAN 待发"=>"can",_=>"resources"};
            if(target!="resources") app.Open(target,r.Name);
        };
        var toolbar=UiKit.Actions(
            UiKit.Button("＋ 自定义资源",(_,_)=>{
                var name=UiKit.AskText(app,"新增 UI 资源","资源名称","新建资源");
                if(name is null)return;
                state.Resources.Add(new UiResource{Name=name,Kind="自定义",Description="UI 会话资源"});
                Sync();state.Changed("新增自定义资源");
            },true),
            UiKit.Button("删除自定义资源",(_,_)=>{
                if(grid.SelectedItem is not UiResource r||r.Kind!="自定义")return;
                var original=state.Resources.FirstOrDefault(x=>x.Id==r.Id);
                if(original is null)return;
                state.Resources.Remove(original);Sync();state.Changed("删除资源");
            }),
            UiKit.Button("刷新索引",(_,_)=>Sync()));
        var dock=new DockPanel();
        var top=UiKit.Vertical(UiKit.Field("资源搜索",filter),toolbar);
        DockPanel.SetDock(top,Dock.Top);dock.Children.Add(top);dock.Children.Add(grid);
        return UiKit.Page("工程资源管理","当前仅为 UI 对象与导航索引：不是正式 PROJECT 清单或修订库。",dock);
    }
}

public sealed class StepAuthoringView : UserControl
{
    private readonly UiStore _store;
    private readonly UiTestCase _case;
    private readonly bool _flow;
    private readonly TreeView _tree=new(){Margin=new Thickness(6),Background=Brushes.White,BorderThickness=new Thickness(0)};
    private readonly StackPanel _inspector=new(){Margin=new Thickness(15)};
    private readonly StackPanel _stepDetails=new();
    private readonly ComboBox _types=new();
    private readonly ComboBox _phase=new();
    private readonly TextBlock _message=new(){TextWrapping=TextWrapping.Wrap,Foreground=UiKit.Muted,Margin=new Thickness(3,6,0,0)};
    private readonly Stack<string> _undo=new();
    private readonly Stack<string> _redo=new();
    private UiStep? _selected;
    private ObservableCollection<UiStep>? _selectedList;
    private bool _refreshing;
    private TextBox? _stepName;
    private readonly Dictionary<string,TextBox> _editors=new();
    private readonly List<(string Label,string Type)> _allTypes=
    [
        ("等待","Wait"),("条件 IF / ELSE","If"),("SWITCH 分支","Switch"),
        ("WHEN 等待","When"),("循环","Loop"),("异常 TRY / CATCH","TryCatch"),
        ("设置变量","SetVariable"),("计算表达式","Calculate"),("期望判定","Expect"),
        ("读取信号","Read"),("写入 / 外部激励","Write"),
        ("CAN 发送","CanSend"),("CAN 接收等待","CanWait"),("信号判定","AssertSignal"),
        ("UDS 诊断请求","UdsRequest"),("ECU 烧录","Flash"),
        ("波形开始","WaveStart"),("波形停止","WaveStop"),
        ("波形分析","WaveAnalyze"),("波形判定","WaveCompare"),
        ("子流程","Subroutine"),("调用公共流程","CallPublicFlow")
    ];
    public StepAuthoringView(UiStore store,UiTestCase testCase,bool flow=false)
    {
        _store=store;_case=testCase;_flow=flow;
        var page=new DockPanel{Margin=new Thickness(16,12,16,13)};
        Content=page;
        var header=UiKit.Vertical(UiKit.Heading(flow?"公共流程编辑":"测试程序编辑",19),
            UiKit.Note(flow?"公共流程采用单一连续步骤树。UI 阶段只验证操作，不生成真实资源引用。":
                "准备 / 测试 / 清理，步骤与流程分支可连续编辑。右侧属性不会弹出新对话框。",12));
        DockPanel.SetDock(header,Dock.Top);page.Children.Add(header);
        var top=UiKit.Actions(
            UiKit.Button("＋ 添加步骤",(_,_)=>Insert(false),true),
            UiKit.Button("添加子步骤",(_,_)=>Insert(true)),
            UiKit.Button("复制",(_,_)=>Duplicate()),
            UiKit.Button("删除",(_,_)=>Delete()),
            UiKit.Button("↑",(_,_)=>Move(-1)),
            UiKit.Button("↓",(_,_)=>Move(1)),
            UiKit.Button("F9 断点",(_,_)=>ToggleBreakpoint()),
            UiKit.Button("撤销",(_,_)=>History(true)),
            UiKit.Button("重做",(_,_)=>History(false)));
        DockPanel.SetDock(top,Dock.Top);page.Children.Add(top);
        var palette=new StackPanel{Margin=new Thickness(12)};
        palette.Children.Add(UiKit.Heading("操作与插入位置",14));
        palette.Children.Add(UiKit.Note("先选择阶段或已有步骤，再选择操作类型。普通添加是同级后插；子步骤仅在容器内添加。"));
        _phase.ItemsSource=flow?new[]{"测试"}:new[]{"准备","测试","清理"};
        _phase.SelectedIndex=flow?0:1;
        palette.Children.Add(UiKit.Field("阶段",_phase));
        _types.ItemsSource=_allTypes.Select(x=>x.Label).ToList();
        _types.SelectedIndex=0;
        palette.Children.Add(UiKit.Field("步骤类型",_types));
        palette.Children.Add(UiKit.Button("插入到当前位置",(_,_)=>Insert(false),true));
        palette.Children.Add(UiKit.Note("编辑是 UI 事务，可撤销与重做；未接入正式 TEST DSL，因此不执行、也不声称可直接下发。",12,
            new Thickness(0,20,0,0)));
        var structure=new DockPanel();
        var rootTitle=UiKit.Heading("步骤结构 · "+_case.Name,14,new Thickness(12,12,6,8));
        DockPanel.SetDock(rootTitle,Dock.Top);structure.Children.Add(rootTitle);
        structure.Children.Add(_tree);
        _tree.SelectedItemChanged+=(_,_)=>{
            if(_refreshing)return;
            if(!CommitInspector())return;
            _selected=(_tree.SelectedItem as TreeViewItem)?.Tag as UiStep;
            _selectedList=FindOwnerList(_selected);
            ShowInspector();
        };
        var rightTitle=UiKit.Heading("当前步骤 · 属性与参数",14);
        _inspector.Children.Add(rightTitle);
        _inspector.Children.Add(_message);
        _inspector.Children.Add(_stepDetails);
        var inspectorScroll=new ScrollViewer{Content=_inspector,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var grid=UiKit.ThreeColumns(UiKit.Side("操作面板",UiKit.Scroll(palette)),
            UiKit.Side("测试步骤",structure),UiKit.Side("步骤详情",inspectorScroll),225,315);
        page.Children.Add(grid);
        PreviewKeyDown+=(_,e)=>
        {
            if(Keyboard.Modifiers!=ModifierKeys.Control)return;
            if(e.Key==Key.Z){History(true);e.Handled=true;}
            if(e.Key==Key.Y){History(false);e.Handled=true;}
        };
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.F9){ToggleBreakpoint();e.Handled=true;}
        };
        Refresh();
    }
    private ObservableCollection<UiStep> PhaseList(string phase)=>phase switch
    {
        "准备"=>_case.Prepare,
        "清理"=>_case.Cleanup,
        _=>_case.Test
    };
    private ObservableCollection<UiStep>? FindOwnerList(UiStep? node)
    {
        if(node is null)return null;
        foreach(var list in new[]{_case.Prepare,_case.Test,_case.Cleanup})
        {
            if(list.Contains(node))return list;
            var nested=Nested(list,node);
            if(nested is not null)return nested;
        }
        return null;
    }
    private static ObservableCollection<UiStep>? Nested(IEnumerable<UiStep> source,UiStep node)
    {
        foreach(var s in source)
        {
            if(s.Children.Contains(node))return s.Children;
            var nested=Nested(s.Children,node);
            if(nested is not null)return nested;
        }
        return null;
    }
    private string Snapshot()=>JsonSerializer.Serialize(_case);
    private void Checkpoint()
    {
        _undo.Push(Snapshot());
        _redo.Clear();
    }
    private void Restore(string json)
    {
        var x=JsonSerializer.Deserialize<UiTestCase>(json);
        if(x is null)return;
        _case.Name=x.Name;
        _case.Prepare.Clear();foreach(var n in x.Prepare)_case.Prepare.Add(n);
        _case.Test.Clear();foreach(var n in x.Test)_case.Test.Add(n);
        _case.Cleanup.Clear();foreach(var n in x.Cleanup)_case.Cleanup.Add(n);
    }
    private void History(bool undo)
    {
        if(!CommitInspector())return;
        var from=undo?_undo:_redo;var to=undo?_redo:_undo;
        if(from.Count==0){_message.Text=undo?"没有可撤销的操作。":"没有可重做的操作。";return;}
        to.Push(Snapshot());Restore(from.Pop());
        _selected=null;_selectedList=null;Refresh();
        _store.Changed(undo?"撤销编辑":"重做编辑");
    }
    private void Insert(bool child)
    {
        if(!CommitInspector())return;
        var type=_allTypes[Math.Clamp(_types.SelectedIndex,0,_allTypes.Count-1)].Type;
        var phase=_selected?.Phase??_phase.SelectedItem as string??"测试";
        ObservableCollection<UiStep> target;
        int index;
        if(child)
        {
            if(_selected is null){_message.Text="先选择有子步骤的流程节点。";return;}
            if(_selected.Type is not ("If" or "Switch" or "When" or "Loop" or "TryCatch" or "Subroutine"))
            { _message.Text="当前步骤不是流程容器。选择 IF、SWITCH、循环等容器后添加。";return; }
            target=_selected.Children;index=target.Count;
        }
        else if(_selected is not null && _selectedList is not null)
        {
            target=_selectedList;index=target.IndexOf(_selected)+1;
        }
        else {target=PhaseList(phase);index=target.Count;}
        Checkpoint();
        var step=_store.NewStep(type,phase);
        if(child&&_selected is not null)step.Branch=_selected.Type switch
        {
            "If"=>"THEN","Switch"=>"CASE","TryCatch"=>"TRY",_=>"BODY"
        };
        target.Insert(index,step);
        _store.Changed("添加 "+step.Name);
        Refresh(step.Id);
    }
    private void Duplicate()
    {
        if(_selected is null||_selectedList is null||!CommitInspector())return;
        Checkpoint();
        var copy=UiStore.CloneStep(_selected);
        _selectedList.Insert(_selectedList.IndexOf(_selected)+1,copy);
        _store.Changed("复制步骤");Refresh(copy.Id);
    }
    private void Delete()
    {
        if(_selected is null||_selectedList is null)return;
        Checkpoint();_selectedList.Remove(_selected);
        _selected=null;_selectedList=null;_store.Changed("删除步骤");Refresh();
    }
    private void ToggleBreakpoint()
    {
        if(_selected is null||!CommitInspector())return;
        Checkpoint();
        _selected.Breakpoint=!_selected.Breakpoint;
        _store.Changed(_selected.Breakpoint?"设置步骤断点":"清除步骤断点");
        Refresh(_selected.Id);
    }
    private void Move(int direction)
    {
        if(_selected is null||_selectedList is null||!CommitInspector())return;
        var index=_selectedList.IndexOf(_selected);var to=index+direction;
        if(to<0||to>=_selectedList.Count)return;
        Checkpoint();_selectedList.Move(index,to);
        _store.Changed("调整步骤顺序");Refresh(_selected.Id);
    }
    private void Refresh(string? selectId=null)
    {
        _refreshing=true;
        try
        {
            _tree.Items.Clear();
            foreach(var phase in _flow?new[]{"测试"}:new[]{"准备","测试","清理"})
            {
                var root=new TreeViewItem{Header=phase+" 阶段",IsExpanded=true,FontWeight=FontWeights.SemiBold};
                root.Tag=null;_tree.Items.Add(root);
                AddNodes(root,PhaseList(phase),selectId);
            }
        }
        finally{_refreshing=false;}
        ShowInspector();
    }
    private void AddNodes(ItemsControl parent,IEnumerable<UiStep> nodes,string? selectId)
    {
        foreach(var s in nodes)
        {
            var item=new TreeViewItem{Header=(s.Breakpoint?"● ":"")+(s.Enabled?"":"[禁用] ")+
                (s.Branch.Length==0?"":s.Branch+" / ")+s.Name+" · "+s.Type,
                Tag=s,IsExpanded=true,FontWeight=FontWeights.Normal};
            parent.Items.Add(item);
            if(s.Id==selectId){_selected=s;_selectedList=FindOwnerList(s);item.IsSelected=true;}
            AddNodes(item,s.Children,selectId);
        }
    }
    private void ShowInspector()
    {
        _stepDetails.Children.Clear();_editors.Clear();_stepName=null;
        if(_selected is null)
        {
            _message.Text="选择某个步骤，或从左侧直接添加操作。";
            _stepDetails.Children.Add(UiKit.Note("当前无步骤选中。选择准备、测试或清理阶段后添加步骤。"));
            return;
        }
        _message.Text="所选类型："+_selected.Type+" · 关联实体硬件的操作不会在 UI 阶段触发。";
        _stepName=UiKit.Text(_selected.Name);
        _stepDetails.Children.Add(UiKit.Field("步骤名称",_stepName));
        var enabled=UiKit.BoundCheck(_selected,nameof(UiStep.Enabled),"包含在测试程序中");
        enabled.Click+=(_,_)=>_store.Changed("调整步骤启用状态");
        _stepDetails.Children.Add(enabled);
        if(_selected.Children.Count>0)
            _stepDetails.Children.Add(UiKit.Note("已有 "+_selected.Children.Count+" 个子步骤；选中子步骤可直接编辑。"));
        if (_selected.Branch.Length > 0)
        {
            var branch = new ComboBox
            {
                ItemsSource = _selected.Type is "If" ? new[] { "THEN", "ELSE" } :
                    new[] { "THEN", "ELSE", "CASE", "DEFAULT", "TRY", "CATCH", "FINALLY", "BODY" },
                SelectedItem = _selected.Branch
            };
            _stepDetails.Children.Add(UiKit.Field("所属流程分支", branch));
            branch.SelectionChanged += (_, _) =>
            {
                if (branch.SelectedItem is not string selected || _selected is null) return;
                Checkpoint();
                _selected.Branch = selected;
                _store.Changed("修改流程分支");
            };
        }
        foreach(var parameter in _selected.Parameters)
        {
            var field=UiKit.Text(parameter.Value,parameter.Key);
            _editors[parameter.Key]=field;
            _stepDetails.Children.Add(UiKit.Field(parameter.Label,field));
            if(parameter.Key is "target" or "signal" or "message" or "reference" or "flow")
            {
                var candidates = parameter.Key switch
                {
                    "flow" => new[] { "公共流程" },
                    "reference" => _store.WaveSignals.Select(x => x.Name).ToArray(),
                    "signal" => _store.DbcMessages.SelectMany(m => m.Signals.Select(s => m.Name + "." + s.Name)).ToArray(),
                    "message" => _store.DbcMessages.Select(m => m.Name).ToArray(),
                    _ => _store.DbcMessages.SelectMany(m => m.Signals.Select(s => m.Name + "." + s.Name))
                        .Concat(_store.KeyActions.Select(x => x.Name)).ToArray()
                };
                if (candidates.Length > 0)
                {
                    var picker = new ComboBox { ItemsSource = candidates, ToolTip = "从当前 UI 会话资源选择" };
                    picker.SelectionChanged += (_, _) =>
                    {
                        if (picker.SelectedItem is string value) field.Text = value;
                    };
                    _stepDetails.Children.Add(UiKit.Field("从资源中选择（可选）", picker));
                }
                else _stepDetails.Children.Add(UiKit.Note("当前没有可用对象。请先在对应专业工作区创建 UI 资源。", 11));
            }
        }
        _stepDetails.Children.Add(UiKit.Actions(
            UiKit.Button("应用参数",(_,_)=>{if(CommitInspector())Refresh(_selected?.Id);},true),
            UiKit.Button("撤销本次字段输入",(_,_)=>ShowInspector())));
    }
    private bool CommitInspector()
    {
        if(_selected is null||_stepName is null)return true;
        if(string.IsNullOrWhiteSpace(_stepName.Text))
        { _message.Text="步骤名称不能为空。";return false; }
        foreach (var p in _selected.Parameters)
        {
            if (_editors.TryGetValue(p.Key, out var editor) &&
                p.Key is "milliseconds" or "timeoutMs" or "periodMs" or "count" &&
                (!int.TryParse(editor.Text, out var number) || number < 0))
            {
                _message.Text = p.Label + " 必须是非负整数。";
                return false;
            }
        }
        var changes=_selected.Name!=_stepName.Text||
            _selected.Parameters.Any(p=>_editors.TryGetValue(p.Key,out var editor)&&p.Value!=editor.Text);
        if(!changes)return true;
        Checkpoint();_selected.Name=_stepName.Text.Trim();
        foreach(var p in _selected.Parameters)
            if(_editors.TryGetValue(p.Key,out var editor))p.Value=editor.Text;
        _store.Changed("应用步骤参数");
        return true;
    }
}
