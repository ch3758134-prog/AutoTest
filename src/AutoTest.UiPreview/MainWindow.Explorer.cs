using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace AutoTest.UiPreview;

/// <summary>
/// One project explorer presentation for both sample preview and real projects.
/// Only the data source changes; no second set of tree XAML is kept.
/// </summary>
public partial class MainWindow
{
    public ObservableCollection<UiExplorerTest> ExplorerTests { get; } = new();
    public ObservableCollection<UiExplorerGroup> ExplorerResourceGroups { get; } = new();
    public PreviewCanInspector CanInspector { get; } = new();
    private IReadOnlyList<UiDbcCatalog> _dbcCatalogs = [];
    private UiExplorerTest? _activeTest;

    public UiExplorerTest? ActiveTest
    {
        get => _activeTest;
        private set { _activeTest = value; Raise(nameof(ActiveTest)); }
    }
    public string ProjectDisplayTitle => "▣  " + ProjectDisplayName;
    public string ExplorerCountLabel =>
        ExplorerTests.Count(t => t.Selected) + "/" + ExplorerTests.Count;
    public string TestGroupName => "▾  ▱  " + (IsRealWorkspace ? "测试程序" : "研发检查项目");

    private void AddExplorerTest(UiExplorerTest item)
    {
        item.PropertyChanged += ExplorerTest_PropertyChanged;
        ExplorerTests.Add(item);
    }

    private void ExplorerTest_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UiExplorerTest.Selected))
            Raise(nameof(ExplorerCountLabel));
    }

    private void ActivateExplorerTest(string? id)
    {
        foreach (var item in ExplorerTests) item.Active = item.Id == id;
        ActiveTest = ExplorerTests.FirstOrDefault(x => x.Id == id);
    }

    private void InitializePreviewExplorer()
    {
        ExplorerTests.Clear();
        AddExplorerTest(new UiExplorerTest("main", "通信与状态检查"));
        AddExplorerTest(new UiExplorerTest("diagnostic", "诊断信息读取"));
        ExplorerResourceGroups.Clear();
        // Counts belong to the explicit frozen HTML visual fixture, never to real projects.
        ExplorerResourceGroups.Add(new UiExplorerGroup("DBC 报文", [], 13));
        ExplorerResourceGroups.Add(new UiExplorerGroup("按键配置", [], 2));
        ExplorerResourceGroups.Add(new UiExplorerGroup("波形参考", [], 1));
        ExplorerResourceGroups.Add(new UiExplorerGroup("UDS 服务", [], 2));
        ActivateExplorerTest("main");
        Raise(nameof(ExplorerCountLabel));
    }

    private void RefreshRealExplorer()
    {
        if (!IsRealWorkspace) return;
        var selected = ExplorerTests.ToDictionary(t => t.Id, t => t.Selected);
        ExplorerTests.Clear();
        foreach (var asset in RealTestPrograms)
        {
            var row = new UiExplorerTest(asset.Id, asset.Name);
            if (selected.TryGetValue(asset.Id, out var checkedBefore)) row.Selected = checkedBefore;
            AddExplorerTest(row);
        }
        ActivateExplorerTest(_currentRealAsset);

        var assets = RealResources.Select(asset => new UiExplorerAsset(
            asset.Id, asset.Name, asset.Kind,
            _dbcCatalogs.FirstOrDefault(c => c.AssetId == asset.Id)?.Messages ?? [])).ToArray();
        ExplorerResourceGroups.Clear();
        AddGroup("DBC 报文", assets, "dbc");
        AddGroup("按键配置", assets, "controller");
        AddGroup("波形参考", assets, "wave", "wave-settings");
        AddGroup("UDS 服务", assets, "uds", "uds-service");
        var remaining = assets.Where(a => !new[]
        { "dbc", "controller", "wave", "wave-settings", "uds", "uds-service" }.Contains(a.Kind))
            .ToArray();
        if (remaining.Length != 0) ExplorerResourceGroups.Add(new UiExplorerGroup("其他资源", remaining));
        Raise(nameof(ExplorerCountLabel));
        Raise(nameof(TestGroupName));
    }

    private void AddGroup(string title, IReadOnlyList<UiExplorerAsset> assets, params string[] kinds)
    {
        ExplorerResourceGroups.Add(new UiExplorerGroup(title,
            assets.Where(a => kinds.Contains(a.Kind, StringComparer.OrdinalIgnoreCase)).ToArray()));
    }

    private void OpenExplorerTest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: UiExplorerTest entry }) return;
        if (!IsRealWorkspace) SwitchCase(entry.Id);
        else OpenRealTestById(entry.Id);
    }

    private void SelectDbcMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: UiDbcMessage message }) return;
        if (!IsRealWorkspace || _selectedStep?.Kind != "CanSend")
        {
            SetMessage("请先选中一个 CAN 发送步骤，再选择工程 DBC 报文。");
            return;
        }
        RememberUndo();
        CanInspector.SelectedMessage = message;
        SetMessage("已选择报文：" + message.Label);
    }

    private void ActiveTestTab_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTest is null) return;
        if (!IsRealWorkspace) SwitchCase(ActiveTest.Id);
    }

    private void ProjectOverviewTab_Click(object sender, RoutedEventArgs e)
    {
        SetMessage(IsRealWorkspace
            ? "当前工程：" + ProjectDisplayName + "；测试程序 " + RealTestPrograms.Count +
              " 个，工程资源 " + RealResources.Count + " 个。"
            : "工程概览是纯 UI 演示页签。");
    }

    private void TestProjectTab_Click(object sender, RoutedEventArgs e)
    {
        SetMessage(IsRealWorkspace
            ? "请在左侧工程树选择测试程序。" : "研发检查项目是纯 UI 演示页签。");
    }

    private void ProjectConfigurationTab_Click(object sender, RoutedEventArgs e)
    {
        SetMessage(IsRealWorkspace
            ? "工程配置编辑器仍待正式服务接入；不使用示例配置。"
            : "工程配置是纯 UI 演示页签。");
    }
}
