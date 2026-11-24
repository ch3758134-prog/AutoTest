using System.Collections.ObjectModel;
using AutoTest.Desktop.Projects;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using AutoTest.Desktop.Hardware;
using AutoTest.Desktop.Testing;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Operator-facing AUTOMATIC TEST view. The plan is selected and restored here,
/// independently of the developer IDE's selected project. No TEST run or CAN
/// action is issued by this UI-only iteration.
/// </summary>
public sealed partial class AutomaticTestHomeViewModel : BindableBase, IDisposable
{
    private readonly AutomaticTestPlanService _plans;
    private readonly ProjectWorkspaceService _engineering;
    private readonly HardwareConnectionService _hardware;
    private readonly SerialScannerWorkbenchViewModel _scanner;
    private readonly Action _openEngineer;
    private readonly Action _openScannerSettings;
    private readonly Func<bool> _isProductionWorkspace;
    private bool _busy;
    private string _status = "正在读取检测工程";
    private string _usbAutoStatus = "启动后自动连接主控 USB";
    public string UsbAutoStatus
    {
        get => _usbAutoStatus;
        private set => SetProperty(ref _usbAutoStatus, value);
    }
    public void PublishUsbAutoStatus(string message)
    {
        OnUi(() => UsbAutoStatus = message);
    }
    private EngineeringTestProjectPreview? _draftPreview;
    private string _draftIssue = "";
    private long _previewVersion;
    private string _startRequestStatus = "正式生产执行器尚未接通；可检查工程、扫码和工位的具体准备问题。";
    private string _inspectedNode = "点击左侧用例或展开步骤，查看本工程的完整检测结构。";
    private string _inspectedStatus = "只读结构预览，未执行任何实体检测。";
    public string InspectedNode { get => _inspectedNode; private set => SetProperty(ref _inspectedNode, value); }
    public string InspectedStatus { get => _inspectedStatus; private set => SetProperty(ref _inspectedStatus, value); }
    public void SelectOperatorNode(object? selected)
    {
        switch (selected)
        {
            case AutomaticTestCase testCase:
                InspectedNode = testCase.Name;
                InspectedStatus = "测试用例 · " + testCase.Children.Count +
                    " 个顶层步骤 · " + testCase.State + "。实际结果仅能由正式执行器产生。";
                break;
            case AutomaticTestStep step:
                InspectedNode = step.Name;
                InspectedStatus = "步骤 · " + step.State +
                    "。点击只是查看结构，不会更改工程或发送硬件命令。";
                break;
            default:
                InspectedNode = "点击左侧用例或展开步骤，查看本工程的完整检测结构。";
                InspectedStatus = "只读结构预览，未执行任何实体检测。";
                break;
        }
    }

    public AutomaticTestHomeViewModel(AutomaticTestPlanService plans,
        HardwareConnectionService hardware,
        SerialScannerWorkbenchViewModel scanner,
        ProjectWorkspaceService engineering, RealTestWorkbenchService realTest,
        Action openEngineer,
        Action openScannerSettings, Func<bool>? isProductionWorkspace = null)
    {
        _plans = plans;
        _hardware = hardware;
        _scanner = scanner;
        _engineering = engineering;
        _openEngineer = openEngineer;
        _openScannerSettings = openScannerSettings;
        _isProductionWorkspace = isProductionWorkspace ?? (() => true);
        ChoosePlanCommand = new DelegateCommand(() => _openEngineer());
        OpenScannerSettingsCommand = new DelegateCommand(() => _openScannerSettings());
        RefreshStationCommand = new DelegateCommand(() => UpdateConnection());
        InitializeOperatorExecution(realTest);
        RequestSoftwareStartCommand = new DelegateCommand(RequestSoftwareStart);
        _plans.Changed += OnPlanChanged;
        _hardware.ConnectionChanged += OnConnectionChanged;
        _scanner.PropertyChanged += OnScannerPropertyChanged;
        _scanner.CompleteBarcodeReceived += OnCompleteBarcodeReceived;
        _engineering.CurrentChanged += OnEngineeringProjectChanged;
    }

