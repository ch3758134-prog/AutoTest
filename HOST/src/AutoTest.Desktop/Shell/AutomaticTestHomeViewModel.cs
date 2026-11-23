using System.Collections.ObjectModel;
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
public sealed class AutomaticTestHomeViewModel : BindableBase, IDisposable
{
    private readonly AutomaticTestPlanService _plans;
    private readonly HardwareConnectionService _hardware;
    private bool _busy;
    private string _status = "正在读取自动测试工程";

    public AutomaticTestHomeViewModel(AutomaticTestPlanService plans,
        HardwareConnectionService hardware)
    {
        _plans = plans;
        _hardware = hardware;
        ChoosePlanCommand = new DelegateCommand(async () => await ChoosePlanAsync(),
            () => !IsBusy);
        _plans.Changed += OnPlanChanged;
        _hardware.ConnectionChanged += OnConnectionChanged;
    }

    public ObservableCollection<AutomaticTestCase> Cases { get; } = new();
    public DelegateCommand ChoosePlanCommand { get; }
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
    public string DutSerial => "等待扫码";
    public string Status => _status;
    public int CaseCount => Cases.Count;
    public string DeviceStatus => _hardware.IsConnected ? "主控已连接" : "主控未连接";
    public string CurrentCase => "—";
    public string CurrentStep => "—";
    public string ActualValue => "—";
    public string ExpectedValue => "—";
    public string FailureReason => "—";
    public string ExecutionNotice => !HasPlan
        ? "请在本页选择已有 ECU 工程。"
        : _plans.Current!.ResourceIssues.Count != 0
            ? "资源需要修复（不会执行硬件）："+ string.Join("；",
                _plans.Current.ResourceIssues.Take(5))
            : "测试用例和资源已读取；自动启动、前置烧录及整机结果仍需实物接线，不能据此给出合格结论。";

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

    public async Task ChoosePlanAsync()
    {
        if (IsBusy) return;
        var picker = new OpenFileDialog
        {
            Title = "打开测试工程（选择工程文件 project.autotest-project.json）",
            Filter = "AutoTest 工程文件 (project.autotest-project.json)|project.autotest-project.json|AutoTest 工程 (*.autotest-project.json)|*.autotest-project.json",
            FileName = "project.autotest-project.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (_plans.ConfiguredPath is { Length: > 0 } previous &&
            Directory.Exists(Path.GetDirectoryName(previous)))
            picker.InitialDirectory = Path.GetDirectoryName(previous);
        if (picker.ShowDialog(global::System.Windows.Application.Current?.MainWindow) != true) return;
        IsBusy = true;
        try { await _plans.SelectAsync(picker.FileName); }
        finally { IsBusy = false; }
    }

    private void OnPlanChanged(object? sender, EventArgs e)
    {
        Cases.Clear();
        if (_plans.Current is { } plan)
        {
            foreach (var item in plan.Cases)
                Cases.Add(item);
        }
        _status = !HasPlan ? "尚未打开测试工程"
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
        _status = !HasPlan ? "尚未打开测试工程"
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
        RaisePropertyChanged(nameof(Status));
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
    }
}
