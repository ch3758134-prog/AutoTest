using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AutoTest.TestHiFi;

// UI-05 workbenches are local, editable UI drafts. No physical device calls.
public partial class MainWindow
{
    private sealed class ToolDraft
    {
        public string Name { get; set; } = "";
        public Dictionary<string, string> Fields { get; set; } = new();
        public List<string> Items { get; set; } = new();
        public HashSet<int> HeldKeys { get; set; } = new();
    }

    private readonly Dictionary<string, ToolDraft> _toolDrafts = new();
    private readonly Dictionary<string, Window> _toolWindows = new();

    private static bool IsToolName(string name) =>
        name is "DBC 编辑器" or "UDS 调试" or "ECU 烧录" or "波形分析" or "手动台架";

    private void CloseToolWindows()
    {
        foreach (var window in _toolWindows.Values.ToArray()) window.Close();
        _toolWindows.Clear();
    }
    private static readonly Brush ToolText = new SolidColorBrush(Color.FromRgb(38, 56, 76));
    private static readonly Brush ToolMuted = new SolidColorBrush(Color.FromRgb(98, 115, 137));
    private static readonly Brush ToolLine = new SolidColorBrush(Color.FromRgb(220, 230, 242));
    private static readonly Brush ToolBlue = new SolidColorBrush(Color.FromRgb(37, 100, 181));

