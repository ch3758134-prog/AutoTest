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
        ? "AutoTest · 生产检测" : "AutoTest · 测试工程";
    public DelegateCommand ShowProductionWorkspaceCommand { get; private set; } = null!;
    public DelegateCommand ShowEngineerWorkspaceCommand { get; private set; } = null!;
    public DelegateCommand ConfigureAutomaticTestCommand { get; private set; } = null!;
    public DelegateCommand ArmEngineeringFullRunCommand { get; private set; } = null!;
    /// <summary>Single obvious engineering verification entry. Never grants USB writes or START by itself.</summary>
    public DelegateCommand EnterEngineeringVerificationCommand { get; private set; } = null!;
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
    private ProductionProjectLibraryWindow? _productionLibraryWindow;
    private string? _preferredProductionVersionSha;
    private readonly ProductionProjectCatalog _productionCatalog = new();
    public DelegateCommand OpenProductionProjectLibraryCommand { get; private set; } = null!;

    private void InitializeProductionWorkspace()
    {
        OpenProductionProjectLibraryCommand =
            new DelegateCommand(OpenProductionProjectLibrary);
        OpenProductionPreparationCommand = new DelegateCommand(
            OpenProductionPreparationWindow,
            () => !_realTest.IsRunning &&
                  (_automaticTestHome is null || !_automaticTestHome.IsOperatorRunning));
        _automaticTestPlans = new AutomaticTestPlanService();
        _automaticTestHome = new AutomaticTestHomeViewModel(_automaticTestPlans, _hardware,
            _scannerWorkbench, _workspace, _realTest,
            () => { _ = ShowDeveloperAsync(); },
            OpenSerialScannerWindow, () => IsProductionWorkspace,
            _productionCatalog, ActivateCatalogPackageAsync);
        AutomaticTestView = new AutomaticTestHomeView { DataContext = _automaticTestHome };
        EnterEngineeringVerificationCommand = new DelegateCommand(
            async () => await EnterEngineeringVerificationAsync(),
            () => !_realTest.IsRunning && !_automaticTestHome.IsOperatorRunning &&
                !_hardware.IsFlashSecuritySensitive);
        ArmEngineeringFullRunCommand = new DelegateCommand(() =>
        {
            _automaticTestHome.ArmEngineeringVerification();
            ShowEngineeringVerificationMonitor();
            StatusMessage = "工程测试已打开，请扫描 ECU 编号并检查设备连接。";
        }, () => IsEngineerWorkspace && !_automaticTestHome.IsOperatorRunning);
        RunEngineeringFullRunCommand = _automaticTestHome.StartOperatorVerificationCommand;
        StopEngineeringFullRunCommand = _automaticTestHome.StopOperatorVerificationCommand;
        ShowEngineeringFullRunMonitorCommand = new DelegateCommand(
            ShowEngineeringVerificationMonitor, () => IsEngineerWorkspace);
        ConfirmProductionConfigurationCommand = new DelegateCommand(
            async () => { await ConfirmProductionConfigurationAsync(); },
            () => IsEngineerWorkspace && !_realTest.IsRunning &&
                !_automaticTestHome.IsOperatorRunning);
        ConfigureAutomaticTestCommand = new DelegateCommand(
            async () => await ShowDeveloperAsync());
        ShowEngineerWorkspaceCommand = new DelegateCommand(
            async () => await ShowDeveloperAsync());
        ShowProductionWorkspaceCommand = new DelegateCommand(
            async () => await ShowAutomaticTestAsync());
        NavigateProductionPreparationCommand = new DelegateCommand(
            OpenProductionPreparationWindow);
    }

    private void OpenProductionProjectLibrary()
    {
        if (_productionLibraryWindow is { IsVisible: true } opened)
        {
            if (_preferredProductionVersionSha is { } preferred)
            {
                opened.PreferPackage(preferred);
                _preferredProductionVersionSha = null;
            }
            if (opened.WindowState == WindowState.Minimized)
                opened.WindowState = WindowState.Normal;
            opened.Activate();
            return;
        }
        var window = new ProductionProjectLibraryWindow(
            _productionCatalog, _workspace,
            () => !_realTest.IsRunning &&
                  !_automaticTestHome.IsOperatorRunning &&
                  !_hardware.IsTestRunApproved &&
                  !_hardware.IsFlashSecuritySensitive &&
                  !_hardware.HasManualCanRowLeases &&
                  !_hardware.IsManualCanBatchApproved &&
                  !_hardware.IsManualKeyActionApproved,
            () => IsEngineerWorkspace,
            () => _automaticTestHome.OnPublishedConfigurationChanged(),
            _preferredProductionVersionSha)
        { Owner = global::System.Windows.Application.Current?.MainWindow };
        _preferredProductionVersionSha = null;
        _productionLibraryWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_productionLibraryWindow, window))
                _productionLibraryWindow = null;
        };
        window.Show();
        window.Activate();
    }

    private void OpenProductionPreparationWindow()
    {
        if (_realTest.IsRunning || _automaticTestHome.IsOperatorRunning ||
            _hardware.IsTestRunApproved || _hardware.IsFlashSecuritySensitive ||
            _hardware.HasManualCanRowLeases)
        {
            StatusMessage = "设备正在使用中，请完成当前测试后再修改工位设置。";
            return;
        }
        if (_productionPreparationWindow is { IsVisible: true } current)
        {
            if (current.WindowState == WindowState.Minimized)
                current.WindowState = WindowState.Normal;
            current.Activate();
            return;
        }
        var window = new ProductionPreparationWindow(
            _scannerWorkbench,
            () => !_realTest.IsRunning &&
                  !_automaticTestHome.IsOperatorRunning &&
                  !_hardware.IsTestRunApproved &&
                  !_hardware.IsFlashSecuritySensitive &&
                  !_hardware.HasManualCanRowLeases &&
                  !_hardware.IsManualCanBatchApproved &&
                  !_hardware.IsManualKeyActionApproved,
            OpenSerialScannerWindow,
            () => !_hardware.IsConnected ? "主控未连接" :
                _hardware.IsEcuCanBitrateApplied
                    ? "主控已连接 · ECU CAN " +
                      _hardware.ConfirmedEcuCanBitrateKbps + " kbit/s"
                    : "主控已连接 · CAN 波特率尚未确认",
            () => _automaticTestHome.OnPublishedConfigurationChanged())
        { Owner = global::System.Windows.Application.Current?.MainWindow };
        _productionPreparationWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_productionPreparationWindow, window))
                _productionPreparationWindow = null;
        };
        window.Show();
        window.Activate();
    }

    /// <summary>
    /// Loads a verified library package into the production plan without
    /// touching the engineering editor's open project. This is metadata and
    /// CAN bitrate selection only: no fixture, START or FLASH authorization.
    /// </summary>
    private async Task<bool> ActivateCatalogPackageAsync(
        InstalledProductionPackage selected)
    {
        if (!IsProductionWorkspace ||
            _hardwareBusy || _ecuCanBaudApplying ||
            _realTest.IsRunning || _automaticTestHome.IsOperatorRunning ||
            _hardware.IsTestRunApproved || _hardware.HasManualCanRowLeases ||
            _hardware.IsFlashSecuritySensitive ||
            _hardware.IsManualCanBatchApproved ||
            _hardware.IsManualKeyActionApproved)
            throw new InvalidOperationException(
                "工位正在运行或设备被占用，请先完成当前检测。");
        await _productionCatalog.VerifySelectionAsync(selected);
        var path = Path.GetFullPath(selected.ManifestPath);
        var bytes = await File.ReadAllBytesAsync(path);
        var project = await ProjectRepository.OpenDefinitionAsync(path);
        if (project.Id.Value != selected.ProjectId ||
            project.Revision != selected.ProjectRevision)
            throw new InvalidDataException("已安装工程版本与工程库记录不一致。");
        var context = new OpenProjectContext(project, path,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        var native = await NativeFlashPublicationStore.LoadPublishedAsync(
            context.RootDirectory)
            ?? throw new InvalidDataException("检测工程缺少有效烧录配置。");
        _ = NativeFlashPublicationStore.FreezePublished(context.RootDirectory, native);
        // A failed CAN reconfiguration must never activate another TEST/FLASH
        // deployment. The library snapshot is already immutable and verified.
        if (_hardware.IsConnected)
        {
            _hardware.SelectRequestedEcuCanBitrate(native.Profile.EcuCanKbps);
            var confirmed = await _hardware.ApplyRequestedEcuCanBitrateAsync();
            if (confirmed != native.Profile.EcuCanKbps)
                throw new IOException("主控 CAN 波特率未确认，请检查连接后重新扫码。");
        }

        StationDeploymentStore.Verified? previous = null;
        try { previous = await StationDeploymentStore.RestoreAsync(); }
        catch (Exception oldError) when (oldError is IOException or InvalidDataException or
            UnauthorizedAccessException or AutoTest.Projects.Project.ProjectDataException)
        {
            // A removed development directory must not prevent loading a
            // separately verified installed package. It is not a valid
            // rollback target, and will never be used to authorize execution.
            System.Diagnostics.Trace.TraceWarning(
                "Previous production selection unavailable: " + oldError);
        }
        var unchanged = previous is not null &&
            previous.Project.Definition.Id == context.Definition.Id &&
            string.Equals(previous.Project.ManifestSha256, context.ManifestSha256,
                StringComparison.OrdinalIgnoreCase);
        if (unchanged && _automaticTestPlans.Current is { } plan &&
            string.Equals(plan.Project.ManifestPath, context.ManifestPath,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(plan.Project.ManifestSha256, context.ManifestSha256,
                StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            if (!unchanged)
                await StationDeploymentStore.DeployAsync(context);
            await _automaticTestPlans.RestoreAsync(context,
                requireProductionDeployment: true);
            // All real TEST, DBC, WAVE and CAN services share this single
            // active project context; a scan selects that very project.
            await _workspace.OpenAsync(context.ManifestPath);
            if (_automaticTestPlans.Current is null)
                throw new InvalidDataException(_automaticTestPlans.Issue);
        }
        catch
        {
            // A package install/plan error is NOT permission to leave a
            // half-selected candidate. Restore the previous checked package.
            if (previous is not null && !unchanged)
            {
                try
                {
                    await StationDeploymentStore.DeployAsync(previous.Project);
                    await _automaticTestPlans.RestoreAsync(previous.Project,
                        requireProductionDeployment: true);
                }
                catch (Exception restoreError) when (restoreError is not OutOfMemoryException)
                {
                    StatusMessage = "工程切换失败，原工程也无法安全恢复：" +
                        restoreError.Message + "。请停止工位操作并检查工程库。";
                    throw new InvalidDataException(StatusMessage, restoreError);
                }
            }
            throw;
        }
        StatusMessage = "已选择检测工程：" + project.Name +
            "。等待本台 ECU 的启动条件。";
        return true;
    }

    /// <summary>
    /// One explicit acceptance of THIS saved ECU project and its immutable
    /// FLASH release. Not another test plan and never a physical START.
    /// Edits after confirmation invalidate the deployment automatically.
    /// </summary>
    private async Task<bool> ConfirmProductionConfigurationAsync(bool singleApply = false)
    {
        if (!IsEngineerWorkspace || _realTest.IsRunning ||
            _automaticTestHome.IsOperatorRunning || _hardware.IsFlashSecuritySensitive ||
            _hardware.IsManualCanBatchApproved || _hardware.IsManualKeyActionApproved)
        {
            StatusMessage = "工位仍有实体任务；请结束并核实状态后再确认正式检测工程。";
            return false;
        }
        if (!await PrepareEditorsForCloseAsync()) return false;
        // Native FLASH is an owned floating editor, not a main document tab.
        // Commit and close its current draft before computing publication SHA;
        // otherwise the operator could confirm stale bytes while editing.
        if (_nativeFlashProfileWindow is { IsVisible: true } profileWindow &&
            !await profileWindow.PrepareForParentShutdownAsync())
        {
            StatusMessage = "烧录参数尚未保存，无法更新生产工程。";
            return false;
        }
        var project = _workspace.Current;
        if (project is null)
        {
            StatusMessage = "请先打开 ECU 测试工程。";
            return false;
        }
        try
        {
            var draft = await NativeFlashProfileStore.LoadAsync(project.RootDirectory)
                ?? throw new InvalidDataException("该 ECU 工程尚未保存原生烧录工艺。");
            _ = NativeFlashJobBuilder.Freeze(draft.Profile, project.RootDirectory);
            // "更新生产工程" is the explicit operator choice. The operation
            // only installs verified project content; it performs no physical
            // clamp, CAN or FLASH action. Real production retains all gates.
            // Publish the exact observed draft, then bind the station to the
            // verified manifest + ordered TEST + firmware/seed/key fingerprints.
            // On failure the former station pin cannot silently accept a new
            // release: StationDeploymentStore.VerifyAsync will reject it.
            await NativeFlashPublicationStore.PublishAsync(
                project.RootDirectory, draft.ProfileSha256);
            // Install a verified, immutable local production package. This is
            // internal packaging, not a manual user export/import round trip.
            // One exact barcode marker still needs a station-owned binding.
            var installed = await _productionCatalog.InstallEngineeringProjectAsync(
                project.ManifestPath);
            var deployed = await StationDeploymentStore.DeployAsync(project);
            StatusMessage = "工程已准备就绪：" + installed.ProjectName + "。";
            await RefreshEngineeringSetupAsync();
            if (!singleApply)
            {
                _preferredProductionVersionSha = installed.PackageSha256;
                OpenProductionProjectLibrary();
            }
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException or InvalidOperationException or
            AutoTest.Projects.Project.ProjectDataException or FormatException or
            NotSupportedException or System.Text.Json.JsonException)
        {
            StatusMessage = "生产工程更新失败：" +
                error.Message;
            return false;
        }
    }

    /// <summary>
    /// A test operator should not need to navigate production preparation,
    /// engineering menus, and a second "arm scan" step merely to inspect
    /// one real ECU. Switching here only changes UI context, refreshes the
    /// existing pinned resource snapshot, and arms real COM barcode reception.
    /// USB clamp/CAN/FLASH writes still require the existing explicit physical
    /// action consent and the full preflight inside StartOperatorVerificationAsync.
    /// </summary>
    private async Task EnterEngineeringVerificationAsync()
    {
        if (_realTest.IsRunning || _automaticTestHome.IsOperatorRunning ||
            _hardware.IsFlashSecuritySensitive ||
            _hardware.IsTestRunApproved ||
            _hardware.HasManualCanRowLeases ||
            _hardware.IsManualCanBatchApproved ||
            _hardware.IsManualKeyActionApproved)
        {
            StatusMessage = "设备正在执行任务或资源被占用，请先安全停止后进入工程实测。";
            return;
        }
        try
        {
            // Do not open IDE documents or ask the user to prepare a second
            // test plan. The engineering runner still uses the published
            // project, barcode binding and real TEST/FLASH execution service.
            if (IsProductionWorkspace)
            {
                SetWorkspaceMode(false);
                if (_workspace.Current is null)
                    await RestoreAutomaticTestAtStartupAsync();
            }
            await _automaticTestHome.RestoreAsync();
            _automaticTestHome.ArmEngineeringVerification(
                startWithExplicitConsentOnScan: true);
            ShowEngineeringVerificationMonitor();
            StatusMessage = "工程测试已打开，请连接设备并扫描本台 ECU 编号。" +
                "涉及烧录或夹具动作时，开始前会再次请求确认。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or ProjectDataException or FormatException)
        {
            StatusMessage = "无法打开工程测试：" + error.Message;
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
        // A test engineer enters the ONE formal ordered TEST document, not a
        // setup dashboard. Restore existing editors without replacing drafts.
        if (_workspace.Current is not null && OpenEditors.Count == 0)
            await OpenDefaultEngineeringDocumentAsync();
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
        // A normal switch to production synchronizes the current saved ECU
        // project. Internal FLASH freeze, package verification and deployment
        // are not a separate operator wizard or another TEST configuration.
        // No fixture, IGN, CAN transmit or FLASH command is performed here.
        string? preparationError = null;
        if (IsEngineerWorkspace && _workspace.Current is not null &&
            !await ConfirmProductionConfigurationAsync(singleApply: true))
            preparationError = StatusMessage;

        _automaticTestHome.DisarmEngineeringVerification();
        CloseEngineeringVerificationMonitor();
        SetWorkspaceMode(true);
        // The production runner reopens only verified installed resources.
        await _automaticTestHome.RestoreAsync();
        // Show the actual unmet condition; entering the production page remains
        // possible so a scanned barcode can still be displayed and diagnosed.
        if (preparationError is not null) StatusMessage = preparationError;
    }

    private void SetWorkspaceMode(bool automaticTest)
    {
        if (_productionWorkspace == automaticTest) return;
        _productionWorkspace = automaticTest;
        RaisePropertyChanged(nameof(IsProductionWorkspace));
        RaisePropertyChanged(nameof(IsEngineerWorkspace));
        RaisePropertyChanged(nameof(WindowTitle));
        ArmEngineeringFullRunCommand?.RaiseCanExecuteChanged();
        EnterEngineeringVerificationCommand?.RaiseCanExecuteChanged();
        RunEngineeringFullRunCommand?.RaiseCanExecuteChanged();
        ShowEngineeringFullRunMonitorCommand?.RaiseCanExecuteChanged();
        ConfirmProductionConfigurationCommand?.RaiseCanExecuteChanged();
        OpenProductionPreparationCommand?.RaiseCanExecuteChanged();
        NavigateProductionPreparationCommand?.RaiseCanExecuteChanged();
        StatusMessage = automaticTest
            ? "生产检测"
            : "工程开发与调试";
    }
}
