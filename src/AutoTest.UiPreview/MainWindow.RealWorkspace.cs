using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoTest.UiPreview;

public partial class MainWindow
{
    private IEditorWorkspaceGateway? _gateway;
    private string? _currentRealAsset;
    private bool _realProjectOpen;
    private bool _workspaceBusy;
    private string _realActiveTitle = "请选择测试程序";

    public bool IsRealWorkspace => _gateway is not null;
    public bool IsPreviewWorkspace => _gateway is null;
    public ObservableCollection<UiTestAsset> RealTestPrograms { get; } = new();
    public ObservableCollection<UiProjectAsset> RealResources { get; } = new();
    public string ProjectDisplayName { get; private set; } = "控制器 A";

    private void SetProjectOverview(UiProjectSnapshot project)
    {
        _realProjectOpen = true;
        ProjectDisplayName = project.Name;
        RealTestPrograms.Clear();
        foreach (var asset in project.TestPrograms) RealTestPrograms.Add(asset);
        RealResources.Clear();
        foreach (var resource in project.Resources) RealResources.Add(resource);
        Raise(nameof(ProjectDisplayName));
        Raise(nameof(ProjectDisplayTitle));
        RefreshRealExplorer();
    }

    private async Task LoadVerifiedDbcResourcesAsync()
    {
        if (_gateway is null) return;
        _dbcCatalogs = await _gateway.LoadDbcCatalogAsync();
        RefreshRealExplorer();
        ShowInspector();
    }

    private async Task<bool> ConfirmLeaveAsync()
    {
        if (!IsRealWorkspace || !_document.IsDirty || _currentRealAsset is null) return true;
        var choice = MessageBox.Show(this, "当前测试程序有尚未保存的修改。\n保存后继续？",
            "AutoTest · 未保存的修改", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.Yes) return await SaveRealTestAsync();
        _cases.Remove(_currentRealAsset); // No means explicitly discard, not silently cache.
        return true;
    }

    private void RealWorkspace_Closing(object? sender, CancelEventArgs e)
    {
        if (!IsRealWorkspace || !_document.IsDirty) return;
        var choice = MessageBox.Show(this, "有未保存的测试程序修改。确定放弃并退出吗？",
            "AutoTest · 未保存", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes) e.Cancel = true;
    }

