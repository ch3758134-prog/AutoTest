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
        _hardware.EcuCanBitrateSelectionChanged += OnEcuCanBitrateChanged;
        _hardware.KeyRequestStateChanged += OnKeyRequestStateChanged;
        _scanner.PropertyChanged += OnScannerPropertyChanged;
        _scanner.CompleteBarcodeReceived += OnCompleteBarcodeReceived;
        _scanner.RawBarcodeObserved += OnRawBarcodeObserved;
        _scanner.IncompleteBarcodeObserved += OnIncompleteBarcodeObserved;
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
    // These are projections of existing project, scanner and station facts.
    // They never infer a physical START, a production RunId or a final PASS.
    public string ProductionHeadline =>
        _isProductionWorkspace() ? FormalAdmission.Headline :
        IsOperatorRunning ? "工程测试进行中" :
        _plans.Current is null
            ? _engineering.Current is null ? "工位未配置检测工程" : "检测工程核验未通过" :
        !_plans.Current.ResourceReady ? "检测工程资源未就绪" :
        !_hardware.IsConnected ? "等待主控连接" :
        !_hardware.IsEcuCanBitrateApplied ? "ECU CAN 码率未确认" :
        !_scanner.IsConnected ? "等待扫码枪连接" :
        _prepared is not null &&
        _hardware.ConfirmedEcuCanBitrateKbps != _prepared.EcuCanKbps
            ? "ECU CAN 与工程烧录配置不一致" :
        _prepared is null ? "等待 ECU 扫码" :
        "ECU 已识别 · 正式检测尚不可启动";

    public string ProductionDisplayStage =>
        _isProductionWorkspace() ? FormalAdmission.Stage :
        IsOperatorRunning ? OperatorRunState :
        _plans.Current is null || !_plans.Current.ResourceReady ? "工程预检" :
        !_hardware.IsConnected || !_hardware.IsEcuCanBitrateApplied ||
        !_scanner.IsConnected ||
        (_prepared is not null &&
         _hardware.ConfirmedEcuCanBitrateKbps != _prepared.EcuCanKbps)
            ? "设备预检" :
        _prepared is null ? "扫码识别" : "实体 START 待接线";

    public string ProductionGuidance =>
        _isProductionWorkspace() ? FormalAdmission.Guidance :
        IsOperatorRunning
            ? "仅显示已有工程验证事实；不会计入正式生产结果。"
            : _plans.Current is null
                ? _engineering.Current is null
                    ? "请由工程人员建立并保存对应型号的 ECU 检测工程。"
                    : "工程不能用于生产：" + _plans.Issue
            : !_plans.Current.ResourceReady
                ? "工程资源阻断：" +
                    (_plans.Current.ResourceIssues.FirstOrDefault() ??
                     "请工程人员修正 TEST、DBC、波形或烧录资源。")
            : !_hardware.IsConnected
                ? "主控未连接，请检查设备连接。"
            : !_hardware.IsEcuCanBitrateApplied
                ? "ECU CAN 波特率尚未完成主控回读确认。"
            : !_scanner.IsConnected
                ? "扫码枪未连接，请检查端口或连接设备。"
            : _prepared is not null &&
                _hardware.ConfirmedEcuCanBitrateKbps != _prepared.EcuCanKbps
                ? "已冻结工程要求 ECU CAN " + _prepared.EcuCanKbps +
                  " kbit/s，主控当前 " +
                  _hardware.ConfirmedEcuCanBitrateKbps +
                  " kbit/s；工程师须核对并应用目标码率后重新扫码。"
            : _prepared is null
                ? "请扫描本台 ECU 编号。"
                : "扫码身份及工程资源已核对；实体 START 和正式生产归档尚未接线。";

    /// <summary>
    /// One authoritative production admission projection. A scanner match,
    /// USB ACK or test case plan never authorizes a physical START.
    /// Hardware START and formal RECORD inputs remain false until their
    /// actual, reviewed physical/durable adapters are wired.
    /// </summary>
    private ProductionAdmissionResult FormalAdmission
    {
        get
        {
            var plan = _plans.Current;
            var prepared = _prepared;
            var observed = prepared?.AcceptedScannerObservation;
            var projectSame = plan is not null && prepared is not null &&
                plan.Project.Definition.Id.Value == prepared.ProjectId &&
                string.Equals(plan.Project.ManifestPath, prepared.ManifestPath,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(plan.Project.ManifestSha256, prepared.ManifestSha256,
                    StringComparison.OrdinalIgnoreCase);
            return ProductionAdmissionReview.Evaluate(new ProductionAdmissionFacts(
                plan is not null,
                plan?.ResourceReady == true,
                _hardware.IsConnected && _hardware.CurrentStationEpoch > 0,
                _hardware.IsEcuCanBitrateApplied,
                _scanner.IsConnected,
                observed is not null,
                projectSame,
                prepared is not null && _acceptedStationEpoch > 0 &&
                    _acceptedStationEpoch == _hardware.CurrentStationEpoch,
                observed is not null && string.Equals(observed.ScannerPort,
                    _scanner.ConnectedPort, StringComparison.OrdinalIgnoreCase),
                prepared is not null &&
                    _hardware.ConfirmedEcuCanBitrateKbps == prepared.EcuCanKbps &&
                    _hardware.RequestedEcuCanBitrateKbps == prepared.EcuCanKbps,
                TrustedPhysicalStartConnected: false,
                VerifiedInspectionWriterConnected: false));
        }
    }

    // Formal station facts do not exist until the physical START and
    // production RECORD adapter are wired. Never project the last development
    // verification into the production operator's active DUT.
    // The read-only operator must not display fabricated case/measurement
    // placeholders before a real formal Inspection event source exists.
    // The production adapter must raise this when formal StepOccurrence
    // evidence becomes available; engineering verifications do not count.
    public bool HasFormalExecutionFacts => false;
    public string ProductionCurrentCase => "—";
    public string ProductionCurrentStep => "—";
    public string ProductionStepStatus => "正式检测未开始";
    public string ProductionMeasuredValue => "—";
    public string ProductionExpectedValue => "—";
    public string ProductionFailure => "";
    public string ProductionFlashState => "尚未烧录";
    public string ProductionTestState => "尚未测试";
    public string ProductionRecordState => "尚无生产检测记录";
    // UI severity is a separate fact; do not classify errors by translated text.
    public string ProductionAlert =>
        _prepared is null && _scanAlertRequired ? _scanStatus : "";

    public string ProductionResultLabel =>
        "等待检测结果";

    public string PhysicalStartStatus => "启动按钮未接入";
    public string StartInputHint =>
        "工程实测可使用软件启动；正式生产放行尚需实体 START 与生产记录验收。";

    private void RequestSoftwareStart()
    {
        if (_isProductionWorkspace())
        {
            StartRequestStatus = "正式生产禁止软件 START：必须由可信主控实体 START 事件及正式 Inspection 记录链路发起。";
            return;
        }
        // A readiness query is not a START. Until a verified production
        // adapter and durable RunId record are wired, NEVER issue CAN/FLASH,
        // claim a START edge, fabricate progress or return PASS.
        if (_plans.Current is not { } plan)
            StartRequestStatus = "请先打开 ECU 工程。";
        else if (!plan.ResourceReady)
            StartRequestStatus = "工程未就绪，请检查用例和资源。";
        else if (!_scanner.IsConnected)
            StartRequestStatus = "扫码枪未连接。";
        else if (_prepared is null || string.IsNullOrWhiteSpace(_lastIdentity))
            StartRequestStatus = "请扫描当前 ECU 编号。";
        else if (!_hardware.IsConnected)
            StartRequestStatus = "主控未连接。";
        else if (!_hardware.IsEcuCanBitrateApplied)
            StartRequestStatus = "ECU CAN 波特率待确认：" + _hardware.EcuCanBitrateStatus;
        else
            StartRequestStatus = "工程准备检查通过；正式生产功能尚待验收。";
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
    public string StationReadiness => _isProductionWorkspace()
        ? "工位检查 " + FormalAdmission.SatisfiedChecks + "/" +
          FormalAdmission.TotalChecks + " · 启动条件未满足" : !_hardware.IsConnected ? "等待主控连接" :
        !_hardware.IsEcuCanBitrateApplied ? "CAN 波特率待确认" :
        !_scanner.IsConnected ? "等待扫码枪" :
        _plans.Current is null ? "请打开工程" :
        !_plans.Current.ResourceReady ? "工程未就绪" :
        _prepared is null ? "等待扫码" :
        _hardware.ConfirmedEcuCanBitrateKbps != _prepared.EcuCanKbps
            ? "工程/ECU CAN 速率不符" :
        IsOperatorRunning ? "工程实测进行中" :
        _isProductionWorkspace() ? "已核对扫码；实体 START 未接线" :
        "工程实测可启动";
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
        : "使用已确认工程 · 实体 START/RECORD 尚待接入";
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
                : "扫码与工程资源已核对；真实夹具、START、FLASH、TEST 和 RECORD 的整机执行尚未接通。";

    public async Task RestoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _plans.RestoreAsync(_engineering.Current,
                requireProductionDeployment: _isProductionWorkspace());
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
