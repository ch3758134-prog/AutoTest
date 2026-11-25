using System.IO;
using AutoTest.FlashLab.Programming;
using System.Windows;
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
    private EngineeringVerificationWindow? _engineeringVerificationWindow;

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
    public DelegateCommand ShowEngineeringFullRunMonitorCommand { get; private set; } = null!;
    public DelegateCommand ConfirmProductionConfigurationCommand { get; private set; } = null!;
    public DelegateCommand OpenProductionPreparationCommand { get; private set; } = null!;
    /// <summary>The first-level task entry stays available even when starting
    /// in production. It first goes through the existing safe mode transition,
    /// then opens the single preparation workbench; it never creates a recipe.</summary>
    public DelegateCommand NavigateProductionPreparationCommand { get; private set; } = null!;
    private ProductionPreparationWindow? _productionPreparationWindow;

    private void InitializeProductionWorkspace()
    {
        OpenProductionPreparationCommand = new DelegateCommand(
            OpenProductionPreparationWindow, () => IsEngineerWorkspace &&
                !_realTest.IsRunning &&
                (_automaticTestHome is null || !_automaticTestHome.IsOperatorRunning));
        _automaticTestPlans = new AutomaticTestPlanService();
        _automaticTestHome = new AutomaticTestHomeViewModel(_automaticTestPlans, _hardware,
            _scannerWorkbench, _workspace, _realTest,
            () => { _ = ShowDeveloperAsync(); },
            OpenSerialScannerWindow, () => IsProductionWorkspace);
        AutomaticTestView = new AutomaticTestHomeView { DataContext = _automaticTestHome };
        ArmEngineeringFullRunCommand = new DelegateCommand(() =>
        {
            _automaticTestHome.ArmEngineeringVerification();
            ShowEngineeringVerificationMonitor();
            StatusMessage = "工程验证已准备：扫描本台真实 ECU 后，在监视窗口启动包含 FLASH 的整组实测。";
        }, () => IsEngineerWorkspace && !_automaticTestHome.IsOperatorRunning);
        RunEngineeringFullRunCommand = _automaticTestHome.StartOperatorVerificationCommand;
        StopEngineeringFullRunCommand = _automaticTestHome.StopOperatorVerificationCommand;
        ShowEngineeringFullRunMonitorCommand = new DelegateCommand(
            ShowEngineeringVerificationMonitor, () => IsEngineerWorkspace);
        ConfirmProductionConfigurationCommand = new DelegateCommand(
            async () => await ConfirmProductionConfigurationAsync(),
            () => IsEngineerWorkspace && !_realTest.IsRunning &&
                !_automaticTestHome.IsOperatorRunning);
        ConfigureAutomaticTestCommand = new DelegateCommand(
            async () => await ShowDeveloperAsync());
        ShowEngineerWorkspaceCommand = new DelegateCommand(
            async () => await ShowDeveloperAsync());
        ShowProductionWorkspaceCommand = new DelegateCommand(
            async () => await ShowAutomaticTestAsync());
        NavigateProductionPreparationCommand = new DelegateCommand(async () =>
        {
            if (_realTest.IsRunning || _automaticTestHome.IsOperatorRunning ||
                _hardware.IsTestRunApproved || _hardware.IsFlashSecuritySensitive ||
                _hardware.HasManualCanRowLeases)
            {
                StatusMessage = "当前工位存在实际运行或设备资源占用；请先完成安全收尾再进入生产准备。";
                return;
            }
            await ShowDeveloperAsync();
            if (IsEngineerWorkspace) OpenProductionPreparationWindow();
        });
    }

    private void OpenProductionPreparationWindow()
    {
        if (!IsEngineerWorkspace || _realTest.IsRunning ||
            _automaticTestHome.IsOperatorRunning) return;
        if (_productionPreparationWindow is { IsVisible: true } current)
        { current.Activate(); return; }
        var window = new ProductionPreparationWindow(_workspace,
            () => IsEngineerWorkspace && !_realTest.IsRunning &&
                !_automaticTestHome.IsOperatorRunning &&
                !_hardware.IsFlashSecuritySensitive &&
                !_hardware.IsManualCanBatchApproved &&
                !_hardware.IsManualKeyActionApproved,
            _scannerWorkbench, () => _automaticTestHome.OnPublishedConfigurationChanged(),
            ActivatePublishedProjectAsync,
            async () =>
            {
                await ConfirmProductionConfigurationAsync();
                return StatusMessage;
            })
        { Owner = global::System.Windows.Application.Current?.MainWindow };
        _productionPreparationWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_productionPreparationWindow, window))
                _productionPreparationWindow = null;
            _ = RefreshEngineeringSetupAsync();
        };
        window.Show(); window.Activate();
    }

    /// <summary>Switch only an explicitly published exact product at an IDLE
    /// engineering station. Changing ECU bitrate requires actual USB readback;
    /// no implicit project/firmware fallback and no controller disconnect.</summary>
    private async Task<string> ActivatePublishedProjectAsync(ProductionPublishedProduct pin)
    {
        if (!IsEngineerWorkspace || _hardwareBusy || _ecuCanBaudApplying ||
            _realTest.IsRunning || _automaticTestHome.IsOperatorRunning ||
            _hardware.IsTestRunApproved ||
            _hardware.HasManualCanRowLeases ||
            _hardware.IsFlashSecuritySensitive ||
            _hardware.IsManualCanBatchApproved ||
            _hardware.IsManualKeyActionApproved)
            throw new InvalidOperationException(
                "当前存在 CAN 周期任务、烧录、TEST 或其它实体动作；必须确认工位空闲后才能切换已发布型号。");
        await StationProductionConfigurationStore.VerifyProductAsync(pin);
        if (!await PrepareEditorsForCloseAsync())
            throw new IOException("工程编辑尚未保存，工位切换取消。");
        if (!await PrepareDbcWindowsForCloseAsync(exitingApplication: false))
            throw new IOException("DBC 编辑器仍有待完成修改。");
        if (_hardware.IsConnected)
        {
            _hardware.SelectRequestedEcuCanBitrate(pin.EcuCanKbps);
            var confirmed = await _hardware.ApplyRequestedEcuCanBitrateAsync();
            if (confirmed != pin.EcuCanKbps)
                throw new IOException("ECU CAN 实际回读码率与目标已发布版本不符。");
        }
        await _workspace.OpenAsync(pin.ManifestPath);
        var project = _workspace.Current ?? throw new IOException("目标工程未打开。");
        if (project.Definition.Id.Value != pin.ProjectId ||
            !string.Equals(project.ManifestSha256, pin.ManifestSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("目标工程的发布身份/内容指纹已改变。");
        var deployed = await StationDeploymentStore.DeployAsync(project);
        _automaticTestHome.OnPublishedConfigurationChanged();
        await _automaticTestHome.RestoreAsync();
        return "工位已切换到已发布型号：" + pin.ModelCode +
            (pin.HardwareCode.Length == 0 ? "" : " / " + pin.HardwareCode) +
            " · TEST " + deployed.Test.Cases.Count + " 个；" +
            (_hardware.IsConnected ? "ECU CAN 已真实回读确认 " + pin.EcuCanKbps + " kbit/s。" :
                "主控当前未连接，后续连接必须重新应用并回读 CAN 码率。");
    }

    /// <summary>
    /// One explicit acceptance of THIS saved ECU project and its immutable
    /// FLASH release. Not another test plan and never a physical START.
    /// Edits after confirmation invalidate the deployment automatically.
    /// </summary>
    private async Task ConfirmProductionConfigurationAsync()
    {
        if (!IsEngineerWorkspace || _realTest.IsRunning ||
            _automaticTestHome.IsOperatorRunning || _hardware.IsFlashSecuritySensitive ||
            _hardware.IsManualCanBatchApproved || _hardware.IsManualKeyActionApproved)
        {
            StatusMessage = "工位仍有实体任务；请结束并核实状态后再确认正式检测工程。";
            return;
        }
        if (!await PrepareEditorsForCloseAsync()) return;
        // Native FLASH is an owned floating editor, not a main document tab.
        // Commit and close its current draft before computing publication SHA;
        // otherwise the operator could confirm stale bytes while editing.
        if (_nativeFlashProfileWindow is { IsVisible: true } profileWindow &&
            !await profileWindow.PrepareForParentShutdownAsync())
        {
            StatusMessage = "原生 FLASH 编辑尚未保存完成，生产确认已取消。";
            return;
        }
        var project = _workspace.Current;
        if (project is null)
        {
            StatusMessage = "没有打开 ECU 工程，无法确认生产配置。";
            return;
        }
        try
        {
            var draft = await NativeFlashProfileStore.LoadAsync(project.RootDirectory)
                ?? throw new InvalidDataException("该 ECU 工程尚未保存原生烧录工艺。");
            var job = NativeFlashJobBuilder.Freeze(draft.Profile, project.RootDirectory);
            if (!string.Equals(draft.Profile.EcuModelCode,
                    project.Definition.EcuModelCode, StringComparison.Ordinal))
                throw new InvalidDataException("原生烧录工艺与当前 ECU 工程型号不一致。");
            var decision = MessageBox.Show(
                global::System.Windows.Application.Current?.MainWindow,
                "即将核对并冻结当前 ECU 工程作为本工位正式生产配置。\n" +
                "无需创建第二套测试方案，正式检测仍执行工程内全部有序 TEST。\n\n" +
                "型号：" + project.Definition.EcuModelCode + "\n" +
                "工程：" + project.Definition.Name + "\n" +
                "FLASH 固件 SHA256：" + job.FirmwareSha256 + "\n" +
                "原生工艺 SHA256：" + draft.ProfileSha256 + "\n\n" +
                "确认不会操作实体主控或烧录 ECU。确认后任何工程、" +
                "工艺或固件更改均需重新核对。",
                "确认当前工程用于正式检测", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No);
            if (decision != MessageBoxResult.Yes) return;
            // Publish the exact observed draft, then bind the station to the
            // verified manifest + ordered TEST + firmware/seed/key fingerprints.
            // On failure the former station pin cannot silently accept a new
            // release: StationDeploymentStore.VerifyAsync will reject it.
            await NativeFlashPublicationStore.PublishAsync(
                project.RootDirectory, draft.ProfileSha256);
            var deployed = await StationDeploymentStore.DeployAsync(project);
            StatusMessage = "当前 ECU 工程已确认用于正式检测：" +
                deployed.Project.Definition.EcuModelCode +
                "；下一步仅在真实扫码、实体 START 与生产 RECORD 均通过后允许启动。";
            await RefreshEngineeringSetupAsync();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException or InvalidOperationException or
            AutoTest.Projects.Project.ProjectDataException or FormatException or
            NotSupportedException or System.Text.Json.JsonException)
        {
            StatusMessage = "正式生产配置确认失败，没有取得新的生产执行许可：" +
                error.Message;
        }
    }

    private void ShowEngineeringVerificationMonitor()
    {
        if (!IsEngineerWorkspace) return;
        if (_engineeringVerificationWindow is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }
        var window = new EngineeringVerificationWindow(_automaticTestHome, OpenRecordHistory)
        {
            Owner = global::System.Windows.Application.Current?.MainWindow
        };
        _engineeringVerificationWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_engineeringVerificationWindow, window))
                _engineeringVerificationWindow = null;
        };
        window.Show();
        window.Activate();
    }

    private void CloseEngineeringVerificationMonitor()
    {
        if (_engineeringVerificationWindow is { IsVisible: true } window)
            window.Close();
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
        // Mode switch must reload the one project under the correct policy:
        // engineering may inspect saved drafts, while production only reads
        // a verified station deployment. Never carry the previous mode's
        // loaded plan into engineer-run preflight.
        await _automaticTestHome.RestoreAsync();
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
        CloseEngineeringVerificationMonitor();
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
        ShowEngineeringFullRunMonitorCommand?.RaiseCanExecuteChanged();
        ConfirmProductionConfigurationCommand?.RaiseCanExecuteChanged();
        OpenProductionPreparationCommand?.RaiseCanExecuteChanged();
        NavigateProductionPreparationCommand?.RaiseCanExecuteChanged();
        StatusMessage = automaticTest
            ? "自动测试使用当前保存的 ECU 工程。"
            : "开发与调试工作区：可独立打开、编辑 ECU 工程。";
    }
}
