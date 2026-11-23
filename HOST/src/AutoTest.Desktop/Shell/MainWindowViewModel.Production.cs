using Prism.Commands;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Independent entry points: automatic-test plan is an operator-local selection,
/// while the developer IDE owns its own recent/open project and editor tabs.
/// Sharing domain codecs is not sharing the active user workflow.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private AutomaticTestPlanService _automaticTestPlans = null!;
    private AutomaticTestHomeViewModel _automaticTestHome = null!;
    private bool _productionWorkspace = true;
    private bool _developerRestored;

    public AutomaticTestHomeView AutomaticTestView { get; private set; } = null!;
    public bool IsProductionWorkspace => _productionWorkspace;
    public bool IsEngineerWorkspace => !_productionWorkspace;
    public string WindowTitle => IsProductionWorkspace
        ? "AutoTest · 自动测试" : "AutoTest · 开发与调试";
    public DelegateCommand ShowProductionWorkspaceCommand { get; private set; } = null!;
    public DelegateCommand ShowEngineerWorkspaceCommand { get; private set; } = null!;
    public DelegateCommand ConfigureAutomaticTestCommand { get; private set; } = null!;

    private void InitializeProductionWorkspace()
    {
        _automaticTestPlans = new AutomaticTestPlanService();
        _automaticTestHome = new AutomaticTestHomeViewModel(_automaticTestPlans, _hardware);
        AutomaticTestView = new AutomaticTestHomeView { DataContext = _automaticTestHome };
        ConfigureAutomaticTestCommand = new DelegateCommand(
            async () => await _automaticTestHome.ChoosePlanAsync());
        ShowEngineerWorkspaceCommand = new DelegateCommand(
            async () => await ShowDeveloperAsync());
        ShowProductionWorkspaceCommand = new DelegateCommand(
            async () => await ShowAutomaticTestAsync());
    }

    /// <summary>
    /// Start as an automatic test terminal. Do not open or modify the IDE's
    /// LastProject in the process; that is restored only upon entering Studio.
    /// </summary>
    public Task RestoreAutomaticTestAtStartupAsync() => _automaticTestHome.RestoreAsync();

    private async Task ShowDeveloperAsync()
    {
        if (_realTest.IsRunning)
        {
            StatusMessage = "开发测试正在执行，请先结束当前测试再切换工作区。";
            return;
        }
        SetWorkspaceMode(false);
        if (!_developerRestored)
        {
            _developerRestored = true;
            if (_workspace.Current is null) await RestoreLastProjectAsync();
        }
    }

    private async Task ShowAutomaticTestAsync()
    {
        if (_realTest.IsRunning)
        {
            StatusMessage = "开发测试正在运行；请先停止，避免自动测试与调试争用工位。";
            return;
        }
        SetWorkspaceMode(true);
        // The saved operator plan is reverified independently; IDE edits or
        // file changes cannot silently swap the selected automatic-test plan.
        await _automaticTestHome.RefreshPlanAsync();
    }

    private void SetWorkspaceMode(bool automaticTest)
    {
        if (_productionWorkspace == automaticTest) return;
        _productionWorkspace = automaticTest;
        RaisePropertyChanged(nameof(IsProductionWorkspace));
        RaisePropertyChanged(nameof(IsEngineerWorkspace));
        RaisePropertyChanged(nameof(WindowTitle));
        StatusMessage = automaticTest
            ? "自动测试方案独立恢复；开发调试工程不会自动替换该方案。"
            : "开发与调试工作区：可独立打开、编辑 ECU 工程。";
    }
}
