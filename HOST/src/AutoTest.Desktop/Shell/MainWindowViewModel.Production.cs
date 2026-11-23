using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.Projects.Project;
using Prism.Commands;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One ECU project owns the formal inspection definition. The operator sees
/// only an explicitly deployed, pinned revision of that canonical project;
/// the developer may continue editing a separate un-deployed draft.
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
    public DelegateCommand DeployCurrentProjectCommand { get; private set; } = null!;

    private void InitializeProductionWorkspace()
    {
        _automaticTestPlans = new AutomaticTestPlanService();
        _automaticTestHome = new AutomaticTestHomeViewModel(_automaticTestPlans, _hardware,
            () => { _ = ShowDeveloperAsync(); });
        AutomaticTestView = new AutomaticTestHomeView { DataContext = _automaticTestHome };
        ConfigureAutomaticTestCommand = new DelegateCommand(
            async () => await ShowDeveloperAsync());
        DeployCurrentProjectCommand = new DelegateCommand(
            async () => await DeployCurrentProjectAsync());
        ShowEngineerWorkspaceCommand = new DelegateCommand(
            async () => await ShowDeveloperAsync());
        ShowProductionWorkspaceCommand = new DelegateCommand(
            async () => await ShowAutomaticTestAsync());
    }

    private async Task DeployCurrentProjectAsync()
    {
        if (_productionWorkspace || _realTest.IsRunning || _hardwareBusy)
        {
            StatusMessage = "请切换到工程与调试，并停止当前实体任务，再部署工程。";
            return;
        }
        var project = _workspace.Current;
        if (project is null)
        {
            StatusMessage = "请先打开 ECU 工程。";
            return;
        }
        try
        {
            await _automaticTestPlans.DeployAsync(project);
            StatusMessage = "当前 ECU 工程配置已部署到本工位（冻结资源指纹）。" +
                "该操作不执行烧录，不代表实体夹具或整机验收通过。";
            await RefreshEngineeringSetupAsync();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException or FormatException or
            NotSupportedException or ProjectDataException or System.Text.Json.JsonException)
        {
            StatusMessage = "部署被拒绝（保留旧部署）： " + error.Message;
        }
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
        // Engineer home is the existing project setup/validation document,
        // not a separate TEST or production recipe. Preserve open editors.
        if (_workspace.Current is not null && OpenEditors.Count == 0)
            ShowProjectConfiguration();
    }

    private async Task ShowAutomaticTestAsync()
    {
        if (_realTest.IsRunning)
        {
            StatusMessage = "开发测试正在运行；请先停止，避免自动测试与调试争用工位。";
            return;
        }
        SetWorkspaceMode(true);
        // Never adopt an IDE draft. Revalidate the explicit deployment
        // fingerprint and all referenced assets whenever entering production.
        await _automaticTestHome.RestoreAsync();
    }

    private void SetWorkspaceMode(bool automaticTest)
    {
        if (_productionWorkspace == automaticTest) return;
        _productionWorkspace = automaticTest;
        RaisePropertyChanged(nameof(IsProductionWorkspace));
        RaisePropertyChanged(nameof(IsEngineerWorkspace));
        RaisePropertyChanged(nameof(WindowTitle));
        StatusMessage = automaticTest
            ? "自动测试使用本工位已部署且指纹核对的 ECU 工程；编辑草稿不能自动替换部署。"
            : "开发与调试工作区：可独立打开、编辑 ECU 工程。";
    }
}
