using System.Windows.Data;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AutoTest.TestHiFi;

public partial class MainWindow
{
    public ObservableCollection<UiAsset> Assets { get; } = [];
    public ObservableCollection<WorkspacePage> OpenPages { get; } = [];
    public ObservableCollection<string> ToolTasks { get; } = [];
    private UiWorkspaceManifest? _workspace;
    private string? _workspacePath;
    private UiAsset? _selectedAsset;
    private UiFileDocument? _activeFile;
    private WorkspacePage? _activePage;
    private UiAsset? _copiedAsset;
    private string _navigatorMode = "cases";
    private bool _restoringSession;
    private LocalSession _session = new();
    private Dictionary<string,string> _recovery = [];
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };

    private sealed record WorkspaceUndo(Action Undo, Action Redo, string Title);
    private readonly Stack<WorkspaceUndo> _projectUndo = new();
    private readonly Stack<WorkspaceUndo> _projectRedo = new();
    private WorkspacePage? _previewPage;

    public UiFileDocument? ActiveFile
    {
        get => _activeFile;
        private set { _activeFile = value; Raise(nameof(ActiveFile)); }
    }
    private string _projectBreadcrumb = "未打开 UI 工程";
    public string ProjectBreadcrumb
    {
        get => _projectBreadcrumb;
        private set { _projectBreadcrumb = value; Raise(nameof(ProjectBreadcrumb)); }
    }
    private string _projectStatusText = "   │   UI 工作区未打开   │   TEST 视觉样本";
    public string ProjectStatusText
    {
        get => _projectStatusText;
        private set { _projectStatusText = value; Raise(nameof(ProjectStatusText)); }
    }
    private string _toolTitle = "";
    public string ToolTitle { get => _toolTitle; private set { _toolTitle = value; Raise(nameof(ToolTitle)); } }
    private string _toolIntro = "";
    public string ToolIntro { get => _toolIntro; private set { _toolIntro = value; Raise(nameof(ToolIntro)); } }

    private void SetupWorkspace()
    {
        _previewPage = new WorkspacePage { Key = "TEST", Kind = "TEST_PREVIEW", Title = "测试程序 · 视觉基线" };
        OpenPages.Add(_previewPage);
        _recoveryTimer.Tick += (_, _) => { _recoveryTimer.Stop(); UpdateRecovery(); };
        _restoringSession = true; // Do not overwrite the stored session before Loaded restores it.
        ShowPage(_previewPage);
    }

    private static bool IsFileError(Exception e) => e is IOException or UnauthorizedAccessException
        or ArgumentException or InvalidDataException or JsonException or NotSupportedException;

    private void FileFailure(string action, Exception e)
    {
        StatusText = action + "失败：" + e.Message;
        MessageBox.Show(this, e.Message, action + "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool NeedProject()
    {
        if (_workspace is not null && _workspacePath is not null) return true;
        MessageBox.Show(this, "请先通过“新建工程”或“打开工程”进入本地 UI 工作区。\n" +
            "注意：本批 *.atui-project.json 是界面与文件操作草稿格式，不是正式 AutoTest 工程。",
            "尚未打开工作区", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void SaveManifest()
    {
        if (_workspace is null || _workspacePath is null) throw new InvalidOperationException("未打开工程");
        _workspace.Assets = Assets.ToList();
        WorkspaceStorage.SaveManifest(_workspacePath, _workspace);
        ProjectBreadcrumb = _workspace.Name;
        ProjectStatusText = "   │   UI 工程：" + _workspace.Name + "   │   文件 " + Assets.Count + " 项";
    }

    private void RememberProject(string path)
    {
        _session.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _session.RecentProjects.Insert(0, path);
        if (_session.RecentProjects.Count > 9) _session.RecentProjects.RemoveRange(9,_session.RecentProjects.Count-9);
        RecentProjectsMenu.Items.Clear();
        foreach (var recent in _session.RecentProjects.Where(File.Exists))
        {
            var captured = recent;
            var item = new MenuItem { Header = Path.GetFileName(recent) + "   ·   " + Path.GetDirectoryName(recent),
                ToolTip = recent };
            item.Click += (_, _) => OpenProjectAt(captured);
            RecentProjectsMenu.Items.Add(item);
        }
        if (RecentProjectsMenu.Items.Count == 0)
            RecentProjectsMenu.Items.Add(new MenuItem { Header="无最近工程", IsEnabled=false });
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (!CanLeaveCurrentProject()) return;
        var name = new NameDialog(this, "新建 UI 工程", "输入 UI 工程名称（与正式 AutoTest 工程文件完全分离）",
                                  "新建测试工作区");
        if (name.ShowDialog() != true) return;
        var pick = new SaveFileDialog
        {
            Title = "选择新建 UI 工程文件的位置",
            Filter = "AutoTest UI 工程 (*.atui-project.json)|*.atui-project.json",
            FileName = name.Value + WorkspaceStorage.Extension,
            AddExtension = true, OverwritePrompt = true
        };
        if (pick.ShowDialog(this) != true) return;
        try
        {
            if (File.Exists(pick.FileName))
            {
                MessageBox.Show(this, "该工程文件已经存在。请使用“打开工程”，避免覆盖已有资源。",
                    "文件已存在", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var created = WorkspaceStorage.New(name.Value);
            WorkspaceStorage.SaveManifest(pick.FileName, created);
            LoadProject(created, pick.FileName);
            StatusText = "已在磁盘创建 UI 工程：" + pick.FileName;
        }
        catch (Exception ex) when (IsFileError(ex)) { FileFailure("新建工程",ex); }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!CanLeaveCurrentProject()) return;
        var pick = new OpenFileDialog
        {
            Title = "打开 AutoTest UI 工作区",
            Filter = "AutoTest UI 工程 (*.atui-project.json)|*.atui-project.json",
            CheckFileExists = true
        };
        if (pick.ShowDialog(this) == true) OpenProjectAt(pick.FileName, alreadyConfirmed:true);
    }

    private void OpenProjectAt(string path, bool alreadyConfirmed = false)
    {
        if (!alreadyConfirmed && !CanLeaveCurrentProject()) return;
        try
        {
            var manifest = WorkspaceStorage.OpenManifest(path);
            LoadProject(manifest,path);
            StatusText = "已从磁盘打开 UI 工程：" + path;
        }
        catch (Exception ex) when (IsFileError(ex)) { FileFailure("打开工程",ex); }
    }

    private void LoadProject(UiWorkspaceManifest manifest, string path)
    {
        _workspace = manifest; _workspacePath = Path.GetFullPath(path);
        foreach (var page in OpenPages.Where(p=>p.Kind=="FILE").ToArray())
        {
            DetachPage(page);
            OpenPages.Remove(page);
        }
        _projectUndo.Clear(); _projectRedo.Clear();
        Assets.Clear();
        foreach (var asset in manifest.Assets) Assets.Add(asset);
        ProjectBreadcrumb = manifest.Name;
        ProjectStatusText = "   │   UI 工程：" + manifest.Name + "   │   文件 " + Assets.Count + " 项";
        _session.ProjectPath = _workspacePath;
        RememberProject(_workspacePath);
        ShowNavigator("resources");
        ShowPage(_previewPage!);
        SaveSessionState();
    }

    private bool CanLeaveCurrentProject()
    {
        var dirty = OpenPages.Where(p=>p.Document?.Changed==true).ToArray();
        if (dirty.Length==0) return true;
        var choice = MessageBox.Show(this, "有 " + dirty.Length +
            " 个未保存的 UI 资源。\n“是”保存全部，“否”放弃更改，“取消”继续编辑。",
            "未保存的文件", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.Yes) return SaveAllDocuments();
        // Explicit discard: no recovered copy should later pretend to be an intended edit.
        foreach (var page in dirty)
            if (page.Document is { } d)
            {
                _recovery.Remove(d.FilePath);
                d.LoadContent(d.LastSavedContent); // Explicit discard must not later resurrect a draft.
            }
        try { WorkspaceStorage.SaveRecovery(_recovery); }
        catch (Exception ex) when(IsFileError(ex)) { FileFailure("清理恢复记录", ex); return false; }
        return true;
    }

    private void CloseProject_Click(object sender, RoutedEventArgs e)
    {
        if (!CanLeaveCurrentProject()) return;
        _workspace = null; _workspacePath = null; _session.ProjectPath = null;
        foreach (var page in OpenPages.Where(p=>p.Kind=="FILE").ToArray())
        {
            DetachPage(page); OpenPages.Remove(page);
        }
        Assets.Clear(); _projectUndo.Clear(); _projectRedo.Clear();
        ProjectBreadcrumb = "未打开 UI 工程";
        ProjectStatusText = "   │   UI 工程未打开   │   TEST 视觉样本";
        ShowPage(_previewPage!); ShowNavigator("cases"); SaveSessionState();
        StatusText = "已关闭 UI 工程。";
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void NewResource_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedProject()) return;
        var dialog = new NameDialog(this, "新建 UI 资源", "资源名称与资源类型", "新建测试程序", true);
        if (dialog.ShowDialog() != true) return;
        CreateResource(dialog.Value, dialog.Kind, null);
    }

    private void CreateResource(string name, string kind, string? content)
    {
        if (!NeedProject()) return;
        if (Assets.Any(a=>a.Kind == kind && string.Equals(a.Name,name,StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "同类资源已存在同名对象，请更换名称。","重名",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        try
        {
            var target = WorkspaceStorage.UniqueFilePath(_workspacePath!,kind);
            var asset = new UiAsset { Name = name, Kind = kind,
                RelativePath = Path.GetRelativePath(Path.GetDirectoryName(_workspacePath!)!,target) };
            WorkspaceStorage.WriteAtomic(target,content ?? WorkspaceStorage.DefaultText(asset));
            Assets.Add(asset);
            try { SaveManifest(); }
            catch { Assets.Remove(asset); if (File.Exists(target)) File.Delete(target); throw; }
            RecordProjectUndo("新建资源 " + name,
                () => { MoveAssetToTrash(asset); Assets.Remove(asset); SaveManifest(); },
                () => { RestoreAssetFromTrash(asset); Assets.Add(asset); SaveManifest(); });
            OpenAsset(asset);
            StatusText = "已创建文件：" + target;
        }
        catch (Exception ex) when (IsFileError(ex)) { FileFailure("创建资源",ex); }
    }

    private void ImportResource_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedProject()) return;
        var pick = new OpenFileDialog
        {
            Filter = "可编辑文本资源 (*.dbc;*.json;*.csv;*.txt;*.md)|*.dbc;*.json;*.csv;*.txt;*.md|所有文件 (*.*)|*.*",
            Title = "导入资源文件（复制到 UI 工程，不更改原文件）",
            CheckFileExists = true
        };
        if (pick.ShowDialog(this) != true) return;
        try
        {
            var ext = Path.GetExtension(pick.FileName).ToLowerInvariant();
            if (ext is not (".dbc" or ".json" or ".csv" or ".txt" or ".md"))
                throw new InvalidDataException("此 UI 批次只允许导入文本资源，不能把 HEX/DLL/二进制当作文本保存。");
            var content = WorkspaceStorage.ReadText(pick.FileName);
            var kind = ext switch { ".dbc"=>"DBC", ".csv"=>"WAVE", _=>"NOTE" };
            CreateResource(Path.GetFileNameWithoutExtension(pick.FileName),kind,content);
        }
        catch (Exception ex) when (IsFileError(ex)) { FileFailure("导入资源",ex); }
    }

    private void AssetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssetList.SelectedItem is UiAsset asset) _selectedAsset=asset;
    }
    private void AssetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AssetList.SelectedItem is UiAsset asset) OpenAsset(asset);
    }
    private void OpenSelectedAsset_Click(object sender, RoutedEventArgs e)
    {
        if (GetAsset() is UiAsset asset) OpenAsset(asset);
    }
    private UiAsset? GetAsset() => AssetList.SelectedItem as UiAsset ?? _selectedAsset ??
        (OpenPages.FirstOrDefault(p=>ReferenceEquals(p,_activePage))?.Document?.Asset);

    private void OpenAsset(UiAsset asset, bool activate=true)
    {
        if (!NeedProject()) return;
        var key="FILE:"+asset.Id;
        var page=OpenPages.FirstOrDefault(p=>p.Key==key);
        if(page is not null) { if(activate)ShowPage(page); return; }
        try
        {
            var file=WorkspaceStorage.Resolve(_workspacePath!,asset.RelativePath);
            var doc=new UiFileDocument{Asset=asset,FilePath=file};
            doc.LoadContent(WorkspaceStorage.ReadText(file));
            var recovered=_recovery.TryGetValue(file,out var draft)?draft:null;
            if(recovered is not null && recovered!=doc.Content)
            {
                var yes=MessageBox.Show(this, "发现这个文件的未保存 UI 编辑快照。\n" +
                    "是否恢复快照内容？恢复后必须主动保存才能覆盖磁盘文件。\n" + file,
                    "恢复未保存草稿", MessageBoxButton.YesNo,MessageBoxImage.Question);
                if(yes==MessageBoxResult.Yes) doc.Content=recovered;
                else _recovery.Remove(file);
            }
            page = new WorkspacePage{Key=key,Kind="FILE",Title=asset.Name,Document=doc};
            doc.PropertyChanged+=OnFileDocChanged;
            OpenPages.Add(page);
            if (activate) ShowPage(page);
        }
        catch (Exception ex) when (IsFileError(ex)) { FileFailure("打开资源",ex); }
    }

    private void OnFileDocChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not UiFileDocument doc) return;
        if(e.PropertyName is nameof(UiFileDocument.Changed) or nameof(UiFileDocument.DisplayName))
        {
            OpenPages.FirstOrDefault(p=>ReferenceEquals(p.Document,doc))?.NotifyLabel();
            Raise(nameof(ActiveFile));
        }
        if(e.PropertyName is nameof(UiFileDocument.Content) or nameof(UiFileDocument.Changed))
        {
            _recoveryTimer.Stop();
            _recoveryTimer.Start();
        }
    }
    private void DetachPage(WorkspacePage p)
    {
        if(p.Document is { } doc) doc.PropertyChanged-=OnFileDocChanged;
    }

    private void UpdateRecovery()
    {
        foreach (var page in OpenPages)
        {
            if(page.Document is not { } d) continue;
            if(d.Changed) _recovery[d.FilePath]=d.Content;
            else _recovery.Remove(d.FilePath);
        }
        try { WorkspaceStorage.SaveRecovery(_recovery); }
        catch (Exception ex) when (IsFileError(ex))
        {
            // Recovery failure should never silently overwrite a formal resource.
            StatusText = "自动恢复快照保存失败：" + ex.Message;
        }
    }

    private bool SaveDoc(UiFileDocument doc)
    {
        try
        {
            WorkspaceStorage.WriteAtomic(doc.FilePath,doc.Content);
            doc.MarkSaved();
            _recovery.Remove(doc.FilePath);
            UpdateRecovery();
            StatusText="文件已保存到磁盘："+doc.FilePath;
            return true;
        }
        catch (Exception ex) when (IsFileError(ex)) { FileFailure("保存资源",ex); return false; }
    }
    private void SaveCurrent_Click(object sender, RoutedEventArgs e)
    {
        if(ActiveFile is not null) SaveDoc(ActiveFile);
        else StatusText="当前选中的是工作页/TEST 设计样本，不是文件。请选择左侧 UI 工程资源后保存。";
    }
    private bool SaveAllDocuments()
    {
        foreach(var doc in OpenPages.Select(p=>p.Document).OfType<UiFileDocument>().Where(d=>d.Changed).ToArray())
            if(!SaveDoc(doc))return false;
        StatusText="已保存所有修改的 UI 工程文件。";
        return true;
    }
    private void SaveAll_Click(object sender, RoutedEventArgs e) => SaveAllDocuments();

    private void SaveAs_Click(object sender,RoutedEventArgs e)
    {
        if(ActiveFile is not { } doc || !NeedProject()) return;
        var dialog=new NameDialog(this, "另存资源副本", "生成新的工程资源与独立文件；原文件保持不变。",
            doc.Asset.Name+" 副本");
        if(dialog.ShowDialog()==true)CreateResource(dialog.Value,doc.Asset.Kind,doc.Content);
    }

    private void RenameResource_Click(object sender,RoutedEventArgs e)
    {
        if(!NeedProject() || GetAsset() is not UiAsset asset) return;
        var dlg=new NameDialog(this,"重命名资源","更改资源显示名称（稳定文件路径与 ID 不变）",asset.Name);
        if(dlg.ShowDialog()!=true || dlg.Value==asset.Name)return;
        if(Assets.Any(a=>a!=asset && a.Kind==asset.Kind &&
            string.Equals(a.Name,dlg.Value,StringComparison.OrdinalIgnoreCase)))
        { MessageBox.Show(this,"同类资源重名，请更换名称。","重名",MessageBoxButton.OK,MessageBoxImage.Information);return; }
        var old=asset.Name;var next=dlg.Value;
        try
        {
            asset.Name=next; SaveManifest();
            foreach(var page in OpenPages.Where(p=>ReferenceEquals(p.Document?.Asset,asset)))page.NotifyLabel();
            RecordProjectUndo("重命名资源",
                ()=>{asset.Name=old;SaveManifest();RefreshPageLabels();},
                ()=>{asset.Name=next;SaveManifest();RefreshPageLabels();});
            StatusText="已修改工程资源名称；磁盘文件使用稳定 ID 路径，没有被偷偷改名。";
        }
        catch(Exception ex)when(IsFileError(ex)){asset.Name=old;FileFailure("重命名资源",ex);}
    }

    private void CopyResource_Click(object sender,RoutedEventArgs e)
    {
        _copiedAsset=GetAsset();
        StatusText=_copiedAsset is null?"请选择要复制的工程资源。":
            "已将资源 "+_copiedAsset.Name+" 放入本程序内部复制缓冲。";
    }
    private void PasteResource_Click(object sender,RoutedEventArgs e)
    {
        if(_copiedAsset is null || !NeedProject())return;
        try
        {
            var src=WorkspaceStorage.Resolve(_workspacePath!,_copiedAsset.RelativePath);
            CreateResource(_copiedAsset.Name+" 副本",_copiedAsset.Kind,WorkspaceStorage.ReadText(src));
        }
        catch(Exception ex)when(IsFileError(ex)){FileFailure("复制资源",ex);}
    }

    private string TrashPath(UiAsset asset)
    {
        var folder=Path.Combine(Path.GetDirectoryName(_workspacePath!)!,"ui-trash");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder,asset.Id+Path.GetExtension(asset.RelativePath));
    }
    private void MoveAssetToTrash(UiAsset asset)
    {
        var source=WorkspaceStorage.Resolve(_workspacePath!,asset.RelativePath);
        var trash=TrashPath(asset);
        if(File.Exists(source))
            File.Move(source,trash,true);
    }
    private void RestoreAssetFromTrash(UiAsset asset)
    {
        var target=WorkspaceStorage.Resolve(_workspacePath!,asset.RelativePath);
        var trash=TrashPath(asset);
        if(!File.Exists(trash))throw new FileNotFoundException("回收区中的资源文件已丢失。",trash);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(trash,target,true);
    }

    private void DeleteResource_Click(object sender,RoutedEventArgs e)
    {
        if(!NeedProject() || GetAsset() is not UiAsset asset)return;
        if(MessageBox.Show(this,
            "删除项目资源“"+asset.Name+"”？\n原文件将移动到本工程 ui-trash 回收区，可在本次会话通过“撤销”恢复。\n"+
            "此批尚未解析正式跨资源引用，请确认没有其他文件依赖。",
            "删除资源",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        var open=OpenPages.FirstOrDefault(p=>p.Document?.Asset.Id==asset.Id);
        if(open is not null && !ClosePage(open))return;
        var index=Assets.IndexOf(asset);
        try
        {
            MoveAssetToTrash(asset); Assets.Remove(asset);SaveManifest();
            RecordProjectUndo("删除资源",
                ()=>{RestoreAssetFromTrash(asset);Assets.Insert(Math.Clamp(index,0,Assets.Count),asset);SaveManifest();},
                ()=>{MoveAssetToTrash(asset);Assets.Remove(asset);SaveManifest();});
            _selectedAsset=null;
            StatusText="已删除资源至工程 ui-trash，Ctrl+Z 可以撤销。";
        }
        catch(Exception ex)when(IsFileError(ex)){FileFailure("删除资源",ex);}
    }

    private void RecordProjectUndo(string title,Action undo,Action redo)
    {
        _projectUndo.Push(new WorkspaceUndo(undo,redo,title));
        _projectRedo.Clear();
    }
    private void RefreshPageLabels(){foreach(var page in OpenPages)page.NotifyLabel();}
    private void UndoDocument_Click(object sender,RoutedEventArgs e)
    {
        if(ActiveFile is {CanUndo:true} doc) { doc.Undo(); StatusText="已撤销文档文字编辑。"; return; }
        if(_projectUndo.Count==0) {StatusText="没有可以撤销的工程事务。";return;}
        var op=_projectUndo.Peek();
        try {op.Undo();_projectUndo.Pop();_projectRedo.Push(op);StatusText="已撤销："+op.Title;}
        catch(Exception ex)when(IsFileError(ex)){FileFailure("撤销事务",ex);}
    }
    private void RedoDocument_Click(object sender,RoutedEventArgs e)
    {
        if(ActiveFile is {CanRedo:true} doc) {doc.Redo();StatusText="已重做文档文字编辑。";return;}
        if(_projectRedo.Count==0){StatusText="没有可以重做的工程事务。";return;}
        var op=_projectRedo.Peek();
        try{op.Redo();_projectRedo.Pop();_projectUndo.Push(op);StatusText="已重做："+op.Title;}
        catch(Exception ex)when(IsFileError(ex)){FileFailure("重做事务",ex);}
    }

    private void ShowPage(WorkspacePage page)
    {
        foreach(var item in OpenPages)item.Active=ReferenceEquals(item,page);
        _activePage=page;
        if(page.Kind=="TEST_PREVIEW")
        {
            ActiveFile=null;
            DocumentOverlay.Visibility=Visibility.Collapsed;
        }
        else if(page.Kind=="FILE")
        {
            ActiveFile=page.Document;
            DocumentOverlay.Visibility=Visibility.Visible;
            FileEditorPanel.Visibility=Visibility.Visible;
            ToolPanel.Visibility=Visibility.Collapsed;
        }
        else
        {
            ActiveFile=null;
            DocumentOverlay.Visibility=Visibility.Visible;
            FileEditorPanel.Visibility=Visibility.Collapsed;
            ToolPanel.Visibility=Visibility.Visible;
            ToolTitle=page.Title;
            ToolIntro=page.Title+" · 已纳入统一工作区导航。专业业务操作等待后续模块接入。";
            ToolTasks.Clear();
            foreach(var task in ToolTasksFor(page.Title))ToolTasks.Add(task);
        }
        if(!_restoringSession)SaveSessionState();
    }
    private static IEnumerable<string> ToolTasksFor(string title) => title switch
    {
        "CAN 工作台"=>["DBC 报文选择与多帧发送编辑","周期与单次发送任务","实时 CAN 帧日志与过滤","外部输入动作与 DUT 响应关联"],
        "DBC 编辑器"=>["打开工程中的 DBC 文件","浏览报文与信号","编辑名称、长度、位定义和枚举值","保存前检查信号引用"],
        "UDS 诊断"=>["请求服务与原始负载编辑","连续请求列表","响应与 NRC 解析","完整操作时间线"],
        "ECU 烧录"=>["选择真实烧录配置与文件","确认工程与 DUT 身份","阶段进度与应答证据","失败日志及诊断"],
        "波形分析"=>["选择采样信号与参考波形","波形主画布与双游标","参考采集与保存","波形判定及证据"],
        "手动台架"=>["20 Key 组合动作及保持时间","IGN 与夹具控制","CAN 响应实时观察","动作与 ACK 日志"],
        "设备诊断"=>["主控板连接状态与固件身份","ECU CAN/按键板 CAN 通路","操作 ACK / 超时 / 错误","重连、取消及诊断导出"],
        "生产检测"=>["扫码枪唯一编号捕获","测试程序与设备配置匹配","启动前校验与可选测试子集","单件结果及生产统计"],
        "记录与报告"=>["按时间、编号与结果检索","操作时间线与 TEST 结果","CAN/UDS/波形证据链接","导出和追溯记录"],
        _=>["选择工程资源","切换现有工作页","检查状态"]
    };
    private void ActivatePage_Click(object sender,RoutedEventArgs e)
    {
        if(sender is Button {Tag:WorkspacePage p})ShowPage(p);
    }
    private void ClosePage_Click(object sender,RoutedEventArgs e)
    {
        if(sender is Button {Tag:WorkspacePage p}) {ClosePage(p);e.Handled=true;}
    }
    private void CloseActivePage_Click(object sender,RoutedEventArgs e)
    {
        if(_activePage is { }page)ClosePage(page);
    }
    private bool ClosePage(WorkspacePage page)
    {
        if(page.Kind=="TEST_PREVIEW")
        {ShowPage(_previewPage!);StatusText="TEST 参考页为设计基线，可在导航中重新打开。";return true;}
        if(page.Document is {Changed:true} doc)
        {
            var choice=MessageBox.Show(this,"文件“"+doc.Asset.Name+"”有未保存修改。\n"+
                "“是”保存，“否”丢弃，“取消”返回编辑。",
                "关闭文档",MessageBoxButton.YesNoCancel,MessageBoxImage.Warning);
            if(choice==MessageBoxResult.Cancel)return false;
            if(choice==MessageBoxResult.Yes && !SaveDoc(doc))return false;
            if(choice==MessageBoxResult.No)
            {
                _recovery.Remove(doc.FilePath);
                try{WorkspaceStorage.SaveRecovery(_recovery);}
                catch(Exception ex)when(IsFileError(ex)){FileFailure("丢弃恢复草稿",ex);return false;}
            }
        }
        DetachPage(page);
        OpenPages.Remove(page);
        if(ReferenceEquals(_activePage,page))ShowPage(OpenPages.LastOrDefault()??_previewPage!);
        SaveSessionState();
        return true;
    }

    private void OpenTool_Click(object sender,RoutedEventArgs e)
    {
        var source=sender as FrameworkElement;
        var title=source?.Tag as string;
        if(string.IsNullOrWhiteSpace(title))return;
        var key="TOOL:"+title;
        var page=OpenPages.FirstOrDefault(p=>p.Key==key);
        if(page is null)
        {page=new WorkspacePage{Key=key,Kind="TOOL",Title=title};OpenPages.Add(page);}
        ShowPage(page);
    }
    private void ShowTest_Click(object sender,RoutedEventArgs e) => ShowPage(_previewPage!);
    private void ShowProjectExplorer_Click(object sender,RoutedEventArgs e)=>ShowNavigator("resources");

    private void NavigatorTab_Click(object sender,RoutedEventArgs e)
    {
        if(sender is Button{Tag:string t})ShowNavigator(t=="FLOW"?"FLOW":t);
    }
    private void ShowNavigator(string mode)
    {
        _navigatorMode=mode;
        CaseSearch.Text = "";
        CaseSearchHint.Text = mode=="cases" ? "⌕  筛选测试用例…" : "⌕  筛选工程资源…";
        bool cases=mode=="cases";
        CaseList.Visibility=cases?Visibility.Visible:Visibility.Collapsed;
        ResourceExplorer.Visibility=cases?Visibility.Collapsed:Visibility.Visible;
        CaseTab.Style=(Style)FindResource(cases?"TopTabSelected":"TopTab");
        FlowTab.Style=(Style)FindResource(mode=="FLOW"?"TopTabSelected":"TopTab");
        ResourceTab.Style=(Style)FindResource(mode=="resources"?"TopTabSelected":"TopTab");
        var view=CollectionViewSource.GetDefaultView(Assets);
        view.Filter=o=>o is UiAsset a && (mode!="FLOW" || a.Kind=="FLOW");
        view.Refresh();
        if(_workspace is null && !cases)
            StatusText="请先通过文件菜单创建 UI 工程；此处将显示实际磁盘资源。";
    }

    private void SearchResources()
    {
        if (_navigatorMode=="cases") return;
        var q=CaseSearch.Text.Trim();
        var v=CollectionViewSource.GetDefaultView(Assets);
        v.Filter=o=>o is UiAsset a && (_navigatorMode!="FLOW" || a.Kind=="FLOW") &&
            (q.Length==0 || a.Name.Contains(q,StringComparison.OrdinalIgnoreCase) ||
             a.KindLabel.Contains(q,StringComparison.OrdinalIgnoreCase));
        v.Refresh();
    }
    private void ResetLayout_Click(object sender,RoutedEventArgs e)
    {
        NavigatorColumn.Width=new GridLength(294);
        InspectorColumn.Width=new GridLength(400);
        IssuesRow.Height=new GridLength(165);
        if(!_issuesExpanded)CollapseProblems_Click(sender,e);
        SaveSessionState();
        StatusText="已恢复默认工作空间尺寸。";
    }

    private void SaveSessionState()
    {
        if(_restoringSession)return;
        _session.ProjectPath=_workspacePath;
        _session.OpenPageKeys=OpenPages.Where(p=>p.Kind!="TEST_PREVIEW").Select(p=>p.Key).ToList();
        _session.ActivePageKey=_activePage?.Key;
        _session.NavigatorWidth=NavigatorColumn.Width.Value;
        _session.InspectorWidth=InspectorColumn.Width.Value;
        _session.IssuesHeight=IssuesRow.Height.Value;
        _session.IssuesExpanded=_issuesExpanded;
        _session.SelectedPreviewCaseId=SelectedCase?.Id;
        _session.SelectedPreviewPhase=_phase;
        _session.SelectedPreviewStep=(StepList.SelectedItem as DemoStep)?.Number ?? 16;
        _session.NavigatorMode=_navigatorMode;
        try {WorkspaceStorage.SaveSession(_session);}
        catch(Exception ex) when(IsFileError(ex)){StatusText="会话布局保存失败："+ex.Message;}
    }
    private void Window_Loaded(object sender,RoutedEventArgs e)
    {
        _session=WorkspaceStorage.ReadSession();
        _recovery=WorkspaceStorage.ReadRecovery();
        _restoringSession=true;
        try
        {
            RememberProjectEntries();
            NavigatorColumn.Width=new GridLength(Math.Clamp(_session.NavigatorWidth,215,370));
            InspectorColumn.Width=new GridLength(Math.Clamp(_session.InspectorWidth,315,520));
            if(!_session.IssuesExpanded)CollapseProblems_Click(sender,e);
            if(_session.IssuesHeight>=80 && _session.IssuesHeight<=380 && _session.IssuesExpanded)
                IssuesRow.Height=new GridLength(_session.IssuesHeight);
            var restoredCase=Cases.FirstOrDefault(c=>c.Id==_session.SelectedPreviewCaseId);
            if(restoredCase is not null)CaseList.SelectedItem=restoredCase;
            if(_session.SelectedPreviewPhase is "准备" or "测试" or "清理")
            {
                ShowPhase(_session.SelectedPreviewPhase);
                var restoredStep=VisibleSteps.FirstOrDefault(x=>x.Number==_session.SelectedPreviewStep);
                if(restoredStep is not null)StepList.SelectedItem=restoredStep;
            }
            if(_session.ProjectPath is { } path && File.Exists(path))
            {
                var manifest=WorkspaceStorage.OpenManifest(path);
                LoadProject(manifest,path);
                foreach(var key in _session.OpenPageKeys)
                {
                    if(key.StartsWith("FILE:",StringComparison.Ordinal))
                    {
                        var asset=Assets.FirstOrDefault(a=>"FILE:"+a.Id==key);
                        if(asset is not null)OpenAsset(asset,activate:false);
                    }
                    else if(key.StartsWith("TOOL:",StringComparison.Ordinal))
                    {
                        var title=key["TOOL:".Length..];
                        OpenPages.Add(new WorkspacePage{Key=key,Kind="TOOL",Title=title});
                    }
                }
                var selected=OpenPages.FirstOrDefault(p=>p.Key==_session.ActivePageKey);
                ShowPage(selected??_previewPage!);
                ShowNavigator(_session.NavigatorMode is "cases" or "resources" or "FLOW" ? _session.NavigatorMode : "cases");
            }
        }
        catch(Exception ex)when(IsFileError(ex))
        {
            StatusText="上次 UI 会话未能恢复："+ex.Message;
        }
        finally{_restoringSession=false;}
    }

    private void RememberProjectEntries()
    {
        RecentProjectsMenu.Items.Clear();
        foreach(var recent in _session.RecentProjects.Where(File.Exists))
        {
            var captured=recent;
            var item=new MenuItem{Header=Path.GetFileName(recent)+" · "+Path.GetDirectoryName(recent)};
            item.Click+=(_,_)=>OpenProjectAt(captured);
            RecentProjectsMenu.Items.Add(item);
        }
    }
    private void Window_Closing(object? sender,CancelEventArgs e)
    {
        if(!CanLeaveCurrentProject()){e.Cancel=true;return;}
        _recoveryTimer.Stop();
        UpdateRecovery();
        SaveSessionState();
    }
}
