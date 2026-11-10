using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Desktop.G4;
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
    private bool _closed;
    private bool _disposing;
    private string _activeKey = "project";

    public MainWindow()
    {
        InitializeComponent();
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
        if (!ReferenceEquals(args.Source, WorkbenchTabs)) return;
        var index = WorkbenchTabs.SelectedIndex;
        _activeKey = index == 1 ? "station" : index switch
        {
            0 => "project", 2 => "dbc", 3 => "controller", 4 => "bench",
            5 => "wave", 6 => "uds", 7 => "flash", 8 => "program",
            9 => "run", 10 => "production", 11 => "records",
            12 => "operations", _ => "project"
        };
        PopulateTab(index);
        UpdateContext(index);
    }

    private void PopulateTab(int tab)
    {
        if (tab == 1 && R1StationHost.Content is null)
        {
            // Never open a second DeviceLab Window after embedding. Both
            // Station and CAN pages refer to this one view/session.
            _device ??= new AutoTest.DeviceLab.DeviceLabEmbeddedView();
            R1StationHost.Content = _device;
            _device.ShowStation();
            return;
        }
        if (DataContext is not MainWindowViewModel vm || vm.G4 is null) return;
        var host = vm.G4;
        switch (tab)
        {
            case 2 when R1DbcHost.Content is null:
                R1DbcHost.Content = new DbcToolView(new DbcToolViewModel());
                break;
            case 3 when R1ControllerHost.Content is null:
                R1ControllerHost.Content = new ControllerConfigToolView(new ControllerConfigToolViewModel());
                break;
            case 5 when R1WaveHost.Content is null:
            {
                var wave = new WaveWorkbenchView();
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTest");
                wave.Model.BindBackend(new G4WaveReplayBackend(host,
                    Path.Combine(appData,"G4-MockOnly","wave-workbench")));
                R1WaveHost.Content = wave;
                break;
            }
            case 9 when R1RunHost.Content is null:
                R1RunHost.Content = new LabTestWorkspaceView();
                break;
            case 10 when R1ProductionHost.Content is null:
                _production = new LabProductionToolView();
                R1ProductionHost.Content = _production;
                break;
            case 11 when R1RecordsHost.Content is null:
            {
                var database = host.Records.Database;
                var root = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "artifacts");
                var reports = new RunReportExporter(database,new ArtifactRepository(root));
                var history = new RecordHistoryToolSource(new RunHistoryQuery(database),
                    new ProductionStatistics(database),reports);
                R1RecordsHost.Content = new RecordsView(new RecordsViewModel(history));
                break;
            }
            case 12 when R1OperationsHost.Content is null:
            {
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTest");
                var root = Path.Combine(appData, "Operations");
                var audit = new SqliteOperationAuditStore(Path.Combine(root,"operation-audit.sqlite"));
                var trace = new OperationTraceArchive(Path.Combine(root,"traces"));
                R1OperationsHost.Content = new OperationsView(new OperationsViewModel(
                    audit,trace,new OperationAuditExporter(audit)));
                break;
            }
        }
    }

    private void UpdateContext(int tab)
    {
        if (R1InspectorTitle is null) return;
        var names = new[]
        {
            "工程 / Revision","设备 / Station","DBC / Signal","Controller / State",
            "Manual Bench","波形 / Cursor","UDS / ECU Profile","CAN Flash / Image",
            "TEST / Step","Run / StepFact","Production / DUT","Records / Report",
            "Operations / Audit"
        };
        R1InspectorTitle.Text = tab>=0 && tab<names.Length ? names[tab] : "资源";
        R1InspectorDescription.Text = tab switch
        {
            1 => "主控 WinUSB 与 CAN1 监视使用同一 DEVICE 会话。",
            2 => "DBC 的消息、信号、枚举表及 Revision 请在中心编辑器中管理。",
            5 => "信号选择、参考捕获、双游标、时间窗口及质量证据由 WAVE 工作区管理。",
            6 => "P08：服务、请求参数、原始 ECU 响应和 NRC。",
            7 => "P09：固件段、配置来源、擦写阶段与 ECU 最终复验。",
            10 => "扫描身份与生产流程由 Production 专业负责。",
            11 => "数据库历史记录与导出；不以 Mock 成功计出厂。",
            _ => "选择资源或工作区中的对象，操作作用于该专业持久模型。"
        };
        if (R1StepInspectorHost is not null)
            R1StepInspectorHost.Visibility = tab==8 ? Visibility.Visible : Visibility.Collapsed;
        if (R1InspectorContext is not null)
            R1InspectorContext.Visibility = tab==8 ? Visibility.Collapsed : Visibility.Visible;
        if (R1BottomLabel is not null)
            R1BottomLabel.Text = tab switch
            {
                6 => "UDS · RAW TRACE / NRC",
                7 => "FLASH · JOB STAGES / AUDIT",
                5 => "WAVE · CAPTURE QUALITY",
                1 => "USB / CAN · HARDWARE TRACE",
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

    private void OpenProject_Click(object sender,RoutedEventArgs e) => Navigate(0,"project");
    private void OpenStation_Click(object sender,RoutedEventArgs e)
    {
        Navigate(1,"station");
        _device?.ShowStation();
    }
    private void OpenCan_Click(object sender,RoutedEventArgs e)
    {
        Navigate(1,"can");
        _device?.ShowCan();
    }
    private void OpenDbc_Click(object sender,RoutedEventArgs e) => Navigate(2,"dbc");
    private void OpenController_Click(object sender,RoutedEventArgs e) => Navigate(3,"controller");
    private void OpenBench_Click(object sender,RoutedEventArgs e) => Navigate(4,"bench");
    private void OpenWave_Click(object sender,RoutedEventArgs e) => Navigate(5,"wave");
    private void OpenUds_Click(object sender,RoutedEventArgs e) => Navigate(6,"uds");
    private void OpenFlash_Click(object sender,RoutedEventArgs e) => Navigate(7,"flash");
    private void OpenTest_Click(object sender,RoutedEventArgs e) => Navigate(8,"program");
    private void OpenRun_Click(object sender,RoutedEventArgs e) => Navigate(9,"run");
    private void OpenProduction_Click(object sender,RoutedEventArgs e) => Navigate(10,"production");
    private void OpenRecords_Click(object sender,RoutedEventArgs e) => Navigate(11,"records");
    private void OpenAudit_Click(object sender,RoutedEventArgs e) => Navigate(12,"operations");

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
