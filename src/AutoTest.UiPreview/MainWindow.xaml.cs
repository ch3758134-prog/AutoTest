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
    private readonly PreviewEditorDocument _document = new();
    public ObservableCollection<PreviewRow> Rows => _document.Rows;
    public ICommand AddRowCommand { get; }
    public ICommand TogglePhaseCommand { get; }
    private List<DemoPhase> _phases => _document.Phases;
    private readonly PreviewEditHistory _history = new();
    private readonly PreviewPropertyEditor _propertyEditor = new();
    public ObservableCollection<PreviewPropertyField> PropertyFields => _propertyEditor.Fields;
    public PreviewInspectorState Inspector { get; } = new();
    private readonly IReadOnlyList<PaletteKind> _palette = PreviewSampleFactory.MakePalette();
    private PreviewRow? _targetRow;
    private PreviewRow? _selectedRow => _document.SelectedRow;
    private DemoStep? _selectedStep
    {
        get => _document.SelectedStep;
        set => _document.SelectStep(value);
    }
    private bool _rebuilding;
    private bool _updatingInspector;
    public PreviewCaseSelectionState Selection { get; } = new();
    private readonly PreviewWorkspaceState _workspace = new();
    private static readonly Brush InkMuted = PreviewRow.Solid("#607891");

    public MainWindow()
    {
        InitializeComponent();
        AddRowCommand = new PreviewRowCommand(row =>
        {
            _targetRow = row;
            OpenPalette();
        });
        TogglePhaseCommand = new PreviewRowCommand(row =>
        {
            row.Phase.Collapsed = !row.Phase.Collapsed;
            RebuildRows();
        });
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
        _phases.AddRange(PreviewSampleFactory.CreatePhases());
        RebuildRows();
        SelectStep(_phases[0].Steps[0]);
        SetMessage("就绪");
    }

    private void RebuildRows()
    {
        _rebuilding = true;
        try
        {
            _document.Rebuild(SearchText.Text);
            StepRowsList.SelectedItem = _selectedRow;
            StepCountLabel.Text = AllSteps().Count().ToString() + " 步";
        }
        finally { _rebuilding = false; }
        ShowInspector();
    }

    private IEnumerable<DemoStep> AllSteps() => _document.AllSteps();
    private static IEnumerable<DemoStep> Traverse(IEnumerable<DemoStep> steps) =>
        PreviewEditorDocument.Traverse(steps);

    private void SelectStep(DemoStep? step)
    {
        _document.SelectStep(step);
        StepRowsList.SelectedItem=_selectedRow;
        if (_selectedRow is not null) StepRowsList.ScrollIntoView(_selectedRow);
        ShowInspector();
    }
    private void StepRowsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding) return;
        _document.SelectRow(StepRowsList.SelectedItem as PreviewRow);
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

    private void ShowInspector()
    {
        if (InspectorStepPanel is null) return;
        _updatingInspector=true;
        try
        {
            InspectorStepPanel.Visibility=_selectedStep is null ? Visibility.Collapsed : Visibility.Visible;
            InspectorEmptyPanel.Visibility=_selectedStep is null ? Visibility.Visible : Visibility.Collapsed;
            if (_selectedStep is null)
            {
                Inspector.Bind(null, () => { });
                _propertyEditor.Bind(null, (_, _) => { });
                return;
            }
            var match=_palette.FirstOrDefault(x=>x.Code==_selectedStep.Kind);
            SelectedTypeLabel.Text=match?.Label ?? "步骤";
            SelectedTypeIcon.Text=match?.Icon ?? "·";
            SelectedStepId.Text=_selectedStep.Id.ToString("N")[^5..].ToUpperInvariant();
            SelectedTypeBadge.Background=_selectedRow?.CategoryBackground ?? PreviewRow.Solid("#EEF2F7");
            SelectedTypeIcon.Foreground=_selectedRow?.CategoryForeground ?? InkMuted;
            Inspector.Bind(_selectedStep, () => _selectedRow?.Notify());
            RenderParameterFields();
        }
        finally { _updatingInspector=false; }
    }

    private void RenderParameterFields() =>
        _propertyEditor.Bind(_selectedStep, (step, field) =>
        {
            if (!ReferenceEquals(_selectedStep, step)) return;
            RefreshSelectedRowSummary();
            if (field.HasError) SetMessage(field.Error);
            if (field.RebuildOnChange)
            {
                // Do not remove the active ComboBox while its SelectedItem binding is updating.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ReferenceEquals(_selectedStep, step))
                        _propertyEditor.Rebuild();
                }), DispatcherPriority.Background);
            }
        });

    private void InspectorParameterFields_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // One undo checkpoint per field-focus transition, not one per keystroke.
        if (e.NewFocus is FrameworkElement { DataContext: PreviewPropertyField } &&
            !_updatingInspector && _selectedStep is not null)
            RememberUndo();
    }

    private void RefreshSelectedRowSummary()
    {
        _selectedRow?.Notify();
    }

    private void AddToolbar_Click(object sender,RoutedEventArgs e)
    {
        _targetRow=_selectedRow ?? Rows.FirstOrDefault(x=>x.IsPhase && x.Phase.Key=="test");
        OpenPalette();
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
            PaletteGroupsPanel.Children.Add(new TextBlock
            {
                Text=group,
                Style=(Style)FindResource("AtPreviewPaletteGroup")
            });
            var wrap=new WrapPanel();
            foreach(var item in kinds)
            {
                var button=new Button
                {
                    Content=item.Icon+"  "+item.Label,
                    Tag=item,
                    Style=(Style)FindResource("AtPreviewPaletteButton")
                };
                button.Click+=(_,_)=>CreatePreviewStep(item);
                wrap.Children.Add(button);
            }
            PaletteGroupsPanel.Children.Add(wrap);
        }
    }

    private void CreatePreviewStep(PaletteKind option)
    {
        var added=PreviewSampleFactory.CreateStep(option.Code,option.Label);
        switch(option.Code)
        {
            case "ign": added.SetParam("state","ON"); break;
            case "wait": added.SetParam("ms","100"); break;
            case "can": added.SetParam("message","未选择报文"); added.SetParam("period","0"); break;
            case "read": added.SetParam("signal","Signal_A"); break;
        }

        RememberUndo();
        _document.InsertAfter(_targetRow, added);
        PaletteOverlay.Visibility=Visibility.Collapsed;
        RebuildRows();
        SelectStep(added);
        Dispatcher.BeginInvoke(new Action(() => { StepNameField.Focus(); StepNameField.SelectAll(); }),
            DispatcherPriority.Input);
        SetMessage("已添加步骤：“"+added.Name+"”");
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStep is not { } selected || _document.Locate(selected) is null) return;
        RememberUndo();
        var copy = _document.Duplicate(selected);
        RebuildRows();
        if (copy is not null) SelectStep(copy);
        SetMessage("已复制步骤");
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStep is not { } selected || _document.Locate(selected) is null) return;
        RememberUndo();
        _document.Delete(selected);
        RebuildRows();
        SetMessage("已删除步骤；Ctrl+Z 可撤销");
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);
    private void MoveSelected(int delta)
    {
        if (_selectedStep is not { } selected || !_document.CanMove(selected, delta)) return;
        RememberUndo();
        _document.Move(selected, delta);
        RebuildRows();
    }

    private void RememberUndo() => _history.Remember(_phases, _selectedStep?.Id);

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!_history.TryUndo(_phases, _selectedStep?.Id, out var selected))
        {
            SetMessage("当前没有可撤销的 UI 操作。");
            return;
        }
        _selectedStep = AllSteps().FirstOrDefault(step => step.Id == selected);
        RebuildRows();
        SetMessage("已撤销");
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (!_history.TryRedo(_phases, _selectedStep?.Id, out var selected))
        {
            SetMessage("当前没有可重做的 UI 操作。");
            return;
        }
        _selectedStep = AllSteps().FirstOrDefault(step => step.Id == selected);
        RebuildRows();
        SetMessage("已重做");
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
        InspectorColumn.Width = new GridLength(_workspace.InspectorVisible ? 302 : 0);
        InspectorDock.Visibility = _workspace.InspectorVisible ? Visibility.Visible : Visibility.Collapsed;
        BottomRow.Height = new GridLength(_workspace.BottomVisible ? 165 : 0);
        BottomDock.Visibility = _workspace.BottomVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AdaptDocksToViewport()
    {
        _workspace.Adapt(ActualWidth, ActualHeight);
        ApplyDockLayout();
    }

    private void ToggleBottom_Click(object sender, RoutedEventArgs e)
    {
        _workspace.ToggleBottom();
        ApplyDockLayout();
        SetMessage(_workspace.BottomVisible ? "已展开底部观察区。" : "已折叠底部观察区。");
    }

    private void ToggleInspector_Click(object sender, RoutedEventArgs e)
    {
        _workspace.ToggleInspector();
        ApplyDockLayout();
        SetMessage(_workspace.InspectorVisible ? "已打开步骤属性。" : "已折叠步骤属性；按 Ctrl+I 可恢复。");
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
    // Template buttons bind to these UI-only commands. No business layer dependencies.
    private sealed class PreviewRowCommand(Action<PreviewRow> action) : ICommand
    {
        public bool CanExecute(object? parameter) => parameter is PreviewRow;
        public void Execute(object? parameter)
        {
            if (parameter is PreviewRow row) action(row);
        }
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }

}
