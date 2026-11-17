using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoTest.UiPreview;

/// <summary>
/// Deliberately UI-only. All data lives in memory; this process does not use
/// the AutoTest solution's TEST, Project, DBC, USB, CAN, UDS or Device services.
/// </summary>
public partial class MainWindow : Window
{
    public ObservableCollection<PreviewRow> Rows { get; } = new();
    private readonly List<DemoPhase> _phases = new();
    private readonly Stack<DemoSnapshot> _undo = new();
    private readonly Stack<DemoSnapshot> _redo = new();
    private readonly IReadOnlyList<PaletteKind> _palette = MakePalette();
    private PreviewRow? _targetRow;
    private PreviewRow? _selectedRow;
    private DemoStep? _selectedStep;
    private bool _rebuilding;
    private bool _updatingInspector;
    private bool _syncingChecks;
    private bool _bottomVisible = true;
    private bool _inspectorVisible = true;
    private bool _manualInspectorLayout;
    private bool _manualBottomLayout;
    private static readonly Brush InkMuted = PreviewRow.Solid("#607891");

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        StepNameField.GotKeyboardFocus += (_, _) =>
        {
            if (_selectedStep is not null && !_updatingInspector) RememberUndo();
        };
        EnabledBox.DropDownOpened += (_, _) => RememberUndo();
        BreakpointBox.DropDownOpened += (_, _) => RememberUndo();
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(MonitorWorkAreaHook);
        };
        SizeChanged += (_, _) => AdaptDocksToViewport();
        SeedHtmlSampleData();
        RebuildRows();
        SelectStep(_phases[0].Steps[0]);
        SetMessage("就绪");
    }

    private static IReadOnlyList<PaletteKind> MakePalette() =>
    [
        new("can","发送 CAN 报文","CAN 与数据","⇄"),
        new("canwait","等待 CAN 报文","CAN 与数据","◷"),
        new("read","读取信号","CAN 与数据","↧"),
        new("expect","检查条件","CAN 与数据","✓"),
        new("input","按键动作","工位操作","◈"),
        new("ign","IGN 控制","工位操作","◈"),
        new("sol","夹具动作","工位操作","◈"),
        new("uds","UDS 服务","UDS 服务","◇"),
        new("wave","采集信号","波形分析","〰"),
        new("wavecheck","对比波形参考","波形分析","≋"),
        new("wait","固定等待","流程与计算","◷"),
        new("var","设置变量","流程与计算","𝑥"),
        new("if","IF / ELSE","流程与计算","⑂"),
        new("switch","SWITCH / CASE","流程与计算","⑂"),
        new("when","WHEN 等待","流程与计算","◷"),
        new("loop","LOOP 循环","流程与计算","⟳"),
        new("try","TRY / CATCH","流程与计算","⚑"),
        new("sub","调用子程序","流程与计算","↳"),
        new("comment","备注","流程与计算","·"),
    ];

    private static DemoStep Step(string kind,string name,params (string Key,string Value)[] args)
    {
        var result = new DemoStep { Kind = kind,Name = name };
        foreach (var (key,value) in args) result.Parameters[key] = value;
        if (kind == "if")
        {
            result.Branches.Add(new DemoBranch("THEN"));
            result.Branches.Add(new DemoBranch("ELSE"));
        }
        if (kind == "switch")
        {
            result.Branches.Add(new DemoBranch("CASE"));
            result.Branches.Add(new DemoBranch("DEFAULT"));
        }
        if (kind == "loop") result.Branches.Add(new DemoBranch("BODY"));
        if (kind == "try")
        {
            result.Branches.Add(new DemoBranch("TRY"));
            result.Branches.Add(new DemoBranch("CATCH"));
            result.Branches.Add(new DemoBranch("FINALLY"));
        }
        return result;
    }

    private void SeedHtmlSampleData()
    {
        var prepare = new DemoPhase("prepare","准备");
        prepare.Steps.Add(Step("ign","设置点火输入",("state","ON")));
        prepare.Steps.Add(Step("can","发送运行条件",("message","未选择报文"),("signalCount","2"),("period","100")));
        prepare.Steps.Add(Step("wait","等待上电稳定",("ms","200")));
        var test = new DemoPhase("test","测试");
        test.Steps.Add(Step("read","读取当前状态",("signal","Signal_A"),("variable","$actual")));
        test.Steps.Add(Step("expect","检查状态值",("expression","$actual >= 0")));
        var branch = Step("if","根据读取值分支",("condition","$actual > 2"));
        branch.Branches[0].Steps.Add(Step("wave","采集信号",("signal","Signal_A"),("duration","1200")));
        branch.Branches[0].Steps.Add(Step("wavecheck","对比参考记录",("reference","参考记录01")));
        branch.Branches[1].Steps.Add(Step("uds","读取诊断信息",("job","读取标识信息")));
        test.Steps.Add(branch);
        test.Steps.Add(Step("canwait","等待目标帧",("message","未选择报文"),("timeout","2000")));
        var cleanup = new DemoPhase("cleanup","清理");
        cleanup.Steps.Add(Step("ign","关闭点火输入",("state","OFF")));
        _phases.AddRange([prepare,test,cleanup]);
    }

    private void RebuildRows()
    {
        var selectedId = _selectedStep?.Id;
        _rebuilding = true;
        try
        {
            Rows.Clear();
            foreach (var phase in _phases)
            {
                Rows.Add(new PreviewRow { Phase = phase });
                if (!phase.Collapsed) Append(phase,phase.Steps,null,null,0);
            }
            foreach (var row in Rows)
                row.IsMatch = row.IsStep && !string.IsNullOrWhiteSpace(SearchText.Text) &&
                    (row.Name+" "+row.Summary+" "+row.Resource)
                    .Contains(SearchText.Text.Trim(),StringComparison.CurrentCultureIgnoreCase);
            _selectedRow = Rows.FirstOrDefault(row => row.Step?.Id == selectedId);
            StepRowsList.SelectedItem = _selectedRow;
            StepCountLabel.Text = AllSteps().Count().ToString() + " 步";
        }
        finally { _rebuilding = false; }
        ShowInspector();
    }

    private void Append(DemoPhase phase,ObservableCollection<DemoStep> items,
        DemoBranch? parentBranch,DemoStep? owner,int depth)
    {
        for (int i=0; i<items.Count;i++)
        {
            var step=items[i];
            Rows.Add(new PreviewRow { Phase=phase,Step=step,Branch=null,Owner=owner,Depth=depth,Index=i });
            foreach (var branch in step.Branches)
            {
                Rows.Add(new PreviewRow { Phase=phase,Branch=branch,Owner=step,Depth=depth+1 });
                Append(phase,branch.Steps,branch,step,depth+1);
            }
        }
    }

    private IEnumerable<DemoStep> AllSteps()
    {
        foreach (var phase in _phases)
            foreach (var step in Traverse(phase.Steps)) yield return step;
    }
    private static IEnumerable<DemoStep> Traverse(IEnumerable<DemoStep> list)
    {
        foreach (var step in list)
        {
            yield return step;
            foreach (var branch in step.Branches)
                foreach (var child in Traverse(branch.Steps)) yield return child;
        }
    }

    private void SelectStep(DemoStep? step)
    {
        _selectedStep=step;
        var row=Rows.FirstOrDefault(x=>x.Step?.Id==step?.Id && x.IsStep);
        _selectedRow=row;
        StepRowsList.SelectedItem=row;
        if (row is not null) StepRowsList.ScrollIntoView(row);
        ShowInspector();
    }
    private void StepRowsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding) return;
        _selectedRow=StepRowsList.SelectedItem as PreviewRow;
        _selectedStep=_selectedRow?.Step;
        ShowInspector();
    }
    private void StepRowsList_DoubleClick(object sender,MouseButtonEventArgs e)
    {
        if (_selectedStep is not null)
        {
            StepNameField.Focus();
            StepNameField.SelectAll();
        }
    }
    private void TogglePhase_Click(object sender,RoutedEventArgs e)
    {
        if (sender is not Button {Tag:PreviewRow row}) return;
        row.Phase.Collapsed=!row.Phase.Collapsed;
        RebuildRows();
        e.Handled=true;
    }

    private void ShowInspector()
    {
        if (InspectorStepPanel is null) return;
        _updatingInspector=true;
        try
        {
            InspectorStepPanel.Visibility=_selectedStep is null ? Visibility.Collapsed : Visibility.Visible;
            InspectorEmptyPanel.Visibility=_selectedStep is null ? Visibility.Visible : Visibility.Collapsed;
            if (_selectedStep is null) return;
            var match=_palette.FirstOrDefault(x=>x.Code==_selectedStep.Kind);
            SelectedTypeLabel.Text=match?.Label ?? "步骤";
            SelectedTypeIcon.Text=match?.Icon ?? "·";
            SelectedStepId.Text=_selectedStep.Id.ToString("N")[^5..].ToUpperInvariant();
            SelectedTypeBadge.Background=_selectedRow?.CategoryBackground ?? PreviewRow.Solid("#EEF2F7");
            SelectedTypeIcon.Foreground=_selectedRow?.CategoryForeground ?? InkMuted;
            StepNameField.Text=_selectedStep.Name;
            EnabledBox.SelectedIndex=_selectedStep.Enabled ? 0:1;
            BreakpointBox.SelectedIndex=_selectedStep.Breakpoint ? 1:0;
            RenderParameterFields();
        }
        finally { _updatingInspector=false; }
    }

    private void RenderParameterFields()
    {
        if (InspectorParameterFields is null || _selectedStep is null) return;
        InspectorParameterFields.Children.Clear();
        var kind=_selectedStep.Kind;
        if(kind=="ign") AddChoice("请求输出","state",["ON","OFF"]);
        else if(kind=="sol") AddChoice("夹具请求","state",["CLAMP","OPEN"]);
        else if(kind=="can")
        {
            AddChoice("编辑方式","mode",["DBC 报文及信号","原始 CAN 帧"],true);
            if (_selectedStep.Param("mode","DBC 报文及信号")=="原始 CAN 帧")
            {
                AddField("CAN ID","id","0x100");
                AddField("数据（Hex）","data","00 00 00 00 00 00 00 00");
            }
            else
            {
                AddChoice("DBC 报文","message",
                    ["未选择报文","Debug_702 · 0x702","EPB_VCU_Status · 0xCFE2750"]);
                AddField("KeyState（物理值）","signal1","0");
                AddField("key.Release_out（物理值）","signal2","1");
            }
            AddField("发送周期（ms；0 为单次）","period","0");
        }
        else if(kind=="canwait")
        {
            AddChoice("目标 CAN 报文","message",["未选择报文","Debug_702 · 0x702","EPB_VCU_Status · 0xCFE2750"]);
            AddField("超时时间（ms）","timeout","1000");
        }
        else if(kind=="input") AddChoice("按键动作","button",["未选择按键动作","驻车按键","释放按键"]);
        else if(kind=="read") { AddField("DBC 信号名称","signal","Signal_A"); AddField("保存到变量","variable","$actual");}
        else if(kind=="expect") AddField("判定表达式","expression","$actual >= 0");
        else if(kind=="if"||kind=="when") {AddField("布尔条件","condition","$actual > 2"); if(kind=="when")AddField("等待超时（ms）","timeout","1000");}
        else if(kind=="loop") AddField("重复次数","count","3");
        else if(kind=="wait") AddField("等待时长（ms）","ms","200");
        else if(kind=="switch") AddField("选择表达式","expression","$state");
        else if(kind=="wave") {AddField("采集信号","signal","Signal_A");AddField("窗口时长（ms）","duration","1200");}
        else if(kind=="wavecheck") AddField("参考波形","reference","参考记录01");
        else if(kind=="uds") AddChoice("UDS 服务配置","job",["未选择","读取标识信息","读取故障码"]);
        else if(kind=="var") { AddField("变量名称","variable","$value");AddField("表达式","expression","0");}
        else if(kind=="try") AddChoice("异常处理方式","onError",["停止并记录","继续并记录","转入异常分支"]);
        else if(kind=="sub") AddField("子程序引用","target","");
        else AddField("备注或表达式","text","");
    }

    private void AddField(string label,string key,string fallback)
    {
        if (_selectedStep is null) return;
        var text=new TextBlock{Text=label,Style=(Style)FindResource("AtPreviewDynamicLabel")};
        InspectorParameterFields.Children.Add(text);
        var input=new TextBox{
            Text=_selectedStep.Param(key,fallback),Style=(Style)FindResource("AtPreviewDynamicInput")};
        var editingStep=_selectedStep;
        input.GotKeyboardFocus+=(_,_)=>RememberUndo();
        input.TextChanged+=(_,_)=>{
            if (_updatingInspector)return;
            editingStep.SetParam(key,input.Text);
            RefreshSelectedRowSummary();
        };
        InspectorParameterFields.Children.Add(input);
    }

    private void AddChoice(string label,string key,string[] options,bool reformat=false)
    {
        if (_selectedStep is null)return;
        InspectorParameterFields.Children.Add(new TextBlock{
            Text=label,Style=(Style)FindResource("AtPreviewDynamicLabel")});
        var box=new ComboBox{Style=(Style)FindResource("AtPreviewDynamicChoice"),ItemsSource=options};
        var editingStep=_selectedStep;
        box.DropDownOpened+=(_,_)=>RememberUndo();
        box.SelectedItem=editingStep.Param(key,options[0]);
        if (box.SelectedItem is null) box.SelectedIndex=0;
        box.SelectionChanged+=(_,_)=>{
            if (_updatingInspector || box.SelectedItem is not string choice)return;
            editingStep.SetParam(key,choice);
            RefreshSelectedRowSummary();
            if (reformat && ReferenceEquals(_selectedStep,editingStep))
                RenderParameterFields();
        };
        InspectorParameterFields.Children.Add(box);
    }

    private void RefreshSelectedRowSummary()
    {
        _selectedRow?.Notify();
    }

    private void StepNameField_TextChanged(object sender,TextChangedEventArgs e)
    {
        if (_updatingInspector || _selectedStep is null)return;
        _selectedStep.Name=StepNameField.Text;
        _selectedRow?.Notify();
        SetMessage("已修改步骤名称");
    }
    private void EnabledBox_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if (_updatingInspector || _selectedStep is null)return;
        _selectedStep.Enabled=EnabledBox.SelectedIndex!=1;
        _selectedRow?.Notify();
    }
    private void BreakpointBox_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if (_updatingInspector || _selectedStep is null)return;
        _selectedStep.Breakpoint=BreakpointBox.SelectedIndex==1;
        _selectedRow?.Notify();
    }

    private void AddToolbar_Click(object sender,RoutedEventArgs e)
    {
        _targetRow=_selectedRow ?? Rows.FirstOrDefault(x=>x.IsPhase && x.Phase.Key=="test");
        OpenPalette();
    }
    private void AddAtRow_Click(object sender,RoutedEventArgs e)
    {
        _targetRow=(sender as Button)?.Tag as PreviewRow;
        OpenPalette();
        e.Handled=true;
    }
    private void InsertAfter_Click(object sender,RoutedEventArgs e)=>AddToolbar_Click(sender,e);

    private void OpenPalette()
    {
        PaletteSearchText.Text="";
        FillPalette("");
        PaletteOverlay.Visibility=Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(()=>{
            PaletteSearchText.Focus();
            PaletteSearchText.SelectAll();
        }),DispatcherPriority.Input);
    }
    private void ClosePalette_Click(object sender,RoutedEventArgs e) =>
        PaletteOverlay.Visibility=Visibility.Collapsed;
    private void PaletteSearchText_Changed(object sender,TextChangedEventArgs e)
    {
        if (PaletteGroupsPanel is not null)FillPalette(PaletteSearchText.Text.Trim());
    }
    private void PaletteSearchText_KeyDown(object sender,KeyEventArgs e)
    {
        if (e.Key==Key.Escape) {PaletteOverlay.Visibility=Visibility.Collapsed;e.Handled=true;}
        if (e.Key==Key.Enter)
        {
            var match=_palette.FirstOrDefault(x=>x.Label.Contains(PaletteSearchText.Text,StringComparison.CurrentCultureIgnoreCase));
            if(match is not null){CreatePreviewStep(match);e.Handled=true;}
        }
    }
    private void FillPalette(string search)
    {
        PaletteGroupsPanel.Children.Clear();
        foreach(var group in _palette.Select(x=>x.Group).Distinct())
        {
            var kinds=_palette.Where(x=>x.Group==group && (search.Length==0 ||
                (x.Label+" "+x.Group).Contains(search,StringComparison.CurrentCultureIgnoreCase))).ToList();
            if(kinds.Count==0)continue;
            PaletteGroupsPanel.Children.Add(new TextBlock{Text=group,
                Foreground=PreviewRow.Solid("#547089"),FontWeight=FontWeights.SemiBold,
                FontSize=11,Margin=new Thickness(0,10,0,6)});
            var wrap=new WrapPanel();
            foreach(var item in kinds)
            {
                var button=new Button{Content=item.Icon+"  "+item.Label,Tag=item,
                    Width=226,MinHeight=30,Margin=new Thickness(0,0,8,6),
                    Padding=new Thickness(8,5,8,5),FontSize=11,
                    HorizontalContentAlignment=HorizontalAlignment.Left,
                    Foreground=PreviewRow.Solid("#28486D"),
                    Background=Brushes.White,BorderBrush=PreviewRow.Solid("#D8E3EF"),
                    BorderThickness=new Thickness(1),Cursor=Cursors.Hand};
                button.Click+=(_,_)=>CreatePreviewStep(item);
                wrap.Children.Add(button);
            }
            PaletteGroupsPanel.Children.Add(wrap);
        }
    }

    private void CreatePreviewStep(PaletteKind option)
    {
        var target=_targetRow;
        var added=Step(option.Code,option.Label);
        switch(option.Code)
        {
            case "ign":added.SetParam("state","ON");break;
            case "wait":added.SetParam("ms","100");break;
            case "can":added.SetParam("message","未选择报文");added.SetParam("period","0");break;
            case "read":added.SetParam("signal","Signal_A");break;
        }
        var targetPhase=target?.Phase ?? _phases[1];
        ObservableCollection<DemoStep> collection;
        var insertIndex=-1;
        if(target?.IsBranch==true && target.Branch is not null)collection=target.Branch.Steps;
        else if(target?.IsStep==true && target.Step is not null)
        {
            var found=Locate(target.Step);
            collection=found?.Items ?? targetPhase.Steps;
            insertIndex=(found?.Index ?? collection.Count-1)+1;
        }
        else collection=targetPhase.Steps;
        RememberUndo();
        if (insertIndex<0 || insertIndex>collection.Count)collection.Add(added);
        else collection.Insert(insertIndex,added);
        targetPhase.Collapsed=false;
        _selectedStep=added;
        PaletteOverlay.Visibility=Visibility.Collapsed;
        RebuildRows();
        SelectStep(added);
        Dispatcher.BeginInvoke(new Action(()=>{StepNameField.Focus();StepNameField.SelectAll();}),DispatcherPriority.Input);
        SetMessage("已添加步骤：“"+added.Name+"”");
    }

    private (ObservableCollection<DemoStep> Items,int Index)? Locate(DemoStep selected)
    {
        foreach (var phase in _phases)
        {
            var found=LocateIn(phase.Steps,selected);
            if(found is not null)return found;
        }
        return null;
    }
    private static (ObservableCollection<DemoStep> Items,int Index)? LocateIn(ObservableCollection<DemoStep> list,DemoStep selected)
    {
        for(var i=0;i<list.Count;i++)
        {
            if(ReferenceEquals(list[i],selected))return(list,i);
            foreach (var branch in list[i].Branches)
            {
                var found=LocateIn(branch.Steps,selected);
                if(found is not null)return found;
            }
        }
        return null;
    }

    private void Duplicate_Click(object sender,RoutedEventArgs e)
    {
        if(_selectedStep is null)return;
        var located=Locate(_selectedStep);
        if(located is null)return;
        RememberUndo();
        var clone=_selectedStep.Copy();
        located.Value.Items.Insert(located.Value.Index+1,clone);
        _selectedStep=clone;RebuildRows();SelectStep(clone);
        SetMessage("已复制步骤");
    }
    private void Delete_Click(object sender,RoutedEventArgs e)
    {
        if(_selectedStep is null)return;
        var located=Locate(_selectedStep);
        if(located is null)return;
        RememberUndo();
        located.Value.Items.RemoveAt(located.Value.Index);
        _selectedStep=located.Value.Items.ElementAtOrDefault(Math.Min(located.Value.Index,located.Value.Items.Count-1));
        RebuildRows();
        SetMessage("已删除步骤；Ctrl+Z 可撤销");
    }
    private void MoveUp_Click(object sender,RoutedEventArgs e)=>MoveSelected(-1);
    private void MoveDown_Click(object sender,RoutedEventArgs e)=>MoveSelected(+1);
    private void MoveSelected(int delta)
    {
        if(_selectedStep is null)return;
        var located=Locate(_selectedStep);
        if(located is null)return;
        var next=located.Value.Index+delta;
        if(next<0||next>=located.Value.Items.Count)return;
        RememberUndo();
        located.Value.Items.Move(located.Value.Index,next);
        RebuildRows();
    }

    private sealed record DemoSnapshot(List<DemoPhase> Phases,Guid? Selected);
    private DemoSnapshot Capture()
    {
        var phases=new List<DemoPhase>();
        foreach(var source in _phases)
        {
            var phase=new DemoPhase(source.Key,source.Title){Collapsed=source.Collapsed};
            foreach(var child in source.Steps)phase.Steps.Add(ClonePreservingId(child));
            phases.Add(phase);
        }
        return new DemoSnapshot(phases,_selectedStep?.Id);
    }
    private static DemoStep ClonePreservingId(DemoStep src)
    {
        var copy=new DemoStep{Id=src.Id,Kind=src.Kind,Name=src.Name,
            Enabled=src.Enabled,Breakpoint=src.Breakpoint};
        foreach(var pair in src.Parameters)copy.Parameters[pair.Key]=pair.Value;
        foreach(var branch in src.Branches)
        {
            var fork=new DemoBranch(branch.Name);
            foreach(var item in branch.Steps)fork.Steps.Add(ClonePreservingId(item));
            copy.Branches.Add(fork);
        }
        return copy;
    }
    private void Restore(DemoSnapshot snapshot)
    {
        _phases.Clear();
        _phases.AddRange(snapshot.Phases);
        _selectedStep=AllSteps().FirstOrDefault(x=>x.Id==snapshot.Selected);
        RebuildRows();
    }
    private void RememberUndo(){_undo.Push(Capture());_redo.Clear();}
    private void Undo_Click(object sender,RoutedEventArgs e)
    {
        if(_undo.Count==0){SetMessage("当前没有可撤销的 UI 操作。");return;}
        _redo.Push(Capture());Restore(_undo.Pop());SetMessage("已撤销");
    }
    private void Redo_Click(object sender,RoutedEventArgs e)
    {
        if(_redo.Count==0){SetMessage("当前没有可重做的 UI 操作。");return;}
        _undo.Push(Capture());Restore(_redo.Pop());SetMessage("已重做");
    }

    private void SearchText_Changed(object sender,TextChangedEventArgs e)
    {
        if (StepRowsList is not null)RebuildRows();
    }
    private void SearchText_KeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Enter)return;
        e.Handled=true;
        var query=SearchText.Text.Trim();
        if(query.Length==0)return;
        var found=AllSteps().Where(x=>(x.Name+" "+x.Summary+" "+x.Resource)
            .Contains(query,StringComparison.CurrentCultureIgnoreCase)).ToList();
        if(found.Count==0){SetMessage("没有匹配的步骤");return;}
        var position=found.FindIndex(x=>x.Id==_selectedStep?.Id);
        var direction=(Keyboard.Modifiers & ModifierKeys.Shift)!=0?-1:1;
        var index=position<0 ? (direction==1?0:found.Count-1):
            (position+direction+found.Count)%found.Count;
        var step=found[index];
        var phase=_phases.First(p=>Traverse(p.Steps).Any(item=>item.Id==step.Id));
        phase.Collapsed=false;
        RebuildRows();SelectStep(step);
        SetMessage("搜索匹配 "+found.Count+" 条，已定位第 "+(index+1)+" 条。");
    }

    private void RunSelection_Changed(object sender,RoutedEventArgs e)
    {
        if(_syncingChecks||ProjectCase1Check is null||CurrentRunCheck is null)return;
        _syncingChecks=true;
        try
        {
            CurrentRunCheck.IsChecked=ProjectCase1Check.IsChecked;
            UpdateTreeSelectionCount();
        }
        finally{_syncingChecks=false;}
    }
    private void CaseHeaderCheck_Changed(object sender,RoutedEventArgs e)
    {
        if(_syncingChecks||ProjectCase1Check is null||CurrentRunCheck is null)return;
        _syncingChecks=true;
        try
        {
            ProjectCase1Check.IsChecked=CurrentRunCheck.IsChecked;
            UpdateTreeSelectionCount();
        }
        finally{_syncingChecks=false;}
    }
    private void UpdateTreeSelectionCount()
    {
        if (TreeSelectionCount is null || ProjectCase1Check is null || ProjectCase2Check is null) return;
        var count=(ProjectCase1Check.IsChecked==true?1:0)+(ProjectCase2Check.IsChecked==true?1:0);
        TreeSelectionCount.Text=count+"/2";
    }

    private void OpenDemoCase_Click(object sender,RoutedEventArgs e)
    {
        SetMessage("已打开通信与状态检查");
    }
    private void RenameCase_Click(object sender,RoutedEventArgs e)
    {
        SetMessage("重命名功能尚未接入");
    }
    private void BottomTab_Click(object sender,RoutedEventArgs e)
    {
        if(sender is Button button)
            BottomEmpty.Text=button.Content+" · 暂无记录";
    }
    private void ApplyDockLayout()
    {
        if (InspectorColumn is null || BottomRow is null) return;
        InspectorColumn.Width=new GridLength(_inspectorVisible?302:0);
        InspectorDock.Visibility=_inspectorVisible?Visibility.Visible:Visibility.Collapsed;
        BottomRow.Height=new GridLength(_bottomVisible?165:0);
        BottomDock.Visibility=_bottomVisible?Visibility.Visible:Visibility.Collapsed;
    }

    private void AdaptDocksToViewport()
    {
        // Physical 1366×768 at 150% gives only ~911×512 DIPs.
        // Keep steps readable; do not shrink fonts or cover the taskbar.
        if(!_manualInspectorLayout) _inspectorVisible=ActualWidth>=1120;
        if(!_manualBottomLayout) _bottomVisible=ActualHeight>=640;
        ApplyDockLayout();
    }

    private void ToggleBottom_Click(object sender,RoutedEventArgs e)
    {
        _manualBottomLayout=true;
        _bottomVisible=!_bottomVisible;
        ApplyDockLayout();
        SetMessage(_bottomVisible?"已展开底部观察区。":"已折叠底部观察区。");
    }
    private void ToggleInspector_Click(object sender,RoutedEventArgs e)
    {
        _manualInspectorLayout=true;
        _inspectorVisible=!_inspectorVisible;
        ApplyDockLayout();
        SetMessage(_inspectorVisible?"已打开步骤属性。":"已折叠步骤属性；按 Ctrl+I 可恢复。");
    }

    private void PreviewOnly_Click(object sender,RoutedEventArgs e)
    {
        SetMessage("功能尚未接入");
    }
    private void SetMessage(string text)=>StatusText.Text=text;

    private void Window_PreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape && PaletteOverlay.Visibility==Visibility.Visible)
        {PaletteOverlay.Visibility=Visibility.Collapsed;e.Handled=true;return;}
        if(Keyboard.Modifiers==ModifierKeys.Control)
        {
            if(e.Key==Key.F){SearchText.Focus();SearchText.SelectAll();e.Handled=true;}
            if(e.Key==Key.Z && Keyboard.FocusedElement is not TextBox)
            {Undo_Click(sender,new RoutedEventArgs());e.Handled=true;}
            if(e.Key==Key.Y && Keyboard.FocusedElement is not TextBox)
            {Redo_Click(sender,new RoutedEventArgs());e.Handled=true;}
            if(e.Key==Key.I){ToggleInspector_Click(sender,new RoutedEventArgs());e.Handled=true;}
            if(e.Key==Key.S){SetMessage("保存功能尚未接入");e.Handled=true;}
        }
        if(e.Key==Key.F2 && _selectedStep is not null)
        {StepNameField.Focus();StepNameField.SelectAll();e.Handled=true;}
        if(e.Key==Key.Delete && _selectedStep is not null &&
            !StepNameField.IsKeyboardFocusWithin && !SearchText.IsKeyboardFocusWithin)
        {Delete_Click(sender,new RoutedEventArgs());e.Handled=true;}
    }

    private void Minimize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void MaxRestore_Click(object sender,RoutedEventArgs e)=>
        WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();

    private const int WmGetMinMaxInfo=0x0024;
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct NativeMinMaxInfo
    {
        public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Auto)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor,Work;
        public int Flags;
    }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd,int flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor,ref NativeMonitorInfo info);
    private static IntPtr MonitorWorkAreaHook(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(message!=WmGetMinMaxInfo)return IntPtr.Zero;
        var monitor=MonitorFromWindow(hwnd,2);
        if(monitor==IntPtr.Zero)return IntPtr.Zero;
        var info=new NativeMonitorInfo{Size=Marshal.SizeOf<NativeMonitorInfo>()};
        if(!GetMonitorInfo(monitor,ref info))return IntPtr.Zero;
        var desired=Marshal.PtrToStructure<NativeMinMaxInfo>(lp);
        desired.MaxPosition.X=info.Work.Left-info.Monitor.Left;
        desired.MaxPosition.Y=info.Work.Top-info.Monitor.Top;
        desired.MaxSize.X=info.Work.Right-info.Work.Left;
        desired.MaxSize.Y=info.Work.Bottom-info.Work.Top;
        Marshal.StructureToPtr(desired,lp,false);
        handled=true;
        return IntPtr.Zero;
    }
}
