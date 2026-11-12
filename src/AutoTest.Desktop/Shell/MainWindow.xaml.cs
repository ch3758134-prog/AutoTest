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
        ["program"] = "测试程序编辑",
        ["station"] = "设备与 CAN 诊断",
        ["dbc"] = "DBC 数据库",
        ["controller"] = "控制器与按键配置",
        ["bench"] = "手动台架",
        ["wave"] = "波形分析",
        ["uds"] = "UDS 诊断",
        ["flash"] = "CAN 烧录",
        ["scan"] = "扫码与生产配置",
        ["records"] = "运行记录与报告",
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
    private bool _connectionBusy;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindWorkspace();
        Loaded += (_, _) =>
        {
            BindWorkspace();
            if (DataContext is MainWindowViewModel vm)
                SetupWorkspace(vm);
            TryLoadReviewSampleOnStartup();
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
        {
            SetupWorkspace(_workspaceEventsOwner);
            TryLoadReviewSampleOnStartup();
        }
    }

    private bool _reviewSampleAttempted;

    private void TryLoadReviewSampleOnStartup()
    {
        // Prism can attach DataContext after Loaded. Start only after its real
        // PROJECT/G4 workspace exists, once, and only by explicit review flag.
        if (_reviewSampleAttempted || !IsLoaded ||
            DataContext is not MainWindowViewModel { G4: not null } ||
            !Environment.GetCommandLineArgs().Any(arg =>
                string.Equals(arg, "--ui-review", StringComparison.OrdinalIgnoreCase)))
            return;
        _reviewSampleAttempted = true;
        _ = LoadReviewSampleAsync();
    }

    private async Task LoadReviewSampleAsync()
    {
        if (DataContext is not MainWindowViewModel vm || vm.G4 is null) return;
        var reviewPath = Path.Combine(AppContext.BaseDirectory, "samples",
            "ui-review-epb", "EPB-UI-review.autotest-project.json");
        if (!File.Exists(reviewPath))
        {
            MessageBox.Show(this, "当前程序未包含配套示例工程。请核对编译输出中的 samples/ui-review-epb 文件夹；本次不会打开其他目录的旧工程。",
                "示例工程缺失", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await vm.OpenProjectAsync(reviewPath);
        if (!string.Equals(vm.Workspace.ProjectPath, Path.GetFullPath(reviewPath), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, vm.Diagnostic, "示例工程打开失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        // Same real PROJECT / LAB TEST / DBC data model, not an in-memory imitation.
        EnsureTestModel(vm.G4.Workspace).ReloadFromProject();
        OpenDocument("program");
    }

    private async void OpenReviewSample_Click(object sender, RoutedEventArgs e)
        => await LoadReviewSampleAsync();

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
        
        OpenDocument("program");
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
        var rawDetail = scan.MatchDetail ?? "";
        var readable = rawDetail.Contains("CATALOG_NOT_READY", StringComparison.OrdinalIgnoreCase)
            ? "请先设置扫码编号规则"
            : rawDetail;
        ScannerStatus.Text = "扫码：" + number + (string.IsNullOrWhiteSpace(readable) ? "" : " · " + readable);
        // Keep the raw diagnostic for service and offline trace analysis.
        ScannerStatus.ToolTip = "扫码状态：" + rawDetail;
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
        // The bench uses the SAME active PROJECT DBC database as DBC/WAVE.
        // Refresh its catalog without replacing unsaved raw CAN TX rows.
        _benchModel?.CanMessages.RefreshDbcCatalog();
    }

    /// <summary>Only one tab for each currently opened actual document/tool.</summary>
    private void OpenDocument(string key)
    {
        if (_closed) return;
        if (key == "project")
        {
            ShowExplorer();
            
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
    /// <summary>
    /// Independent tools receive the SAME visual IDE chrome, not a separate
    /// hardware session, project model, or editor tab. The tool itself remains
    /// a maximized top-level Window and retains all its native controls.
    /// </summary>
    private FrameworkElement BuildProfessionalChrome(string key, FrameworkElement view)
    {
        var vm = DataContext as MainWindowViewModel;
        Brush ColorBrush(string color) => new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color));
        var root = new Grid { Background = ColorBrush("#F4F8FD") };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(45) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

        var header = new DockPanel
        {
            Background = ColorBrush("#F6FAFF"),
            LastChildFill = true,
            Margin = new Thickness(0)
        };
        var brand = new TextBlock
        {
            Text = "▣  AutoTest", FontSize = 17, FontWeight = FontWeights.Bold,
            Foreground = ColorBrush("#263A54"), Margin = new Thickness(17, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(brand, Dock.Left);
        header.Children.Add(brand);
        var breadcrumb = new TextBlock
        {
            Text = " /  " + _titles[key], FontSize = 13, Foreground = ColorBrush("#617B97"),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(breadcrumb);
        Grid.SetRow(header, 0); root.Children.Add(header);

        Button ToolButton(string label, Action action, bool important = false)
        {
            var b = new Button
            {
                Content = label, MinHeight = 34, Padding = new Thickness(13, 6, 13, 6),
                Margin = new Thickness(0, 0, 5, 0),
                FontSize = 13
            };
            if (System.Windows.Application.Current.TryFindResource(
                important ? "AtPrimaryButton" : "AtQuietButton") is Style style)
                b.Style = style;
            b.Click += (_, _) => action();
            return b;
        }
        var bar = new DockPanel
        {
            Background = Brushes.White,
            LastChildFill = false,
            Margin = new Thickness(13, 0, 13, 0)
        };
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (vm is not null)
        {
            var open = ToolButton("打开工程", () =>
            {
                if (vm.OpenProjectCommand.CanExecute(null))
                    vm.OpenProjectCommand.Execute(null);
            });
            var save = ToolButton("保存工程", () =>
            {
                if (vm.SaveCommand.CanExecute(null))
                    vm.SaveCommand.Execute(null);
            });
            controls.Children.Add(open);
            controls.Children.Add(save);
        }
        controls.Children.Add(ToolButton("✎  编辑测试程序", () =>
        {
            OpenDocument("program");
            Activate();
        }, true));
        controls.Children.Add(ToolButton("▷  运行测试", () =>
        {
            OpenDocument("run");
            Activate();
        }));
        controls.Children.Add(ToolButton("▣  手动台架", () => OpenProfessionalToolWindow("bench")));
        var toolMenu = new Menu { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        var toolRoot = new MenuItem { Header = "◫  专业工具 ▾", FontSize = 13 };
        foreach (var entry in new[]
        {
            ("DBC 数据库编辑", "dbc"), ("UDS 诊断", "uds"),
            ("CAN 烧录", "flash"), ("波形分析", "wave"),
            ("控制器与按键配置", "controller"),
            ("扫码与生产", "scan"), ("运行记录", "records")
        })
        {
            var target = entry.Item2;
            var item = new MenuItem { Header = entry.Item1 };
            item.Click += (_, _) => OpenProfessionalToolWindow(target);
            toolRoot.Items.Add(item);
        }
        toolMenu.Items.Add(toolRoot);
        controls.Children.Add(toolMenu);
        DockPanel.SetDock(controls, Dock.Left);
        bar.Children.Add(controls);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var indicator = new TextBlock
        {
            FontSize = 12, Foreground = ColorBrush("#657D96"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        };
        indicator.SetBinding(TextBlock.TextProperty,
            new Binding("Text") { Source = StationStatus, Mode = BindingMode.OneWay });
        right.Children.Add(indicator);
        right.Children.Add(ToolButton("连接设备", () =>
            OpenStation_Click(this, new RoutedEventArgs())));
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        Grid.SetRow(bar, 1); root.Children.Add(bar);

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(275) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rail = new StackPanel
        {
            Background = ColorBrush("#F7FAFF"), Margin = new Thickness(4, 11, 4, 0)
        };
        rail.Children.Add(ToolButton("⌂", () => { Activate(); ShowExplorer(); }));
        rail.Children.Add(ToolButton("▤", () => { OpenDocument("program"); Activate(); }));
        rail.Children.Add(ToolButton("▷", () => { OpenDocument("run"); Activate(); }));
        rail.Children.Add(ToolButton("▣", () => OpenProfessionalToolWindow("bench")));
        rail.Children.Add(ToolButton("⌁", () => OpenProfessionalToolWindow("wave")));
        Grid.SetColumn(rail, 0); main.Children.Add(rail);

        if (vm is not null)
        {
            var explorer = new ProjectExplorerView(vm);
            explorer.OpenRequested += OpenDocument;
            Grid.SetColumn(explorer, 1);
            main.Children.Add(explorer);
        }
        var separator = new Border { Background = ColorBrush("#E6EDF6") };
        Grid.SetColumn(separator, 2); main.Children.Add(separator);
        var toolFrame = new Border
        {
            Background = ColorBrush("#F4F8FD"),
            BorderBrush = ColorBrush("#E1EAF5"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = view
        };
        Grid.SetColumn(toolFrame, 3); main.Children.Add(toolFrame);
        Grid.SetRow(main, 2); root.Children.Add(main);

        var footer = new DockPanel
        {
            Background = Brushes.White, Margin = new Thickness(15, 0, 15, 0)
        };
        var footerLeft = new TextBlock
        {
            Text = "AutoTest  ·  独立专业工具窗口  ·  与主工作台共享当前工程和设备",
            Foreground = ColorBrush("#778DA5"), FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(footerLeft);
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        return root;
    }

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
            Width = key == "bench" ? 1560 : 1380,
            Height = key == "bench" ? 900 : 840,
            MinWidth = 1220,
            MinHeight = 680,
            Background = System.Windows.Application.Current.TryFindResource("StudioBg") as Brush ?? Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Owner = this,
            ShowInTaskbar = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowState = WindowState.Maximized,
            Content = BuildProfessionalChrome(key, view)
        };
        tool.Closing += (_, e) =>
        {
            if (key == "bench" && (_benchModel?.KeyActions.IsActionRunning == true ||
                _benchModel?.CanMessages.IsPeriodicRunning == true))
            {
                e.Cancel = true;
                MessageBox.Show(tool,
                    "手动台架仍在发送真实 KEY_SIM 动作或 CAN1 周期帧。请先停止发送，再关闭窗口。",
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
            if (view.Parent is Border viewHost)
                viewHost.Child = null; // detach cached professional view before reopening
            tool.Content = null;
            _floating.Remove(key);
            if (key == "wave" && view is WaveWorkbenchView wave)
                wave.CancelArchiveRecording();
        };
        _floating[key] = tool;
        tool.Show();
    }

    private Window DialogOwnerFor(string toolKey) =>
        _floating.TryGetValue(toolKey, out var window) ? window : this;

    // Confirmation must belong to the window the operator is using. A modal
    // dialog owned by the hidden shell can be inaccessible behind the bench.
    private Window ActiveConnectionOwner() =>
        _floating.Values.FirstOrDefault(window => window.IsActive) ?? this;

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
                            MessageBox.Show(DialogOwnerFor("bench"),
                                (mask == 0 ? "松开全部真实按键？\n" : "发送当前真实按键开关组合？\n") +
                                $"按键板开关组合：0x{mask:X5}\n" +
                                "这会改变当前按键板输出；主控确认不代表 ECU 已识别。\n" +
                                "请确认接线安全后执行。",
                                "确认按键板操作",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes,
                        approveIgn: (on, generation) =>
                            MessageBox.Show(DialogOwnerFor("bench"),
                                (on ? "打开" : "关闭") + "真实点火信号（IGN）？\n" +
                                "此操作会改变主控的点火控制输出。\n" +
                                "主控确认不等于实际电压已测量，请确认接线和负载安全。",
                                "确认 IGN 输出",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes,
                        testRunning: () => _test?.Busy == true,
                        approveSolenoid: (action, generation) =>
                            MessageBox.Show(DialogOwnerFor("bench"),
                                "确认真实夹具电磁阀" + (action == 0 ? "升起" : "下落") + "？\n" +
                                "动作会改变夹具机械状态，请检查人员、工装与 ECU 位置。\n" +
                                "主控确认或回读不代表夹具机械位置已经验证。",
                                "确认夹具操作",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes,
                        projectPath: () => host.Workspace.ProjectPath,
                        projectId: () => host.Workspace.Project.Id.Value.ToString("D"),
                        approveKeyAction: (action, generation) =>
                            MessageBox.Show(DialogOwnerFor("bench"),
                                $"执行按键动作“{action.Name}”？\n" +
                                $"动作包含 {action.Steps.Count} 个步骤，按顺序向实际按键板发送开关组合。\n" +
                                "完成或取消后不会自动松开按键；实际输出电压仍需实测。",
                                "确认执行按键动作",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes,
                        approveCan: (message, generation, periodic) =>
                            MessageBox.Show(DialogOwnerFor("bench"),
                                $"向真实 ECU CAN1 发送报文“{message.Name}”？\n" +
                                $"CAN ID：0x{message.ArbitrationId:X}（{(message.Extended ? "扩展帧" : "标准帧")}）\n" +
                                $"数据：{message.DataHex}\n" +
                                (periodic
                                    ? $"按 {message.PeriodMs} ms 配置周期重复发送；主机计时，非硬实时，最多 10000 帧或 30 分钟。\n"
                                    : "只发送一次。\n") +
                                "请确认 ECU 接线及报文内容。主控确认不代表 ECU 已接收。",
                                "确认 CAN1 实际发送",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes,
                        projectName: () => host.Workspace.Project.Name,
                        approveCanBatch: (selected, generation) =>
                            MessageBox.Show(DialogOwnerFor("bench"),
                                $"向真实 ECU CAN1 周期发送 {selected.Count} 条已勾选报文？\n\n" +
                                string.Join("\n", selected.Take(6).Select(f =>
                                    $"0x{f.ArbitrationId:X} · {f.DataHex} · {f.PeriodMs} ms")) +
                                (selected.Count > 6 ? $"\n其余 {selected.Count - 6} 条已勾选报文也将发送。" : "") +
                                "\n\n全部报文按各自周期发送，由 Windows 主机计时，非硬实时。\n" +
                                "请确认报文配置和 ECU 接线；停止发送后不会改变按键或 IGN 状态。",
                                "确认开始 CAN1 周期发送",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) == MessageBoxResult.Yes,
                        projectDbc: () => host.Workspace.Database,
                        projectDbcRevision: () => host.Workspace.DbcAssetId is { } dbcId
                            ? host.Workspace.AssetToken(dbcId) : null),
                    () => station.Controller);
                station.Controller.ConnectionChanged += state =>
                {
                    if (!_closed && !Dispatcher.HasShutdownStarted)
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_benchModel is not null)
                            {
                                _ = _benchModel.RefreshConnectionAsync();
                                if (_views.TryGetValue("bench", out var benchView) && benchView.IsLoaded)
                                    _benchModel.CanMonitor.Start();
                            }
                        }));
                };
                var bench = new BenchWorkbenchView(_benchModel);
                bench.OpenStationRequested += (_, _) =>
                {
                    // Use the same direct device discovery from the main toolbar,
                    // never replace the operator bench with a device-debug page.
                    if (_device?.ViewModel.IsConnected != true)
                        OpenStation_Click(this, new RoutedEventArgs());
                };
                bench.OpenCanRequested += (_, _) => OpenDocument("can");
                bench.OpenDbcRequested += (_, _) => OpenDocument("dbc");
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
                        request => MessageBox.Show(DialogOwnerFor("uds"), request,
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
                Source = _device.ViewModel,
                Mode = BindingMode.OneWay
            });
        void Led()
        {
            var state = _device.ViewModel.StatusText;
            StationLed.Fill = _device.ViewModel.IsConnected ? Brushes.DodgerBlue : Brushes.SlateGray;
            StationConnectButton.Content = _device.ViewModel.IsConnected ? "断开设备" : "连接设备";
            StationConnectButton.IsEnabled = !_connectionBusy;
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
    private void ShowExplorer()
    {
        if (!_explorerHidden) return;
        ToggleExplorer_Click(this, new RoutedEventArgs());
    }
    private void ToggleExplorer_Click(object sender, RoutedEventArgs e)
    {
        _explorerHidden = !_explorerHidden;
        ExplorerColumn.Width = new GridLength(_explorerHidden ? 0 : 295);
        ExplorerSplitterColumn.Width = new GridLength(_explorerHidden ? 0 : 5);
        ExplorerPanel.Visibility = _explorerHidden ? Visibility.Collapsed : Visibility.Visible;
        ExplorerSplitter.Visibility = _explorerHidden ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ToggleBottom_Click(object sender, RoutedEventArgs e)
    {
        _logsExpanded = !_logsExpanded;
        LogRow.MinHeight = _logsExpanded ? 110 : 0;
        LogRow.Height = new GridLength(_logsExpanded ? 175 : 0);
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
    private async void OpenStation_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionBusy) return;
        var model = EnsureDevice().ViewModel;
        _connectionBusy = true;
        StationConnectButton.IsEnabled = false;
        StationStatus.Text = model.IsConnected ? "正在断开设备…" : "正在搜索设备…";
        try
        {
            if (model.IsConnected)
                await model.DisconnectFromShellAsync();
            else
                await model.ConnectFromShellAsync(ChoosePhysicalController, () =>
                    MessageBox.Show(ActiveConnectionOwner(),
                        "请确认主控、按键板和 ECU 的接线以及外部供电状态安全。\n" +
                        "连接只建立 USB/CAN 会话，不自动控制 IGN、按键、夹具或发送 CAN。\n" +
                        "是否连接当前主控板？",
                        "连接设备前的安全确认", MessageBoxButton.YesNo,
                        MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        }
        finally
        {
            _connectionBusy = false;
            StationConnectButton.IsEnabled = true;
            StationConnectButton.Content = model.IsConnected ? "断开设备" : "连接设备";
            StationStatus.Text = model.StatusText;
            if (!model.IsConnected && model.StatusText.Contains("操作失败", StringComparison.Ordinal))
                MessageBox.Show(ActiveConnectionOwner(), model.ResultText, "设备连接失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private AutoTest.Devices.Usb.WinUsbCandidate? ChoosePhysicalController(
        IReadOnlyList<AutoTest.Devices.Usb.WinUsbCandidate> candidates)
    {
        var dialog = new Window
        {
            Title = "选择主控设备", Owner = ActiveConnectionOwner(), Width = 540, Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new DockPanel { Margin = new Thickness(18) };
        var title = new TextBlock
        {
            Text = "发现多台可连接的主控板，请选择实际接线的设备：",
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 14
        };
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(actions, Dock.Bottom);
        var list = new ListBox { ItemsSource = candidates, DisplayMemberPath = "DeviceInstanceId" };
        var cancel = new Button { Content = "取消", MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) };
        var select = new Button { Content = "选择并连接", MinWidth = 108, IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        select.Click += (_, _) =>
        {
            if (list.SelectedItem is not null) dialog.DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(select);
        panel.Children.Add(actions);
        panel.Children.Add(list);
        dialog.Content = panel;
        return dialog.ShowDialog() == true
            ? list.SelectedItem as AutoTest.Devices.Usb.WinUsbCandidate : null;
    }
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
        if ((_benchModel?.KeyActions.IsActionRunning == true ||
                _benchModel?.CanMessages.IsPeriodicRunning == true))
        {
            e.Cancel = true;
            MessageBox.Show(this,
                "手动台架的真实按键动作或 CAN 周期发送仍在执行。请先停止，避免退出时隐藏实际输出。",
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
