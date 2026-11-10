using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Desktop.G4;
using AutoTest.Desktop.Bench;
using AutoTest.Desktop.Tools.ControllerConfig;
using AutoTest.Desktop.Tools.Dbc;
using AutoTest.Desktop.Tools.Flash;
using AutoTest.Desktop.Tools.Operations;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Desktop.Tools.Records;
using AutoTest.Desktop.Tools.Testing;
using AutoTest.Desktop.Tools.Uds;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Records.Exports;
using AutoTest.Records.Operations;
using AutoTest.Records.Queries;
using AutoTest.Records.Storage;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Exactly one formal R1 Shell. Professional Views are hosted in-place, sharing
/// G4 Application/Record resources rather than launching competing workspaces.
/// The embedded DEVICE view owns at most one physical USB connection and is
/// constructed lazily on the first Station/CAN navigation.
/// </summary>
public partial class MainWindow : Window
{
    private G4ToolsWindow? _compatTools;
    private AutoTest.DeviceLab.DeviceLabEmbeddedView? _device;
    private LabProductionToolView? _production;
    private ControllerConfigToolViewModel? _controllerConfiguration;
    private bool _closed;
    private bool _disposing;
    private bool _explorerHidden;
    private bool _inspectorHidden;
    private bool _bottomHidden;
    private GridLength _bottomSaved = new(190);
    private GridLength _explorerSaved = new(255);
    private GridLength _inspectorSaved = new(286);
    private string _activeKey = "project";

    public MainWindow()
    {
        InitializeComponent();
        if (R1UdsView.DataContext is UdsToolViewModel udsSource)
            R1BottomUdsGrid.ItemsSource=udsSource.TraceRows;
        if (R1FlashView.DataContext is FlashToolViewModel flashModel)
            R1FlashView.AttachPhysicalLink(() => _device?.Controller);
        if (R1UdsView.DataContext is UdsToolViewModel udsModel)
            udsModel.AttachPhysicalLink(
                () => _device?.Controller,
                request => MessageBox.Show(this,request,"确认真实 CAN1 UDS 只读诊断",
                    MessageBoxButton.YesNo,MessageBoxImage.Question,
                    MessageBoxResult.No)==MessageBoxResult.Yes);
        DataContextChanged += (_,_) => PopulateTab(WorkbenchTabs.SelectedIndex);
        UpdateContext(0);
    }

    private void Navigate(int tab, string key)
    {
        _activeKey = key;
        WorkbenchTabs.SelectedIndex = tab;
        PopulateTab(tab);
        UpdateContext(tab);
    }

