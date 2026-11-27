using System.IO;
using System.Collections.ObjectModel;
using AutoTest.Desktop.Testing;
using AutoTest.Desktop.Projects;
using AutoTest.FlashLab.Programming;
using AutoTest.Projects.Project;
using Prism.Commands;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Engineer's single project workflow overview. Readiness is derived from the
/// canonical project, native published flash and local station; this file does
/// not store another recipe, TEST selection or scanner-to-flash mapping.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private int _setupRefreshId;
    private string _setupModelStatus = "尚未检查";
    private string _setupTestStatus = "尚未检查";
    private string _setupTestIssueDetails = "请先打开 ECU 工程并进行资源预检。";
    public ObservableCollection<ResourceDependencyGroup> SetupDependencyGroups { get; } = new();
    public int SetupRootCauseCount => SetupDependencyGroups.Count;
    public string SetupDependencySummary => SetupRootCauseCount == 0
        ? "没有发现测试用例引用的资源缺失。是否通过检测，以实际执行结果为准。"
        : "发现 " + SetupRootCauseCount +
          " 组资源问题。展开查看受影响的用例。";
    private string _setupScanStatus = "尚未检查";
    private string _setupFlashStatus = "尚未检查";
    private string _setupStationStatus = "尚未检查";
    private string _setupOverallStatus = "工程和工位尚未核对。";
    public async Task OpenDependencyGroupAsync(ResourceDependencyGroup group)
    {
        if (_workspace.Current is null) return;
        if (group.InvocationId is Guid invocation)
        {
            await OpenFirstAssetAsync(ProjectWorkspaceService.TestProjectKind,
                CreateTestProjectAsync);
            if (ActiveEditorTab?.Editor is TestProjectWorkbenchViewModel project &&
                project.FocusInvocation(invocation))
            {
                StatusMessage = "已定位受影响的 TEST 调用实例。请检查对应定义、引用版本和步骤映射：" +
                    group.Title;
                return;
            }
        }
        if (group.ResourceId is Guid resource)
        {
            var asset = _workspace.Current.Definition.Assets.FirstOrDefault(x =>
                x.Id.Value == resource);
            if (asset is not null && asset.Kind != ProjectWorkspaceService.TestProjectKind)
            {
                SelectAsset(asset.Id);
                await OpenSelectedResourceAsync();
                StatusMessage = "已定位原始资源；请核对资产内容及受影响调用后再明确采用修订。";
                return;
            }
        }
        await OpenFirstAssetAsync(ProjectWorkspaceService.TestProjectKind,
            CreateTestProjectAsync);
        StatusMessage = "已打开正式 TEST 项目；此问题需在工程中核对：" + group.Title;
    }

    public DelegateCommand RefreshEngineeringSetupCommand { get; private set; } = null!;
    public DelegateCommand ReadEngineeringClampGpioCommand { get; private set; } = null!;
    public DelegateCommand EngineeringClamp1PressCommand { get; private set; } = null!;
    public DelegateCommand EngineeringClamp1ReleaseCommand { get; private set; } = null!;
    public DelegateCommand EngineeringClamp2PressCommand { get; private set; } = null!;
    public DelegateCommand EngineeringClamp2ReleaseCommand { get; private set; } = null!;
    public DelegateCommand EngineeringIgnOnCommand { get; private set; } = null!;
    public DelegateCommand EngineeringIgnOffCommand { get; private set; } = null!;
    private int _engineeringOperationBusy;

    /// <summary>
    /// Real, one-shot engineering verification using exactly the same manual
    /// device calls as the existing bench. This NEVER saves a production
    /// fixture procedure or grants a TEST run authorization.
    /// </summary>
    private async Task RunEngineeringClampAsync(byte clamp, bool press)
    {
        if (System.Threading.Interlocked.Exchange(ref _engineeringOperationBusy, 1) != 0)
            return;
        try
        {
            if (!_hardware.IsConnected)
                throw new IOException("主控 USB 未连接。");
            if (!_hardware.HasV02GpioContract)
                throw new InvalidOperationException(
                    "当前主控未确认夹具协议，禁止向电磁阀发送动作。");
            var consent = "AUTHORIZE CLAMP " + clamp + (press ? " PRESS" : " RELEASE");
            await _hardware.SendManualClampAsync(clamp, press, consent);
            EngineeringClampReadback = "夹具" + clamp + " 已向主控请求" +
                (press ? "压紧" : "释放") + "，USB ACK 已返回；正在读取 MCU GPIO。";
            await ReadEngineeringClampGpioAsync();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
            InvalidDataException or TimeoutException or UnauthorizedAccessException)
        {
            EngineeringClampReadback = "夹具" + clamp + " 实体操作未确认，实际状态 UNKNOWN：" +
                error.Message;
        }
        finally { System.Threading.Interlocked.Exchange(ref _engineeringOperationBusy, 0); }
    }

    private async Task RunEngineeringIgnAsync(bool on)
    {
        if (System.Threading.Interlocked.Exchange(ref _engineeringOperationBusy, 1) != 0)
            return;
        try
        {
            if (!_hardware.IsConnected)
                throw new IOException("主控 USB 未连接。");
            await _hardware.SendManualIgnAsync(on,
                "AUTHORIZE IGN " + (on ? "ON" : "OFF"));
            var level = await _hardware.ReadIgnLevelAsync();
            EngineeringClampReadback = "IGN " + (on ? "ON" : "OFF") +
                " 请求已由主控 ACK；MCU GPIO 回读为 " +
                (level ? "HIGH" : "LOW") +
                "。不代表 ECU 的 IGN 引脚实测电压。";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
            InvalidDataException or TimeoutException or UnauthorizedAccessException)
        {
            EngineeringClampReadback = "IGN 请求状态 UNKNOWN：" + error.Message;
        }
        finally { System.Threading.Interlocked.Exchange(ref _engineeringOperationBusy, 0); }
    }

    private string _engineeringClampReadback = "尚未在本次工位会话读取夹具 MCU GPIO。";
    public string EngineeringClampReadback
    {
        get => _engineeringClampReadback;
        private set => SetProperty(ref _engineeringClampReadback, value);
    }

    private async Task ReadEngineeringClampGpioAsync()
    {
        if (!_hardware.IsConnected)
        {
            EngineeringClampReadback = "USB 主控未连接；没有读取任何 GPIO。";
            return;
        }
        if (!_hardware.HasV02GpioContract)
        {
            EngineeringClampReadback =
                "主控尚未确认夹具协议版本；不发送夹具读取或控制命令。";
            return;
        }
        try
        {
            var value = await _hardware.ReadClampLevelsAsync();
            EngineeringClampReadback = "本次真实 MCU GPIO 回读：" +
                "夹具1=" + ((value & 1) != 0 ? "HIGH" : "LOW") +
                "；夹具2=" + ((value & 2) != 0 ? "HIGH" : "LOW") +
                "。这不是机械到位信号，不能宣告工位就绪。";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
            InvalidDataException or TimeoutException or UnauthorizedAccessException)
        {
            EngineeringClampReadback = "读取失败，实际夹具状态 UNKNOWN：" + error.Message;
        }
    }


    public string SetupModelStatus => _setupModelStatus;
    public string SetupTestStatus => _setupTestStatus;
    /// <summary>Exact authoring resource blockers returned by the one
    /// canonical TEST resolver. Diagnostic only: never creates an alternate
    /// test plan or changes physical execution permissions.</summary>
    public string SetupTestIssueDetails => _setupTestIssueDetails;
    public string SetupResourcesStatus
    {
        get
        {
            var assets = _workspace.Current?.Definition.Assets;
            if (assets is null) return "请先打开 ECU 工程。";
            var dbc = assets.Value.Count(x => x.Kind == ProjectWorkspaceService.DbcKind);
            var wave = assets.Value.Count(x =>
                x.Kind is ProjectWorkspaceService.WaveKind or
                    ProjectWorkspaceService.WaveSettingsKind);
            return "DBC " + dbc + " 份 · 波形/判定资源 " + wave +
                   " 项。只在实际用到时导入；测试步骤直接引用资源。";
        }
    }
    public string SetupScanStatus => _setupScanStatus;
    public string SetupFlashStatus => _setupFlashStatus;
    public string SetupStationStatus => _setupStationStatus;
    public string SetupOverallStatus => _setupOverallStatus;

    private void UpdateSetupStatuses(string model, string test, string scan,
        string flash, string station, string overall)
    {
        _setupModelStatus = model;
        _setupTestStatus = test;
        _setupScanStatus = scan;
        _setupFlashStatus = flash;
        _setupStationStatus = station;
        _setupOverallStatus = overall;
        RaisePropertyChanged(nameof(SetupModelStatus));
        RaisePropertyChanged(nameof(SetupTestStatus));
        RaisePropertyChanged(nameof(SetupResourcesStatus));
        RaisePropertyChanged(nameof(SetupScanStatus));
        RaisePropertyChanged(nameof(SetupFlashStatus));
        RaisePropertyChanged(nameof(SetupStationStatus));
        RaisePropertyChanged(nameof(SetupOverallStatus));
    }

    public async Task RefreshEngineeringSetupAsync()
    {
        var version = ++_setupRefreshId;
        var project = _workspace.Current;
        if (project is null)
        {
            _setupTestIssueDetails = "请先打开 ECU 工程。";
            RaisePropertyChanged(nameof(SetupTestIssueDetails));
            SetupDependencyGroups.Clear();
            RaisePropertyChanged(nameof(SetupRootCauseCount));
            RaisePropertyChanged(nameof(SetupDependencySummary));
            UpdateSetupStatuses("请先打开 ECU 工程", "未打开工程",
                "等待工程", "等待工程", "待连接",
                "先创建 ECU 工程并编辑 TEST、DBC 和原生 FLASH；生产检测根据扫码直接选择工程。");
            return;
        }
        // Never claim a live result based on a resource file or USB ACK.
        var model = string.IsNullOrWhiteSpace(project.Definition.EcuModelCode)
            ? "尚未设置 ECU 型号代码；编辑测试用例不受影响，正式烧录前需要匹配。"
            : "当前工程型号：" + project.Definition.EcuModelCode;
        var test = "正在核对 TEST 资源…";
        var testDetails = "正在解析 TEST 项目中的资源引用。";
        IReadOnlyList<ResourceDependencyGroup> problemGroups =
            Array.Empty<ResourceDependencyGroup>();
        var scan = "正在核对工位扫码设置…";
        var flash = "正在读取烧录配置…";
        var station = _hardware.IsConnected
            ? "主控 USB 已连接；夹具机械状态和 START 尚未现场验证"
            : "主控未连接；可先编辑配置与测试用例";
        var productionPin = "正在读取本机生产工程库";
        UpdateSetupStatuses(model, test, scan, flash, station,
            "读取正式工程与工位配置中；不会触发任何实体动作。");

        try
        {
            try
            {
                var snapshot = await AutomaticTestResourceContract.ResolveAsync(project);
                problemGroups = snapshot.DependencyGroups;
                testDetails = snapshot.Issues.Count == 0
                    ? "未发现当前 TEST 项目的静态资源阻断。仍需在真实 ECU 上验证功能与数据质量。"
                    : string.Join(Environment.NewLine,
                        snapshot.Issues.Select((issue, i) => $"{i + 1}. {issue}"));
                test = snapshot.CanExecute
                    ? snapshot.Cases.Count + " 个用例的资源检查通过；实际结果需运行测试确认"
                    : snapshot.Cases.Count + " 个有效引用 · " +
                      snapshot.Issues.Count + " 项资源阻断；打开 TEST 项目检查引用与步骤。" +
                      "所需资源未齐备前不能执行完整测试。";
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                ProjectDataException or ArgumentException or FormatException or
                UnauthorizedAccessException or NotSupportedException)
            {
                test = "TEST 未就绪：" + error.Message;
                testDetails = "无法完成 TEST 资源解析：" + error.Message;
            }

            try
            {
                // Production routing is independent of the opened engineering
                // project. No station barcode release/configuration in PROJECT.
                var installed = await _productionCatalog.ListAsync();
                var routes = await _productionCatalog.ListBindingsAsync();
                scan = installed.Count + " 个已安装生产工程版本、" +
                    routes.Count + " 条固定标记关联；" +
                    (_scannerWorkbench.IsConnected ? "扫码枪 COM 已连接。" :
                        "扫码枪 COM 未连接。");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException or FormatException or
                System.Text.Json.JsonException)
            {
                scan = "生产工程库需要检查：" + error.Message;
            }

            try
            {
                var saved = await NativeFlashProfileStore.LoadAsync(project.RootDirectory);
                if (saved is null)
                    flash = "尚未配置该工程的原生烧录工艺";
                else if (!string.Equals(saved.Profile.EcuModelCode,
                    project.Definition.EcuModelCode, StringComparison.Ordinal))
                    flash = "烧录型号与工程不符，不能正式运行";
                else
                {
                    var draftJob = await Task.Run(() =>
                        NativeFlashJobBuilder.Freeze(saved.Profile, project.RootDirectory));
                    flash = "原生 FLASH 配置已保存 · 固件与参数可创建实际烧录计划；" +
                        "最终烧录与 APP 后验以真实 ECU 运行结果为准。";
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException or FormatException or
                NotSupportedException or System.Text.Json.JsonException)
            {
                flash = "原生烧录配置待修复：" + error.Message;
            }

            try
            {
                var installed = await _productionCatalog.ListAsync();
                productionPin = installed.Any(x =>
                    x.ProjectId == project.Definition.Id.Value)
                    ? "本型号工程已在本机生产工程库中；切换到生产检测可直接扫码匹配。"
                    : "当前工程已保存；进入生产检测时自动复制到本机工程库。";
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException or NotSupportedException or
                ProjectDataException or FormatException or System.Text.Json.JsonException)
            {
                productionPin = "本机生产工程库需要检查：" + error.Message;
            }
        }
        finally
        {
            // A previous refresh must not overwrite a newly opened project.
            if (version == _setupRefreshId &&
                string.Equals(project.ManifestPath, _workspace.Current?.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _setupTestIssueDetails = testDetails;
                RaisePropertyChanged(nameof(SetupTestIssueDetails));
                SetupDependencyGroups.Clear();
                foreach (var group in problemGroups) SetupDependencyGroups.Add(group);
                RaisePropertyChanged(nameof(SetupRootCauseCount));
                RaisePropertyChanged(nameof(SetupDependencySummary));
                UpdateSetupStatuses(model, test, scan, flash, station,
                    productionPin);
            }
        }
    }
}
