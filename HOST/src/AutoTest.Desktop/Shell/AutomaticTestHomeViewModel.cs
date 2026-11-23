using System.Collections.ObjectModel;
using AutoTest.Desktop.Projects;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using AutoTest.Desktop.Hardware;
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
    private readonly HardwareConnectionService _hardware;
    private readonly Action _openEngineer;
    private bool _busy;
    private string _status = "正在读取自动测试工程";
    private string _startRequestStatus = "请完成工程与工位准备。软件 START 和实体 START 共享同一检测启动入口。";

    public AutomaticTestHomeViewModel(AutomaticTestPlanService plans,
        HardwareConnectionService hardware,
        SerialScannerWorkbenchViewModel scanner, Action openEngineer)
    {
        _plans = plans;
        _hardware = hardware;
        _scanner = scanner;
        _openEngineer = openEngineer;
        ChoosePlanCommand = new DelegateCommand(() => _openEngineer());
        RequestSoftwareStartCommand = new DelegateCommand(RequestSoftwareStart);
        _plans.Changed += OnPlanChanged;
        _hardware.ConnectionChanged += OnConnectionChanged;
        _scanner.PropertyChanged += OnScannerPropertyChanged;
        _scanner.CompleteBarcodeReceived += OnCompleteBarcodeReceived;
    }

    public ObservableCollection<AutomaticTestCase> Cases { get; } = new();
    public DelegateCommand ChoosePlanCommand { get; }
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
    public string PhysicalStartStatus => "实体 START：未接线／未验证（已保留统一启动入口）";
    public string StartInputHint => "软件按钮或实体按键均可发起同一次检测；运行中与未复位时不会再次启动。";

    private void RequestSoftwareStart()
    {
        // R1 operator UX: START is always clickable. Until the production
        // FLASH -> TEST -> RECORD adapter is connected, clicking is a silent
        // no-op (no popup, no grey-out, no fake RUN or status mutation).
        // The one-shot gate in ProductionCommandHandler must remain the only
        // authority once physical execution is connected.
    }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value)) ChoosePlanCommand.RaiseCanExecuteChanged();
        }
    }
    public bool HasPlan => _plans.Current is not null;
    public bool NeedsSetup => !HasPlan || Cases.Count == 0;
    public string SetupIssue => _plans.Issue;
    public string ModelCode => _plans.Current?.Project.Definition.EcuModelCode is { Length: > 0 } code
        ? code : "未标定型号";
    public string DutSerial => _dutSerial;
    public string ScanStatus => _scanStatus;
    public string Status => _status;
    public string DeploymentStatus => _plans.Current is null
        ? "无有效工位部署"
        : "已核对工程部署 " + _plans.Current.DeploymentFingerprint[..12] +
          "…；实体工位和整机执行仍需验证";
    public int CaseCount => Cases.Count;
    public string DeviceStatus => (_hardware.IsConnected ? "主控已连接" : "主控未连接") +
        " · " + (_scanner.IsConnected ? "扫码枪已连接" : "扫码枪未连接");
    public string CurrentCase => "—";
    public string CurrentStep => "—";
    public string ActualValue => "—";
    public string ExpectedValue => "—";
    public string FailureReason => "—";
    public string ExecutionNotice => !HasPlan
        ? "本工位尚无有效工程部署。进入开发与调试，核对正式资源及工艺后部署。"
        : _plans.Current!.ResourceIssues.Count != 0
            ? "当前部署包含 " + _plans.Current.ResourceIssues.Count +
              " 项资源问题，请在工程与调试中定位修复。"
            : _prepared is null
                ? "部署资源已读取；请完成真实扫码和工位核验。"
                : "扫码身份及本次工程资源已冻结；夹具、START、前置烧录、TEST 与 RECORD 的正式执行仍待接线。";

    public async Task RestoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await _plans.RestoreAsync(); }
        finally { IsBusy = false; }
    }

    public async Task RefreshPlanAsync()
    {
        if (!HasPlan || IsBusy) return;
        await RestoreAsync(); // Reverify the operator plan, NOT the IDE's project.
    }

    public Task ChoosePlanAsync()
    {
        // The operator cannot create a second TEST plan or select a mutable
        // developer file. Deployment is explicitly controlled in the IDE.
        _openEngineer();
        return Task.CompletedTask;
    }

    private void OnPlanChanged(object? sender, EventArgs e)
    {
        InvalidateScan("部署已加载或变化，请重新扫描当前 ECU。");
        StartRequestStatus = "需完成真实扫码、工位预检及新的检测任务后才能启动。";
        Cases.Clear();
        if (_plans.Current is { } plan)
        {
            foreach (var item in plan.Cases)
                Cases.Add(item);
        }
        _status = !HasPlan ? "本工位尚未部署工程"
            : !_plans.Current!.ResourceReady ? "测试资源未就绪"
            : !_hardware.IsConnected ? "主控未连接"
            : "自动执行尚未接通";
        Notify();
    }

    private void OnConnectionChanged(long _)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) UpdateConnection();
        else dispatcher.BeginInvoke(new Action(UpdateConnection));
    }

    private void UpdateConnection()
    {
        _status = !HasPlan ? "本工位尚未部署工程"
            : !_plans.Current!.ResourceReady ? "测试资源未就绪"
            : !_hardware.IsConnected ? "主控未连接"
            : "自动执行尚未接通";
        Notify();
    }

    private void Notify()
    {
        RaisePropertyChanged(nameof(HasPlan));
        RaisePropertyChanged(nameof(NeedsSetup));
        RaisePropertyChanged(nameof(SetupIssue));
        RaisePropertyChanged(nameof(ModelCode));
        RaisePropertyChanged(nameof(DutSerial));
        RaisePropertyChanged(nameof(ScanStatus));
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
    }

    public void Dispose()
    {
        _plans.Changed -= OnPlanChanged;
        _hardware.ConnectionChanged -= OnConnectionChanged;
        _scanner.PropertyChanged -= OnScannerPropertyChanged;
        _scanner.CompleteBarcodeReceived -= OnCompleteBarcodeReceived;
    }
}