    private ToolDraft Draft(string name)
    {
        if (_toolDrafts.TryGetValue(name, out var existing)) return existing;
        var draft = new ToolDraft { Name = name };
        _toolDrafts[name] = draft;
        if (!_workFiles.HasProject) return draft;
        var kind = DraftKind(name);
        var asset = _workFiles.Workspace!.Assets.FirstOrDefault(a => a.Kind == kind &&
            a.Name == DraftAssetName(name));
        if (asset is null) return draft;
        try
        {
            var data = JsonSerializer.Deserialize<ToolDraft>(File.ReadAllText(_workFiles.FullPath(asset.RelativePath)));
            if (data is not null && data.Name == name)
            {
                data.Fields ??= new();
                data.Items ??= new();
                data.HeldKeys ??= new();
                _toolDrafts[name] = data;
                return data;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { StatusText = "工作台配置读取失败：" + ex.Message; }
        return draft;
    }

    private static string DraftKind(string name) => name switch
    {
        "UDS 调试" => "诊断配置",
        "ECU 烧录" => "烧录配置",
        "手动台架" => "输入配置",
        "波形分析" => "其他配置",
        "DBC 编辑器" => "其他配置",
        _ => "其他配置"
    };
    private static string DraftAssetName(string name) => "UI05 " + name + "草稿";

    private static TextBlock ToolLabel(string value, bool heading = false) => new()
    {
        Text = value, Foreground = heading ? ToolText : ToolMuted,
        FontSize = heading ? 15 : 12, FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = new Thickness(0, heading ? 0 : 7, 0, heading ? 14 : 6),
        TextWrapping = TextWrapping.Wrap
    };
    private static Border ToolCard(UIElement child, Thickness? margin = null) => new()
    {
        Child = child, Background = Brushes.White, BorderBrush = ToolLine,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
        Padding = new Thickness(16), Margin = margin ?? new Thickness(0, 0, 9, 0)
    };
    private static Button ToolButton(string label, Action action, bool primary = false)
    {
        var button = new Button
        {
            Content = label, Height = 33, MinWidth = 82, Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 8), Background = primary ? ToolBlue : Brushes.White,
            Foreground = primary ? Brushes.White : ToolText, BorderBrush = ToolLine,
            BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => action();
        return button;
    }
    private TextBox ToolField(StackPanel host, ToolDraft draft, string key, string fallback = "")
    {
        host.Children.Add(ToolLabel(key));
        var field = new TextBox
        {
            Text = draft.Fields.TryGetValue(key, out var value) ? value : fallback,
            MinHeight = 34, FontSize = 12.5, Padding = new Thickness(9, 7, 9, 7),
            Foreground = ToolText, Background = Brushes.White, BorderBrush = ToolLine,
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 5)
        };
        draft.Fields[key] = field.Text;
        field.TextChanged += (_, _) => draft.Fields[key] = field.Text;
        host.Children.Add(field);
        return field;
    }
    private static void ToolNote(StackPanel host, string message)
    {
        host.Children.Add(new TextBlock
        {
            Text = message, TextWrapping = TextWrapping.Wrap, Foreground = ToolMuted,
            FontSize = 12, LineHeight = 21, Margin = new Thickness(0, 8, 0, 12)
        });
    }

    private bool SaveToolDraft(ToolDraft draft)
    {
        if (!_workFiles.HasProject)
        {
            MessageBox.Show(this, "先创建或打开 UI 工程，再保存此工具的本地配置。", "保存工作台",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        try
        {
            var kind = DraftKind(draft.Name);
            var title = DraftAssetName(draft.Name);
            var asset = _workFiles.Workspace!.Assets.FirstOrDefault(a => a.Kind == kind && a.Name == title)
                ?? _workFiles.NewAsset(kind, title);
            var doc = _workFiles.OpenAsset(asset);
            doc.Content = JsonSerializer.Serialize(draft, new JsonSerializerOptions { WriteIndented = true });
            _workFiles.Save(doc);
            RefreshProjectTree();
            RefreshTabStrip();
            StoreSession();
            StatusText = "已保存本地 UI 工作台草稿：" + asset.RelativePath + "。未向 ECU 发送命令。";
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        { ShowFileError("保存工具草稿失败", ex); return false; }
    }

    private void RenderToolWorkbench(string name)
    {
        if (!IsToolName(name)) return;
        var draft = Draft(name);
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(247, 249, 252)) };
        // Each tool is an independent owned window. Do not waste its width on a
        // second copy of the main application's tool/navigation tree.
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(315) });
        var sidebar = new StackPanel();
        sidebar.Children.Add(ToolLabel("工具", true));
        foreach (var item in new[] { "DBC 编辑器", "UDS 调试", "ECU 烧录", "波形分析", "手动台架" })
        {
            var tool = item;
            var itemButton = ToolButton((tool == name ? "●  " : "   ") + tool,
                () => ShowToolWorkspace(tool), tool == name);
            itemButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            itemButton.Width = 195;
            sidebar.Children.Add(itemButton);
        }
        ToolNote(sidebar, "这些工具服务于 ECU 测试，也可以独立编辑配置。当前仅验证界面及真实本地文件保存，不执行硬件请求。");
        var sideCard = ToolCard(sidebar);
        sideCard.Visibility = Visibility.Collapsed;
        Grid.SetColumn(sideCard, 0); root.Children.Add(sideCard);

        var center = new StackPanel();
        var centerScroll = new ScrollViewer
        {
            Content = center, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(3, 3, 3, 12)
        };
        var centerCard = ToolCard(centerScroll);
        Grid.SetColumn(centerCard, 1); root.Children.Add(centerCard);

        var inspector = new StackPanel();
        var inspectorScroll = new ScrollViewer
        {
            Content = inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(1)
        };
        var inspectorCard = ToolCard(inspectorScroll, new Thickness(0));
        Grid.SetColumn(inspectorCard, 2); root.Children.Add(inspectorCard);

        center.Children.Add(ToolLabel(name + " · 工作区", true));
        inspector.Children.Add(ToolLabel("配置与操作", true));
        switch (name)
        {
            case "DBC 编辑器": BuildDbcWorkbench(center, inspector, draft); break;
            case "UDS 调试": BuildUdsWorkbench(center, inspector, draft); break;
            case "ECU 烧录": BuildFlashWorkbench(center, inspector, draft); break;
            case "波形分析": BuildWaveWorkbench(center, inspector, draft); break;
            case "手动台架": BuildBenchWorkbench(center, inspector, draft); break;
        }
        inspector.Children.Add(ToolLabel("本地工作区", true));
        inspector.Children.Add(ToolButton("保存工具配置", () => SaveToolDraft(draft), true));
        ToolNote(inspector, "保存的是当前 UI 工作台草稿，并非正式 TEST、UDS、Flash 或 DBC 执行配置。硬件执行命令未启用。");
        if (!_toolWindows.TryGetValue(name, out var window))
        {
            window = new Window
            {
                Owner = this, Title = "AutoTest · " + name,
                Width = 1280, Height = 820, MinWidth = 850, MinHeight = 560,
                Background = Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13,
                ShowInTaskbar = false
            };
            _toolWindows[name] = window;
            window.Closed += (_, _) => _toolWindows.Remove(name);
            window.Content = root;
            window.Show();
        }
        else
        {
            window.Content = null;
            window.Content = root;
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }
        StatusText = name + " · 独立工具窗口与主界面属于同一个进程；硬件未连接。";
    }

