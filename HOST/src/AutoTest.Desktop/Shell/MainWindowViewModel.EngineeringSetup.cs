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
            ? "未设置正式型号：请在扫码与身份页完成"
            : "工程型号 " + project.Definition.EcuModelCode + "（已保存工程身份）";
        var test = "正在核对 TEST 资源…";
        var scan = "正在核对工位扫码设置…";
        var flash = "正在核对烧录发布版本…";
        var station = _hardware.IsConnected
            ? "主控 USB 已连接；夹具机械状态和 START 尚未现场验证"
            : "主控未连接；可先编辑配置与测试用例";
        UpdateSetupStatuses(model, test, scan, flash, station,
            "读取正式工程与工位配置中；不会触发任何实体动作。");

        try
        {
            try
            {
                var snapshot = await AutomaticTestResourceContract.ResolveAsync(project);
                test = snapshot.CanExecute
                    ? snapshot.Cases.Count + " 个正式用例的静态资源检查通过（尚未实体运行）"
                    : snapshot.Cases.Count + " 个用例；待修复：" +
                      string.Join("；", snapshot.Issues.Take(2));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                ProjectDataException or ArgumentException or FormatException or
                UnauthorizedAccessException or NotSupportedException)
            {
                test = "TEST 未就绪：" + error.Message;
            }

            try
            {
                var policy = await StationScanIdentityPolicyStore.LoadAsync();
                scan = policy is null ? "未配置条码规则；可打开扫码页试扫真实数据"
                    : "规则 " + policy.Format + " 已保存；" +
                      (_scannerWorkbench.IsConnected
                          ? "串口已连接，仍须真实扫码核对身份"
                          : "扫码设备未连接，仍须真实试扫");
                if (project.Definition.EcuModelCode is { Length: > 0 } code)
                {
                    var registration = await new StationModelCatalog().ResolveAsync(code);
                    if (!registration.Matched ||
                        !string.Equals(registration.Entry?.ManifestPath,
                            project.ManifestPath, StringComparison.OrdinalIgnoreCase))
                        scan += "；当前型号尚未登记为本工位唯一工程";
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException or FormatException or
                System.Text.Json.JsonException)
            {
                scan = "扫码配置需要修复：" + error.Message;
            }

            try
            {
                var published = await NativeFlashPublicationStore.LoadPublishedAsync(
                    project.RootDirectory);
                if (published is null)
                {
                    flash = File.Exists(NativeFlashProfileStore.FullProfilePath(project.RootDirectory))
                        ? "原生工艺只有工程草稿；尚未发布当前生产版"
                        : "尚未配置该工程的原生烧录工艺";
                }
                else
                {
                    flash = !string.Equals(published.Profile.EcuModelCode,
                        project.Definition.EcuModelCode, StringComparison.Ordinal)
                        ? "已发布烧录型号与工程不符，禁止正式运行"
                        : "已发布原生烧录配置（仅文件审查，不代表真实 ECU 烧录通过）";
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException or FormatException or
                NotSupportedException or System.Text.Json.JsonException)
            {
                flash = "原生烧录配置待修复：" + error.Message;
            }
        }
        finally
        {
            // A previous refresh must not overwrite a newly opened project.
            if (version == _setupRefreshId &&
                string.Equals(project.ManifestPath, _workspace.Current?.ManifestPath,
                    StringComparison.OrdinalIgnoreCase))
                UpdateSetupStatuses(model, test, scan, flash, station,
                    "已显示工程配置和可确认的工位事实。正式夹具/实体 START → 前置 Flash → TEST → RECORD 尚未完成整机现场验收，不能显示生产 PASS。");
        }
    }
}
