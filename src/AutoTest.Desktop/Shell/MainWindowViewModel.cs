using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Configuration;
using AutoTest.Contracts;
using AutoTest.Devices.Can;
using System.Globalization;
using AutoTest.Specialists.Scan;
using System.IO;
using System.Windows.Controls;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Dbc;
using AutoTest.Desktop.Testing;
using AutoTest.Desktop.Diagnostics;
using System.Text.Json;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

public sealed class EditorTabModel(string key, string title, UserControl view, Func<bool> confirmClose)
{
    public string Key { get; } = key;
    public string Title { get; set; } = title;
    public bool IsPrimaryTab => Key is "home" or "config-overview";
    public UserControl View { get; } = view;
    public Func<bool> ConfirmClose { get; } = confirmClose;
    /// <summary>Right rail is the editor's only inspector, never a second copy.</summary>
    public ContentControl? Inspector => View switch
    {
        TestEditorView test => test.InspectorHost,
        TestProjectWorkbenchView project => project.InspectorHost,
        ProjectOverviewView home => home.InspectorHost,
        ProjectConfigurationOverviewView config => config.InspectorHost,
        _ => null
    };
    public object? Editor => View.DataContext;

    public CloseDraftDocument CloseDraft => View switch
    {
        TestEditorView view when Editor is TestEditorViewModel testVm =>
            new(Title, () => testVm.IsBusy, () => testVm.IsDirty || view.HasPendingInspectorDraft,
                view.SaveForCloseAsync),
        TestProjectWorkbenchView when Editor is TestProjectWorkbenchViewModel projectVm =>
            new(Title, () => projectVm.IsBusy, () => projectVm.IsDirty, projectVm.SaveForCloseAsync),
        DbcWorkbenchView when Editor is DbcWorkbenchViewModel dbcVm =>
            new(Title, () => dbcVm.IsBusy, () => dbcVm.IsDirty, dbcVm.SaveForCloseAsync),
        ConfigurationWorkbenchView when Editor is ConfigurationWorkbenchViewModel configVm =>
            new(Title, () => configVm.IsBusy, () => configVm.IsDirty, configVm.SaveForCloseAsync),
        ProjectConfigurationOverviewView configurationOverview =>
            new(Title, () => configurationOverview.IsBusy,
                () => configurationOverview.HasUnsavedDrafts,
                configurationOverview.SaveForCloseAsync),
        _ => new(Title, () => false, () => false, () => Task.FromResult(true))
    };
}