    private void ImportToolResource(string kind)
    {
        if (!_workFiles.HasProject)
        {
            MessageBox.Show(this, "请先创建或打开 UI 工作区。", "导入专业资源",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入" + kind,
            Filter = kind == "DBC" ? "DBC 文件|*.dbc|所有文件|*.*"
                : "波形文件|*.csv;*.txt|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var asset = _workFiles.Import(dialog.FileName, kind);
            RefreshProjectTree();
            StoreSession();
            if (kind == "DBC")
            {
                Draft("DBC 编辑器").Fields["DBC SelectedId"] = asset.Id;
                RenderToolWorkbench("DBC 编辑器");
            }
            else if (kind == "参考波形")
            {
                Draft("波形分析").Fields["波形资源 ID"] = asset.Id;
                RenderToolWorkbench("波形分析");
            }
            else OpenAsset(asset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { ShowFileError("导入资源失败", ex); }
    }

    private void BuildDbcWorkbench(StackPanel center, StackPanel inspector, ToolDraft draft)
    {
        ToolNote(center, "可直接创建、打开并编辑实际 DBC 文本文件，无需先导入。报文/信号的结构化编辑与校验仍由正式 DBC 模块负责。");
        if (!_workFiles.HasProject)
        {
            ToolNote(center, "先从主窗口建立或打开工程，才能创建 DBC 资源。");
            return;
        }
        var toolbar = new WrapPanel();
        toolbar.Children.Add(ToolButton("＋ 新建 DBC", () =>
        {
            var name = AskName(this, "新建 DBC", "新建DBC");
            if (name is null) return;
            try
            {
                var asset = _workFiles.NewAsset("DBC", name);
                draft.Fields["DBC SelectedId"] = asset.Id;
                RefreshProjectTree();
                RenderToolWorkbench("DBC 编辑器");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { ShowFileError("新建 DBC 失败", ex); }
        }, true));
        toolbar.Children.Add(ToolButton("导入 DBC…", () => ImportToolResource("DBC")));
        center.Children.Add(toolbar);
        var assets = _workFiles.Workspace!.Assets.Where(a => a.Kind == "DBC").OrderBy(a => a.Name).ToArray();
        var assetList = new ComboBox
        {
            ItemsSource = assets, DisplayMemberPath = "Name", Margin = new Thickness(0, 10, 0, 12),
            Height = 35, Padding = new Thickness(7), BorderBrush = ToolLine
        };
        center.Children.Add(assetList);
        var assetId = draft.Fields.GetValueOrDefault("DBC SelectedId");
        var selected = assets.FirstOrDefault(a => a.Id == assetId) ?? assets.FirstOrDefault();
        if (selected is null)
        {
            ToolNote(center, "工程尚无 DBC。点击“新建 DBC”即可开始编写。");
            return;
        }
        assetList.SelectedItem = selected;
        assetList.SelectionChanged += (_, _) =>
        {
            if (assetList.SelectedItem is UiAsset item)
            {
                draft.Fields["DBC SelectedId"] = item.Id;
                RenderToolWorkbench("DBC 编辑器");
            }
        };
        UiDocument document;
        try { document = _workFiles.OpenAsset(selected); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { ShowFileError("打开 DBC 失败", ex); return; }
        var messages = UiDbcEditing.Messages(document.Content);
        var messageChooser = new ComboBox
        {
            ItemsSource = messages, DisplayMemberPath = "Label",
            Height = 33, Margin = new Thickness(0, 0, 0, 12), BorderBrush = ToolLine
        };
        var messageId = draft.Fields.GetValueOrDefault("DBC MessageId", "");
        var message = messages.FirstOrDefault(m => m.EncodedId.ToString() == messageId)
            ?? messages.FirstOrDefault();
        messageChooser.SelectedItem = message;
        messageChooser.SelectionChanged += (_, _) =>
        {
            if (messageChooser.SelectedItem is UiDbcEditing.Message row)
                draft.Fields["DBC MessageId"] = row.EncodedId.ToString();
        };
        center.Children.Add(ToolLabel("报文与信号 · 已有真实 DBC 定义（" + messages.Count + " 条报文）", true));
        center.Children.Add(messageChooser);
        foreach (var msg in messages.Take(40))
            center.Children.Add(ToolLabel("▤ " + msg.Label));
        center.Children.Add(ToolLabel("DBC 源文件（可直接编辑）", true));
        var editor = new TextBox
        {
            Text = document.Content, AcceptsReturn = true, AcceptsTab = true,
            FontFamily = new FontFamily("Consolas"), FontSize = 13,
            MinHeight = 300, MaxHeight = 580, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Foreground = ToolText, Background = Brushes.White, BorderBrush = ToolLine,
            BorderThickness = new Thickness(1), Padding = new Thickness(12)
        };
        editor.TextChanged += (_, _) => document.Content = editor.Text;
        center.Children.Add(editor);
        center.Children.Add(ToolButton("保存 DBC 文件", () =>
        {
            try
            {
                _workFiles.Save(document);
                RefreshProjectTree(); RefreshTabStrip(); StoreSession();
                StatusText = "DBC 文件已保存：" + selected.RelativePath;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { ShowFileError("保存 DBC 失败", ex); }
        }, true));
        inspector.Children.Add(ToolLabel("新增报文", true));
        var idInput = ToolField(inspector, draft, "报文 ID（十进制或 0x）", "0x100");
        var nameInput = ToolField(inspector, draft, "报文名称", "NewMessage");
        var dlcInput = ToolField(inspector, draft, "数据长度 DLC", "8");
        var senderInput = ToolField(inspector, draft, "发送节点", "ECU");
        inspector.Children.Add(ToolButton("添加报文到 DBC", () =>
        {
            try
            {
                document.Content = UiDbcEditing.AddMessage(document.Content, idInput.Text,
                    nameInput.Text, dlcInput.Text, senderInput.Text);
                editor.Text = document.Content;
                var last = UiDbcEditing.Messages(document.Content).Last();
                draft.Fields["DBC MessageId"] = last.EncodedId.ToString();
                RenderToolWorkbench("DBC 编辑器");
            }
            catch (ArgumentException ex) { ShowFileError("报文参数无效", ex); }
        }, true));
        inspector.Children.Add(ToolLabel("向选中报文添加信号", true));
        var signalName = ToolField(inspector, draft, "信号名称", "NewSignal");
        var signalStart = ToolField(inspector, draft, "起始位", "0");
        var signalLength = ToolField(inspector, draft, "位长度", "8");
        var signalEndian = ToolField(inspector, draft, "字节序 (Intel / Motorola)", "Intel");
        var signalSign = ToolField(inspector, draft, "符号 (+ / -)", "+");
        var factor = ToolField(inspector, draft, "缩放", "1");
        var offset = ToolField(inspector, draft, "偏移", "0");
        var lower = ToolField(inspector, draft, "物理最小值", "0");
        var upper = ToolField(inspector, draft, "物理最大值", "255");
        var unit = ToolField(inspector, draft, "单位");
        var receiver = ToolField(inspector, draft, "接收节点", "Vector__XXX");
        inspector.Children.Add(ToolButton("添加信号到选中报文", () =>
        {
            if (messageChooser.SelectedItem is not UiDbcEditing.Message chosen)
            {
                MessageBox.Show(this, "请先选择已有报文，或新建一条报文。", "信号归属");
                return;
            }
            try
            {
                document.Content = UiDbcEditing.AddSignal(document.Content, chosen.EncodedId,
                    signalName.Text, signalStart.Text, signalLength.Text,
                    signalEndian.Text, signalSign.Text, factor.Text, offset.Text,
                    lower.Text, upper.Text, unit.Text, receiver.Text);
                editor.Text = document.Content;
                RenderToolWorkbench("DBC 编辑器");
            }
            catch (ArgumentException ex) { ShowFileError("信号参数无效", ex); }
        }, true));
        inspector.Children.Add(ToolLabel("显示与筛选", true));
        ToolField(inspector, draft, "显示别名方案", "中文信号名称");
        ToolField(inspector, draft, "信号筛选条件");
        ToolNote(inspector, "基础 BO_/SG_ 编辑结果直接写入当前 DBC 草稿。请点击“保存 DBC 文件”提交真实文件；完整语法、跨报文引用和信号重叠检查待正式 DBC 后端校验。");
    }

    private void BuildUdsWorkbench(StackPanel center, StackPanel inspector, ToolDraft draft)
    {
        ToolNote(center, "编辑多条连续 UDS 请求及其顺序。仅建立可保存的请求草稿；当前未连接 ISO-TP/UDS 后端，不会发送请求或显示伪响应。");
        var entries = new ListBox { MinHeight = 220, MaxHeight = 350, BorderBrush = ToolLine,
            BorderThickness = new Thickness(1), FontSize = 13, Padding = new Thickness(6) };
        void Refresh() => entries.ItemsSource = draft.Items.Select((v,i) => (i+1).ToString("00") + "   " + v).ToArray();
        Refresh();
        center.Children.Add(entries);
        var bar = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        center.Children.Add(bar);
        var service = ToolField(inspector, draft, "服务 / Payload (HEX)", "22 F1 90");
        ToolField(inspector, draft, "请求 ID (HEX)", "18DA50F1");
        ToolField(inspector, draft, "响应 ID (HEX)", "18DAF150");
        ToolField(inspector, draft, "超时 (ms)", "1000");
        bar.Children.Add(ToolButton("添加请求", () => { if (!string.IsNullOrWhiteSpace(service.Text)) {
            draft.Items.Add(service.Text.Trim()); Refresh(); } }));
        bar.Children.Add(ToolButton("移除选中", () => { if (entries.SelectedIndex >= 0) {
            draft.Items.RemoveAt(entries.SelectedIndex); Refresh(); } }));
        bar.Children.Add(ToolButton("上移", () => { var i = entries.SelectedIndex;
            if (i > 0) { (draft.Items[i-1],draft.Items[i]) = (draft.Items[i],draft.Items[i-1]); Refresh(); entries.SelectedIndex = i-1; } }));
        bar.Children.Add(ToolButton("下移", () => { var i = entries.SelectedIndex;
            if (i >= 0 && i < draft.Items.Count - 1) { (draft.Items[i+1],draft.Items[i]) =
                (draft.Items[i],draft.Items[i+1]); Refresh(); entries.SelectedIndex = i+1; } }));
        ToolNote(inspector, "物理请求 / ISO-TP / NRC / 关联时间线：待正式后端接线，不提供模拟成功按钮。");
    }

    private void BuildFlashWorkbench(StackPanel center, StackPanel inspector, ToolDraft draft)
    {
        ToolNote(center, "烧录工具分为文件与配置、前置检查、执行阶段和结果证据。本页只编辑烧录方案，不提供假进度和假成功结果。");
        ToolField(center, draft, "固件文件路径");
        center.Children.Add(ToolButton("选择固件文件…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "选择 ECU 固件",
                Filter = "固件文件|*.hex;*.s19;*.mot;*.bin|所有文件|*.*" };
            if (dlg.ShowDialog(this) == true)
            {
                draft.Fields["固件文件路径"] = dlg.FileName;
                RenderToolWorkbench("ECU 烧录");
            }
        }));
        ToolField(center, draft, "Seed / Key DLL 路径");
        center.Children.Add(ToolButton("选择 Key DLL…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "选择 Seed / Key 算法库",
                Filter = "DLL 文件|*.dll|所有文件|*.*" };
            if (dlg.ShowDialog(this) == true)
            {
                draft.Fields["Seed / Key DLL 路径"] = dlg.FileName;
                RenderToolWorkbench("ECU 烧录");
            }
        }));
        center.Children.Add(ToolButton("检查本地文件", () =>
        {
            var firmware = draft.Fields.GetValueOrDefault("固件文件路径", "");
            var keyDll = draft.Fields.GetValueOrDefault("Seed / Key DLL 路径", "");
            var report = "固件文件：" + (File.Exists(firmware) ? "存在" : "不存在或未选择") +
                         "\n算法库：" + (File.Exists(keyDll) ? "存在" : "不存在或未选择") +
                         "\n\n仅检查本地文件是否存在，不代表固件格式、算法兼容性或 ECU 烧录前置检查通过。";
            MessageBox.Show(this, report, "本地文件检查", MessageBoxButton.OK, MessageBoxImage.Information);
        }));
        center.Children.Add(ToolLabel("烧录阶段", true));
        foreach (var phase in new[] { "前置检查", "编程会话", "安全访问", "擦除", "传输下载", "完整性检查", "ECU 复位", "结束归档" })
            center.Children.Add(ToolLabel("○  " + phase));
        ToolField(inspector, draft, "应用请求 ID (HEX)");
        ToolField(inspector, draft, "Boot 请求 ID (HEX)");
        ToolField(inspector, draft, "Boot 响应 ID (HEX)");
        ToolNote(inspector, "所有报文 ID 必须由目标 ECU 的烧录配置确认；当前不执行烧录。");
    }

    private void BuildWaveWorkbench(StackPanel center, StackPanel inspector, ToolDraft draft)
    {
        ToolNote(center, "本窗口仅显示从工程真实 CSV 文件读出的波形。鼠标点击曲线区域交替定位 A/B 游标，可放大、缩小、复位查看；没有文件时不显示虚假采集结果。");
        var toolbar = new WrapPanel();
        toolbar.Children.Add(ToolButton("导入参考波形…", () => ImportToolResource("参考波形")));
        center.Children.Add(toolbar);
        var files = _workFiles.Workspace?.Assets.Where(a => a.Kind == "参考波形")
            .OrderBy(a => a.Name).ToArray() ?? [];
        var selection = new ComboBox
        {
            ItemsSource = files, DisplayMemberPath = "Name",
            Height = 34, Margin = new Thickness(0, 8, 0, 12), BorderBrush = ToolLine
        };
        center.Children.Add(selection);
        var savedId = draft.Fields.GetValueOrDefault("波形资源 ID", "");
        var selected = files.FirstOrDefault(a => a.Id == savedId) ?? files.FirstOrDefault();
        selection.SelectedItem = selected;
        selection.SelectionChanged += (_, _) =>
        {
            if (selection.SelectedItem is UiAsset choice)
            {
                draft.Fields["波形资源 ID"] = choice.Id;
                RenderToolWorkbench("波形分析");
            }
        };
        center.Children.Add(ToolLabel("波形画布 · 文件样本", true));
        var chart = new UiWavePlot();
        var info = ToolLabel("游标 A / B 尚未定位");
        chart.CursorChanged += () => info.Text = chart.CursorInfo;
        var chartBorder = new Border
        {
            Child = chart, MinHeight = 285, BorderBrush = ToolLine,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromRgb(249,251,254))
        };
        center.Children.Add(chartBorder);
        var controls = new WrapPanel { Margin = new Thickness(0, 12, 0, 4) };
        controls.Children.Add(ToolButton("放大 2×", chart.ZoomIn));
        controls.Children.Add(ToolButton("缩小 2×", chart.ZoomOut));
        controls.Children.Add(ToolButton("适应全幅", chart.ResetZoom));
        center.Children.Add(controls);
        center.Children.Add(info);
        if (selected is null)
            ToolNote(center, "当前没有参考 CSV。导入格式为 time_ms,value，支持首行标题。");
        else
        {
            try
            {
                chart.LoadCsv(_workFiles.FullPath(selected.RelativePath));
                center.Children.Add(ToolLabel(selected.RelativePath + " · 有效样本：" + chart.Count));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                ToolNote(center, "波形加载失败：" + ex.Message);
            }
        }
        ToolField(inspector, draft, "波形名称");
        ToolField(inspector, draft, "信号 / 通道");
        ToolField(inspector, draft, "参考容差");
        ToolNote(inspector, "A/B 游标仅对现有样本定位。自动采样和阈值判定须接入正式 RECORD/TEST 后端，当前不生成判定结论。");
    }

    private void BuildBenchWorkbench(StackPanel center, StackPanel inspector, ToolDraft draft)
    {
        ToolNote(center, "20 路 Key 为电阻网络开关，组合生成 5 路模拟电压输出。此处仅编辑闭合 / 保持的目标状态；不推算虚构电压，不发送控制命令。");
        center.Children.Add(ToolLabel("20 Key 开关目标状态", true));
        var keys = new UniformGrid { Columns = 5, Rows = 4, Margin = new Thickness(0,0,0,14) };
        void RebuildKeys()
        {
            keys.Children.Clear();
            for (int i = 1; i <= 20; i++)
            {
                var id = i;
                var active = draft.HeldKeys.Contains(id);
                var aliasKey = "KeyName." + id.ToString("00");
                var alias = draft.Fields.GetValueOrDefault(aliasKey, "Key " + id.ToString("00"));
                var button = ToolButton(alias + (active ? "  ●" : "  ○"), () =>
                {
                    if (!draft.HeldKeys.Add(id)) draft.HeldKeys.Remove(id);
                    RebuildKeys();
                }, active);
                button.Margin = new Thickness(3);
                button.ToolTip = "左键切换组合目标状态；右键修改按键显示名称";
                var rename = new ContextMenu();
                var editName = new MenuItem { Header = "重命名按键…" };
                editName.Click += (_, _) =>
                {
                    var renamed = AskName(this, "按键 " + id.ToString("00") + " 显示名称", alias);
                    if (renamed is not null)
                    {
                        draft.Fields[aliasKey] = renamed;
                        RebuildKeys();
                    }
                };
                rename.Items.Add(editName);
                button.ContextMenu = rename;
                keys.Children.Add(button);
            }
        }
        RebuildKeys();
        center.Children.Add(keys);
        var controls = new WrapPanel();
        controls.Children.Add(ToolButton("全部释放", () => { draft.HeldKeys.Clear(); RebuildKeys(); }));
        controls.Children.Add(ToolButton("加入动作序列", () =>
        {
            draft.Items.Add("Key[" + string.Join(",", draft.HeldKeys.Order()) + "] · " +
                draft.Fields.GetValueOrDefault("保持时间 (ms)", "500") + " ms");
            RenderToolWorkbench("手动台架");
        }));
        center.Children.Add(controls);
        center.Children.Add(ToolLabel("动作序列", true));
        var list = new ListBox { ItemsSource = draft.Items, MinHeight = 120,
            BorderThickness = new Thickness(1), BorderBrush = ToolLine };
        center.Children.Add(list);
        center.Children.Add(ToolButton("删除选中动作", () =>
        {
            if (list.SelectedIndex >= 0) { draft.Items.RemoveAt(list.SelectedIndex); RenderToolWorkbench("手动台架"); }
        }));
        ToolField(inspector, draft, "保持时间 (ms)", "500");
        inspector.Children.Add(ToolLabel("工位控制目标（仅草稿）", true));
        var ign = ToolButton("IGN 目标：" + draft.Fields.GetValueOrDefault("IGN 目标", "关闭"), () =>
        {
            draft.Fields["IGN 目标"] = draft.Fields.GetValueOrDefault("IGN 目标", "关闭") == "关闭" ? "开启" : "关闭";
            RenderToolWorkbench("手动台架");
        });
        inspector.Children.Add(ign);
        inspector.Children.Add(ToolButton("夹具目标：" + draft.Fields.GetValueOrDefault("夹具目标", "释放"), () =>
        {
            draft.Fields["夹具目标"] = draft.Fields.GetValueOrDefault("夹具目标", "释放") == "释放" ? "压紧" : "释放";
            RenderToolWorkbench("手动台架");
        }));
        ToolNote(inspector, "这些开关只是待执行目标，绝不代表实际 IO 或 ACK 状态。待 DEVICE 合同接入后才能操作实物。");
    }
}
