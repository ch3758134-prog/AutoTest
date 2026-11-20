using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AutoTest.TestHiFi;

public partial class MainWindow
{
    private readonly UiWorkspaceFiles _workFiles = new();
    private UiDocument? _currentDocument;
    private UiAsset? _selectedAsset;
    private string _activeToolPage = "工程与文件";
    private bool _switchingEditor;
    private bool _sessionRestored;

    private static bool IsBinary(UiAsset asset) =>
        Path.GetExtension(asset.RelativePath).ToLowerInvariant() is
            ".hex" or ".bin" or ".dll" or ".exe" or ".elf" or ".mot" or ".s19" or ".zip" or ".xlsx" or ".pdf";

    private void InitializeWorkspaceShell()
    {
        // The high fidelity TC-003 TEST editor remains alive below the workspace
        // overlay. Switching tabs preserves its selection, scroll and Inspector.
        RefreshProjectTree();
        RefreshTabStrip();
        ShowTestWorkspace();
        Dispatcher.BeginInvoke(new Action(RestoreLastSession));
    }

    private void RestoreLastSession()
    {
        if (_sessionRestored) return;
        _sessionRestored = true;
        var recent = UiWorkspaceFiles.ReadSession();
        if (string.IsNullOrWhiteSpace(recent.LastProjectManifest) ||
            !File.Exists(recent.LastProjectManifest)) return;
        if (MessageBox.Show(this,
              "发现上次打开的本地 UI 工作区。是否恢复工程及已打开的文件？\n\n" +
              recent.LastProjectManifest,
              "恢复上次会话", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            _workFiles.Open(recent.LastProjectManifest);
            RefreshProjectTree();
            foreach (var id in recent.OpenAssetIds.Distinct())
            {
                var asset = _workFiles.Workspace!.Assets.FirstOrDefault(a => a.Id == id);
                if (asset is not null && !IsBinary(asset) && File.Exists(_workFiles.FullPath(asset.RelativePath)))
                    _workFiles.OpenAsset(asset);
            }
            var current = _workFiles.OpenDocuments.FirstOrDefault(d => d.Asset.Id == recent.LastOpenAssetId)
                          ?? _workFiles.OpenDocuments.LastOrDefault();
            if (current is not null) ActivateDocument(current);
            else ShowProjectWorkspace();
            StatusText = "已恢复本地 UI 工作区与文件标签。此应用未连接真实 ECU。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            ShowFileError("恢复会话失败", ex);
        }
    }

    private void StoreSession()
    {
        try
        {
            UiWorkspaceFiles.SaveSession(new UiSession
            {
                LastProjectManifest = _workFiles.Manifest,
                LastOpenAssetId = _currentDocument?.Asset.Id,
                OpenAssetIds = _workFiles.OpenDocuments.Select(d => d.Asset.Id).ToList()
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "会话状态保存失败：" + ex.Message;
        }
    }

    private void ShowTest_Click(object sender, RoutedEventArgs e) => ShowTestWorkspace();
    private void ShowProject_Click(object sender, RoutedEventArgs e) => ShowProjectWorkspace();
    private void ShowResources_Click(object sender, RoutedEventArgs e) =>
        ShowProjectWorkspace("工程资源");
    private void ShowReports_Click(object sender, RoutedEventArgs e) =>
        ShowToolWorkspace("生产与记录");
    private void DeviceStatus_Click(object sender, RoutedEventArgs e) =>
        ShowToolWorkspace("设备诊断");

    private void ShowTestWorkspace()
    {
        _activeToolPage = "TEST";
        WorkspaceOverlay.Visibility = Visibility.Collapsed;
        ProjectNavigator.Visibility = Visibility.Collapsed;
        CaseList.Visibility = Visibility.Visible;
        HighlightNavigation(TestNavButton);
        StatusText = "TEST 视觉样本：原有用例、阶段与步骤状态保留。设备未连接；示例结果不是实测结果。";
    }

    private void ShowProjectWorkspace(string? header = null)
    {
        _activeToolPage = "工程与文件";
        _currentDocument = null;
        WorkspaceOverlay.Visibility = Visibility.Visible;
        ProjectNavigator.Visibility = Visibility.Visible;
        CaseList.Visibility = Visibility.Collapsed;
        HighlightNavigation(header == "工程资源" ? ResourcesNavButton : ProjectNavButton);
        ShowWorkspaceWelcome(header ?? "工程与文件",
            _workFiles.HasProject
              ? "当前 UI 工作区：" + _workFiles.Workspace!.Name + "。\n\n在左侧工程树双击资源即可打开为文档。右键资源可重命名、复制、移入工程回收区。文件编辑与保存真实写入所选工程文件夹。"
              : "尚未打开工程。请选择“新建工程”创建本地工作目录，或“打开工程”选择 .autotest-ui-workspace.json。\n\n这是一套真实磁盘操作的 UI 工作区，不是正式 AutoTest PROJECT Schema；不会伪造 ECU 操作或 TEST 执行。");
        RefreshTabStrip();
    }

    private void ShowTools_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu();
        foreach (var name in new[] { "CAN 工作台", "DBC 编辑器", "手动台架",
                                     "UDS 调试", "ECU 烧录", "波形分析", "设备诊断" })
        {
            var item = new MenuItem { Header = name, Padding = new Thickness(12,7,30,7) };
            item.Click += (_, _) => ShowToolWorkspace(name);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ShowToolWorkspace(string name)
    {
        _activeToolPage = name;
        _currentDocument = null;
        WorkspaceOverlay.Visibility = Visibility.Visible;
        ProjectNavigator.Visibility = Visibility.Visible;
        CaseList.Visibility = Visibility.Collapsed;
        HighlightNavigation(name == "生产与记录" ? ReportsNavButton : ToolsNavButton);
        var explanation = name switch
        {
            "设备诊断" => "USB 主控板状态：未连接。\nECU 通信通路：未接线。\n按键板通信通路：未接线。\n\n本版没有可用的设备驱动，不能点击连接后伪造在线状态。此处保留设备状态与诊断入口；真实连接和 ACK/超时证据将在 DEVICE 集成时接入。",
            "CAN 工作台" => "独立 CAN 工具工作页已进入文档导航体系。\n\n后续专业页按“左侧发送报文编辑、中间按键/输入动作（综合台架模式）、右侧 CAN 日志”布局实现。真实发送/周期启停当前不可用，避免假日志与假数据。",
            "UDS 调试" => "独立 UDS 工作页导航已建立。\n\n后续支持可保存的多服务连续请求序列、请求参数、解析结果和关联时间线。当前未接 ISO-TP/UDS 后端，不能执行请求。",
            "ECU 烧录" => "烧录配置、固件文件、阶段进度和诊断证据将在专业页与已有烧录后端对接。\n\n当前不加载固件或触发物理烧录。",
            "DBC 编辑器" => "在左侧工程文件浏览器中导入或打开 DBC，可先完成真实文本文件保存与路径管理。\n\n专业 DBC 报文/信号表格、中文别名、位编辑和完整 DBC 校验将在专业编辑批次提供。",
            "波形分析" => "波形画布、参考波形、游标和判定结果将在独立专业页提供。\n\n本版可以先导入、打开和管理工程内的 CSV 参考文件，不制造测量数据。",
            "手动台架" => "手动台架独立工作页入口已建立。\n\n20 Key 采用电阻网络组合形成 5 路模拟电压输出；操作配置和真实 USB ACK 必须由专项后端接线。",
            "生产与记录" => "生产检测与历史记录全局工作模式入口已建立。\n\n扫码、匹配、真实执行和追溯将在后续业务批次接线；本版不产生虚假 PASS、FAIL、统计或记录。",
            _ => "此工作页尚未连接专业功能。"
        };
        ShowWorkspaceWelcome(name, explanation);
        RefreshTabStrip();
        StatusText = name + " · 已保留 TEST 工作对象和文件会话；业务连接未接线。";
    }

    private void ShowWorkspaceWelcome(string heading, string body)
    {
        WorkspaceTitle.Text = heading;
        WorkspaceSubtitle.Text = _workFiles.HasProject
            ? "UI 工作区：" + _workFiles.Workspace!.Name
            : "未打开本地 UI 工作区";
        ToolHeading.Text = heading;
        ToolBody.Text = body;
        ToolDescriptionPanel.Visibility = Visibility.Visible;
        DocumentEditorPanel.Visibility = Visibility.Collapsed;
        WorkspaceStatusText.Text = _workFiles.HasProject
            ? _workFiles.Root! : "尚未选择工作区 · 主控板未连接";
    }

    private void HighlightNavigation(Button selected)
    {
        foreach (var button in new[] { TestNavButton, ProjectNavButton, ResourcesNavButton, ToolsNavButton, ReportsNavButton })
            button.Style = (Style)FindResource(ReferenceEquals(selected,button) ? "TopTabSelected" : "TopTab");
    }

    private void RefreshTabStrip()
    {
        DocumentTabStrip.Children.Clear();
        var test = new Button
        {
            Content = "▥  TEST · " + (SelectedCase?.Name ?? "示例用例"),
            Style = (Style)FindResource(_activeToolPage == "TEST" ? "SelectedButton" : "OutlineButton"),
            Margin = new Thickness(0,0,6,0),
            Height = 32,
            MaxWidth = 250
        };
        test.Click += (_,_) => ShowTestWorkspace();
        DocumentTabStrip.Children.Add(test);
        foreach (var doc in _workFiles.OpenDocuments)
        {
            var current = doc;
            var host = new StackPanel { Orientation = Orientation.Horizontal };
            var switcher = new Button
            {
                Content = current.Caption, Tag = current, Height = 32,
                MinWidth = 75, MaxWidth = 220,
                Style = (Style)FindResource(ReferenceEquals(_currentDocument,current)
                    ? "SelectedButton" : "OutlineButton")
            };
            switcher.Click += (_,_) => ActivateDocument(current);
            var close = new Button
            {
                Content = "×", Width = 27, Height = 32, Tag = current,
                Style = (Style)FindResource("ActionButton"), ToolTip = "关闭此文档"
            };
            close.Click += (_,_) => CloseOneDocument(current);
            host.Children.Add(switcher); host.Children.Add(close);
            var border = new Border { Child=host, Margin = new Thickness(0,0,6,0) };
            DocumentTabStrip.Children.Add(border);
        }
        if (_activeToolPage is not ("工程与文件" or "TEST") &&
            _currentDocument is null)
        {
            var title = new Button
            {
                Content = _activeToolPage + " · 尚未接线",
                Height = 32,
                Style = (Style)FindResource("SelectedButton")
            };
            DocumentTabStrip.Children.Add(title);
        }
    }

    private void RefreshProjectTree()
    {
        ProjectTree.Items.Clear();
        ProjectNameText.Text = _workFiles.Workspace is null
            ? "尚未打开 UI 工作区" : _workFiles.Workspace.Name;
        if (_workFiles.Workspace is null) return;
        var root = new TreeViewItem
        {
            Header="▣ " + _workFiles.Workspace.Name,
            IsExpanded=true, FontWeight=FontWeights.SemiBold
        };
        ProjectTree.Items.Add(root);
        foreach (var category in new[] { "测试程序", "公共流程", "DBC", "参考波形", "输入配置", "诊断配置", "烧录配置", "生产配置", "其他配置" })
        {
            var assets = _workFiles.Workspace.Assets.Where(a => a.Kind == category).OrderBy(a=>a.Name).ToArray();
            var group = new TreeViewItem
            {
                Header = category + " (" + assets.Length + ")",
                IsExpanded = true, FontWeight = FontWeights.Medium
            };
            root.Items.Add(group);
            foreach (var asset in assets)
            {
                var a = asset;
                var leaf = new TreeViewItem
                {
                    Header = "▤ " + a.Name,
                    Tag = a, FontWeight=FontWeights.Normal,
                    ToolTip = a.RelativePath + "\n修订 " + a.Revision
                };
                var menu = new ContextMenu();
                AddMenu(menu,"打开",()=>OpenAsset(a));
                AddMenu(menu,"重命名",()=>RenameAsset(a));
                AddMenu(menu,"复制文件",()=>DuplicateAsset(a));
                AddMenu(menu,"移入回收区",()=>DeleteAsset(a));
                leaf.ContextMenu = menu;
                group.Items.Add(leaf);
            }
        }
    }
    private static void AddMenu(ContextMenu menu, string title, Action perform)
    {
        var item = new MenuItem { Header=title };
        item.Click += (_,_) => perform();
        menu.Items.Add(item);
    }
    private void ProjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: UiAsset asset }) _selectedAsset = asset;
    }
    private void ProjectTree_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectTree.SelectedItem is TreeViewItem { Tag: UiAsset asset }) OpenAsset(asset);
    }

    private void ActivateDocument(UiDocument document)
    {
        _currentDocument = document;
        _selectedAsset = document.Asset;
        _activeToolPage = "工程与文件";
        WorkspaceOverlay.Visibility = Visibility.Visible;
        ProjectNavigator.Visibility = Visibility.Visible; CaseList.Visibility = Visibility.Collapsed;
        HighlightNavigation(ProjectNavButton);
        WorkspaceTitle.Text = document.Asset.Name;
        WorkspaceSubtitle.Text = document.Asset.Kind + " · " + document.Asset.RelativePath;
        FileNameLabel.Text = document.Asset.Name;
        FileKindLabel.Text = document.Asset.Kind;
        FilePathLabel.Text = document.Asset.RelativePath;
        FileRevisionLabel.Text = "Revision " + document.Asset.Revision;
        ToolDescriptionPanel.Visibility = Visibility.Collapsed;
        DocumentEditorPanel.Visibility = Visibility.Visible;
        _switchingEditor = true;
        DocumentTextEditor.Text = document.Content;
        _switchingEditor = false;
        WorkspaceStatusText.Text = _workFiles.FullPath(document.Asset.RelativePath);
        RefreshTabStrip();
        StatusText = "当前编辑：" + document.Asset.Name + " · 使用 Ctrl+S 保存真实文件内容。";
    }
    private void OpenAsset(UiAsset asset)
    {
        if (IsBinary(asset))
        {
            ShowToolWorkspace("资源文件");
            ToolHeading.Text = asset.Name;
            ToolBody.Text = "这是不可作为 UTF-8 文本编辑的资源文件。\n\n文件：" +
                            asset.RelativePath + "\n\n在当前 UI-02 可进行重命名、复制、删除管理；专业烧录/数据解析需后端接入。";
            return;
        }
        try { ActivateDocument(_workFiles.OpenAsset(asset)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { ShowFileError("打开资源失败",ex); }
    }
    private void DocumentTextEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_switchingEditor || _currentDocument is null) return;
        _currentDocument.Content = DocumentTextEditor.Text;
        WorkspaceStatusText.Text = _currentDocument.Dirty ? "● 未保存更改  ·  Ctrl+S 保存"
                                                      : "已保存到磁盘";
        RefreshTabStrip();
    }
    private static string? AskName(Window owner, string title, string initial)
    {
        var w = new Window { Owner=owner, Title=title, Width=410, Height=186,
            ResizeMode=ResizeMode.NoResize, WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=Brushes.White, FontFamily=new FontFamily("Microsoft YaHei UI") };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text=title, FontSize=14,FontWeight=FontWeights.SemiBold,
            Margin=new Thickness(0,0,0,12) });
        var input = new TextBox { Text=initial, Height=34, Padding=new Thickness(8,5,8,5), FontSize=13 };
        panel.Children.Add(input);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,
                                   Margin=new Thickness(0,15,0,0)};
        var cancel=new Button { Content="取消",Width=75,Height=31,Margin=new Thickness(0,0,8,0)};
        var ok=new Button {Content="确定",Width=82,Height=31,IsDefault=true};
        cancel.Click+=(_,_)=>w.DialogResult=false;
        ok.Click+=(_,_)=>w.DialogResult=true;
        buttons.Children.Add(cancel);buttons.Children.Add(ok);
        panel.Children.Add(buttons);w.Content=panel;
        w.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};
        return w.ShowDialog()==true?input.Text.Trim():null;
    }
    private static string? AskAssetKind(Window owner)
    {
        var choices = new[] { "测试程序", "公共流程", "DBC", "参考波形", "输入配置",
                              "诊断配置", "烧录配置", "生产配置", "其他配置" };
        var w=new Window {Owner=owner,Title="选择资源类型",Width=360,Height=183,
            ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=Brushes.White,FontFamily=new FontFamily("Microsoft YaHei UI")};
        var panel=new StackPanel {Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock {Text="资源类型",FontSize=13,Margin=new Thickness(0,0,0,10)});
        var combo=new ComboBox {ItemsSource=choices,SelectedIndex=0,Height=32};
        panel.Children.Add(combo);
        var ok=new Button {Content="继续",Width=85,Height=32,HorizontalAlignment=HorizontalAlignment.Right,
                           IsDefault=true,Margin=new Thickness(0,13,0,0)};
        ok.Click+=(_,_)=>w.DialogResult=true;
        panel.Children.Add(ok);w.Content=panel;
        return w.ShowDialog()==true?combo.SelectedItem?.ToString():null;
    }
    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardOpenFiles()) return;
        var name=AskName(this,"新建本地 UI 工作区名称","新建工作区");
        if (name is null) return;
        var dlg=new OpenFolderDialog { Title="选择工程上级目录（将创建独立子文件夹）" };
        if (dlg.ShowDialog(this)!=true) return;
        try
        {
            _workFiles.Create(dlg.FolderName,name);
            _currentDocument=null; _selectedAsset=null;
            RefreshProjectTree();ShowProjectWorkspace();StoreSession();
            WorkspaceStatusText.Text="工程创建完成：" + _workFiles.Root;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        { ShowFileError("创建工程失败",ex); }
    }
    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardOpenFiles()) return;
        var dlg=new OpenFileDialog {Title="打开 UI 工作区清单",FileName=UiWorkspaceFiles.ManifestName,
            Filter="AutoTest UI 工作区 (.autotest-ui-workspace.json)|*.autotest-ui-workspace.json|JSON 文件|*.json"};
        if(dlg.ShowDialog(this)!=true)return;
        try
        {
            _workFiles.Open(dlg.FileName);
            _currentDocument=null; _selectedAsset=null;
            RefreshProjectTree();ShowProjectWorkspace();StoreSession();
            StatusText="已打开 UI 工程：" + _workFiles.Workspace!.Name;
        }
        catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidDataException)
        { ShowFileError("打开工程失败",ex); }
    }
    private void OpenRecent_Click(object sender, RoutedEventArgs e)
    {
        var session=UiWorkspaceFiles.ReadSession();
        if(string.IsNullOrWhiteSpace(session.LastProjectManifest)||!File.Exists(session.LastProjectManifest))
        {
            MessageBox.Show(this,"没有可用的最近工程。","最近工程",
                            MessageBoxButton.OK,MessageBoxImage.Information);return;
        }
        if(!ConfirmDiscardOpenFiles())return;
        try
        {
            _workFiles.Open(session.LastProjectManifest);
            _currentDocument=null; RefreshProjectTree(); ShowProjectWorkspace(); StoreSession();
        }
        catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or
                                  System.Text.Json.JsonException)
        {ShowFileError("打开最近工程失败",ex);}
    }
    private void NewAsset_Click(object sender,RoutedEventArgs e)
    {
        if(!_workFiles.HasProject)
        {
            MessageBox.Show(this,"请先新建或打开本地 UI 工作区。","新建资源",
                MessageBoxButton.OK,MessageBoxImage.Information);return;
        }
        var kind=AskAssetKind(this);if(kind is null)return;
        var name=AskName(this,"新建"+kind,kind+" 01");if(name is null)return;
        try
        {
            var asset=_workFiles.NewAsset(kind,name);
            RefreshProjectTree();OpenAsset(asset);StoreSession();
        }
        catch(Exception ex)when(ex is IOException or ArgumentException or UnauthorizedAccessException)
        {ShowFileError("创建文件失败",ex);}
    }
    private void ImportAsset_Click(object sender,RoutedEventArgs e)
    {
        if(!_workFiles.HasProject)
        {
            MessageBox.Show(this,"请先打开本地 UI 工作区。","导入文件",
                MessageBoxButton.OK,MessageBoxImage.Information);return;
        }
        var kind=AskAssetKind(this);if(kind is null)return;
        var dlg=new OpenFileDialog {Title="选择需要复制进工程的资源文件",Filter="所有文件 (*.*)|*.*"};
        if(dlg.ShowDialog(this)!=true)return;
        try
        {
            var asset=_workFiles.Import(dlg.FileName,kind);
            RefreshProjectTree();OpenAsset(asset);StoreSession();
        }
        catch(Exception ex)when(ex is IOException or ArgumentException or UnauthorizedAccessException)
        {ShowFileError("导入资源失败",ex);}
    }
    private UiAsset? TargetAsset() => _selectedAsset ?? _currentDocument?.Asset;
    private void RenameAsset_Click(object sender,RoutedEventArgs e)
    {
        var asset=TargetAsset();if(asset is null){NeedSelectAsset();return;}
        var name=AskName(this,"重命名资源",asset.Name);if(name is null||name==asset.Name)return;
        if(MessageBox.Show(this,
          "将同时重命名实际文件。\n\n当前 UI-02 不自动修复其他文件内部的文本引用，请确认没有相关引用需要同步修改。\n\n继续？",
          "重命名与引用提示",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try
        {
            _workFiles.Rename(asset,name);RefreshProjectTree();
            if(_currentDocument?.Asset.Id==asset.Id)ActivateDocument(_currentDocument);
            RefreshTabStrip();StoreSession();
        }
        catch(Exception ex)when(ex is IOException or ArgumentException or UnauthorizedAccessException)
        {ShowFileError("重命名失败",ex);}
    }
    private void DuplicateAsset_Click(object sender,RoutedEventArgs e)
    {
        var asset=TargetAsset();if(asset is null){NeedSelectAsset();return;}
        try
        {
            var copy=_workFiles.Duplicate(asset);
            RefreshProjectTree();OpenAsset(copy);StoreSession();
        }
        catch(Exception ex)when(ex is IOException or ArgumentException or UnauthorizedAccessException)
        {ShowFileError("复制失败",ex);}
    }
    private void DeleteAsset_Click(object sender,RoutedEventArgs e)
    {
        var asset=TargetAsset();if(asset is null){NeedSelectAsset();return;}
        if(MessageBox.Show(this,
          "资源将从工程清单移除，源文件移动至工程内 .ui-trash 回收目录，不会永久删除。\n\n其他文件中的引用不会自动清理。\n\n继续移除 “"+asset.Name+"” 吗？",
          "移除资源",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try
        {
            _workFiles.Delete(asset);
            if(_currentDocument?.Asset.Id==asset.Id)_currentDocument=null;
            _selectedAsset=null;RefreshProjectTree();ShowProjectWorkspace();StoreSession();
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {ShowFileError("移除资源失败",ex);}
    }
    private void SaveDocument_Click(object sender,RoutedEventArgs e)=>SaveCurrentDocument();
    private bool SaveCurrentDocument()
    {
        if (_activeToolPage == "TEST")
        {
            StatusText = "TEST 页面仍是高保真视觉样本，不能作为正式测试程序保存。请从工程树打开实际资源文件。";
            return false;
        }
        if(_currentDocument is null)
        {
            StatusText="当前是视觉 TEST 或工具工作区，没有可保存的文件。请在工程树中打开真实文件。";
            return false;
        }
        try
        {
            _workFiles.Save(_currentDocument);
            FileRevisionLabel.Text="Revision "+_currentDocument.Asset.Revision;
            RefreshProjectTree();RefreshTabStrip();StoreSession();
            WorkspaceStatusText.Text="已保存："+_workFiles.FullPath(_currentDocument.Asset.RelativePath);
            StatusText="已保存："+_currentDocument.Asset.Name;
            return true;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {ShowFileError("保存失败",ex);return false;}
    }
    private void SaveAll_Click(object sender,RoutedEventArgs e)=>SaveAllDocuments();
    private bool SaveAllDocuments()
    {
        try
        {
            _workFiles.SaveAll();
            RefreshProjectTree();RefreshTabStrip();StoreSession();
            WorkspaceStatusText.Text="所有已修改文件已写入磁盘。";
            StatusText="保存全部完成。";
            return true;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {ShowFileError("保存全部失败",ex);return false;}
    }
    private void SaveAs_Click(object sender,RoutedEventArgs e)
    {
        if (_activeToolPage == "TEST") { StatusText = "视觉样本不能另存为正式 TEST 工程。"; return; }
        if(_currentDocument is null){StatusText="请先打开一个工程资源文件。";return;}
        var dialog=new SaveFileDialog {Title="将当前内容另存为独立文件副本",
            FileName=Path.GetFileName(_currentDocument.Asset.RelativePath),
            Filter="所有文件 (*.*)|*.*"};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            // Save As produces an external copy; the project still references the original.
            File.WriteAllText(dialog.FileName,_currentDocument.Content,new UTF8Encoding(false));
            StatusText="已另存副本："+dialog.FileName+"；当前工程引用未改变。";
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException)
        {ShowFileError("另存为失败",ex);}
    }
    private void UndoDocument_Click(object sender,RoutedEventArgs e)
    {
        if(_currentDocument is not null&&DocumentTextEditor.CanUndo)DocumentTextEditor.Undo();
        else StatusText="当前文档没有可撤销的文本编辑。TEST 结构编辑将在 UI-03 接入。";
    }
    private void RedoDocument_Click(object sender,RoutedEventArgs e)
    {
        if(_currentDocument is not null&&DocumentTextEditor.CanRedo)DocumentTextEditor.Redo();
        else StatusText="当前文档没有可重做的文本编辑。";
    }
    private void CloseDocument_Click(object sender,RoutedEventArgs e)
    {
        if(_currentDocument is not null)CloseOneDocument(_currentDocument);
        else ShowTestWorkspace();
    }
    private bool CloseOneDocument(UiDocument doc)
    {
        if(doc.Dirty)
        {
            var choice=MessageBox.Show(this,"文档 “"+doc.Asset.Name+"” 存在未保存的更改。\n\n是：保存并关闭\n否：放弃编辑并关闭\n取消：返回",
                "关闭文档",MessageBoxButton.YesNoCancel,MessageBoxImage.Warning);
            if(choice==MessageBoxResult.Cancel)return false;
            if(choice==MessageBoxResult.Yes)
            {
                try {_workFiles.Save(doc);}
                catch(Exception ex)when(ex is IOException or UnauthorizedAccessException)
                {ShowFileError("保存失败，未关闭文档",ex);return false;}
            }
        }
        _workFiles.OpenDocuments.Remove(doc);
        if(ReferenceEquals(_currentDocument,doc))
        {
            _currentDocument=null;
            var last=_workFiles.OpenDocuments.LastOrDefault();
            if(last is not null)ActivateDocument(last);
            else ShowProjectWorkspace();
        }
        RefreshTabStrip();StoreSession();
        return true;
    }
    private bool ConfirmDiscardOpenFiles()
    {
        var dirty=_workFiles.OpenDocuments.Where(x=>x.Dirty).ToArray();
        if(dirty.Length==0)return true;
        var choice=MessageBox.Show(this,
            "共有 "+dirty.Length+" 个未保存的文件：\n"+string.Join("\n",dirty.Select(x=>"• "+x.Asset.Name))+
            "\n\n是：保存全部\n否：丢弃全部未保存内容\n取消：返回编辑",
            "切换工程前确认",MessageBoxButton.YesNoCancel,MessageBoxImage.Warning);
        if(choice==MessageBoxResult.Cancel)return false;
        return choice!=MessageBoxResult.Yes||SaveAllDocuments();
    }
    private void CloseProject_Click(object sender,RoutedEventArgs e)
    {
        if(!ConfirmDiscardOpenFiles())return;
        _workFiles.Close();
        _currentDocument=null;_selectedAsset=null;
        RefreshProjectTree(); ShowTestWorkspace(); StoreSession();
        StatusText="已关闭本地 UI 工作区。可从文件菜单创建或打开其他工程。";
    }
    private void NeedSelectAsset()=>
        MessageBox.Show(this,"请先从左侧工程树选择一个实际文件资源。","选择资源",
            MessageBoxButton.OK,MessageBoxImage.Information);
    private void ShowFileError(string title,Exception ex)
    {
        MessageBox.Show(this,title+"：\n"+ex.Message,title,
            MessageBoxButton.OK,MessageBoxImage.Warning);
        StatusText=title+"："+ex.Message;
    }
    private void MainWindow_Closing(object? sender,System.ComponentModel.CancelEventArgs e)
    {
        if(!ConfirmDiscardOpenFiles()){e.Cancel=true;return;}
        StoreSession();
    }
    private void FileShortcut(KeyEventArgs e)
    {
        var ctrl=Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if(ctrl&&shift&&e.Key==Key.S){SaveAllDocuments();e.Handled=true;}
        else if(ctrl&&e.Key==Key.S){SaveCurrentDocument();e.Handled=true;}
        else if(ctrl&&e.Key==Key.O){OpenProject_Click(this,e);e.Handled=true;}
        else if(ctrl&&e.Key==Key.W){CloseDocument_Click(this,e);e.Handled=true;}
        else if(ctrl&&e.Key==Key.Tab)
        {
            if(_workFiles.OpenDocuments.Count>0)
            {
                int i=_workFiles.OpenDocuments.IndexOf(_currentDocument!);
                ActivateDocument(_workFiles.OpenDocuments[(i+1+_workFiles.OpenDocuments.Count)%_workFiles.OpenDocuments.Count]);
                e.Handled=true;
            }
        }
        else if(e.Key==Key.F2&&_selectedAsset is not null)
        {RenameAsset(_selectedAsset);e.Handled=true;}
        else if(ctrl&&e.Key==Key.Z&&_currentDocument is not null)
        {UndoDocument_Click(this,e);e.Handled=true;}
        else if(ctrl&&e.Key==Key.Y&&_currentDocument is not null)
        {RedoDocument_Click(this,e);e.Handled=true;}
    }
}
