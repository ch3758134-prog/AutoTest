using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Desktop.Bench;
using AutoTest.Desktop.G4;
using AutoTest.Desktop.Project;
using AutoTest.Desktop.Tools.ControllerConfig;
using AutoTest.Desktop.Tools.Dbc;
using AutoTest.Desktop.Tools.Flash;
using AutoTest.Desktop.Tools.Operations;
using AutoTest.Desktop.Tools.Production;
using AutoTest.Desktop.Tools.Records;
using AutoTest.Desktop.Tools.Testing;
using AutoTest.Desktop.Tools.Uds;
using AutoTest.Desktop.Tools.Wave;
using AutoTest.Devices.Can;
using AutoTest.Records.Exports;
using AutoTest.Records.Operations;
using AutoTest.Records.Queries;
using AutoTest.Records.Storage;
using AutoTest.Specialists.Wave;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// The one and only R1 product shell.  Documents are opened on demand, TEST is
/// always the primary document, and the explorer is present in both running and
/// editing. There are NO numbered pages, NO mode switcher, NO second operator
/// surface and NO duplicate testing/device services.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Dictionary<string, TabItem> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _views = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Window> _floating = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _titles = new(StringComparer.Ordinal)
    {
        ["run"] = "自动测试",
        ["program"] = "TEST 编辑",
        ["station"] = "设备 / CAN",
        ["dbc"] = "DBC 编辑",
        ["controller"] = "Controller",
        ["bench"] = "手动台架",
        ["wave"] = "Wave Studio",
        ["uds"] = "UDS Console",
        ["flash"] = "CAN Flash",
        ["scan"] = "编号绑定 / COM",
        ["records"] = "记录报告",
        ["operations"] = "操作审计"
    };

    private MainWindowViewModel? _workspaceEventsOwner;
    private LabTestWorkspaceViewModel? _test;
    private Guid? _testProjectId;
    private ProjectExplorerView? _explorerView;
    private AutoTest.DeviceLab.DeviceLabEmbeddedView? _device;
    private LabProductionToolView? _scannerView;
    private ControllerConfigToolViewModel? _controllerConfig;
    private CanFrameHub? _waveReplayHub;
    private FlashToolView? _flash;
    private BenchWorkbenchViewModel? _benchModel;
    private bool _stationStatusSubscribed;
    private bool _closing;
    private bool _closed;
    private bool _closeAfterFlash;
    private bool _explorerHidden;
    private bool _logsExpanded;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindWorkspace();
        Loaded += (_, _) =>
        {
            BindWorkspace();
            if (DataContext is MainWindowViewModel vm)
                SetupWorkspace(vm);
        };
    }

    private void BindWorkspace()
    {
        if (_workspaceEventsOwner is not null)
            _workspaceEventsOwner.WorkspaceAssetsChanged -= OnAssetsChanged;
        _workspaceEventsOwner = DataContext as MainWindowViewModel;
        if (_workspaceEventsOwner is not null)
            _workspaceEventsOwner.WorkspaceAssetsChanged += OnAssetsChanged;
        if (IsLoaded && _workspaceEventsOwner is not null)
            SetupWorkspace(_workspaceEventsOwner);
    }

    private void SetupWorkspace(MainWindowViewModel vm)
    {
        if (vm.G4 is null) return;
        if (_explorerView is null)
        {
            _explorerView = new ProjectExplorerView(vm);
            _explorerView.OpenRequested += OpenDocument;
            ExplorerDocumentHost.Content = _explorerView;
        }
        EnsureTestModel(vm.G4.Workspace);
        EnsureScanner(vm);
        OpenDocument("run");
    }

    private LabTestWorkspaceViewModel EnsureTestModel(AutoTest.Application.V1.V1Workspace workspace)
    {
        if (_test is null)
        {
            _test = new LabTestWorkspaceViewModel(workspace);
            _test.AttachPhysicalReadOnly(
                () => _device?.Controller,
                () => MessageBox.Show(this,
                    "本次工程 TEST 将通过 R1 Station 的 CAN1 读取真实 ECU。\n" +
                    "请核对接线、工程及 DBC 修订。仅已授权且实际实现的物理操作允许执行。\n" +
                    "确认本次实际 TEST 运行？",
                    "真实 ECU 测试授权", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
            _test.AttachPhysicalController(
                () => _controllerConfig?.CurrentProfile,
                () => _controllerConfig?.FilePath,
                summary => MessageBox.Show(this, summary,
                    "真实 CAN0 / KEY_SIM 组合按键确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) == MessageBoxResult.Yes);
            _testProjectId = workspace.Project.Id.Value;
            ExplorerTabs.DataContext = _test;
            TestCasesList.ItemsSource = _test.TestCases;
            PlannedStepsList.DataContext = _test;
            // A separate grouped view avoids grouping the main execution
            // DataGrid and preserves one shared SelectedRunStep across panes.
            var groupedSteps = new ListCollectionView(_test.RunStepRows);
            groupedSteps.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(R1RunStepRow.Phase)));
            PlannedStepsList.ItemsSource = groupedSteps;
        }
        else if (_testProjectId != workspace.Project.Id.Value && !_test.Busy)
        {
            _test.ReloadFromProject();
            _testProjectId = workspace.Project.Id.Value;
        }
        return _test;
    }

    private void EnsureScanner(MainWindowViewModel vm)
    {
        if (_scannerView is not null || vm.G4 is null) return;
        var scan = new R1ProductionScannerViewModel();
        scan.AttachProject(vm.G4.Workspace);
        scan.PhysicalScanMatched += binding =>
        {
            // This path always runs the complete published TEST; the checkboxes
            // in the engineering tree never affect production barcode selection.
            Dispatcher.InvokeAsync(() =>
            {
                OpenDocument("run");
                _ = RunScannedTestAsync(scan, binding, vm.G4.Workspace,
                    vm.G4.Records.Database);
            });
        };
        scan.PropertyChanged += (_, _) =>
        {
            if (!_closed && !Dispatcher.HasShutdownStarted)
                Dispatcher.InvokeAsync(RefreshScannerStatus);
        };
        _scannerView = new LabProductionToolView(scan);
        RefreshScannerStatus();
    }

    private void RefreshScannerStatus()
    {
        if (_scannerView?.DataContext is not R1ProductionScannerViewModel scan) return;
        var number = string.IsNullOrWhiteSpace(scan.ScanInput) ? "等待编号" : scan.ScanInput;
        ScannerStatus.Text = "扫码：" + number + " · " + scan.MatchDetail;
        ScannerStatus.ToolTip = ScannerStatus.Text;
    }

    private void OnAssetsChanged(object? sender, EventArgs e)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => OnAssetsChanged(sender, e));
            return;
        }
        if (DataContext is not MainWindowViewModel vm) return;
        if (_test is not null && _testProjectId != vm.Workspace.Project.Id.Value)
        {
            if (_test.Busy) _test.StopCommand.Execute(null);
            else
            {
                _test.ReloadFromProject();
                _testProjectId = vm.Workspace.Project.Id.Value;
            }
        }
        if (_views.TryGetValue("dbc", out var dbc) &&
            dbc.DataContext is DbcToolViewModel dbcVm)
            dbcVm.RefreshProjectStatus();
        if (_views.TryGetValue("wave", out var wave) && wave is WaveWorkbenchView w)
            w.Model.RefreshSignals();
    }

    /// <summary>Only one tab for each currently opened actual document/tool.</summary>
    private void OpenDocument(string key)
    {
        if (_closed) return;
        if (key == "project")
        {
            ShowExplorer();
            ExplorerTabs.SelectedIndex = 1;
            return;
        }
        if (key == "can" || key == "station")
        {
            var device = EnsureDevice();
            if (key == "can") device.ShowCan();
            else device.ShowStation();
            key = "station"; // same physical controller AND the same WPF view
        }
        // Professional tools are deliberately NOT IDE document tabs.
        // Opening any of them gives it a whole, maximizable top-level window
        // instead of fitting its native control surface into the TEST viewport.
        if (R1ToolWindowPolicy.IsProfessionalTool(key))
        {
            OpenProfessionalToolWindow(key);
            return;
        }
        if (_floating.TryGetValue(key, out var window))
        {
            RestoreToolWindow(window);
            return;
        }
        if (_documents.TryGetValue(key, out var existing))
        {
            DocumentTabs.SelectedItem = existing;
            return;
        }

        if (!_titles.ContainsKey(key)) return;
        var view = GetOrCreateView(key);
        if (view is null) return;
        var tab = new TabItem { Content = view, Tag = key };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = _titles[key], VerticalAlignment = VerticalAlignment.Center,
            FontWeight = key == "run" ? FontWeights.SemiBold : FontWeights.Normal
        });
        if (key != "run")
        {
            var close = new Button
            {
                Content = "×", Padding = new Thickness(5, 0, 3, 0),
                Margin = new Thickness(9, 0, 0, 0), MinHeight = 18,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = Brushes.SlateGray
            };
            close.Click += (_, _) => CloseDocument(key);
            header.Children.Add(close);
        }
        tab.Header = header;
        _documents[key] = tab;
        DocumentTabs.Items.Add(tab);
        DocumentTabs.SelectedItem = tab;
    }

    private static void RestoreToolWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Maximized;
        window.Activate();
        window.Focus();
    }

    /// <summary>
    /// Full-size professional windows share the same project, physical device
    /// and tool ViewModels held by this Shell. Closing a window never creates a
    /// new WinUSB owner, changes TEST documents or disposes the COM scanner.
    /// </summary>
    private void OpenProfessionalToolWindow(string key)
    {
        key = R1ToolWindowPolicy.Normalize(key);
        if (_floating.TryGetValue(key, out var existing))
        {
            RestoreToolWindow(existing);
            return;
        }
        if (!_titles.ContainsKey(key)) return;
        var view = GetOrCreateView(key);
        if (view is null) return;

        // Remove any old central tab if present in a previously opened session.
        if (_documents.Remove(key, out var obsolete))
        {
            obsolete.Content = null;
            DocumentTabs.Items.Remove(obsolete);
        }

        // The application can keep running a TEST while an engineer analyzes
        // CAN/UDS/Wave/Flash in a second full-screen or second-monitor window.
        var tool = new Window
        {
            Title = "AutoTest · " + _titles[key],
            Width = 1380,
            Height = 840,
            MinWidth = 860,
            MinHeight = 580,
            Background = Brushes.White,
            Owner = this,
            ShowInTaskbar = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowState = WindowState.Normal,
            Content = view
        };
        tool.Closing += (_, e) =>
        {
            if (key == "bench" && _benchModel?.KeyActions.IsActionRunning == true)
            {
                e.Cancel = true;
                MessageBox.Show(tool,
                    "真实按键连续动作仍在执行。请在手动台架停止动作并等待当前指令返回，再关闭窗口。",
                    "手动台架正在执行", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (key == "flash" && _flash?.IsPhysicalCan1Busy == true)
            {
                e.Cancel = true;
                if (!_closing)
                    MessageBox.Show(tool,
                        "实际 ECU 烧录仍在执行。请在烧录界面请求停止并等待安全结束，不能通过关闭窗口隐藏正在执行的操作。",
                        "烧录正在执行", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        tool.Closed += (_, _) =>
        {
            // A closed Window cannot be reused, but its professional View can.
            // Detach the existing View to reopen it in a fresh dedicated window.
            tool.Content = null;
            _floating.Remove(key);
            if (key == "wave" && view is WaveWorkbenchView wave)
                wave.CancelArchiveRecording();
        };
        _floating[key] = tool;
        tool.Show();
    }

    private FrameworkElement? GetOrCreateView(string key)
    {
        if (_views.TryGetValue(key, out var cached)) return cached;
        if (DataContext is not MainWindowViewModel { G4: not null } vm)
            return null;
        var host = vm.G4;
        FrameworkElement view;
        switch (key)
        {
            case "run":
            {
                var runView = new R1ProjectRunView(EnsureTestModel(host.Workspace));
                runView.EditProgramRequested += (_, _) => OpenDocument("program");
                view = runView;
                break;
            }
            case "program":
                view = new LabTestWorkspaceView(EnsureTestModel(host.Workspace));
                break;
            case "station":
                view = EnsureDevice();
                break;
            case "dbc":
            {
                var dbc = new DbcToolViewModel(host.Workspace);
                dbc.ProjectDbcImported += (_, _) => vm.RefreshExternalWorkspaceChanges();
                view = new DbcToolView(dbc);
                break;
            }
            case "controller":
                _controllerConfig ??= new ControllerConfigToolViewModel();
                view = new ControllerConfigToolView(_controllerConfig);
                break;
            case "bench":
            {
                // Prepare the shared Station facade, but do NOT connect USB.
                // Bench and every tool continue to use this exact same handle.
                var station = EnsureDevice();
                _controllerConfig ??= new ControllerConfigToolViewModel();
                _benchModel ??= new BenchWorkbenchViewModel(
                    new R1BenchWorkspacePort(() => _controllerConfig,
                        () => station.Controller, approveKeySim: (mask, generation) =>
                            MessageBox.Show(this,
                                $"真实按键板操作：CAN0 / KEY_SIM / 20 Key 掩码 0x{mask:X5}\n" +
                                $"USB Generation：{generation}\n" +
                                (mask == 0 ? "将释放全部按键组合。\n" :
                                     "将闭合并保持对应电阻网络开关。\n") +
                                "USB CAN_SEND ACK 不代表 DUT 已确认。是否执行？",
                                "真实 KEY_SIM 操作确认",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes,
                        approveIgn: (on, generation) =>
                            MessageBox.Show(this,
                                "真实主控 IGN 物理输出切换：" + (on ? "ON（高边输出导通）" : "OFF（高边输出断开）") + "\n" +
                                "USB 命令 SET_IGN=0x12，Payload=" + (on ? "01" : "00") + "\n" +
                                "Session Generation=" + generation + "\n" +
                                "注意：主控 ACK 不等于实际电压已确认；请确保负载接线安全，是否发出单次命令？",
                                "确认真实 IGN 输出",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes,
                        testRunning: () => _test?.Busy == true,
                        approveSolenoid: (action, generation) =>
                            MessageBox.Show(this,
                                "正在请求真实夹具电磁阀动作：" +
                                (action == 0 ? "升起（命令 00）" : "下落（命令 01）") + "\n" +
                                "USB CMD SET_SOL=0x16，Session Generation=" + generation + "\n" +
                                "当前动作会改变夹具机械状态。主控 ACK 与 GET_SOL 不证明机械压紧到位，" +
                                "请核对工装、人员与 DUT 位置。是否执行本次单次命令？",
                                "真实夹具电磁阀操作确认",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes,
                        projectPath: () => host.Workspace.ProjectPath,
                        projectId: () => host.Workspace.Project.Id.Value.ToString("D"),
                        approveKeyAction: (action, generation) =>
                            MessageBox.Show(this,
                                $"执行当前工程的按键动作：{action.Name}\\n" +
                                $"步骤数：{action.Steps.Count}\\n" +
                                $"会按步骤发送真实 KEY_SIM 开关状态并保持指定时间。\\n" +
                                $"主控 Session：{generation}\\n" +
                                "结束或停止时均不自动复位；请核对现场接线。是否执行？",
                                "确认真实连续按键动作",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes));
                station.Controller.ConnectionChanged += state =>
                {
                    if (!_closed && !Dispatcher.HasShutdownStarted)
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_benchModel is not null)
                                _ = _benchModel.RefreshConnectionAsync();
                        }));
                };
                var bench = new BenchWorkbenchView(_benchModel);
                bench.OpenStationRequested += (_, _) => OpenDocument("station");
                view = bench;
                break;
            }
            case "wave":
            {
                var wave = new WaveWorkbenchView();
                wave.AttachArchiveCapture(() => _device?.Controller, () => host.Workspace);
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AutoTest", "R1", "wave-workbench");
                var capture = new WaveCaptureService(
                    new R1PhysicalWaveSignalSource(host.Workspace, () => _device?.Controller),
                    new WaveEvidenceStore(Path.Combine(appData, "live-captures")),
                    new WaveReferenceRepository(Path.Combine(appData, "live-references")));
                var live = new WaveSharedLiveProvider(capture, "Physical");
                var replay = _waveReplayHub ??= new CanFrameHub();
                var recorded = new RecordedCanWaveReplayProvider(
                    replay, replay.Publish, () => true,
                    host.Workspace, Path.Combine(appData, "recorded"));
                wave.Model.BindBackend(new G4WaveReplayBackend(
                    host, appData, live: live, recorded: recorded));
                view = wave;
                break;
            }
            case "uds":
            {
                var uds = new UdsToolView();
                if (uds.DataContext is UdsToolViewModel model)
                {
                    model.AttachPhysicalLink(() => _device?.Controller,
                        request => MessageBox.Show(this, request,
                            "实际 CAN1 UDS 诊断确认",
                            MessageBoxButton.YesNo, MessageBoxImage.Question,
                            MessageBoxResult.No) == MessageBoxResult.Yes);
                    UdsTraceGrid.ItemsSource = model.TraceRows;
                }
                view = uds;
                break;
            }
            case "flash":
            {
                _flash = new FlashToolView();
                _flash.AttachPhysicalLink(() => _device?.Controller);
                _flash.PhysicalOperationStopped += (_, _) =>
                {
                    if (_closeAfterFlash && !_closed && !Dispatcher.HasShutdownStarted)
                        Dispatcher.BeginInvoke(new Action(Close));
                };
                view = _flash;
                break;
            }
            case "scan":
                EnsureScanner(vm);
                view = _scannerView!;
                break;
            case "records":
            {
                var db = host.Records.Database;
                var folder = Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, "artifacts");
                var exporter = new RunReportExporter(db, new ArtifactRepository(folder));
                var source = new RecordHistoryToolSource(
                    new RunHistoryQuery(db), new ProductionStatistics(db), exporter);
                view = new RecordsView(new RecordsViewModel(source));
                break;
            }
            case "operations":
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AutoTest", "Operations");
                var audit = new SqliteOperationAuditStore(
                    Path.Combine(root, "operation-audit.sqlite"));
                var archive = new OperationTraceArchive(Path.Combine(root, "traces"));
                var model = new OperationsViewModel(
                    audit, archive, new OperationAuditExporter(audit));
                view = new OperationsView(model);
                AuditGrid.ItemsSource = model.Entries;
                break;
            }
            default:
                return null;
        }
        _views[key] = view;
        return view;
    }

    private AutoTest.DeviceLab.DeviceLabEmbeddedView EnsureDevice()
    {
        if (_device is not null) return _device;
        _device = new AutoTest.DeviceLab.DeviceLabEmbeddedView();
        CanTraceGrid.ItemsSource = _device.ViewModel.CanTimeline;
        BindingOperations.SetBinding(StationStatus, TextBlock.TextProperty,
            new Binding(nameof(AutoTest.DeviceLab.DeviceLabViewModel.StatusText))
            {
                Source = _device.ViewModel, StringFormat = "Station · {0}",
                Mode = BindingMode.OneWay
            });
        void Led()
        {
            var state = _device.ViewModel.StatusText;
            StationLed.Fill = state.Contains("已连接", StringComparison.Ordinal) ||
                state.Contains("Connected", StringComparison.OrdinalIgnoreCase)
                ? Brushes.MediumSeaGreen : Brushes.SlateGray;
        }
        if (!_stationStatusSubscribed)
        {
            _stationStatusSubscribed = true;
            _device.ViewModel.PropertyChanged += (_, e) =>
            {
                if (!_closed && !Dispatcher.HasShutdownStarted &&
                    e.PropertyName == nameof(AutoTest.DeviceLab.DeviceLabViewModel.StatusText))
                    Dispatcher.InvokeAsync(Led);
            };
        }
        Led();
        return _device;
    }

    private void CloseDocument(string key)
    {
        if (key == "run") return; // primary TEST is never closed
        if (key == "flash" && _flash?.IsPhysicalCan1Busy == true)
        {
            // Closing a document must never conceal an in-flight destructive
            // ECU operation; the window can only close after a safe stop.
            MessageBox.Show(this,
                "CAN 烧录仍在执行。请先在烧录工具中停止操作并等待实际结束，之后才能关闭此文档。",
                "设备会话正在执行烧录", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenDocument("flash");
            return;
        }
        if (R1ToolWindowPolicy.IsProfessionalTool(key))
        {
            // A tool's close control lives on its dedicated window, not as an
            // extra removable tab in the central TEST area.
            if (_floating.TryGetValue(R1ToolWindowPolicy.Normalize(key), out var toolWindow))
                toolWindow.Close();
            return;
        }
        if (_floating.TryGetValue(key, out var floating))
            floating.Close();
        if (_documents.Remove(key, out var tab))
        {
            tab.Content = null;
            DocumentTabs.Items.Remove(tab);
        }
        // Retain services/VM, not the obsolete UI tabs. Reopening the tool
        // reuses its real device/service instance where that is required.
        if (DocumentTabs.SelectedItem is null)
            OpenDocument("run");
    }

    private void FloatDocument(string key)
    {
        if (R1ToolWindowPolicy.IsProfessionalTool(key))
        {
            OpenDocument(key);
            return;
        }
        OpenDocument(key);
        if (_floating.TryGetValue(key, out var active))
        {
            active.Activate();
            return;
        }
        if (!_documents.TryGetValue(key, out var tab) ||
            tab.Content is not FrameworkElement view) return;
        tab.Content = new TextBlock
        {
            Text = "此文档已在独立窗口中打开。关闭独立窗口将回到此处。",
            Margin = new Thickness(24), Foreground = Brushes.SlateGray
        };
        var window = new Window
        {
            Title = "AutoTest Studio · " + _titles[key],
            Width = 1200, Height = 790, MinWidth = 810, MinHeight = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, Background = Brushes.White, Content = view
        };
        _floating[key] = window;
        window.Closed += (_, _) =>
        {
            window.Content = null;
            _floating.Remove(key);
            if (!_closed && _documents.ContainsKey(key))
                tab.Content = view;
        };
        window.Show();
    }

    private async Task RunScannedTestAsync(
        R1ProductionScannerViewModel scanner, R1PhysicalScanBinding binding,
        AutoTest.Application.V1.V1Workspace project, RecordDatabase database)
    {
        try
        {
            binding.Verify(project);
            var previous = await new RunHistoryQuery(database).FindByDutAsync(binding.UnitKey);
            if (previous.Length != 0 &&
                MessageBox.Show(this,
                    "该 DUT 已有 " + previous.Length + " 条运行记录。是否明确作为复测继续？",
                    "重复编号 / 复测确认", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                scanner.MarkRunCompleted(binding, null, "RETEST_CONFIRMATION_REQUIRED", null);
                return;
            }
            var test = EnsureTestModel(project);
            if (test.Busy) throw new InvalidOperationException("PROJECT_TEST_ALREADY_RUNNING");
            var result = await test.RunPhysicalReadOnlyAsync(binding);
            if (result is null)
            {
                scanner.MarkRunCompleted(binding, null, "未执行 / " + test.Status, null);
                return;
            }
            try
            {
                var controller = _device?.Controller ??
                    throw new InvalidOperationException("PHYSICAL_DEVICE_NOT_AVAILABLE");
                var stored = await new R1PhysicalRunRecordBridge(database)
                    .PersistAsync(project, controller, binding, result, test.PhysicalEvidencePath);
                scanner.MarkRunCompleted(binding, stored.Value,
                    (binding.ProductionTestEnabled ? "真实生产检测 / " : "诊断检测 / ") +
                    result.Verdict + " · RunId 已持久化",
                    test.PhysicalEvidencePath);
            }
            catch (Exception ex)
            {
                scanner.MarkRunCompleted(binding, result.RunId,
                    "SQLITE_NOT_DURABLE: " + ex.Message +
                    " · 原始证据待核验，不能算作已入库", test.PhysicalEvidencePath);
            }
        }
        catch (Exception ex)
        {
            scanner.MarkRunCompleted(binding, null, "执行拒绝：" + ex.Message, null);
        }
    }

    private void DocumentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only one document host exists. Selection changes must NEVER recreate
        // a TEST runner, rebind an active device or change the running program.
        if (!ReferenceEquals(e.Source, DocumentTabs)) return;
    }
    private void PlannedSteps_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_test is not null && PlannedStepsList.SelectedItem is R1RunStepRow step)
        {
            _test.SelectedRunStep = step;
            PlannedStepsList.ScrollIntoView(step);
        }
    }
    private void ShowExplorer()
    {
        if (!_explorerHidden) return;
        ToggleExplorer_Click(this, new RoutedEventArgs());
    }
    private void ToggleExplorer_Click(object sender, RoutedEventArgs e)
    {
        _explorerHidden = !_explorerHidden;
        ExplorerColumn.Width = new GridLength(_explorerHidden ? 0 : 257);
        ExplorerSplitterColumn.Width = new GridLength(_explorerHidden ? 0 : 5);
        ExplorerPanel.Visibility = _explorerHidden ? Visibility.Collapsed : Visibility.Visible;
        ExplorerSplitter.Visibility = _explorerHidden ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ToggleBottom_Click(object sender, RoutedEventArgs e)
    {
        _logsExpanded = !_logsExpanded;
        LogRow.MinHeight = _logsExpanded ? 110 : 29;
        LogRow.Height = new GridLength(_logsExpanded ? 175 : 29);
        EvidenceTabs.Visibility = _logsExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)
        {
            e.Handled = true;
            ShowPalette();
            return;
        }
        // Conventional IDE navigation: keeps one document surface regardless
        // of which professional tool or TEST editor currently has keyboard focus.
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
        {
            if (DocumentTabs.SelectedItem is TabItem selected &&
                selected.Tag is string key && key != "run")
            {
                e.Handled = true;
                CloseDocument(key);
            }
            return;
        }
        if (e.Key == Key.Tab &&
            (Keyboard.Modifiers == ModifierKeys.Control ||
             Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            if (DocumentTabs.Items.Count <= 1) return;
            e.Handled = true;
            var direction = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            var index = DocumentTabs.SelectedIndex;
            DocumentTabs.SelectedIndex =
                (index + direction + DocumentTabs.Items.Count) % DocumentTabs.Items.Count;
        }
    }
    private void CommandPalette_Click(object sender, RoutedEventArgs e) => ShowPalette();
    private void ShowPalette()
    {
        var palette = new R1CommandPalette(OpenDocument) { Owner = this };
        palette.ShowDialog();
    }
    private void OpenRun_Click(object sender, RoutedEventArgs e) => OpenDocument("run");
    private void OpenTest_Click(object sender, RoutedEventArgs e) => OpenDocument("program");
    private void OpenStation_Click(object sender, RoutedEventArgs e) => OpenDocument("station");
    private void OpenCan_Click(object sender, RoutedEventArgs e) => OpenDocument("can");
    private void OpenDbc_Click(object sender, RoutedEventArgs e) => OpenDocument("dbc");
    private void OpenController_Click(object sender, RoutedEventArgs e) => OpenDocument("controller");
    private void OpenBench_Click(object sender, RoutedEventArgs e) => OpenDocument("bench");
    private void OpenWave_Click(object sender, RoutedEventArgs e) => OpenDocument("wave");
    private void OpenUds_Click(object sender, RoutedEventArgs e) => OpenDocument("uds");
    private void OpenFlash_Click(object sender, RoutedEventArgs e) => OpenDocument("flash");
    private void OpenProduction_Click(object sender, RoutedEventArgs e) => OpenDocument("scan");
    private void OpenRecords_Click(object sender, RoutedEventArgs e) => OpenDocument("records");
    private void OpenAudit_Click(object sender, RoutedEventArgs e) => OpenDocument("operations");
    private void FloatTest_Click(object sender, RoutedEventArgs e) => FloatDocument("program");
    private void FloatDbc_Click(object sender, RoutedEventArgs e) => FloatDocument("dbc");
    private void FloatWave_Click(object sender, RoutedEventArgs e) => FloatDocument("wave");
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static async Task DisposeSafelyAsync(string name, Func<Task> action)
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(4)); }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "AutoTest-R1-close-errors.log"),
                    $"{DateTimeOffset.Now:O} {name}: {ex}\n");
            }
            catch { /* a log failure must not trap the desktop process */ }
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closed || _closing) return;
        if (_benchModel?.KeyActions.IsActionRunning == true)
        {
            e.Cancel = true;
            MessageBox.Show(this,
                "真实按键动作正在执行。先在手动台架停止动作，避免退出时隐藏正在进行的真实输出。",
                "手动台架正在执行", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_flash?.IsPhysicalCan1Busy == true)
        {
            e.Cancel = true;
            if (!_closeAfterFlash)
            {
                _closeAfterFlash = true;
                _flash.RequestCloseAfterPhysicalStop();
            }
            return;
        }
        e.Cancel = true;
        _closing = true;
        IsEnabled = false;
        try
        {
            if (_views.TryGetValue("wave", out var w) && w is WaveWorkbenchView wave)
                wave.CancelArchiveRecording();
            foreach (var floating in _floating.Values.ToArray())
                floating.Close();
            if (_scannerView is not null)
                await DisposeSafelyAsync("COM scanner",
                    () => _scannerView.DisposeAsync().AsTask());
            if (_device is not null)
                await DisposeSafelyAsync("WinUSB controller",
                    () => _device.DisposeAsync().AsTask());
        }
        finally
        {
            try { _waveReplayHub?.Dispose(); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); }
            if (_workspaceEventsOwner is not null)
                _workspaceEventsOwner.WorkspaceAssetsChanged -= OnAssetsChanged;
            _closed = true;
            Close();
        }
    }
}
