using System.IO;
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
    private string _setupScanStatus = "尚未检查";
    private string _setupFlashStatus = "尚未检查";
    private string _setupStationStatus = "尚未检查";
    private string _setupOverallStatus = "工程和工位尚未核对。";
    public DelegateCommand OpenInspectionProcedureCommand { get; private set; } = null!;

    private async Task OpenInspectionProcedureAsync()
    {
        var project = _workspace.Current;
        if (project is null || _realTest.IsRunning || _hardwareBusy)
        {
            StatusMessage = "请先打开工程并停止正在执行的设备任务，再编辑工位动作。";
            return;
        }
        try
        {
            var candidates = project.Definition.Assets.Where(x =>
                x.Kind == ProjectWorkspaceService.InspectionKind).ToArray();
            if (candidates.Length > 1)
                throw new InvalidDataException(
                    "同一工程存在多个工位过程配置，请先处理资源冲突，不能自动选择。");
            var asset = candidates.Length == 1 ? candidates[0]
                : await _workspace.CreateConfigurationAsync(
                    ProjectWorkspaceService.InspectionKind, "当前检测工位动作");
            var session = await _workspace.OpenConfigurationAsync(asset.Id);
            ShowProjectConfiguration();
            var view = OpenEditors.FirstOrDefault(x => x.Key == "config-overview")?.View
                as ProjectConfigurationOverviewView;
            view?.ShowInspectionSession(session);
            StatusMessage = "已打开工程内受控工位动作配置。实体操作另行使用真实工位按钮。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            ArgumentException or InvalidOperationException or ProjectDataException or
            UnauthorizedAccessException or FormatException)
        {
            StatusMessage = "工位动作配置无法打开：" + error.Message;
        }
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
            UpdateSetupStatuses("请先打开 ECU 工程", "未打开工程",
                "等待工程", "等待工程", "待连接",
                "打开工程后，在同一个检测准备工作台逐项配置和真实验证。");
            return;
        }
        // Never claim a live result based on a resource file or USB ACK.
        var model = string.IsNullOrWhiteSpace(project.Definition.EcuModelCode)
            ? "尚未设置 ECU 型号代码；编辑测试用例不受影响，正式烧录前需要匹配。"
            : "当前工程型号：" + project.Definition.EcuModelCode;
        var test = "正在核对 TEST 资源…";
        var scan = "正在核对工位扫码设置…";
        var flash = "正在读取烧录配置…";
        var station = _hardware.IsConnected
            ? "主控 USB 已连接；夹具机械状态和 START 尚未现场验证"
            : "主控未连接；可先编辑配置与测试用例";
        var productionPin = "尚未确认本工位的生产版本";
        UpdateSetupStatuses(model, test, scan, flash, station,
            "读取正式工程与工位配置中；不会触发任何实体动作。");

        try
        {
            try
            {
                var snapshot = await AutomaticTestResourceContract.ResolveAsync(project);
                test = snapshot.CanExecute
                    ? snapshot.Cases.Count + " 个正式用例的静态资源检查通过（仍需实体运行）"
                    : snapshot.Cases.Count + " 个有效引用 · " +
                      snapshot.Issues.Count + " 项资源阻断；打开 TEST 项目检查引用与步骤。" +
                      "所需资源未齐备前不能执行完整测试。";
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                ProjectDataException or ArgumentException or FormatException or
                UnauthorizedAccessException or NotSupportedException)
            {
                test = "TEST 未就绪：" + error.Message;
            }

            try
            {
                var published = await StationProductionConfigurationStore.LoadPublishedAsync();
                var draft = await StationProductionConfigurationStore.LoadDraftAsync();
                scan = published is null
                    ? "工位编码配置草稿：" + draft.Rules.Count + " 条规则；尚未发布。" +
                      "请在“生产准备”中绑定型号与工程、批量试扫并发布。"
                    : "已发布工位编码规则 " +
                      published.Configuration.Rules.Count(x => x.Enabled) +
                      " 条，关联完整工程 " + published.Projects.Count + " 个；" +
                      (_scannerWorkbench.IsConnected ? "真实 COM 已连接" :
                       "COM 未连接（不妨碍离线试扫）。");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException or FormatException or
                System.Text.Json.JsonException or ProjectDataException)
            {
                scan = "扫码配置需要修复：" + error.Message;
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
                    var published = await NativeFlashPublicationStore.LoadPublishedAsync(
                        project.RootDirectory);
                    flash = published is null
                        ? "烧录草稿校验通过；尚未确认用于生产。"
                        : string.Equals(published.ProfileSha256, saved.ProfileSha256,
                              StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(published.JobFingerprint, draftJob.Fingerprint,
                              StringComparison.OrdinalIgnoreCase)
                            ? "烧录草稿与已发布工艺一致；仍需现场 ECU/APP 后验。"
                            : "烧录草稿已更改，与已发布生产工艺不同；" +
                              "旧版生产仍固定使用已发布版本，确认后新草稿才生效。";
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
                var verified = await StationDeploymentStore.RestoreAsync();
                productionPin = verified is null
                    ? "当前工程尚未确认用于正式生产；请核对后点击确认。"
                    : string.Equals(verified.Project.ManifestPath, project.ManifestPath,
                          StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(verified.Project.ManifestSha256, project.ManifestSha256,
                          StringComparison.OrdinalIgnoreCase)
                        ? "此 ECU 工程及发布工艺已经本工位锁定；" +
                          "实体 START、实际夹具到位和生产 RECORD 仍未验收。"
                        : "工位绑定的是另一版 ECU 工程；请核对当前工程后重新确认。";
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException or NotSupportedException or
                ProjectDataException or FormatException or System.Text.Json.JsonException)
            {
                productionPin = "当前生产配置已失效：" + error.Message;
            }
        }
        finally
        {
            // A previous refresh must not overwrite a newly opened project.
            if (version == _setupRefreshId &&
                string.Equals(project.ManifestPath, _workspace.Current?.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
                UpdateSetupStatuses(model, test, scan, flash, station,
                    productionPin);
        }
    }
}