/// <summary>Only presentation state and UX commands. Project data remains in ProjectWorkspaceService.</summary>
public sealed class MainWindowViewModel : BindableBase
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly HardwareConnectionService _hardware;
    private readonly RealTestWorkbenchService _realTest;
    private readonly LiveDbcSignalService _liveSignals;
    private readonly KeyboardWedgeCapture _scanCapture;
    private readonly ProjectScanMatcher _scanMatcher;
    private readonly CancellationTokenSource _scanStop = new();
    private readonly Task _scanReader;
    private string? _activeEditorProjectPath;
    private string _hardwareStatus = "USB 未连接";
    private string _scanStatus = "扫码未开始";
    private bool _hardwareBusy;
    private string _canId = "";
    private string _canData = "";
    private string _canConfirmation = "";
    private string _canTxStatus = "仅允许操作员逐帧确认发送；发送结果以设备 ACK 为准。";
    private readonly NewProjectDialogService _newDialog;
    private readonly ProjectFileDialogService _fileDialogs;
    private readonly DbcEditorDialogs _dbcDialogs;
    private readonly TestEditorDialogs _testDialogs;
    private EditorTabModel? _activeEditorTab;
    private bool _closeGuardActive;
    private bool _savingActive;
    public ObservableCollection<EditorTabModel> OpenEditors { get; } = new();
    private readonly RecentProjectStore _recentProjects;
    private string _statusMessage = "尚未打开工程";
    private bool _isBusy;
    private ResourceTreeNode? _selectedResource;
    private readonly ProjectRunSelectionState _runSelections = new();

    public MainWindowViewModel(
        ProjectWorkspaceService workspace, NewProjectDialogService newDialog,
        ProjectFileDialogService fileDialogs, DbcEditorDialogs dbcDialogs,
        TestEditorDialogs testDialogs, RecentProjectStore recentProjects,
        HardwareConnectionService hardware, RealTestWorkbenchService realTest,
        LiveDbcSignalService liveSignals)
    {
        _workspace = workspace;
        _runSelections.Changed += RefreshProjectCounts;
        _hardware = hardware;
        _realTest = realTest;
        _liveSignals = liveSignals;
        var router = new BarcodeInputRouter();
        router.AttachMode(ScannerCaptureMode.KeyboardWedge, new InputPolicy(true, 512, "keyboard"));
        router.SetAdmissionGate(() => !_hardwareBusy);
        _scanCapture = new KeyboardWedgeCapture(router);
        _scanMatcher = new ProjectScanMatcher(_workspace);
        _scanReader = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in router.ObserveAsync(new InputPolicy(true, 512, "keyboard"),
                                   _scanStop.Token)) { /* Drain accepted ingress; UI updates from capture results. */ }
            }
            catch (OperationCanceledException) when (_scanStop.IsCancellationRequested) { }
        });
        _newDialog = newDialog;
        _fileDialogs = fileDialogs;
        _dbcDialogs = dbcDialogs;
        _testDialogs = testDialogs;
        _recentProjects = recentProjects;
        NewProjectCommand = new DelegateCommand(async () => await OpenNewProjectAsync(), () => !IsBusy);
        OpenProjectCommand = new DelegateCommand(async () => await OpenProjectAsync(), () => !IsBusy);
        NewTestProgramCommand = new DelegateCommand(async () => await CreateTestProgramAsync(),
            () => HasProject && !IsBusy);
        NewTestProjectCommand = new DelegateCommand(async () => await CreateTestProjectAsync(),
            () => HasProject && !IsBusy);
        ImportDbcCommand = new DelegateCommand(async () => await ImportDbcAsync(),
            () => HasProject && !IsBusy);
        ImportFirmwareCommand = new DelegateCommand(async () => await ImportFirmwareAsync(),
            () => HasProject && !IsBusy);
        ImportWaveCommand = new DelegateCommand(async () => await ImportWaveAsync(),
            () => HasProject && !IsBusy);
        CreateControllerConfigCommand = new DelegateCommand(
            async () => await CreateConfigurationAsync(ProjectWorkspaceService.ControllerKind),
            () => HasProject && !IsBusy);
        CreateWaveConfigCommand = new DelegateCommand(
            async () => await CreateConfigurationAsync(ProjectWorkspaceService.WaveSettingsKind),
            () => HasProject && !IsBusy);
        CreateFlashConfigCommand = new DelegateCommand(
            async () => await CreateConfigurationAsync(ProjectWorkspaceService.FlashSettingsKind),
            () => HasProject && !IsBusy);
        CreateScanConfigCommand = new DelegateCommand(
            async () => await CreateConfigurationAsync(ProjectWorkspaceService.ScanSettingsKind),
            () => HasProject && !IsBusy);
        CreateTcfCommand = new DelegateCommand(
            async () => await CreateConfigurationAsync(ProjectWorkspaceService.TestConfigurationKind),
            () => HasProject && !IsBusy);
        CreateTbcCommand = new DelegateCommand(
            async () => await CreateConfigurationAsync(ProjectWorkspaceService.BenchConfigurationKind),
            () => HasProject && !IsBusy);
        OpenConfigurationCommand = new DelegateCommand(async () => await OpenSelectedConfigurationAsync(),
            () => SelectedResourceIsConfiguration && !IsBusy);
        OpenDbcCommand = new DelegateCommand(async () => await OpenSelectedDbcAsync(),
            () => SelectedResourceIsDbc && !IsBusy);
        OpenTestProgramCommand = new DelegateCommand(async () => await OpenSelectedTestProgramAsync(),
            () => SelectedResourceIsTestProgram && !IsBusy);
        RenameResourceCommand = new DelegateCommand(async () => await RenameResourceAsync(),
            () => _selectedResource?.Asset is not null && !IsBusy);
        RemoveReferenceCommand = new DelegateCommand(async () => await RemoveReferenceAsync(),
            () => _selectedResource?.Asset is not null && !IsBusy);
        ConnectHardwareCommand = new DelegateCommand(async () => await ToggleHardwareAsync(),
            () => !_hardwareBusy);
        OpenCanMonitorCommand = new DelegateCommand(async () => await OpenCanMonitorAsync());
        TransmitCanCommand = new DelegateCommand(async () => await TransmitCanAsync(), () => !_hardwareBusy);
        SaveActiveCommand = new DelegateCommand(async () => await SaveActiveAsync());
        UndoActiveCommand = new DelegateCommand(UndoActive);
        RedoActiveCommand = new DelegateCommand(RedoActive);
        CheckActiveCommand = new DelegateCommand(CheckActive);
        OpenDbcToolCommand = new DelegateCommand(async () => await OpenDbcToolAsync());
        OpenFirstTestProjectCommand = new DelegateCommand(async () => await OpenFirstAssetAsync(
            ProjectWorkspaceService.TestProjectKind, CreateTestProjectAsync));
        OpenFirstTestProgramCommand = new DelegateCommand(async () => await OpenFirstAssetAsync(
            ProjectWorkspaceService.TestProgramKind, CreateTestProgramAsync));
        OpenProjectConfigurationOverviewCommand = new DelegateCommand(ShowProjectConfiguration);
        OpenTcfCommand = new DelegateCommand(async () => await OpenConfigurationKindAsync(
            ProjectWorkspaceService.TestConfigurationKind));
        OpenControllerCommand = new DelegateCommand(async () => await OpenConfigurationKindAsync(
            ProjectWorkspaceService.ControllerKind));
        OpenTbcCommand = new DelegateCommand(async () => await OpenConfigurationKindAsync(
            ProjectWorkspaceService.BenchConfigurationKind));
        OpenFirstConfigurationCommand = new DelegateCommand(async () =>
        {
            if (_workspace.Current is null) return;
            var first = _workspace.Current.Definition.Assets.FirstOrDefault(a =>
                a.Kind == ProjectWorkspaceService.TestConfigurationKind ||
                a.Kind == ProjectWorkspaceService.ControllerKind ||
                a.Kind == ProjectWorkspaceService.BenchConfigurationKind);
            if (first is null) { await CreateConfigurationAsync(ProjectWorkspaceService.TestConfigurationKind); return; }
            SelectAsset(first.Id);
            await OpenSelectedConfigurationAsync();
        });
        HelpCommand = new DelegateCommand(() =>
            MessageBox.Show(
                "工程资源：左侧点击打开。测试用例：Ctrl+Space 新增、F2 编辑名称、F9 断点、" +
                "Ctrl+F 查找、Ctrl+Z/Y 撤销/重做、Delete 删除、拖动调整顺序。" +
                "\n测试项目勾选只作用于本次检查，工程默认独立保存。" +
                "\n真实设备测试、生产与 Flash 尚未通过项目级预检，请勿将结构检查视为 ECU PASS。",
                "AutoTest · 编辑帮助", MessageBoxButton.OK, MessageBoxImage.Information));
        _hardware.EvidenceRecorded += line =>
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                HardwareStatus = line;
                StatusMessage = line;
            }));
        _liveSignals.SignalReceived += (name, value) =>
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var time = value.Observation?.Time.Utc.ToLocalTime().ToString("HH:mm:ss.fff") ?? "";
                var reading = value.Observation?.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
                DecodedCanRecords.Add(new DecodedCanRow(time, name, reading,
                    value.Observation?.Unit ?? "", value.Status.ToString()));
                if (DecodedCanRecords.Count > 500) DecodedCanRecords.RemoveAt(0);
            }));
        _hardware.FrameObserved += frame =>
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var f = frame.Frame;
                CanRecords.Add(new CanMonitorRow(
                    frame.Time.Utc.ToLocalTime().ToString("HH:mm:ss.fff"),
                    f.Role == BusRole.DutCan ? "CAN1 · DUT" : "CAN0 · 按键板",
                    "0x" + f.Id.ToString("X"), f.ExtendedId ? "扩展" : "标准",
                    f.Dlc, Convert.ToHexString(f.Data.ToArray()), frame.LostFrames));
                if (CanRecords.Count > 500) CanRecords.RemoveAt(0);
            }));
        _workspace.SelectedConfigurationsChanged += (_, _) =>
            RaisePropertyChanged(nameof(SelectedConfigurationSummary));
        _workspace.CurrentChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(SelectedConfigurationSummary));
            if (_activeEditorProjectPath is not null &&
                !string.Equals(_activeEditorProjectPath, _workspace.Current?.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
                ClearEditor();
            RaisePropertyChanged(nameof(HasProject));
            RaisePropertyChanged(nameof(CurrentProjectName));
            RaisePropertyChanged(nameof(CurrentProjectPath));
            RaisePropertyChanged(nameof(AssetCount));
            RaisePropertyChanged(nameof(TestProjectCount));
            RaisePropertyChanged(nameof(TestProgramCount));
            RaisePropertyChanged(nameof(ConfigurationCount));
            foreach (var projectTab in OpenEditors)
                if (projectTab.Editor is TestProjectWorkbenchViewModel projectEditor)
                    projectEditor.RefreshReferences();
            RebuildResourceTree();
            EnsureOverview();
            StatusMessage = _workspace.Current is { } current
                ? "工程已载入：" + current.ManifestPath
                : "尚未打开工程";
            if (_workspace.Current is { } opened)
            {
                try { _recentProjects.Remember(opened.ManifestPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                                   ArgumentException or JsonException)
                {
                    StatusMessage = "工程已打开，但无法保存最近工程记录：" + error.Message;
                }
            }
            RefreshCommands();
        };
        RebuildResourceTree();
        EnsureOverview();
    }

    public sealed record CanMonitorRow(string Time, string Port, string Id, string Format,
        byte Dlc, string Data, long Lost);
    public sealed record DecodedCanRow(string Time, string Target, string Value,
        string Unit, string Status);
    public ObservableCollection<CanMonitorRow> CanRecords { get; } = new();
    public ObservableCollection<DecodedCanRow> DecodedCanRecords { get; } = new();
    public ObservableCollection<ResourceTreeNode> ResourceNodes { get; } = new();
    public string HardwareStatus
    {
        get => _hardwareStatus;
        private set => SetProperty(ref _hardwareStatus, value);
    }
    public string ScanStatus
    {
        get => _scanStatus;
        private set => SetProperty(ref _scanStatus, value);
    }
    public string ConnectionButtonText => _hardwareBusy ? "正在操作…" :
        _hardware.HasSession ? "断开" : "连接";
    public DelegateCommand ConnectHardwareCommand { get; }
    public DelegateCommand OpenCanMonitorCommand { get; }
    public DelegateCommand TransmitCanCommand { get; }
    public DelegateCommand SaveActiveCommand { get; }
    public DelegateCommand UndoActiveCommand { get; }
    public DelegateCommand RedoActiveCommand { get; }
    public DelegateCommand CheckActiveCommand { get; }
    public DelegateCommand OpenDbcToolCommand { get; }
    public DelegateCommand HelpCommand { get; }
    public DelegateCommand OpenFirstTestProjectCommand { get; }
    public DelegateCommand OpenFirstTestProgramCommand { get; }
    public DelegateCommand OpenFirstConfigurationCommand { get; }
    public DelegateCommand OpenProjectConfigurationOverviewCommand { get; }
    public DelegateCommand OpenTcfCommand { get; }
    public DelegateCommand OpenControllerCommand { get; }
    public DelegateCommand OpenTbcCommand { get; }
    public int TestProjectCount => _workspace.Current?.Definition.Assets.Count(x =>
        x.Kind == ProjectWorkspaceService.TestProjectKind) ?? 0;
    public int TestProgramCount => _workspace.Current?.Definition.Assets.Count(x =>
        x.Kind == ProjectWorkspaceService.TestProgramKind) ?? 0;
    public int ConfigurationCount => _workspace.Current?.Definition.Assets.Count(x =>
        x.Kind is ProjectWorkspaceService.TestConfigurationKind or ProjectWorkspaceService.BenchConfigurationKind
            or ProjectWorkspaceService.ControllerKind or ProjectWorkspaceService.DbcKind) ?? 0;
    private async Task OpenFirstAssetAsync(string kind, Func<Task> create)
    {
        if (_workspace.Current is null) return;
        var first = _workspace.Current.Definition.Assets.FirstOrDefault(a => a.Kind == kind);
        if (first is null) { await create(); return; }
        SelectAsset(first.Id);
        if (kind == ProjectWorkspaceService.TestProjectKind) await OpenSelectedTestProjectAsync();
        else await OpenSelectedTestProgramAsync();
    }
    public void ShowOverview()
    {
        if (_workspace.Current is null) return;
        EnsureOverview();
        var home = OpenEditors.FirstOrDefault(x => x.Key == "home");
        if (home is not null) ActiveEditorTab = home;
    }

    public void ShowProjectConfiguration()
    {
        if (_workspace.Current is null)
        {
            StatusMessage = "请先打开测试工程再编辑配置。";
            return;
        }
        if (IsAlreadyOpen("config-overview")) return;
        var view = new ProjectConfigurationOverviewView(this, _workspace, _fileDialogs);
        ActivateEditor("config-overview", view, "工程配置", view.ConfirmClose);
    }

    private async Task OpenConfigurationKindAsync(string kind)
    {
        var asset = _workspace.Current?.Definition.Assets.FirstOrDefault(x => x.Kind == kind);
        if (asset is null) { await CreateConfigurationAsync(kind); return; }
        SelectAsset(asset.Id);
        await OpenSelectedConfigurationAsync();
    }

    private void EnsureOverview()
    {
        if (_workspace.Current is null || OpenEditors.Any(x => x.Key == "home")) return;
        ActivateEditor("home", new ProjectOverviewView(this), "工程概览", () => true);
    }
    private async Task OpenDbcToolAsync()
    {
        if (_workspace.Current is null)
        {
            StatusMessage = "请先打开测试工程，然后导入或编辑 DBC。";
            return;
        }
        if (!SelectedResourceIsDbc)
        {
            var first = _workspace.Current.Definition.Assets
                .FirstOrDefault(a => a.Kind == ProjectWorkspaceService.DbcKind);
            if (first is null) { await ImportDbcAsync(); return; }
            SelectAsset(first.Id);
        }
        await OpenSelectedDbcAsync();
    }
    private async Task SaveActiveAsync()
    {
        if (_savingActive) return;
        _savingActive = true;
        try
        {
            bool saved = ActiveEditorTab?.View switch
            {
                TestEditorView editor => await editor.SaveForCloseAsync(),
                TestProjectWorkbenchView project when project.DataContext is TestProjectWorkbenchViewModel projectModel
                    => await projectModel.SaveForCloseAsync(),
                DbcWorkbenchView dbc when dbc.DataContext is DbcWorkbenchViewModel dbcModel
                    => await dbcModel.SaveForCloseAsync(),
                ConfigurationWorkbenchView config when config.DataContext is ConfigurationWorkbenchViewModel configModel
                    => await configModel.SaveForCloseAsync(),
                ProjectConfigurationOverviewView overview => await overview.SaveForCloseAsync(),
                _ => false
            };
            StatusMessage = saved ? "当前文档的正式保存检查已通过。"
                : "保存未完成：请检查当前步骤属性、配置字段或编辑器错误提示。草稿已保留。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            StatusMessage = "保存异常，草稿已保留：" + error.Message;
            _fileDialogs.ShowError(StatusMessage);
        }
        finally { _savingActive = false; }
    }
    private void UndoActive()
    {
        if (ActiveEditorTab?.Editor is TestEditorViewModel editor &&
            editor.UndoCommand.CanExecute()) editor.UndoCommand.Execute();
        else StatusMessage = "此编辑页当前没有可撤销的步骤操作。";
    }
    private void RedoActive()
    {
        if (ActiveEditorTab?.Editor is TestEditorViewModel editor &&
            editor.RedoCommand.CanExecute()) editor.RedoCommand.Execute();
        else StatusMessage = "此编辑页当前没有可重做的步骤操作。";
    }
    private void CheckActive()
    {
        switch (ActiveEditorTab?.Editor)
        {
            case TestProjectWorkbenchViewModel project:
                project.PreflightSelectedCommand.Execute(); break;
            case TestEditorViewModel editor:
                editor.PreflightCommand.Execute(); break;
            default: StatusMessage = "请先打开测试项目或用例，再检查真实工程资源与 TEST 结构。"; break;
        }
    }
    public string CanId
    {
        get => _canId;
        set { if (SetProperty(ref _canId, value)) RaisePropertyChanged(nameof(ExpectedCanConfirmation)); }
    }
    public string CanData
    {
        get => _canData;
        set { if (SetProperty(ref _canData, value)) RaisePropertyChanged(nameof(ExpectedCanConfirmation)); }
    }
    public string CanConfirmation
    {
        get => _canConfirmation;
        set => SetProperty(ref _canConfirmation, value);
    }
    public string CanTxStatus
    {
        get => _canTxStatus;
        private set => SetProperty(ref _canTxStatus, value);
    }
    public string ExpectedCanConfirmation
    {
        get
        {
            try { return "AUTHORIZE CAN TX " +
                Convert.ToHexString(CanFrameCodec.ToUsbPayload(BuildCanFrame(),
                    new BusRoleMap(default, DutPort: 1, KeypadPort: 0))); }
            catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
            { return "请输入合法的标准帧 ID 和最多 8 字节 HEX 数据。"; }
        }
    }
    private CanFrame BuildCanFrame()
    {
        var text = CanId.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (!uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var id))
            throw new FormatException("CAN ID 必须是十六进制整数。");
        var bytes = Convert.FromHexString(string.Concat(CanData.Where(c => !char.IsWhiteSpace(c))));
        return CanFrame.Create(BusRole.DutCan, extended: false, id, bytes);
    }
    private async Task TransmitCanAsync()
    {
        if (_hardwareBusy) return;
        try
        {
            var frame = BuildCanFrame();
            if (CanConfirmation != ExpectedCanConfirmation)
            {
                CanTxStatus = "确认文本不匹配，物理发送已拦截。";
                return;
            }
            _hardwareBusy = true;
            ConnectHardwareCommand.RaiseCanExecuteChanged();
            TransmitCanCommand.RaiseCanExecuteChanged();
            await _hardware.SendCanOnceAsync(frame, CanConfirmation);
            CanTxStatus = "设备已 ACK；请核对 CAN 接收和 DUT 响应。";
        }
        catch (Exception error)
        {
            CanTxStatus = "发送失败或状态未知：" + error.Message;
        }
        finally
        {
            CanConfirmation = "";
            _hardwareBusy = false;
            ConnectHardwareCommand.RaiseCanExecuteChanged();
            TransmitCanCommand.RaiseCanExecuteChanged();
        }
    }

    public void CaptureScannerText(string text)
    {
        foreach (var character in text)
        {
            var result = _scanCapture.OnKeyInput(character, scannerFieldHasFocus: true);
            if (result is { Accepted: false }) ScanStatus = "扫码拒绝：" + result.Code;
        }
    }
    public async Task FinishScannerInputAsync()
    {
        var result = _scanCapture.CompleteScan();
        if (!result.Accepted || result.Event is null)
        {
            ScanStatus = "扫码拒绝：" + result.Code;
            return;
        }
        ScanStatus = "已采集：" + result.Event.Scan.Text + "，正在匹配工程配置…";
        try
        {
            var matched = await _scanMatcher.MatchAsync(result.Event);
            ScanStatus = matched.Accepted
                ? "匹配成功：" + matched.RecipeId + " · " + matched.Identity +
                    "（仅匹配，未执行测试）"
                : matched.Explanation;
        }
        catch (Exception error) when (error is ProjectDataException or IOException or
                                      UnauthorizedAccessException or ArgumentException or
                                      InvalidDataException or FormatException)
        {
            ScanStatus = "匹配拒绝：" + error.Message;
        }
    }
    private async Task ToggleHardwareAsync()
    {
        if (_hardwareBusy) return;
        _hardwareBusy = true;
        RaisePropertyChanged(nameof(ConnectionButtonText));
        ConnectHardwareCommand.RaiseCanExecuteChanged();
        try
        {
            if (_hardware.HasSession) await _hardware.DisconnectAsync();
            else await _hardware.ConnectAsync();
            HardwareStatus = _hardware.IsConnected ? "USB 主控已连接（只读检测通过）" : "USB 已断开";
        }
        catch (Exception error)
        {
            HardwareStatus = "USB 操作失败：" + error.Message;
            StatusMessage = HardwareStatus;
        }
        finally
        {
            _hardwareBusy = false;
            RaisePropertyChanged(nameof(ConnectionButtonText));
            ConnectHardwareCommand.RaiseCanExecuteChanged();
        }
    }
    public async Task ShutdownAsync()
    {
        _scanStop.Cancel();
        try { await _scanReader; }
        catch (OperationCanceledException) { }
        _scanStop.Dispose();
        try { await _hardware.DisposeAsync(); }
        catch (Exception error) { StatusMessage = "程序关闭期间主控断开失败：" + error.Message; }
    }

    private async Task OpenCanMonitorAsync()
    {
        if (IsAlreadyOpen("can|monitor")) return;
        if (!_realTest.IsRunning && _workspace.Current is not null)
        {
            try
            {
                var targets = await TestTargetCatalog.LoadAsync(_workspace);
                await _liveSignals.BindAsync(targets.Select(x => x.Mapping)
                    .Where(x => x.Kind == AutoTest.Testing.Authoring.TestTargetKind.Signal)
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First()));
                DecodedCanRecords.Clear();
            }
            catch (Exception ex)
            {
                StatusMessage = "DBC 实时解析未启用：" + ex.Message;
            }
        }
        ActivateEditor("can|monitor", new CanMonitorView(this), "CAN 监视", () => true);
    }

    public EditorTabModel? ActiveEditorTab
    {
        get => _activeEditorTab;
        set
        {
            if (value?.Editor is TestProjectWorkbenchViewModel projectEditor)
                projectEditor.RefreshReferences();
            if (!ReferenceEquals(_activeEditorTab, value) &&
                _activeEditorTab?.View is TestEditorView departing &&
                !departing.TryLeaveInspector())
            {
                RaisePropertyChanged(nameof(ActiveEditorTab));
                return;
            }
            if (SetProperty(ref _activeEditorTab, value))
            {
                RaisePropertyChanged(nameof(ActiveEditorView));
                RaisePropertyChanged(nameof(ActiveEditorTitle));
                RaisePropertyChanged(nameof(ActiveEditorInspector));
                RaisePropertyChanged(nameof(InspectorHeading));
                RaisePropertyChanged(nameof(IsOverviewActive));
                RaisePropertyChanged(nameof(IsProjectActive));
                RaisePropertyChanged(nameof(IsCaseActive));
                RaisePropertyChanged(nameof(IsConfigurationActive));
            }
        }
    }
    public UserControl? ActiveEditorView => ActiveEditorTab?.View;
    public ContentControl? ActiveEditorInspector => ActiveEditorTab?.Inspector;
    public bool IsOverviewActive => ActiveEditorTab?.Key == "home";
    public bool IsProjectActive => ActiveEditorTab?.Key.StartsWith("project|", StringComparison.Ordinal) == true;
    public bool IsCaseActive => ActiveEditorTab?.Key.StartsWith("test|", StringComparison.Ordinal) == true;
    public bool IsConfigurationActive => ActiveEditorTab?.Key == "config-overview" ||
        ActiveEditorTab?.Key.StartsWith("config|", StringComparison.Ordinal) == true;

    public string InspectorHeading => ActiveEditorTab?.View switch
    {
        TestEditorView => "步骤属性",
        TestProjectWorkbenchView => "运行选择",
        ProjectOverviewView => "对象信息",
        ConfigurationWorkbenchView => "配置关联",
        ProjectConfigurationOverviewView => "配置关联",
        _ => "对象信息"
    };
    public bool HasActiveEditor => OpenEditors.Count > 0;
    public bool NoOpenEditors => OpenEditors.Count == 0;
    public string ActiveEditorTitle => ActiveEditorTab?.Title ?? "";
    /// <summary>
    /// One Save/Discard/Cancel gate for app exit, document close and project switch.
    /// A pending TEST inspector is counted as a draft even when its StepNode has not
    /// yet been applied to the formal TEST document.
    /// </summary>
    public async Task<bool> PrepareEditorsForCloseAsync(IEnumerable<EditorTabModel>? subset = null)
    {
        if (_closeGuardActive)
        {
            StatusMessage = "已有关闭确认或保存操作正在进行，请先完成当前操作。";
            return false;
        }
        _closeGuardActive = true;
        try
        {
            var documents = (subset ?? OpenEditors).Select(x => x.CloseDraft).ToList();
            documents.Insert(0, new CloseDraftDocument("当前工程/设备操作",
                () => IsBusy || _savingActive || _hardwareBusy || _realTest.IsRunning,
                () => false, () => Task.FromResult(true)));
            return await CloseDraftGuard.PrepareAsync(documents, PromptDraftClose, ReportCloseBlocked);
        }
        finally { _closeGuardActive = false; }
    }

    private static CloseDraftDecision PromptDraftClose(IReadOnlyList<string> names)
    {
        var lines = string.Join("\n", names.Take(8).Select(x => "  • " + x));
        if (names.Count > 8) lines += "\n  …及另外 " + (names.Count - 8) + " 个文档";
        var answer = MessageBox.Show(Application.Current.MainWindow,
            "以下内容尚未保存（包括右侧尚未应用的步骤属性）：\n\n" + lines +
            "\n\n【是】应用并保存全部修改，再继续关闭" +
            "\n【否】放弃这些草稿，直接继续关闭" +
            "\n【取消】留在当前窗口继续编辑" +
            "\n\n保存失败或校验未通过时不会退出，也不会丢弃草稿。",
            "未保存修改 · 保存 / 放弃 / 取消",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        return answer switch
        {
            MessageBoxResult.Yes => CloseDraftDecision.Save,
            MessageBoxResult.No => CloseDraftDecision.Discard,
            _ => CloseDraftDecision.Cancel
        };
    }

    private void ReportCloseBlocked(string message)
    {
        StatusMessage = message;
        MessageBox.Show(Application.Current.MainWindow, message, "关闭已取消",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public async Task<bool> CloseEditorAsync(EditorTabModel tab)
    {
        if (!OpenEditors.Contains(tab) || !await PrepareEditorsForCloseAsync([tab])) return false;
        return CloseEditor(tab, alreadyConfirmed: true);
    }

    public bool CloseEditor(EditorTabModel tab, bool alreadyConfirmed = false)
    {
        if (!OpenEditors.Contains(tab) || (!alreadyConfirmed && !tab.ConfirmClose())) return false;
        var index = OpenEditors.IndexOf(tab);
        OpenEditors.Remove(tab);
        if (ReferenceEquals(ActiveEditorTab, tab))
            ActiveEditorTab = OpenEditors.Count == 0 ? null :
                OpenEditors[Math.Min(index, OpenEditors.Count - 1)];
        RaisePropertyChanged(nameof(HasActiveEditor));
        RaisePropertyChanged(nameof(NoOpenEditors));
        return true;
    }
    private bool IsAlreadyOpen(string key)
    {
        var opened = OpenEditors.FirstOrDefault(tab => tab.Key == key);
        if (opened is null) return false;
        ActiveEditorTab = opened;
        return true;
    }
    private void ActivateEditor(string key, UserControl editor, string title, Func<bool> confirmClose)
    {
        if (IsAlreadyOpen(key)) return;
        _activeEditorProjectPath = _workspace.Current?.ManifestPath;
        var tab = new EditorTabModel(key, title, editor, confirmClose);
        OpenEditors.Add(tab);
        ActiveEditorTab = tab;
        RaisePropertyChanged(nameof(HasActiveEditor));
        RaisePropertyChanged(nameof(NoOpenEditors));
    }
    private void ClearEditor()
    {
        OpenEditors.Clear();
        ActiveEditorTab = null;
        _activeEditorProjectPath = null;
        RaisePropertyChanged(nameof(HasActiveEditor));
        RaisePropertyChanged(nameof(NoOpenEditors));
    }
    public bool HasProject => _workspace.Current is not null;
    public string CurrentProjectName => _workspace.Current?.Definition.Name ?? "未打开工程";
    public string CurrentProjectPath => _workspace.Current?.ManifestPath ?? "";
    public int AssetCount => _workspace.Current?.Definition.Assets.Length ?? 0;
    public string SelectedConfigurationSummary => "TCF: " +
        _workspace.SelectedTestConfigurationName + "  ·  TBC: " +
        _workspace.SelectedBenchConfigurationName;
    public bool HasSelectedResource => _selectedResource is not null && HasProject;
    public bool SelectedResourceIsAsset => _selectedResource?.Asset is not null;
    public bool SelectedResourceIsDbc => string.Equals(_selectedResource?.Asset?.Kind,
        ProjectWorkspaceService.DbcKind, StringComparison.OrdinalIgnoreCase);
    public bool SelectedResourceIsTestProgram => string.Equals(_selectedResource?.Asset?.Kind,
        ProjectWorkspaceService.TestProgramKind, StringComparison.OrdinalIgnoreCase);
    public bool SelectedResourceIsTestProject => string.Equals(_selectedResource?.Asset?.Kind,
        ProjectWorkspaceService.TestProjectKind, StringComparison.OrdinalIgnoreCase);
    public bool SelectedResourceIsConfiguration => _selectedResource?.Asset is { } asset &&
        ProjectConfigurationCodec.IsConfiguration(asset.Kind);
    public string SelectedResourceTitle => _selectedResource?.Label ?? "";
    public string SelectedResourceDescription => _selectedResource?.Description ?? "";
    public string SelectedResourcePath => _selectedResource?.Asset?.RelativePath ?? "";
    public string SelectedResourceRevision => _selectedResource?.Asset?.Revision.ToString() ?? "";
    public string SelectedResourceKind => _selectedResource?.Asset?.Kind ?? "";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) RefreshCommands();
        }
    }

    public DelegateCommand NewProjectCommand { get; }
    public DelegateCommand OpenProjectCommand { get; }
    public DelegateCommand NewTestProgramCommand { get; }
    public DelegateCommand NewTestProjectCommand { get; }
    public DelegateCommand ImportDbcCommand { get; }
    public DelegateCommand ImportFirmwareCommand { get; }
    public DelegateCommand ImportWaveCommand { get; }
    public DelegateCommand CreateControllerConfigCommand { get; }
    public DelegateCommand CreateWaveConfigCommand { get; }
    public DelegateCommand CreateFlashConfigCommand { get; }
    public DelegateCommand CreateScanConfigCommand { get; }
    public DelegateCommand CreateTcfCommand { get; }
    public DelegateCommand CreateTbcCommand { get; }
    public DelegateCommand OpenConfigurationCommand { get; }
    public DelegateCommand OpenDbcCommand { get; }
    public DelegateCommand OpenTestProgramCommand { get; }
    public DelegateCommand RenameResourceCommand { get; }
    public DelegateCommand RemoveReferenceCommand { get; }

    public async Task RestoreLastProjectAsync()
    {
        if (HasProject || IsBusy) return;
        string? path;
        try { path = _recentProjects.GetLastProjectManifest(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          ArgumentException or JsonException)
        {
            StatusMessage = "无法读取上次工程记录：" + error.Message;
            return;
        }
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path))
        {
            StatusMessage = "上次工程文件不存在，请手动打开：" + path;
            return;
        }
        IsBusy = true;
        try
        {
            await _workspace.OpenAsync(path);
            StatusMessage = "已自动打开上次工程：" + path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          ArgumentException or ProjectDataException or
                                          InvalidDataException or JsonException)
        {
            StatusMessage = "上次工程打开失败，可手动选择：" + error.Message;
        }
        finally { IsBusy = false; }
    }

    // Resource selection can change while an editor is loading from disk.
    // Queue the last selected asset instead of dropping the single-click navigation.
    private bool _openingSelectedResource;
    private bool _selectedResourceChangedDuringOpen;
    public async Task OpenSelectedResourceAsync()
    {
        if (_openingSelectedResource)
        {
            _selectedResourceChangedDuringOpen = true;
            return;
        }
        if (IsBusy) return;
        _openingSelectedResource = true;
        try
        {
            do
            {
                _selectedResourceChangedDuringOpen = false;
                var assetId = _selectedResource?.Asset?.Id;
                if (assetId is null) return;
                if (SelectedResourceIsTestProgram) await OpenSelectedTestProgramAsync();
                else if (SelectedResourceIsTestProject) await OpenSelectedTestProjectAsync();
                else if (SelectedResourceIsDbc) await OpenSelectedDbcAsync();
                else if (SelectedResourceIsConfiguration) await OpenSelectedConfigurationAsync();
                if (_selectedResource?.Asset?.Id != assetId)
                    _selectedResourceChangedDuringOpen = true;
            } while (_selectedResourceChangedDuringOpen);
        }
        finally { _openingSelectedResource = false; }
    }

    public void SelectResource(ResourceTreeNode? item)
    {
        if (ReferenceEquals(_selectedResource, item)) return;
        if (_selectedResource is not null) _selectedResource.IsSelected = false;
        _selectedResource = item;
        if (item is not null) item.IsSelected = true;
        RaiseSelectedResourceChanges();
    }

    private void RaiseSelectedResourceChanges()
    {
        RaisePropertyChanged(nameof(HasSelectedResource));
        RaisePropertyChanged(nameof(SelectedResourceIsAsset));
        RaisePropertyChanged(nameof(SelectedResourceIsDbc));
        RaisePropertyChanged(nameof(SelectedResourceIsTestProgram));
        RaisePropertyChanged(nameof(SelectedResourceIsTestProject));
        RaisePropertyChanged(nameof(SelectedResourceIsConfiguration));
        RaisePropertyChanged(nameof(SelectedResourceTitle));
        RaisePropertyChanged(nameof(SelectedResourceDescription));
        RaisePropertyChanged(nameof(SelectedResourcePath));
        RaisePropertyChanged(nameof(SelectedResourceRevision));
        RaisePropertyChanged(nameof(SelectedResourceKind));
        RefreshCommands();
    }

    private static IEnumerable<ResourceTreeNode> AllNodes(IEnumerable<ResourceTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in AllNodes(node.Children)) yield return child;
        }
    }

    private void RefreshProjectCounts(Guid projectId)
    {
        var node = AllNodes(ResourceNodes).FirstOrDefault(n =>
            n.Role == "Project" && n.Asset?.Id.Value == projectId);
        if (node is not null) UpdateProjectCount(node);
    }

    private void RefreshUnassignedCases()
    {
        var current = _workspace.Current;
        var group = ResourceNodes.FirstOrDefault(x => x.Role == "Folder" &&
            x.Label == "未加入项目的用例");
        if (current is null || group is null) return;
        var assigned = ResourceNodes.Where(x => x.Role == "Project")
            .SelectMany(x => x.Children)
            .Where(x => x.Asset is not null).Select(x => x.Asset!.Id)
            .ToHashSet();
        var missing = current.Definition.Assets
            .Where(x => x.Kind == ProjectWorkspaceService.TestProgramKind &&
                !assigned.Contains(x.Id))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        group.Children.Clear();
        foreach (var entry in missing)
            group.Children.Add(new ResourceTreeNode(entry.Name, "", entry.RelativePath,
                entry.Kind, entry, role: "Case"));
        group.CountLabel = missing.Length.ToString();
    }

    private static void UpdateProjectCount(ResourceTreeNode node)
    {
        var rows = node.Children.Where(n => n.IsRunSelectable).ToArray();
        node.CountLabel = rows.Count(n => n.RunSelection?.IsSelected == true) +
            "/" + rows.Length;
    }

    private ResourceTreeNode CreateCaseLink(AssetEntry project, Guid itemId, AssetEntry? asset,
        string name, bool enabled, int order)
    {
        var choice = _runSelections.Get(project.Id.Value, itemId, enabled);
        return new ResourceTreeNode(name, "", "项目中的第 " + order + " 条执行引用 · 临时勾选",
            ProjectWorkspaceService.TestProgramKind, asset, asset is null, "Case", "", choice);
    }

    private void BindProjectNavigation(AssetEntry projectAsset, TestProjectWorkbenchViewModel editor)
    {
        void UpdateTree()
        {
            var node = AllNodes(ResourceNodes).FirstOrDefault(x =>
                x.Role == "Project" && x.Asset?.Id == projectAsset.Id);
            if (node is null) return;
            node.Children.Clear();
            foreach (var row in editor.Packages)
            {
                var asset = _workspace.Current?.Definition.Assets.FirstOrDefault(x =>
                    x.Id.Value == row.AssetId && x.Kind == ProjectWorkspaceService.TestProgramKind);
                node.Children.Add(CreateCaseLink(projectAsset, row.ItemId, asset,
                    row.Name, row.Enabled, row.Order));
            }
            UpdateProjectCount(node);
            RefreshUnassignedCases();
        }
        editor.OpenPackageRequested = packageId =>
        {
            if (_workspace.Current?.Definition.Assets.Any(x =>
                x.Id.Value == packageId && x.Kind == ProjectWorkspaceService.TestProgramKind) != true)
            {
                StatusMessage = "BLOCKED_RESOURCE：项目引用的测试用例不在当前工程中。";
                return;
            }
            SelectAsset(AssetId.Create(packageId));
            _ = OpenSelectedTestProgramAsync();
        };
        editor.PackageRowsChanged = UpdateTree;
        UpdateTree();
    }

    private async Task PopulateProjectCasesAsync(OpenProjectContext snapshot,
        AssetEntry project, ResourceTreeNode node)
    {
        try
        {
            var session = await _workspace.OpenTestProjectEditorAsync(project.Id);
            if (!ReferenceEquals(snapshot, _workspace.Current) ||
                !AllNodes(ResourceNodes).Contains(node) ||
                OpenEditors.Any(x => x.Key == "project|" + project.Id)) return;
            node.Children.Clear();
            var order = 0;
            foreach (var entry in session.Document.Packages)
            {
                var asset = snapshot.Definition.Assets.FirstOrDefault(x =>
                    x.Id.Value == entry.PackageAssetId &&
                    x.Kind == ProjectWorkspaceService.TestProgramKind);
                node.Children.Add(CreateCaseLink(project, entry.ItemId, asset,
                    asset?.Name ?? entry.DisplayName, entry.Enabled, ++order));
            }
            UpdateProjectCount(node);
            RefreshUnassignedCases();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException or ProjectDataException)
        {
            if (!ReferenceEquals(snapshot, _workspace.Current)) return;
            node.Children.Clear();
            node.Children.Add(new ResourceTreeNode("引用读取失败", "", error.Message,
                role: "Placeholder", isPlaceholder: true));
            node.CountLabel = "!";
        }
    }

    private void RebuildResourceTree()
    {
        var selectedId = _selectedResource?.Asset?.Id;
        ResourceNodes.Clear();
        _selectedResource = null;
        if (_workspace.Current is not { } current)
        {
            RaiseSelectedResourceChanges();
            return;
        }

        // HTML navigation has a visible project root, a project-first test section,
        // and a separate categorized asset section. Do not nest the latter under root.
        ResourceNodes.Add(new ResourceTreeNode(current.Definition.Name, "", current.ManifestPath,
            role: "Root"));
        ResourceNodes.Add(new ResourceTreeNode("自动测试", "", "测试项目与用例",
            ProjectWorkspaceService.TestProjectKind, role: "Section"));
        var assets = current.Definition.Assets;
        var projects = assets.Where(x => x.Kind == ProjectWorkspaceService.TestProjectKind)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        foreach (var project in projects)
        {
            var node = new ResourceTreeNode(project.Name, "", project.RelativePath,
                ProjectWorkspaceService.TestProjectKind, project, role: "Project", count: "…");
            ResourceNodes.Add(node);
            _ = PopulateProjectCasesAsync(current, project, node);
        }
        if (projects.Length == 0)
            ResourceNodes.Add(new ResourceTreeNode("暂无测试项目", "", "点击自动测试分区的＋添加",
                isPlaceholder: true, role: "Placeholder"));

        // Independently saved TEST assets remain visible without creating duplicate
        // business entities; case links refer to the exact same AssetId.
        var allCases = assets.Where(x => x.Kind == ProjectWorkspaceService.TestProgramKind)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var unused = new ResourceTreeNode("未加入项目的用例", "", "独立 TEST 资产",
            role: "Folder", count: allCases.Length.ToString());
        foreach (var entry in allCases)
            unused.Children.Add(new ResourceTreeNode(entry.Name, "", entry.RelativePath,
                entry.Kind, entry, role: "Case"));
        ResourceNodes.Add(unused);
        ResourceNodes.Add(new ResourceTreeNode("工程资源", "", "固定工程的配置及资产",
            role: "Section"));

        var categories = new[]
        {
            (ProjectWorkspaceService.DbcKind, "DBC 报文"),
            (ProjectWorkspaceService.ControllerKind, "按键配置"),
            (ProjectWorkspaceService.WaveKind, "波形记录"),
            (ProjectWorkspaceService.WaveSettingsKind, "波形判定配置"),
            (ProjectWorkspaceService.TestConfigurationKind, "被测系统配置 · TCF"),
            (ProjectWorkspaceService.BenchConfigurationKind, "台架配置 · TBC"),
            (ProjectWorkspaceService.FlashSettingsKind, "烧录配置"),
            (ProjectWorkspaceService.FirmwareKind, "固件资源"),
            (ProjectWorkspaceService.ScanSettingsKind, "扫码匹配配置")
        };
        foreach (var (kind, label) in categories)
        {
            var entries = assets.Where(x => x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            var group = new ResourceTreeNode(label, "", "实际工程资产", kind,
                role: "Folder", count: entries.Length.ToString());
            foreach (var entry in entries)
                group.Children.Add(ToAssetNode(entry));
            ResourceNodes.Add(group);
        }
        var defined = categories.Select(x => x.Item1)
            .Append(ProjectWorkspaceService.TestProjectKind)
            .Append(ProjectWorkspaceService.TestProgramKind)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = assets.Where(x => !defined.Contains(x.Kind)).ToArray();
        if (extras.Length > 0)
        {
            var group = new ResourceTreeNode("其他资源", "", "未归类工程资产",
                role: "Folder", count: extras.Length.ToString());
            foreach (var entry in extras) group.Children.Add(ToAssetNode(entry));
            ResourceNodes.Add(group);
        }

        if (selectedId is { } id)
        {
            _selectedResource = AllNodes(ResourceNodes).FirstOrDefault(x => x.Asset?.Id == id);
            if (_selectedResource is not null) _selectedResource.IsSelected = true;
        }
        RaiseSelectedResourceChanges();
    }

    private static ResourceTreeNode ToAssetNode(AssetEntry entry) => new(
        entry.Name, "", "正式工程资源 · " + entry.RelativePath + " · Revision " + entry.Revision,
        entry.Kind, entry, role: "Asset");

    private async Task OpenNewProjectAsync()
    {
        if (IsBusy || !await PrepareEditorsForCloseAsync()) return;
        IsBusy = true;
        try { _newDialog.ShowDialog(); }
        catch (Exception e) { StatusMessage = "新建工程窗口打开失败：" + e.Message; }
        finally { IsBusy = false; }
    }

    private async Task OpenProjectAsync()
    {
        var path = _fileDialogs.PickProjectManifest();
        if (path is null || !await PrepareEditorsForCloseAsync()) return;
        await RunOperationAsync(async () =>
        {
            // Do not drop the old editor tabs before the new manifest has
            // actually passed ProjectWorkspaceService validation/opening.
            // CurrentChanged clears the old tabs only after successful switch.
            await _workspace.OpenAsync(path);
            StatusMessage = "工程已打开：" + path;
        });
    }

    private async Task CreateTestProgramAsync()
    {
        if (!HasProject) return;
        var name = _fileDialogs.AskResourceName("新建测试程序", "新建测试程序");
        if (name is null) return;
        await RunOperationAsync(async () =>
        {
            var entry = await _workspace.CreateTestProgramAsync(name);
            SelectAsset(entry.Id);
            var session = await _workspace.OpenTestEditorAsync(entry.Id);
            var editor = new TestEditorViewModel(_workspace, _testDialogs, session, _realTest);
            ActivateEditor("test|" + entry.Id, new TestEditorView(editor),
                "测试程序 · " + entry.Name, editor.ConfirmClose);
            StatusMessage = "测试程序已创建并打开：" + entry.Name;
        });
    }

    private async Task CreateTestProjectAsync()
    {
        if (!HasProject) return;
        var name = _fileDialogs.AskResourceName("新建测试项目（Project）", "新建测试项目");
        if (name is null) return;
        await RunOperationAsync(async () =>
        {
            var entry = await _workspace.CreateTestProjectAsync(name);
            SelectAsset(entry.Id);
            var session = await _workspace.OpenTestProjectEditorAsync(entry.Id);
            var editor = new TestProjectWorkbenchViewModel(_workspace, _testDialogs, session, _runSelections);
            BindProjectNavigation(entry, editor);
            ActivateEditor("project|" + entry.Id, new TestProjectWorkbenchView(editor),
                "测试项目 · " + entry.Name, editor.ConfirmClose);
            StatusMessage = "测试项目已创建，请添加测试用例并设置顺序：" + entry.Name;
        });
    }

    private async Task ImportDbcAsync()
    {
        if (!HasProject) return;
        var path = _fileDialogs.PickDbcFile(_workspace.Current?.RootDirectory);
        if (path is null) return;
        await RunOperationAsync(async () =>
        {
            var entry = await _workspace.ImportDbcAsync(path);
            SelectAsset(entry.Id);
            var opened = await _workspace.OpenDbcEditorAsync(entry.Id);
            var editor = new DbcWorkbenchViewModel(_workspace, _dbcDialogs, opened);
            ActivateEditor("dbc|" + entry.Id, new DbcWorkbenchView(editor),
                "DBC · " + entry.Name, editor.ConfirmClose);
            StatusMessage = "DBC 已导入并打开：" + entry.Name;
        });
    }

    private async Task ImportFirmwareAsync()
    {
        if (!HasProject) return;
        var path = _fileDialogs.PickFirmwareFile(_workspace.Current?.RootDirectory);
        if (path is null) return;
        await RunOperationAsync(async () =>
        {
            var entry = await _workspace.ImportFirmwareAsync(path);
            SelectAsset(entry.Id);
            StatusMessage = "固件校验并登记为工程资源：" + entry.Name + "（尚未配置烧录流程）";
        });
    }

    private async Task ImportWaveAsync()
    {
        if (!HasProject) return;
        var path = _fileDialogs.PickWaveEvidenceFile(_workspace.Current?.RootDirectory);
        if (path is null) return;
        await RunOperationAsync(async () =>
        {
            var entry = await _workspace.ImportWaveEvidenceAsync(path);
            SelectAsset(entry.Id);
            StatusMessage = "波形证据校验并登记：" + entry.Name + "（不自动标为参考波形）";
        });
    }

    /// <summary>Shared real create/import path for the resource tree inline add action.</summary>
    public Task CreateFromCategoryAsync(ResourceTreeNode? node)
    {
        if (node is null || !node.IsCreatableCategory || !HasProject || IsBusy)
            return Task.CompletedTask;
        return node.Category switch
        {
            ProjectWorkspaceService.TestProgramKind => CreateTestProgramAsync(),
            ProjectWorkspaceService.TestProjectKind => CreateTestProjectAsync(),
            ProjectWorkspaceService.DbcKind => ImportDbcAsync(),
            ProjectWorkspaceService.FirmwareKind => ImportFirmwareAsync(),
            ProjectWorkspaceService.WaveKind => ImportWaveAsync(),
            ProjectWorkspaceService.ControllerKind => CreateConfigurationAsync(ProjectWorkspaceService.ControllerKind),
            ProjectWorkspaceService.WaveSettingsKind => CreateConfigurationAsync(ProjectWorkspaceService.WaveSettingsKind),
            ProjectWorkspaceService.FlashSettingsKind => CreateConfigurationAsync(ProjectWorkspaceService.FlashSettingsKind),
            ProjectWorkspaceService.ScanSettingsKind => CreateConfigurationAsync(ProjectWorkspaceService.ScanSettingsKind),
            ProjectWorkspaceService.TestConfigurationKind => CreateConfigurationAsync(ProjectWorkspaceService.TestConfigurationKind),
            ProjectWorkspaceService.BenchConfigurationKind => CreateConfigurationAsync(ProjectWorkspaceService.BenchConfigurationKind),
            _ => Task.CompletedTask
        };
    }

    private static bool IsInlineConfiguration(string kind) =>
        kind is ProjectWorkspaceService.TestConfigurationKind or
            ProjectWorkspaceService.ControllerKind or
            ProjectWorkspaceService.BenchConfigurationKind;

    private async Task CreateConfigurationAsync(string kind)
    {
        if (!HasProject) return;
        var display = kind switch
        {
            ProjectWorkspaceService.ControllerKind => "控制器与按键配置",
            ProjectWorkspaceService.WaveSettingsKind => "波形判定配置",
            ProjectWorkspaceService.FlashSettingsKind => "烧录配置",
            ProjectWorkspaceService.ScanSettingsKind => "扫码匹配配置",
            ProjectWorkspaceService.TestConfigurationKind => "TCF 被测系统配置",
            ProjectWorkspaceService.BenchConfigurationKind => "TBC 固定台架配置",
            _ => "工程配置"
        };
        var name = _fileDialogs.AskResourceName("新建" + display, "新建" + display);
        if (name is null) return;
        await RunOperationAsync(async () =>
        {
            var asset = await _workspace.CreateConfigurationAsync(kind, name);
            SelectAsset(asset.Id);
            var opened = await _workspace.OpenConfigurationAsync(asset.Id);
            if (IsInlineConfiguration(kind))
            {
                ShowProjectConfiguration();
                var overview = OpenEditors.First(x => x.Key == "config-overview").View
                    as ProjectConfigurationOverviewView;
                overview!.ShowSession(kind, opened);
            }
            else
            {
                var editor = new ConfigurationWorkbenchViewModel(_workspace, _fileDialogs, opened);
                ActivateEditor("config|" + asset.Id, new ConfigurationWorkbenchView(editor),
                    display + " · " + name, editor.ConfirmClose);
            }
            StatusMessage = "已创建并打开" + display + "：" + name;
        });
    }

    private async Task OpenSelectedConfigurationAsync()
    {
        if (!SelectedResourceIsConfiguration || _selectedResource?.Asset is not { } asset) return;
        if (!IsInlineConfiguration(asset.Kind) && IsAlreadyOpen("config|" + asset.Id)) return;
        await RunOperationAsync(async () =>
        {
            var opened = await _workspace.OpenConfigurationAsync(asset.Id);
            if (IsInlineConfiguration(asset.Kind))
            {
                ShowProjectConfiguration();
                var overview = OpenEditors.First(x => x.Key == "config-overview").View
                    as ProjectConfigurationOverviewView;
                overview!.ShowSession(asset.Kind, opened);
            }
            else
            {
                var editor = new ConfigurationWorkbenchViewModel(_workspace, _fileDialogs, opened);
                ActivateEditor("config|" + asset.Id, new ConfigurationWorkbenchView(editor),
                    editor.TypeLabel + " · " + asset.Name, editor.ConfirmClose);
            }
            StatusMessage = "已打开工程配置：" + asset.Name;
        });
    }

    private async Task OpenSelectedTestProjectAsync()
    {
        if (!SelectedResourceIsTestProject || _selectedResource?.Asset is not { } asset) return;
        if (IsAlreadyOpen("project|" + asset.Id)) return;
        await RunOperationAsync(async () =>
        {
            var session = await _workspace.OpenTestProjectEditorAsync(asset.Id);
            var editor = new TestProjectWorkbenchViewModel(_workspace, _testDialogs, session, _runSelections);
            BindProjectNavigation(asset, editor);
            ActivateEditor("project|" + asset.Id, new TestProjectWorkbenchView(editor),
                asset.Name, editor.ConfirmClose);
            StatusMessage = "已打开测试项目：" + asset.Name;
        });
    }

    private async Task OpenSelectedTestProgramAsync()
    {
        if (!SelectedResourceIsTestProgram || _selectedResource?.Asset is not { } asset) return;
        if (IsAlreadyOpen("test|" + asset.Id)) return;
        await RunOperationAsync(async () =>
        {
            var session = await _workspace.OpenTestEditorAsync(asset.Id);
            var editor = new TestEditorViewModel(_workspace, _testDialogs, session, _realTest);
            ActivateEditor("test|" + asset.Id, new TestEditorView(editor),
                asset.Name, editor.ConfirmClose);
            StatusMessage = "已打开测试程序：" + asset.Name;
        });
    }

    private async Task OpenSelectedDbcAsync()
    {
        if (!SelectedResourceIsDbc || _selectedResource?.Asset is not { } asset) return;
        if (IsAlreadyOpen("dbc|" + asset.Id)) return;
        await RunOperationAsync(async () =>
        {
            var opened = await _workspace.OpenDbcEditorAsync(asset.Id);
            var editor = new DbcWorkbenchViewModel(_workspace, _dbcDialogs, opened);
            ActivateEditor("dbc|" + asset.Id, new DbcWorkbenchView(editor),
                "DBC · " + asset.Name, editor.ConfirmClose);
            StatusMessage = "已打开 DBC：" + asset.Name;
        });
    }

    private async Task RenameResourceAsync()
    {
        var asset = _selectedResource?.Asset;
        if (asset is null) return;
        var name = _fileDialogs.AskResourceName("重命名工程资源", asset.Name);
        if (name is null || name == asset.Name) return;
        await RunOperationAsync(async () =>
        {
            await _workspace.RenameAssetAsync(asset.Id, name);
            SelectAsset(asset.Id);
            StatusMessage = "已更新资源显示名称：" + name;
        });
    }

    private async Task RemoveReferenceAsync()
    {
        var asset = _selectedResource?.Asset;
        if (asset is null) return;
        var editorsUsingAsset = OpenEditors.Where(tab =>
            tab.Key.EndsWith("|" + asset.Id, StringComparison.Ordinal)).ToArray();
        if (!await PrepareEditorsForCloseAsync(editorsUsingAsset)) return;
        if (!_fileDialogs.ConfirmRemoveReference(asset.Name)) return;
        await RunOperationAsync(async () =>
        {
            await _workspace.RemoveAssetReferenceAsync(asset.Id);
            foreach (var tab in editorsUsingAsset)
                CloseEditor(tab, alreadyConfirmed: true);
            SelectResource(null);
            StatusMessage = "已移除工程资源引用，磁盘原文件保留：" + asset.Name;
        });
    }

    private void SelectAsset(AssetId id)
    {
        SelectResource(AllNodes(ResourceNodes).FirstOrDefault(x => x.Asset?.Id == id));
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await operation(); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A WPF view constructor can throw XamlParseException/InvalidOperationException.
            // The previous narrow filter let those exceptions escape an async-void
            // DelegateCommand and terminate the whole desktop process.
            var log = UiExceptionReporter.Record("Resource operation / editor opening", e);
            StatusMessage = "操作失败：" + e.Message;
            var detail = "操作失败：" + e.Message +
                (log is null ? "\n异常日志无法写入，请保留该错误信息。" :
                    "\n完整异常堆栈已保存：" + log);
            try { _fileDialogs.ShowError(detail); }
            catch (Exception dialogError)
            {
                UiExceptionReporter.Record("Failed to display resource error dialog", dialogError);
            }
        }
        finally { IsBusy = false; }
    }

    private void RefreshCommands()
    {
        NewProjectCommand?.RaiseCanExecuteChanged();
        OpenProjectCommand?.RaiseCanExecuteChanged();
        NewTestProgramCommand?.RaiseCanExecuteChanged();
        NewTestProjectCommand?.RaiseCanExecuteChanged();
        ImportDbcCommand?.RaiseCanExecuteChanged();
        ImportFirmwareCommand?.RaiseCanExecuteChanged();
        ImportWaveCommand?.RaiseCanExecuteChanged();
        CreateControllerConfigCommand?.RaiseCanExecuteChanged();
        CreateWaveConfigCommand?.RaiseCanExecuteChanged();
        CreateFlashConfigCommand?.RaiseCanExecuteChanged();
        CreateScanConfigCommand?.RaiseCanExecuteChanged();
        CreateTcfCommand?.RaiseCanExecuteChanged();
        CreateTbcCommand?.RaiseCanExecuteChanged();
        OpenConfigurationCommand?.RaiseCanExecuteChanged();
        OpenDbcCommand?.RaiseCanExecuteChanged();
        OpenTestProgramCommand?.RaiseCanExecuteChanged();
        RenameResourceCommand?.RaiseCanExecuteChanged();
        RemoveReferenceCommand?.RaiseCanExecuteChanged();
    }
}