    private void FileMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!IsRealWorkspace) { PreviewOnly_Click(sender, e); return; }
        if (sender is not Button button) return;
        var menu = new ContextMenu { PlacementTarget = button };
        var open = new MenuItem { Header = "打开工程…" };
        open.Click += Project_Click;
        var create = new MenuItem { Header = "新建工程…" };
        create.Click += NewProject_Click;
        var createTest = new MenuItem { Header = "新建测试程序…" };
        createTest.Click += CreateTest_Click;
        var save = new MenuItem { Header = "保存当前测试程序" };
        save.Click += Save_Click;
        menu.Items.Add(open);
        menu.Items.Add(create);
        menu.Items.Add(new Separator());
        menu.Items.Add(createTest);
        menu.Items.Add(save);
        menu.IsOpen = true;
    }

    private async void Project_Click(object sender, RoutedEventArgs e)
    {
        if (_gateway is null) { PreviewOnly_Click(sender, e); return; }
        if (_workspaceBusy) return;
        var dialog = new OpenFileDialog
        {
            Title = "打开 AutoTest 工程",
            Filter = "AutoTest 工程|project.autotest-project.json|JSON 文件|*.json",
            CheckFileExists = true,
            FileName = "project.autotest-project.json"
        };
        if (dialog.ShowDialog(this) != true || !await ConfirmLeaveAsync()) return;
        _workspaceBusy = true;
        try
        {
            _dbcCatalogs = [];
            SetProjectOverview(await _gateway.OpenProjectAsync(dialog.FileName));
            ResetRealDocuments();
            await LoadVerifiedDbcResourcesAsync();
            SetMessage("工程已打开：" + ProjectDisplayName + "；请选择或新建测试程序");
        }
        catch (Exception error) { ShowWorkspaceError("打开工程失败", error); }
        finally { _workspaceBusy = false; }
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (_gateway is null || _workspaceBusy) return;
        var name = EditorTextPrompt.Ask(this, "新建工程", "工程名称");
        if (name is null) return;
        var folder = new OpenFolderDialog { Title = "选择工程的父文件夹" };
        if (folder.ShowDialog(this) != true) return;
        if (!await ConfirmLeaveAsync()) return;
        _workspaceBusy = true;
        try
        {
            _dbcCatalogs = [];
            SetProjectOverview(await _gateway.CreateProjectAsync(name, folder.FolderName));
            ResetRealDocuments();
            await LoadVerifiedDbcResourcesAsync();
            SetMessage("工程已创建：" + ProjectDisplayName + "；可以新建测试程序");
        }
        catch (Exception error) { ShowWorkspaceError("新建工程失败", error); }
        finally { _workspaceBusy = false; }
    }

    private void ResetRealDocuments()
    {
        _cases.Clear();
        _document = new PreviewEditorDocument();
        _history = new PreviewEditHistory();
        _currentRealAsset = null;
        _realActiveTitle = "请选择测试程序";
        _targetRow = null;
        Raise(nameof(Rows));
        Raise(nameof(CurrentCaseTitle));
        Raise(nameof(CurrentCaseTabTitle));
        RefreshRealExplorer();
        RebuildRows();
    }

    private async void CreateTest_Click(object sender, RoutedEventArgs e)
    {
        if (_gateway is null || _workspaceBusy) return;
        if (!_realProjectOpen) { SetMessage("请先新建或打开工程"); return; }
        var name = EditorTextPrompt.Ask(this, "新建测试程序", "程序名称");
        if (name is null || !await ConfirmLeaveAsync()) return;
        _workspaceBusy = true;
        try
        {
            var project = await _gateway.CreateTestAsync(name);
            SetProjectOverview(project);
            var created = project.TestPrograms.SingleOrDefault(a => a.Name == name);
            if (created is not null)
                await ActivateRealTestAsync(created);
            SetMessage("已创建正式测试程序：" + name);
        }
        catch (Exception error) { ShowWorkspaceError("新建测试程序失败", error); }
        finally { _workspaceBusy = false; }
    }

    private async void ImportDbc_Click(object sender, RoutedEventArgs e)
    {
        if (_gateway is null || _workspaceBusy) return;
        if (!_realProjectOpen) { SetMessage("请先打开工程"); return; }
        var dialog = new OpenFileDialog { Title = "导入真实 DBC 文件",
            Filter = "DBC 网络数据库|*.dbc", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _workspaceBusy = true;
        try
        {
            SetProjectOverview(await _gateway.ImportDbcAsync(dialog.FileName));
            await LoadVerifiedDbcResourcesAsync();
            SetMessage("DBC 已导入，工程树和 CAN 步骤的报文、信号列表已更新");
        }
        catch (Exception error) { ShowWorkspaceError("DBC 导入失败", error); }
        finally { _workspaceBusy = false; }
    }

    private void OpenRealTest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: UiTestAsset asset }) OpenRealTestById(asset.Id);
    }

    private async void OpenRealTestById(string id)
    {
        if (_gateway is null || _workspaceBusy || id == _currentRealAsset) return;
        var asset = RealTestPrograms.FirstOrDefault(x => x.Id == id);
        if (asset is null || !await ConfirmLeaveAsync()) return;
        _workspaceBusy = true;
        try { await ActivateRealTestAsync(asset); }
        catch (Exception error) { ShowWorkspaceError("打开 TEST 失败", error); }
        finally { _workspaceBusy = false; }
    }

    private async Task ActivateRealTestAsync(UiTestAsset asset)
    {
        if (_gateway is null) return;
        if (!_cases.TryGetValue(asset.Id, out var session))
        {
            var snapshot = await _gateway.OpenTestAsync(asset.Id);
            var document = new PreviewEditorDocument();
            document.Phases.AddRange(snapshot.Phases);
            session = (document, new PreviewEditHistory());
            _cases[asset.Id] = session;
        }
        _document = session.Document;
        _history = session.History;
        _currentRealAsset = asset.Id;
        _realActiveTitle = asset.Name;
        ActivateExplorerTest(asset.Id);
        _targetRow = null;
        Raise(nameof(Rows));
        Raise(nameof(CurrentCaseTitle));
        Raise(nameof(CurrentCaseTabTitle));
        RebuildRows();
        SelectStep(_selectedStep ?? _document.AllSteps().FirstOrDefault());
        SetMessage("已打开正式 TEST：" + asset.Name);
    }

    private async void RenameRealTest_Click(object sender, RoutedEventArgs e)
    {
        if (_gateway is null || _workspaceBusy || _currentRealAsset is null) return;
        var id = _currentRealAsset;
        var name = EditorTextPrompt.Ask(this, "重命名测试程序资源", "新名称");
        if (name is null || name == _realActiveTitle || !await ConfirmLeaveAsync()) return;
        _workspaceBusy = true;
        try
        {
            SetProjectOverview(await _gateway.RenameTestAsync(id, name));
            _cases.Remove(id);
            var renamed = RealTestPrograms.Single(x => x.Id == id);
            await ActivateRealTestAsync(renamed);
            SetMessage("已重命名测试程序资源：" + name);
        }
        catch (Exception error) { ShowWorkspaceError("重命名失败", error); }
        finally { _workspaceBusy = false; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_gateway is null) { PreviewOnly_Click(sender, e); return; }
        await SaveRealTestAsync();
    }

    private async Task<bool> SaveRealTestAsync()
    {
        if (_gateway is null || _currentRealAsset is null)
        {
            SetMessage("没有已打开的正式测试程序");
            return false;
        }
        if (_workspaceBusy) { SetMessage("正在处理工程操作"); return false; }
        _workspaceBusy = true;
        try
        {
            if (!_document.IsDirty) { SetMessage("测试程序没有未保存的修改"); return true; }
            // Read and verify the pinned DBC assets before approving any new CAN bindings.
            await LoadVerifiedDbcResourcesAsync();
            var result = await _gateway.SaveTestAsync(_currentRealAsset, _document.Phases);
            _document.AcceptSaved();
            SetMessage("保存成功：" + result.Name);
            return true;
        }
        catch (Exception error) { ShowWorkspaceError("保存 TEST 失败", error); return false; }
        finally { _workspaceBusy = false; }
    }

    private void CheckIssues_Click(object sender, RoutedEventArgs e)
    {
        if (_gateway is null || _currentRealAsset is null)
        {
            SetMessage("正式工程检查需先打开 TEST 程序");
            return;
        }
        var problems = _gateway.Validate(_currentRealAsset, _document.Phases);
        var message = problems.Count == 0 ? "TEST 文档结构检查通过。\n未执行硬件预检。"
            : string.Join(Environment.NewLine, problems.Take(20));
        MessageBox.Show(this, message, "TEST 文档检查",
            MessageBoxButton.OK, problems.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        SetMessage(problems.Count == 0 ? "TEST 结构检查通过" : "TEST 检查发现 " + problems.Count + " 项问题");
    }

    private void ShowWorkspaceError(string title, Exception error)
    {
        SetMessage(title + "：" + error.Message);
        MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
