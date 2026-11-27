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
    private readonly ProductionProjectCatalog _productionCatalog;
    private readonly Func<InstalledProductionPackage, Task<bool>> _activateCatalogPackage;
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
    private string _startRequestStatus = "";
    private string _inspectedNode = "选择用例或步骤";
    private string _inspectedStatus = "";
    public string InspectedNode { get => _inspectedNode; private set => SetProperty(ref _inspectedNode, value); }
    public string InspectedStatus { get => _inspectedStatus; private set => SetProperty(ref _inspectedStatus, value); }
    public bool HasInspectedNode => !string.IsNullOrWhiteSpace(InspectedStatus);
    public void SelectOperatorNode(object? selected)
    {
        switch (selected)
        {
            case AutomaticTestCase testCase:
                InspectedNode = testCase.Name;
                InspectedStatus = testCase.Children.Count + " 个步骤 · " + testCase.State;
                break;
            case AutomaticTestStep step:
                InspectedNode = step.Name;
                InspectedStatus = step.State;
                break;
            default:
                InspectedNode = "选择用例或步骤";
                InspectedStatus = "";
                break;
        }
        RaisePropertyChanged(nameof(HasInspectedNode));
    }

    public AutomaticTestHomeViewModel(AutomaticTestPlanService plans,
        HardwareConnectionService hardware,
        SerialScannerWorkbenchViewModel scanner,
        ProjectWorkspaceService engineering, RealTestWorkbenchService realTest,
        Action openEngineer,
        Action openScannerSettings, Func<bool>? isProductionWorkspace = null,
        ProductionProjectCatalog? productionCatalog = null,
        Func<InstalledProductionPackage, Task<bool>>? activateCatalogPackage = null)
    {
        _plans = plans;
        _hardware = hardware;
        _scanner = scanner;
        _engineering = engineering;
        _openEngineer = openEngineer;
        _openScannerSettings = openScannerSettings;
        _isProductionWorkspace = isProductionWorkspace ?? (() => true);
        _productionCatalog = productionCatalog ?? new ProductionProjectCatalog();
        _activateCatalogPackage = activateCatalogPackage ??
            (_ => Task.FromResult(false));
        ChoosePlanCommand = new DelegateCommand(() => _openEngineer());
        OpenScannerSettingsCommand = new DelegateCommand(() => _openScannerSettings());
        RefreshStationCommand = new DelegateCommand(() => UpdateConnection());
        InitializeOperatorExecution(realTest);
        RequestSoftwareStartCommand = new DelegateCommand(RequestSoftwareStart);
        _plans.Changed += OnPlanChanged;
        _hardware.ConnectionChanged += OnConnectionChanged;
        _hardware.EcuCanBitrateSelectionChanged += OnEcuCanBitrateChanged;
        _hardware.KeyRequestStateChanged += OnKeyRequestStateChanged;
        _scanner.PropertyChanged += OnScannerPropertyChanged;
        _scanner.CompleteBarcodeReceived += OnCompleteBarcodeReceived;
        _scanner.RawBarcodeObserved += OnRawBarcodeObserved;
        _scanner.IncompleteBarcodeObserved += OnIncompleteBarcodeObserved;
        _engineering.CurrentChanged += OnEngineeringProjectChanged;
        _ = RefreshProductionBatchAsync();
    }

    public ObservableCollection<AutomaticTestCase> Cases { get; } = new();
    public DelegateCommand ChoosePlanCommand { get; }
    public DelegateCommand OpenScannerSettingsCommand { get; }
    public DelegateCommand RefreshStationCommand { get; }
    public DelegateCommand RequestSoftwareStartCommand { get; }

    /// <summary>
    /// One real inspection screen using the shared full-project execution.
    /// No separate manufacturing admission approval or fake physical START.
    /// </summary>
    public string StartRequestStatus
    {
        get => _startRequestStatus;
        private set => SetProperty(ref _startRequestStatus, value);
    }

    public string ProductionHeadline =>
        IsOperatorRunning ? "正在检测 ECU" :
        _productionVerdict == "PASS" ? "检测合格" :
        _productionVerdict == "FAIL" ? "检测不合格" :
        _productionVerdict is "记录失败" or "检测未完成" ? "本次检测未完成" :
        !_scanner.IsConnected ? "等待扫码枪连接" :
        !_hardware.IsConnected ? "等待主控连接" :
        _plans.Current is null ? "等待扫码识别 ECU" :
        !_plans.Current.ResourceReady ? "当前工程需要修复" :
        !IdentityAccepted ? "请扫描本台 ECU 编号" :
        !CanBitrateReady ? "ECU CAN 码率未确认" :
        "ECU 已识别 · 可以开始检测";

    public string ProductionDisplayStage =>
        IsOperatorRunning ? OperatorRunState :
        _productionVerdict != "等待检测结果" ? "检测结果" :
        _prepared is null ? "扫码识别" : "等待 START";

    public string ProductionGuidance =>
        IsOperatorRunning ? OperatorCurrentCase + " · " + OperatorCurrentStep :
        _productionVerdict != "等待检测结果" ? "报告与详细证据请查看本次检测记录。" :
        _prepared is null ? _scanStatus :
        !CanBitrateReady ? _hardware.EcuCanBitrateStatus :
        "点击“开始检测”或按实体 START，系统使用同一检测流程。";

    public bool HasFormalExecutionFacts => _isProductionWorkspace() &&
        (IsOperatorRunning || _productionVerdict != "等待检测结果");
    public string ProductionCurrentCase => OperatorCurrentCase;
    public string ProductionCurrentStep => OperatorCurrentStep;
    public string ProductionStepStatus => OperatorStepStatus;
    public string ProductionMeasuredValue => OperatorActual;
    public string ProductionExpectedValue => OperatorExpected;
    public string ProductionFailure => OperatorFailure;
    public string ProductionFlashState => OperatorFlashState;
    public string ProductionTestState => OperatorRunState;
    public string ProductionRecordState => OperatorRecordState;
    public string ProductionAlert =>
        _scanAlertRequired ? _scanStatus :
        _productionVerdict is "检测未完成" or "记录失败" ? OperatorFailure : "";
    public string ProductionResultLabel => _productionVerdict;
    public string PhysicalStartStatus =>
        IsOperatorRunning ? "本次检测已启动" :
        _prepared is null ? "等待扫码" : "等待开始检测";
    public string StartInputHint => "开始检测";

    private void RequestSoftwareStart()
    {
        if (!CanStartOperatorVerification)
        {
            StartRequestStatus = _prepared is null
                ? "请先扫描 ECU 编号并匹配检测工程。"
                : !_hardware.IsConnected ? "主控 USB 尚未连接。"
                : !_hardware.IsEcuCanBitrateApplied ? "请确认 ECU CAN 波特率。"
                : IsOperatorRunning ? "当前 ECU 正在检测。"
                : "当前工程或设备状态不能开始本台检测。";
            return;
        }
        StartRequestStatus = "正在启动本台 ECU 检测";
        StartOperatorVerificationCommand.Execute();
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
        (!_isProductionWorkspace() && _draftPreview is { HasTestProject: true });
    public bool NeedsSetup => Cases.Count == 0;
    public string SetupIssue => _isProductionWorkspace()
        ? _plans.Current is not null ? _plans.Issue :
            string.IsNullOrWhiteSpace(_plans.Issue)
                ? "当前工程尚未确认用于正式生产。"
                : _plans.Issue
        : _plans.Current is not null ? _plans.Issue :
            _draftPreview is not null
                ? "工程可编辑，检测配置待完善：" + _draftPreview.Description
                : string.IsNullOrWhiteSpace(_draftIssue)
                    ? "尚未打开检测工程。进入开发与调试创建或打开 ECU 工程。"
                    : _draftIssue;
    public string ProjectName => _plans.Current?.Project.Definition.Name ??
        (_isProductionWorkspace() ? "正式检测工程未确认" :
            _draftPreview?.Project.Definition.Name ?? "未打开工程");
    public bool MainControllerConnected => _hardware.IsConnected;
    public string KeyRequestStatus => _hardware.KeyRequestStatus;
    public bool ScannerConnected => _scanner.IsConnected;
    public bool CanBitrateReady => _hardware.IsConnected &&
        _hardware.IsEcuCanBitrateApplied &&
        (_prepared is null ||
         _hardware.ConfirmedEcuCanBitrateKbps == _prepared.EcuCanKbps);
    public bool IdentityAccepted => _prepared?.AcceptedScannerObservation is { } observed &&
        (_isProductionWorkspace()
            ? observed.Source == FrozenScanSource.ProductionFixedMarker &&
              observed.PackageSha256.Length == 64 &&
              observed.RuleRevision > 0
            : observed.Source == FrozenScanSource.EngineeringRaw) &&
        string.Equals(observed.RawBarcode, _lastObservedBarcode,
            StringComparison.Ordinal) &&
        _scanner.IsConnected &&
        _hardware.IsConnected && _acceptedStationEpoch > 0 &&
        _hardware.CurrentStationEpoch == _acceptedStationEpoch &&
        string.Equals(observed.ScannerPort, _scanner.ConnectedPort,
            StringComparison.OrdinalIgnoreCase);
    /// <summary>Next engineering action, derived from actual readiness, not used for physical approval.</summary>
    public string EngineeringNextAction
    {
        get
        {
            if (IsOperatorRunning) return "测试进行中，可在下方查看当前步骤。";
            if (!_engineeringVerificationArmed &&
                !string.IsNullOrWhiteSpace(OperatorHtmlReportPath))
                return "测试已结束，可查看本次报告；再次测试请重新扫码。";
            if (_plans.Current is null) return "请先打开检测工程。";
            if (!_plans.Current.ResourceReady)
                return "检测工程需要检查，请修正测试用例或烧录资源。";
            if (!_hardware.IsConnected) return "请连接主控设备。";
            if (!_hardware.IsEcuCanBitrateApplied)
                return "请在主窗口确认 ECU CAN 波特率。";
            if (!_scanner.IsConnected) return "请连接扫码枪并扫描 ECU 编号。";
            if (_prepared is null) return "请扫描本台 ECU 编号。";
            if (_hardware.ConfirmedEcuCanBitrateKbps != _prepared.EcuCanKbps)
                return "CAN 波特率与工程配置不一致，请核对后重新扫码。";
            if (!CanStartOperatorVerification)
                return "请核对当前 ECU、工程和设备状态。";
            return "已准备就绪。点击“开始测试”并确认夹具与烧录操作。";
        }
    }

    public string ControllerLabel => MainControllerConnected ? "主控已连接" : "主控未连接";
    public string ScannerLabel => ScannerConnected ? "扫码已连接" : "扫码未连接";
    public string CanLabel => CanBitrateReady ? "CAN 已配置" : "CAN 待配置";
    public string ControllerDetail => MainControllerConnected
        ? "USB 协议已连接 · " + (_hardware.ConnectedDeviceIdentity ?? "设备已识别")
        : "主工位自动发现设备并受控重试，无需人工连接";
    public string ScannerDetail => ScannerConnected
        ? "实际端口：" + _scanner.ConnectedPort + " · 等待扫码"
        : "已绑定设备自动重连；首次使用或故障时打开扫码枪设置";
    public string CanDetail => _hardware.EcuCanBitrateStatus +
        (_prepared is not null && _hardware.ConfirmedEcuCanBitrateKbps != _prepared.EcuCanKbps
            ? " · 工程要求 " + _prepared.EcuCanKbps + "k"
            : "") + " · 不代表 ECU 已响应";
    public string StationReadiness =>
        !_hardware.IsConnected ? "主控未连接" :
        !_hardware.IsEcuCanBitrateApplied ? "CAN 波特率待确认" :
        !_scanner.IsConnected ? "扫码枪未连接" :
        IsOperatorRunning ? OperatorRunState :
        _prepared is null ? "等待扫码" :
        !CanBitrateReady ? "ECU CAN 与工程配置不一致" :
        "主控已连接 · 已匹配当前工程";
    public string IdentityLabel => IdentityAccepted ? "编号已核对" : "编号待核对";
    public string DutSerial => _dutSerial;
    public string ScanStatus => _scanStatus;
    public string ScannerDiagnostic => _scanner.Status;
    public string Status => _status;
    public string DeploymentStatus => _plans.Current is null
        ? _isProductionWorkspace()
            ? "生产工程未确认：请进入开发与调试的运行前检查"
            : _draftPreview is null
                ? "当前没有可用工程，请进入开发与调试"
                : "当前工程 · 检测配置待完善"
        : "使用当前已匹配工程";
    public int CaseCount => Cases.Count;
    public string DeviceStatus => (_hardware.IsConnected ? "主控已连接" : "主控未连接") +
        " · " + (_scanner.IsConnected ? "扫码枪已连接" : "扫码枪未连接");
    public string CurrentCase => "—";
    public string CurrentStep => "—";
    public string ActualValue => "—";
    public string ExpectedValue => "—";
    public string FailureReason => "—";
    public string ExecutionNotice => _plans.Current is null
        ? _isProductionWorkspace() || _draftPreview is null ? _plans.Issue
            : "仅供工程师预览：当前尚未确认正式生产工程。" +
              _draftPreview.Description
        : _plans.Current.ResourceIssues.Count != 0
            ? "当前工程有 " + _plans.Current.ResourceIssues.Count +
              " 项运行资源问题；请进入开发与调试定位。"
            : _prepared is null
                ? "当前工程已读取；需要实际扫码及工位预检。"
                : "扫码已匹配工程；点击开始检测进入真实夹具、FLASH、TEST 和记录流程。";

    public async Task RestoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (_isProductionWorkspace())
            {
                // Production has no dependence on the engineer's recent file.
                // A physical barcode is the only way to select a package.
                _plans.ClearForProductionScan();
            }
            else
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

    private void OnPlanChanged(object? sender, EventArgs e) =>
        OnUi(() =>
    {
        if (IsOperatorRunning)
        {
            // During real FLASH/TEST, the loaded case projection and frozen
            // resource map must never be replaced with another revision.
            // Stop safely and retain the existing run's failure evidence.
            AddOperatorEvent("工程资源或 TEST 配置在真实运行期间发生变更；请求停止并进行工位收尾。");
            _operatorStop?.Cancel();
            _operatorEngine.RequestStop();
            return;
        }
        _engineeringVerificationArmed = false;
        InvalidateScan("正式工程加载或更新后，需要重新核对本次 ECU 的真实扫码身份。");
        StartRequestStatus = "当前工程已更新：请重新完成扫码与工位核对；正式执行器尚未接通。";
        RebuildCases();
        UpdateConnection();
    });

    private void OnEngineeringProjectChanged(object? sender, EventArgs e) =>
        OnUi(() =>
        {
            // Engineering edits must not silently replace an installed
            // production package selected by the station scanner.
            if (_isProductionWorkspace()) return;
            if (IsOperatorRunning)
            {
                AddOperatorEvent("运行期间工程内容变化；请求停止，防止旧冻结资源与当前编辑混用。");
                _operatorStop?.Cancel();
                _operatorEngine.RequestStop();
                return;
            }
            // A persisted project edit can occur without switching the
            // operator screen. The previously scanned and frozen test/flash
            // assets must not survive a new manifest revision.
            var previous = _plans.Current?.Project;
            var actual = _engineering.Current;
            if (previous is not null && actual is not null &&
                (!string.Equals(previous.ManifestPath, actual.ManifestPath,
                    StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(previous.ManifestSha256, actual.ManifestSha256,
                    StringComparison.OrdinalIgnoreCase)))
            {
                InvalidateScan("工程资源已更新，旧扫码与烧录/TEST 冻结失效；请重新校验并扫码。");
                StartRequestStatus = "工程变更后必须重新完成正式资源检查与扫码。";
                _ = RestoreAsync();
                return;
            }
            if (previous is not null && actual is null)
                InvalidateScan("ECU 工程已关闭，原扫码及生产准备失效。");
            _ = RefreshDraftPreviewAsync();
        });

    private async Task RefreshDraftPreviewAsync()
    {
        if (IsOperatorRunning) return;
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
        if (revision != _previewVersion || IsOperatorRunning ||
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
        else if (!_isProductionWorkspace() && _draftPreview is { } draft)
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

    private void OnKeyRequestStateChanged() => OnUi(() =>
        RaisePropertyChanged(nameof(KeyRequestStatus)));

    private void OnEcuCanBitrateChanged(int targetKbps) => OnUi(() =>
    {
        if (IsOperatorRunning)
        {
            // A benign repeated 0x43 readback may publish a notification.
            // Cancel only when the actual frozen rate is no longer satisfied.
            if (_prepared is not { } pinned ||
                pinned.EcuCanKbps != targetKbps ||
                !_hardware.IsEcuCanBitrateApplied ||
                _hardware.ConfirmedEcuCanBitrateKbps != pinned.EcuCanKbps)
            {
                AddOperatorEvent("实体整组验证期间 ECU CAN 配置不再匹配冻结版本，" +
                    "请求停止；后续 FLASH/TEST 不再继承旧配置。");
                _operatorStop?.Cancel();
                _operatorEngine.RequestStop();
            }
            UpdateConnection();
            return;
        }
        if (_prepared is { } prepared &&
            (prepared.EcuCanKbps != targetKbps || !_hardware.IsEcuCanBitrateApplied))
            InvalidateScan("ECU CAN 配置在扫码后变化；旧测试准备已失效，请确认码率并重新扫码。");
        UpdateConnection();
    });

    private void OnConnectionChanged(long sessionEpoch)
    {
        // A barcode is an observation from ONE station session. A new USB
        // epoch (including reconnect) must never inherit the previous ECU's
        // prepared identity; pending async barcode checks are invalidated too.
        void RefreshSession()
        {
            // A reconnect is a NEW physical session. Cancel ongoing TEST
            // immediately; do not let a paused or waiting case inherit it.
            if (IsOperatorRunning)
            {
                _operatorStop?.Cancel();
                _operatorEngine.RequestStop();
                UpdateConnection();
                return;
            }
            if (!IsOperatorRunning &&
                (_prepared is not null || _dutSerial == "正在核对"))
                InvalidateScan("主控 USB 会话发生变化；请重新确认 ECU CAN 并扫描本台 ECU。");
            UpdateConnection();
        }
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) RefreshSession();
        else dispatcher.BeginInvoke(new Action(RefreshSession));
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
        RaisePropertyChanged(nameof(KeyRequestStatus));
        RaisePropertyChanged(nameof(ProductionHeadline));
        RaisePropertyChanged(nameof(ProductionDisplayStage));
        RaisePropertyChanged(nameof(ProductionGuidance));
        RaisePropertyChanged(nameof(ProductionResultLabel));
        RaisePropertyChanged(nameof(HasFormalExecutionFacts));
        RaisePropertyChanged(nameof(ProductionCurrentCase));
        RaisePropertyChanged(nameof(ProductionCurrentStep));
        RaisePropertyChanged(nameof(ProductionStepStatus));
        RaisePropertyChanged(nameof(ProductionMeasuredValue));
        RaisePropertyChanged(nameof(ProductionExpectedValue));
        RaisePropertyChanged(nameof(ProductionFailure));
        RaisePropertyChanged(nameof(ProductionFlashState));
        RaisePropertyChanged(nameof(ProductionTestState));
        RaisePropertyChanged(nameof(ProductionRecordState));
        RaisePropertyChanged(nameof(PhysicalStartStatus));
        RaisePropertyChanged(nameof(ProductionAlert));
        RaisePropertyChanged(nameof(StationReadiness));
        RaisePropertyChanged(nameof(EngineeringNextAction));
        RaisePropertyChanged(nameof(IdentityLabel));
        RaisePropertyChanged(nameof(HasPlan));
        RaisePropertyChanged(nameof(NeedsSetup));
        RaisePropertyChanged(nameof(SetupIssue));
        RaisePropertyChanged(nameof(ProjectName));
        RaisePropertyChanged(nameof(DutSerial));
        RaisePropertyChanged(nameof(ScanStatus));
        RaisePropertyChanged(nameof(LastObservedBarcode));
        RaisePropertyChanged(nameof(LastObservedAt));
        RaisePropertyChanged(nameof(ScannerDiagnostic));
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
        _hardware.EcuCanBitrateSelectionChanged -= OnEcuCanBitrateChanged;
        _hardware.KeyRequestStateChanged -= OnKeyRequestStateChanged;
        _scanner.PropertyChanged -= OnScannerPropertyChanged;
        _scanner.CompleteBarcodeReceived -= OnCompleteBarcodeReceived;
        _scanner.RawBarcodeObserved -= OnRawBarcodeObserved;
        _scanner.IncompleteBarcodeObserved -= OnIncompleteBarcodeObserved;
        _engineering.CurrentChanged -= OnEngineeringProjectChanged;
    }
}
