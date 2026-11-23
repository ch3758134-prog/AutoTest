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
    private readonly ProjectWorkspaceService _engineering;
    private readonly HardwareConnectionService _hardware;
    private readonly SerialScannerWorkbenchViewModel _scanner;
    private readonly Action _openEngineer;
    private bool _busy;
    private string _status = "正在读取检测工程";
    private EngineeringTestProjectPreview? _draftPreview;
    private string _draftIssue = "";
    private long _previewVersion;
    private string _startRequestStatus = "请完成工程与工位准备。软件 START 和实体 START 共享同一检测启动入口。";

    public AutomaticTestHomeViewModel(AutomaticTestPlanService plans,
        HardwareConnectionService hardware,
        SerialScannerWorkbenchViewModel scanner,
        ProjectWorkspaceService engineering, Action openEngineer)
    {
        _plans = plans;
        _hardware = hardware;
        _scanner = scanner;
        _engineering = engineering;
        _openEngineer = openEngineer;
        ChoosePlanCommand = new DelegateCommand(() => _openEngineer());
        RequestSoftwareStartCommand = new DelegateCommand(RequestSoftwareStart);
        _plans.Changed += OnPlanChanged;
        _hardware.ConnectionChanged += OnConnectionChanged;
        _scanner.PropertyChanged += OnScannerPropertyChanged;
        _scanner.CompleteBarcodeReceived += OnCompleteBarcodeReceived;
        _engineering.CurrentChanged += OnEngineeringProjectChanged;
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
    public bool HasPlan => _plans.Current is not null ||
        _draftPreview is { HasTestProject: true };
    public bool NeedsSetup => Cases.Count == 0;
    public string SetupIssue => _plans.Current is not null ? _plans.Issue :
        _draftPreview is not null
            ? "工程可编辑，但尚未发布供正式自动检测：" + _draftPreview.Description
            : string.IsNullOrWhiteSpace(_draftIssue)
                ? "尚未打开检测工程。进入开发与调试创建或打开 ECU 工程。"
                : _draftIssue;
    public string ModelCode => (_plans.Current?.Project.Definition.EcuModelCode ??
            _draftPreview?.Project.Definition.EcuModelCode) is { Length: > 0 } code
        ? code : "未设置";
    public string DutSerial => _dutSerial;
    public string ScanStatus => _scanStatus;
    public string Status => _status;
    public string DeploymentStatus => _plans.Current is null
        ? _draftPreview is null
            ? "当前没有可用工程，请进入开发与调试"
            : "当前展示的是工程草稿 · 尚未用于正式检测"
        : "正式工程版本已核对 " + _plans.Current.DeploymentFingerprint[..12] +
          "…；实体工位与完整执行还需验证";
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
              "需要完成前置烧录等检测配置并发布后才能进入正式检测。"
        : _plans.Current.ResourceIssues.Count != 0
            ? "正式版本有 " + _plans.Current.ResourceIssues.Count +
              " 项运行资源问题；请进入开发与调试定位。"
            : _prepared is null
                ? "正式版本已读取；需要实际扫码及工位预检。"
                : "扫码与工程资源已核对；真实夹具、START、FLASH、TEST 和 RECORD 的整机执行尚未接通。";

    public async Task RestoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _plans.RestoreAsync();
            await RefreshDraftPreviewAsync();
        }
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
        InvalidateScan("正式工程加载或更新后，需要重新核对本次 ECU 的真实扫码身份。");
        StartRequestStatus = "正式检测需要完整配置、扫码与实体工位确认。";
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
        _status = _plans.Current is null
            ? _draftPreview is null ? "请先打开工程" : "工程草稿 · 尚未发布"
            : !_plans.Current.ResourceReady ? "测试资源未就绪"
            : !_hardware.IsConnected ? "主控未连接"
            : "整机执行待接线";
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
        _engineering.CurrentChanged -= OnEngineeringProjectChanged;
    }
}