    public ObservableCollection<AutomaticTestCase> Cases { get; } = new();
    public DelegateCommand ChoosePlanCommand { get; }
    public DelegateCommand OpenScannerSettingsCommand { get; }
    public DelegateCommand RefreshStationCommand { get; }
    public DelegateCommand RequestSoftwareStartCommand { get; }

    /// <summary>
    /// This operator home does not yet own a verified production execution
    /// adapter. START is visible, but must refuse until that adapter is wired;
    /// it cannot present fake progress or bypass the production gate.
    /// </summary>
    public string StartRequestStatus
    {
        get => _startRequestStatus;
        private set => SetProperty(ref _startRequestStatus, value);
    }
    public string PhysicalStartStatus => "正式检测尚不可启动";
    public string StartInputHint =>
        "实体 START 输入及完整生产执行链尚未接通；当前仅支持配置检查和扫码核对。";

    private void RequestSoftwareStart()
    {
        // A readiness query is not a START. Until a verified production
        // adapter and durable RunId record are wired, NEVER issue CAN/FLASH,
        // claim a START edge, fabricate progress or return PASS.
        if (_plans.Current is not { } plan)
            StartRequestStatus = "不能启动：尚未打开 ECU 工程。请进入开发与调试完成配置并保存。";
        else if (!plan.ResourceReady)
            StartRequestStatus = "不能启动：当前工程存在资源或有序用例问题，请先修复相关配置。";
        else if (!_scanner.IsConnected)
            StartRequestStatus = "不能启动：工位扫码枪尚未建立真实串口会话。";
        else if (_prepared is null || string.IsNullOrWhiteSpace(_lastIdentity))
            StartRequestStatus = "不能启动：尚未接受本次 ECU 的真实扫码并冻结完整工程资源。";
        else if (!_hardware.IsConnected)
            StartRequestStatus = "不能启动：USB 主控未连接。";
        else if (!_hardware.IsEcuCanBitrateApplied)
            StartRequestStatus = "不能启动：ECU CAN 波特率尚未确认应用，" +
                _hardware.EcuCanBitrateStatus;
        else
            StartRequestStatus = "配置与身份已核对，但尚缺真实工位准备、" +
                "软件/实体 START 仲裁、FLASH→全量 TEST→RECORD 的生产执行适配器。" +
                "本按钮不会假执行或生成 PASS；请继续在工程与调试中进行真实专项验证。";
    }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value)) ChoosePlanCommand.RaiseCanExecuteChanged();
        }
    }
    public bool HasPlan => _plans.Current is not null ||
        _draftPreview is { HasTestProject: true };
    public bool NeedsSetup => Cases.Count == 0;
    public string SetupIssue => _plans.Current is not null ? _plans.Issue :
        _draftPreview is not null
            ? "工程可编辑，检测配置待完善：" + _draftPreview.Description
            : string.IsNullOrWhiteSpace(_draftIssue)
                ? "尚未打开检测工程。进入开发与调试创建或打开 ECU 工程。"
                : _draftIssue;
    public string ModelCode => (_plans.Current?.Project.Definition.EcuModelCode ??
            _draftPreview?.Project.Definition.EcuModelCode) is { Length: > 0 } code
        ? code : "未设置";
    public string ProjectName => _plans.Current?.Project.Definition.Name ??
        _draftPreview?.Project.Definition.Name ?? "未打开工程";
    public bool MainControllerConnected => _hardware.IsConnected;
    public bool ScannerConnected => _scanner.IsConnected;
    public bool CanBitrateReady => _hardware.IsConnected && _hardware.IsEcuCanBitrateApplied;
    public bool IdentityAccepted => _prepared is not null;
    public string ControllerLabel => MainControllerConnected ? "主控 USB 已连接" :
        "主控 USB 自动连接中";
    public string ScannerLabel => ScannerConnected ? "扫码枪已连接" : "扫码枪等待连接";
    public string CanLabel => !MainControllerConnected ? "CAN 码率待确认" :
        CanBitrateReady ? "CAN 码率已确认" : "CAN 码率未确认";
    public string ControllerDetail => MainControllerConnected
        ? "USB 协议已连接 · " + (_hardware.ConnectedDeviceIdentity ?? "设备已识别")
        : "主工位自动发现设备并受控重试，无需人工连接";
    public string ScannerDetail => ScannerConnected
        ? "实际端口：" + _scanner.ConnectedPort + " · 等待扫码"
        : "已绑定设备自动重连；首次使用或故障时打开扫码枪设置";
    public string CanDetail => _hardware.EcuCanBitrateStatus +
        " · 此项仅代表主控配置，不代表 ECU 已响应";
    public string StationReadiness => !_hardware.IsConnected ? "设备准备 · 等待主控 USB" :
        !_hardware.IsEcuCanBitrateApplied ? "设备准备 · CAN 码率待确认" :
        !_scanner.IsConnected ? "设备准备 · 扫码枪未连接" :
        _plans.Current is null ? "检测准备 · 无正式检测工程" :
        !_plans.Current.ResourceReady ? "检测准备 · 工程资源未就绪" :
        _prepared is null ? "等待本次 ECU 扫码" :
        "扫码与设备预检完成 · 正式生产执行链尚未接通";
    public string IdentityLabel => IdentityAccepted ? "编号已核对" : "编号待核对";
    public string DutSerial => _dutSerial;
    public string ScanStatus => _scanStatus;
    public string Status => _status;
    public string DeploymentStatus => _plans.Current is null
        ? _draftPreview is null
            ? "当前没有可用工程，请进入开发与调试"
            : "当前工程 · 检测配置待完善"
        : "使用当前工程 · 实体工位与完整执行待接线";
    public int CaseCount => Cases.Count;
    public string DeviceStatus => (_hardware.IsConnected ? "主控已连接" : "主控未连接") +
        " · " + (_scanner.IsConnected ? "扫码枪已连接" : "扫码枪未连接");
    public string CurrentCase => "—";
    public string CurrentStep => "—";
    public string ActualValue => "—";
    public string ExpectedValue => "—";
    public string FailureReason => "—";
    public string ExecutionNotice => _plans.Current is null
        ? _draftPreview is null ? _plans.Issue
            : "已显示当前编辑工程的用例（不会发送硬件指令）。" +
              _draftPreview.Description +
              "需要完成前置烧录等检测配置才能进入正式检测。"
        : _plans.Current.ResourceIssues.Count != 0
            ? "当前工程有 " + _plans.Current.ResourceIssues.Count +
              " 项运行资源问题；请进入开发与调试定位。"
            : _prepared is null
                ? "当前工程已读取；需要实际扫码及工位预检。"
                : "扫码与工程资源已核对；真实夹具、START、FLASH、TEST 和 RECORD 的整机执行尚未接通。";

    public async Task RestoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _plans.RestoreAsync(_engineering.Current);
            await RefreshDraftPreviewAsync();
        }
        finally { IsBusy = false; }
    }

    public async Task RefreshPlanAsync()
    {
        if (!HasPlan || IsBusy) return;
        await RestoreAsync(); // Reload the current saved ECU project.
    }

    public Task ChoosePlanAsync()
    {
        // The operator uses the ECU project edited in the engineering workspace.
        _openEngineer();
        return Task.CompletedTask;
    }

    private void OnPlanChanged(object? sender, EventArgs e)
    {
        InvalidateScan("正式工程加载或更新后，需要重新核对本次 ECU 的真实扫码身份。");
        StartRequestStatus = "当前工程已更新：请重新完成扫码与工位核对；正式执行器尚未接通。";
        RebuildCases();
        UpdateConnection();
    }

    private void OnEngineeringProjectChanged(object? sender, EventArgs e) =>
        OnUi(() => { _ = RefreshDraftPreviewAsync(); });

    private async Task RefreshDraftPreviewAsync()
    {
        var revision = Interlocked.Increment(ref _previewVersion);
        var opened = _engineering.Current;
        EngineeringTestProjectPreview? preview = null;
        string error = "";
        if (opened is not null)
        {
            try
            {
                preview = await EngineeringTestProjectPreviewReader.ReadAsync(opened);
            }
            catch (Exception failure) when (failure is IOException or
                InvalidDataException or UnauthorizedAccessException or ArgumentException or
                FormatException or NotSupportedException or
                AutoTest.Projects.Project.ProjectDataException)
            {
                error = "当前工程的测试用例无法读取：" + failure.Message;
            }
        }
        if (revision != _previewVersion ||
            !ReferenceEquals(_engineering.Current, opened)) return;
        _draftPreview = preview;
        _draftIssue = error;
        RebuildCases();
        UpdateConnection();
    }

    private void RebuildCases()
    {
        Cases.Clear();
        SelectOperatorNode(null);
        if (_plans.Current is { } deployed)
        {
            foreach (var item in deployed.Cases) Cases.Add(item);
        }
        else if (_draftPreview is { } draft)
        {
            for (var i = 0; i < draft.Cases.Count; i++)
            {
                var item = draft.Cases[i];
                Cases.Add(new AutomaticTestCase((i + 1).ToString("D2"),
                    item.Name, default,
                    !item.ReferenceCurrent ? "引用需修复" :
                    item.Included ? "未执行" : "未纳入正式检测"));
            }
        }
    }

    private void OnConnectionChanged(long _)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) UpdateConnection();
        else dispatcher.BeginInvoke(new Action(UpdateConnection));
    }

    private void UpdateConnection()
    {
        _status = StationReadiness;
        Notify();
    }

    private void Notify()
    {
        RaisePropertyChanged(nameof(ProjectName));
        RaisePropertyChanged(nameof(MainControllerConnected));
        RaisePropertyChanged(nameof(ScannerConnected));
        RaisePropertyChanged(nameof(CanBitrateReady));
        RaisePropertyChanged(nameof(IdentityAccepted));
        RaisePropertyChanged(nameof(ControllerLabel));
        RaisePropertyChanged(nameof(ScannerLabel));
        RaisePropertyChanged(nameof(CanLabel));
        RaisePropertyChanged(nameof(ControllerDetail));
        RaisePropertyChanged(nameof(ScannerDetail));
        RaisePropertyChanged(nameof(CanDetail));
        RaisePropertyChanged(nameof(StationReadiness));
        RaisePropertyChanged(nameof(IdentityLabel));
        RaisePropertyChanged(nameof(HasPlan));
        RaisePropertyChanged(nameof(NeedsSetup));
        RaisePropertyChanged(nameof(SetupIssue));
        RaisePropertyChanged(nameof(ModelCode));
        RaisePropertyChanged(nameof(DutSerial));
        RaisePropertyChanged(nameof(ScanStatus));
        RaisePropertyChanged(nameof(ScanTraceSummary));
        RaisePropertyChanged(nameof(Status));
        RaisePropertyChanged(nameof(DeploymentStatus));
        RaisePropertyChanged(nameof(DeviceStatus));
        RaisePropertyChanged(nameof(CaseCount));
        RaisePropertyChanged(nameof(CurrentCase));
        RaisePropertyChanged(nameof(CurrentStep));
        RaisePropertyChanged(nameof(ActualValue));
        RaisePropertyChanged(nameof(ExpectedValue));
        RaisePropertyChanged(nameof(FailureReason));
        RaisePropertyChanged(nameof(ExecutionNotice));
        RefreshOperatorCommands();
    }

    public void Dispose()
    {
        DisposeOperatorExecution();
        _plans.Changed -= OnPlanChanged;
        _hardware.ConnectionChanged -= OnConnectionChanged;
        _scanner.PropertyChanged -= OnScannerPropertyChanged;
        _scanner.CompleteBarcodeReceived -= OnCompleteBarcodeReceived;
        _engineering.CurrentChanged -= OnEngineeringProjectChanged;
    }
}
