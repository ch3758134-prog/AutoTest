using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Prism.Commands;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One saved ECU project supplies both engineering and automatic inspection.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private AutomaticTestPlanService _automaticTestPlans = null!;
    private AutomaticTestHomeViewModel _automaticTestHome = null!;
    private bool _productionWorkspace = true;
    private bool _compactShell;
    /// <summary>
    /// Desktop chrome density only. Does not change the active ECU project,
    /// TEST program, scanner selection or device state.
    /// </summary>
    public bool IsCompactShell => _compactShell;
    public void SetCompactShell(bool compact)
    {
        if (_compactShell == compact) return;
        _compactShell = compact;
        RaisePropertyChanged(nameof(IsCompactShell));
    }
    private bool _developerRestored;

    public AutomaticTestHomeView AutomaticTestView { get; private set; } = null!;
    public bool IsProductionWorkspace => _productionWorkspace;
    public bool IsEngineerWorkspace => !_productionWorkspace;
    public string WindowTitle => IsProductionWorkspace
        ? "AutoTest · 自动测试" : "AutoTest · 开发与调试";
    public DelegateCommand ShowProductionWorkspaceCommand { get; private set; } = null!;
    public DelegateCommand ShowEngineerWorkspaceCommand { get; private set; } = null!;
    public DelegateCommand ConfigureAutomaticTestCommand { get; private set; } = null!;
    public DelegateCommand ArmEngineeringFullRunCommand { get; private set; } = null!;
    public DelegateCommand RunEngineeringFullRunCommand { get; private set; } = null!;
    public DelegateCommand StopEngineeringFullRunCommand { get; private set; } = null!;

    private void InitializeProductionWorkspace()
    {
        _automaticTestPlans = new AutomaticTestPlanService();
        _automaticTestHome = new AutomaticTestHomeViewModel(_automaticTestPlans, _hardware,
            _scannerWorkbench, _workspace, _realTest,
            () => { _ = ShowDeveloperAsync(); },
            OpenSerialScannerWindow, () => IsProductionWorkspace);
        AutomaticTestView = new AutomaticTestHomeView { DataContext = _automaticTestHome };
        ArmEngineeringFullRunCommand = new DelegateCommand(() =>
        {
            _automaticTestHome.ArmEngineeringVerification();
            StatusMessage = "工程整组验证：先扫码核对当前 ECU，确认后从运行菜单启动真实 FLASH 和全量 TEST。";
        }, () => IsEngineerWorkspace && !_automaticTestHome.IsOperatorRunning);
        RunEngineeringFullRunCommand = _automaticTestHome.StartOperatorVerificationCommand;
        StopEngineeringFullRunCommand = _automaticTestHome.StopOperatorVerificationCommand;
        ConfigureAutomaticTestCommand = new DelegateCommand(
            async () => await ShowDeveloperAsync());
        ShowEngineerWorkspaceCommand = new DelegateCommand(
            async () => await ShowDeveloperAsync());
        ShowProductionWorkspaceCommand = new DelegateCommand(
            async () => await ShowAutomaticTestAsync());
    }

    /// <summary>
    /// AutoTest and engineering TEST use one opened ECU project and one station
    /// hardware session. Opening the previous project here does NOT open IDE
    /// editing tabs or perform physical CAN/FLASH actions.
    /// </summary>
    public async Task RestoreAutomaticTestAtStartupAsync()
    {
        if (_workspace.Current is null)
        {
            try
            {
                var path = _recentProjects.GetLastProjectManifest();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    await _workspace.OpenAsync(path);
                else
                    StatusMessage = "自动测试尚未选择 ECU 工程；进入开发与调试完成工程设置。";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                InvalidDataException or ArgumentException or ProjectDataException or
                System.Text.Json.JsonException)
            {
                StatusMessage = "自动测试恢复上次工程失败：" + error.Message;
            }
        }
        await _automaticTestHome.RestoreAsync();
    }

    private async Task ShowDeveloperAsync()
    {
        if (_realTest.IsRunning || _automaticTestHome.IsOperatorRunning)
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
        // Engineer home is the existing project setup/validation document,
        // not a separate TEST or production recipe. Preserve open editors.
        if (_workspace.Current is not null && OpenEditors.Count == 0)
            ShowProjectConfiguration();
    }

    private async Task ShowAutomaticTestAsync()
    {
        if (_realTest.IsRunning || _automaticTestHome.IsOperatorRunning)
        {
            StatusMessage = "开发测试正在运行；请先停止，避免自动测试与调试争用工位。";
            return;
        }
        if (!await PrepareEditorsForCloseAsync()) return;
        _automaticTestHome.DisarmEngineeringVerification();
        SetWorkspaceMode(true);
        // Use the same saved ECU project as the engineering workspace.
        await _automaticTestHome.RestoreAsync();
    }

    private void SetWorkspaceMode(bool automaticTest)
    {
        if (_productionWorkspace == automaticTest) return;
        _productionWorkspace = automaticTest;
        RaisePropertyChanged(nameof(IsProductionWorkspace));
        RaisePropertyChanged(nameof(IsEngineerWorkspace));
        RaisePropertyChanged(nameof(WindowTitle));
        ArmEngineeringFullRunCommand?.RaiseCanExecuteChanged();
        RunEngineeringFullRunCommand?.RaiseCanExecuteChanged();
        StatusMessage = automaticTest
            ? "自动测试使用当前保存的 ECU 工程。"
            : "开发与调试工作区：可独立打开、编辑 ECU 工程。";
    }
}