    private void WorkbenchTabs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Source,WorkbenchTabs)) return;
        int selected=WorkbenchTabs.SelectedIndex;
        _activeKey=selected switch
        {
            0=>"project",1=>"station",2=>"can",3=>"dbc",4=>"controller",
            5=>"bench",6=>"wave",7=>"uds",8=>"flash",9=>"program",
            10=>"run",11=>"production",12=>"records",13=>"operations",
            _=>"project"
        };
        PopulateTab(selected);
        UpdateContext(selected);
    }

    private void PopulateTab(int tab)
    {
        if (tab is 1 or 2)
        {
            _device ??= new AutoTest.DeviceLab.DeviceLabEmbeddedView();
            R1BottomCanGrid.ItemsSource=_device.ViewModel.CanTimeline;
            BindStationStatus();
            // Both R1 documents host the SAME controller + physical WinUSB session.
            if (tab == 1)
            {
                if (ReferenceEquals(R1CanHost.Content,_device))
                    R1CanHost.Content=null;
                R1StationHost.Content=_device;
                _device.ShowStation();
            }
            else
            {
                if (ReferenceEquals(R1StationHost.Content,_device))
                    R1StationHost.Content=null;
                R1CanHost.Content=_device;
                _device.ShowCan();
            }
            return;
        }
        if (DataContext is not MainWindowViewModel vm || vm.G4 is null) return;
        var host = vm.G4;
        switch (tab)
        {
            case 3 when R1DbcHost.Content is null:
                R1DbcHost.Content = new DbcToolView(new DbcToolViewModel());
                break;
            case 4 when R1ControllerHost.Content is null:
                _controllerConfiguration ??= new ControllerConfigToolViewModel();
                R1ControllerHost.Content = new ControllerConfigToolView(_controllerConfiguration);
                break;
            case 5 when R1BenchHost.Content is null:
                _controllerConfiguration ??= new ControllerConfigToolViewModel();
                R1BenchHost.Content = new BenchWorkbenchView(new BenchWorkbenchViewModel(
                    new R1BenchWorkspacePort(() => _controllerConfiguration,
                        () => _device?.Controller)));
                break;
            case 6 when R1WaveHost.Content is null:
            {
                var wave = new WaveWorkbenchView();
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTest");
                wave.Model.BindBackend(new G4WaveReplayBackend(host,
                    Path.Combine(appData,"G4-MockOnly","wave-workbench")));
                R1WaveHost.Content = wave;
                break;
            }
            case 10 when R1RunHost.Content is null:
                R1RunHost.Content = new LabTestWorkspaceView();
                break;
            case 11 when R1ProductionHost.Content is null:
                _production = new LabProductionToolView();
                R1ProductionHost.Content = _production;
                break;
            case 12 when R1RecordsHost.Content is null:
            {
                var database = host.Records.Database;
                var root = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "artifacts");
                var reports = new RunReportExporter(database,new ArtifactRepository(root));
                var history = new RecordHistoryToolSource(new RunHistoryQuery(database),
                    new ProductionStatistics(database),reports);
                R1RecordsHost.Content = new RecordsView(new RecordsViewModel(history));
                break;
            }
            case 13 when R1OperationsHost.Content is null:
            {
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTest");
                var root = Path.Combine(appData, "Operations");
                var audit = new SqliteOperationAuditStore(Path.Combine(root,"operation-audit.sqlite"));
                var trace = new OperationTraceArchive(Path.Combine(root,"traces"));
                var operationsModel=new OperationsViewModel(
                    audit,trace,new OperationAuditExporter(audit));
                R1OperationsHost.Content = new OperationsView(operationsModel);
                R1BottomAuditGrid.ItemsSource=operationsModel.Entries;
                break;
            }
        }
    }

    private void UpdateContext(int tab)
    {
        if (R1InspectorTitle is null) return;
        var names = new[]
        {
            "工程 / Revision","设备 / Station","CAN / Monitor","DBC / Signal",
            "Controller / State","Manual Bench","波形 / Cursor","UDS / ECU Profile",
            "CAN Flash / Image","TEST / Step","Run / StepFact","Production / DUT",
            "Records / Report","Operations / Audit"
        };
        R1InspectorTitle.Text = tab>=0 && tab<names.Length ? names[tab] : "资源";
        R1InspectorDescription.Text = tab switch
        {
            1 => "主控 WinUSB 身份、状态与设备诊断。",
            2 => "DUT CAN1、按键板 CAN0 的接收、筛选和原始 Trace。",
            3 => "DBC 消息、信号、枚举表和 Revision。",
            6 => "波形信号选择、参考曲线、双游标及判定证据。",
            7 => "UDS 服务目录、请求参数、诊断会话、原始响应与 NRC。",
            8 => "烧录镜像、配置来源、编程阶段与 ECU 最终复验。",
            11 => "扫码规则、DUT 身份与生产流程。",
            12 => "数据库历史、报告与独立 DUT 统计。",
            _ => "选择资源或工作区中的对象，操作作用于对应专业模型。"
        };
        if (R1StepInspectorHost is not null)
            R1StepInspectorHost.Visibility = tab==9 ? Visibility.Visible : Visibility.Collapsed;
        if (R1InspectorContext is not null)
            R1InspectorContext.Visibility = tab==9 ? Visibility.Collapsed : Visibility.Visible;
        if (R1BottomLabel is not null)
            R1BottomLabel.Text = tab switch
            {
                7 => "UDS · RAW TRACE / NRC",
                8 => "FLASH · JOB STAGES / AUDIT",
                6 => "WAVE · CAPTURE QUALITY",
                1 or 2 => "USB / CAN · HARDWARE TRACE",
                _ => "OUTPUT / TRACE · StepFact / Verdict"
            };
        UpdateRailSelected();
    }

    private void UpdateRailSelected()
    {
        var buttons = new[]
        {
            R1ProjectRail,R1StationRail,R1CanRail,R1DbcRail,R1ControllerRail,
            R1BenchRail,R1WaveRail,R1UdsRail,R1FlashRail,R1TestRail,
            R1RunRail,R1ProductionRail,R1RecordsRail,R1AuditRail
        };
        var index = _activeKey switch
        {
            "project"=>0,"station"=>1,"can"=>2,"dbc"=>3,"controller"=>4,
            "bench"=>5,"wave"=>6,"uds"=>7,"flash"=>8,"program"=>9,
            "run"=>10,"production"=>11,"records"=>12,"operations"=>13,_=>0
        };
        for (int i=0;i<buttons.Length;i++)
            buttons[i].Tag=i==index ? "active" : null;
    }

    private void ToggleBottom_Click(object sender,RoutedEventArgs e)
    {
        if (Content is not Grid root || root.RowDefinitions.Count<5) return;
        var panel=root.RowDefinitions[4];
        _bottomHidden=!_bottomHidden;
        if (_bottomHidden)
        {
            if (panel.Height.Value>40) _bottomSaved=panel.Height;
            panel.MinHeight=27;
            panel.Height=new GridLength(27);
            R1BottomTabs.Visibility=Visibility.Collapsed;
        }
        else
        {
            panel.MinHeight=125;
            panel.Height=_bottomSaved;
            R1BottomTabs.Visibility=Visibility.Visible;
        }
    }

    private void BindStationStatus()
    {
        if (_device is null) return;
        System.Windows.Data.BindingOperations.SetBinding(R1StationStatus,
            TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(
                AutoTest.DeviceLab.DeviceLabViewModel.StatusText))
            {
                Source=_device.ViewModel,
                StringFormat="Station · {0}",
                Mode=System.Windows.Data.BindingMode.OneWay
            });
        void RefreshLed()
        {
            R1StationDot.Fill=_device.ViewModel.StatusText.Contains("已连接",
                StringComparison.Ordinal) || _device.ViewModel.StatusText.Contains("Connected",
                StringComparison.OrdinalIgnoreCase)
                    ? Brushes.MediumSeaGreen : Brushes.SlateGray;
        }
        _device.ViewModel.PropertyChanged+=(_,e)=>
        {
            if (e.PropertyName==nameof(AutoTest.DeviceLab.DeviceLabViewModel.StatusText))
                Dispatcher.InvokeAsync(RefreshLed);
        };
        RefreshLed();
    }
    private void WorkspaceMode_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if (!IsLoaded || R1WorkspaceMode is null) return;
        switch (R1WorkspaceMode.SelectedIndex)
        {
            case 0: Navigate(0,"project"); break;
            case 1: Navigate(11,"production"); break;
            case 2: Navigate(12,"records"); break;
        }
    }
    private void ExecutionSource_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if (!IsLoaded || R1ExecutionSource.SelectedItem is not ComboBoxItem choice) return;
        string source=choice.Tag as string ?? "Mock";
        if (R1UdsView.DataContext is UdsToolViewModel uds)
            uds.SourceMode=source;
        if (R1BenchHost.Content is BenchWorkbenchView bench &&
            bench.DataContext is BenchWorkbenchViewModel vm)
        {
            vm.Source=source switch
            {
                "Physical"=>BenchEvidenceSource.Physical,
                "Replay"=>BenchEvidenceSource.Replay,
                _=>BenchEvidenceSource.Mock
            };
        }
        // Each other module retains its own verified source. Selecting Physical
        // is NOT a global authorization for real ECU output or programming.
    }

    private void Window_PreviewKeyDown(object sender,KeyEventArgs e)
    {
        if (Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.K)
        {
            e.Handled=true;
            ShowPalette();
        }
    }
    private void CommandPalette_Click(object sender,RoutedEventArgs e) => ShowPalette();
    private void ShowPalette()
    {
        var palette=new R1CommandPalette(OpenPaletteDestination) { Owner=this };
        palette.ShowDialog();
    }
    private void OpenPaletteDestination(string key)
    {
        string[] keys = ["project","station","can","dbc","controller","bench","wave",
            "uds","flash","program","run","production","records","operations"];
        int selected=Array.IndexOf(keys,key);
        if (selected>=0) Navigate(selected,key);
    }
    private void ToggleExplorer_Click(object sender,RoutedEventArgs e)
    {
        _explorerHidden=!_explorerHidden;
        if (_explorerHidden)
        {
            if (R1ExplorerColumn.Width.Value>0) _explorerSaved=R1ExplorerColumn.Width;
            R1ExplorerColumn.Width=new GridLength(0);
            R1ExplorerSizerColumn.Width=new GridLength(0);
            R1ExplorerPanel.Visibility=Visibility.Collapsed;
            R1ExplorerSizer.Visibility=Visibility.Collapsed;
        }
        else
        {
            R1ExplorerColumn.Width=_explorerSaved;
            R1ExplorerSizerColumn.Width=new GridLength(5);
            R1ExplorerPanel.Visibility=Visibility.Visible;
            R1ExplorerSizer.Visibility=Visibility.Visible;
        }
    }
    private void ToggleInspector_Click(object sender,RoutedEventArgs e)
    {
        _inspectorHidden=!_inspectorHidden;
        if (_inspectorHidden)
        {
            if (R1InspectorColumn.Width.Value>0) _inspectorSaved=R1InspectorColumn.Width;
            R1InspectorColumn.Width=new GridLength(0);
            R1InspectorSizerColumn.Width=new GridLength(0);
            R1InspectorPanel.Visibility=Visibility.Collapsed;
            R1InspectorSizer.Visibility=Visibility.Collapsed;
        }
        else
        {
            R1InspectorColumn.Width=_inspectorSaved;
            R1InspectorSizerColumn.Width=new GridLength(5);
            R1InspectorPanel.Visibility=Visibility.Visible;
            R1InspectorSizer.Visibility=Visibility.Visible;
        }
    }

    private void OpenProject_Click(object sender,RoutedEventArgs e) => Navigate(0,"project");
    private void OpenStation_Click(object sender,RoutedEventArgs e) => Navigate(1,"station");
    private void OpenCan_Click(object sender,RoutedEventArgs e) => Navigate(2,"can");
    private void OpenDbc_Click(object sender,RoutedEventArgs e) => Navigate(3,"dbc");
    private void OpenController_Click(object sender,RoutedEventArgs e) => Navigate(4,"controller");
    private void OpenBench_Click(object sender,RoutedEventArgs e) => Navigate(5,"bench");
    private void OpenWave_Click(object sender,RoutedEventArgs e) => Navigate(6,"wave");
    private void OpenUds_Click(object sender,RoutedEventArgs e) => Navigate(7,"uds");
    private void OpenFlash_Click(object sender,RoutedEventArgs e) => Navigate(8,"flash");
    private void OpenTest_Click(object sender,RoutedEventArgs e) => Navigate(9,"program");
    private void OpenRun_Click(object sender,RoutedEventArgs e) => Navigate(10,"run");
    private void OpenProduction_Click(object sender,RoutedEventArgs e) => Navigate(11,"production");
    private void OpenRecords_Click(object sender,RoutedEventArgs e) => Navigate(12,"records");
    private void OpenAudit_Click(object sender,RoutedEventArgs e) => Navigate(13,"operations");
    private void DeviceLab_Click(object sender,RoutedEventArgs e) => OpenStation_Click(sender,e);
    private void Modules_Click(object sender,RoutedEventArgs e) => OpenProject_Click(sender,e);

    private void G4Tools_Click(object sender,RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { G4: not null } vm) return;
        if (_compatTools is { IsVisible: true }) { _compatTools.Activate(); return; }
        _compatTools = new G4ToolsWindow(vm) { Owner=this };
        _compatTools.Show();
    }

    private void R1About_Click(object sender,RoutedEventArgs e) =>
        MessageBox.Show(this,
            "产品设计基准：docs/ui-r1/reference/AutoTest_UI_Interactive_Prototype_V0.1.html\n"+
            "一个 R1 Shell，13 个专业工作区，统一导航、工程资源和审计。"+
            "目前真实物理烧录仍按独立 FieldStudio 流程验收，不以 Mock 模拟代替实测。",
            "AutoTest R1 Studio",MessageBoxButton.OK,MessageBoxImage.Information);
    private void Close_Click(object sender,RoutedEventArgs e) => Close();

    private async void Window_Closing(object? sender,CancelEventArgs e)
    {
        if (_closed || (_device is null && _production is null)) return;
        e.Cancel=true;
        if (_disposing) return;
        _disposing=true;
        IsEnabled=false;
        try
        {
            if (_production is not null) await _production.DisposeAsync();
            if (_device is not null) await _device.DisposeAsync();
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,"R1 设备会话关闭异常，请保留 USB/CAN 证据：\n"+ex,
                "设备关闭失败",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
        finally { _closed=true; Close(); }
    }
}
