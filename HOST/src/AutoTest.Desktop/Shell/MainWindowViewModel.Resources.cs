using System.Collections.ObjectModel;
using System.IO;
using AutoTest.Desktop.Projects;
using AutoTest.FlashLab.Programming;
using AutoTest.Specialists.Wave;
using Prism.Commands;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// One ECU project, four meaningful content types. This is a projection of the
/// actual engineering model, NOT a second asset store/recipe or execution engine.
/// File-oriented DBC, capture-oriented waveform standards, TEST-oriented flows
/// and native FLASH parameters each open their own real specialist environment.
/// </summary>
public sealed partial class MainWindowViewModel
{
    public DelegateCommand OpenEngineeringResourcesCommand { get; private set; } = null!;
    public DelegateCommand RefreshEngineeringResourcesCommand { get; private set; } = null!;
    public DelegateCommand NewEngineeringDbcCommand { get; private set; } = null!;
    public DelegateCommand<ResourceTreeNode> OpenEngineeringFlowCommand { get; private set; } = null!;

    public ObservableCollection<WaveStandardGroup> EngineeringWaveStandards { get; } = new();
    private string _engineeringWaveStatus = "打开工程后查看标准波形";
    private string _engineeringFlashStatus = "打开工程后查看烧录设置";

    public string EngineeringDbcName
    {
        get
        {
            var dbc = _workspace.Current?.Definition.Assets
                .Where(a => a.Kind == ProjectWorkspaceService.DbcKind).ToArray();
            return dbc?.Length switch
            {
                0 => "尚未选择 DBC",
                1 => dbc[0].Name + ".dbc",
                > 1 => "旧工程存在多份 DBC，需先整理",
                _ => "尚未打开工程"
            };
        }
    }

    public string EngineeringDbcDescription => _workspace.Current is null
        ? "打开 ECU 工程后选择 DBC"
        : "整套 TEST 共用此数据库，步骤直接选择报文和信号";

    public string EngineeringWaveStatus
    {
        get => _engineeringWaveStatus;
        private set => SetProperty(ref _engineeringWaveStatus, value);
    }

    public string EngineeringFlashStatus
    {
        get => _engineeringFlashStatus;
        private set => SetProperty(ref _engineeringFlashStatus, value);
    }

    public string EngineeringFlowStatus => PublicFlowNodes.Count == 0
        ? "还没有公共流程；仅在步骤需要复用时创建"
        : "已建立 " + PublicFlowNodes.Count + " 个公共流程，可直接被 TEST 调用";

    private void InitializeEngineeringResourceWorkbench()
    {
        OpenEngineeringResourcesCommand = new DelegateCommand(OpenEngineeringResources,
            () => HasProject && !IsBusy);
        RefreshEngineeringResourcesCommand =
            new DelegateCommand(async () => await RefreshEngineeringResourcesAsync());
        NewEngineeringDbcCommand = new DelegateCommand(async () =>
            await CreateDbcForProjectAsync(), () => HasProject && !IsBusy);
        OpenEngineeringFlowCommand = new DelegateCommand<ResourceTreeNode>(
            async node =>
            {
                if (node?.Asset?.Kind != ProjectWorkspaceService.PublicFlowKind) return;
                SelectResource(node);
                await OpenSelectedPublicFlowAsync();
            });
        _workspace.CurrentChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(EngineeringDbcName));
            RaisePropertyChanged(nameof(EngineeringDbcDescription));
            RaisePropertyChanged(nameof(EngineeringFlowStatus));
            OpenEngineeringResourcesCommand.RaiseCanExecuteChanged();
            NewEngineeringDbcCommand.RaiseCanExecuteChanged();
        };
    }

    private void OpenEngineeringResources()
    {
        if (_workspace.Current is null || IsBusy) return;
        _ = RefreshEngineeringResourcesAsync();
        ActivateEditor("engineering-resources",
            new EngineeringResourcesView { DataContext = this },
            "工程资源", () => true);
    }

    private async Task RefreshEngineeringResourcesAsync()
    {
        var project = _workspace.Current;
        if (project is null)
        {
            EngineeringWaveStandards.Clear();
            EngineeringWaveStatus = "打开 ECU 工程后查看标准波形";
            EngineeringFlashStatus = "打开 ECU 工程后查看烧录设置";
            return;
        }

        try
        {
            var root = project.RootDirectory;
            var standards = await Task.Run(() =>
                new WaveReferenceCatalog(Path.Combine(root, "wave-records", "references"))
                    .ListStandards().ToArray());
            var flash = await NativeFlashProfileStore.LoadAsync(root);
            if (_workspace.Current?.ManifestPath != project.ManifestPath) return;
            EngineeringWaveStandards.Clear();
            foreach (var standard in standards)
                EngineeringWaveStandards.Add(standard);
            EngineeringWaveStatus = standards.Length == 0
                ? "尚无标准波形；从真实采集结果建立标准"
                : "已建立 " + standards.Length + " 项标准；在波形工作台查看和分析多路信号";
            EngineeringFlashStatus = flash is null
                ? "尚未设置 HEX 与烧录参数"
                : flash.Profile.FirmwareRelativePath is null
                    ? "烧录参数已保存，尚未选择 HEX"
                    : (flash.Profile.FirmwareDisplayName ?? "已选择 HEX 固件") +
                      " · ECU CAN " + flash.Profile.EcuCanKbps + " kbit/s";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or FormatException or ArgumentException)
        {
            if (_workspace.Current?.ManifestPath != project.ManifestPath) return;
            EngineeringWaveStandards.Clear();
            EngineeringWaveStatus = "工程资源读取失败：" + ex.Message;
            EngineeringFlashStatus = "请在 FLASH 工具中检查当前参数";
        }
        RaisePropertyChanged(nameof(EngineeringDbcName));
        RaisePropertyChanged(nameof(EngineeringFlowStatus));
    }
}
