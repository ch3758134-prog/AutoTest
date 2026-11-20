using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace AutoTest.TestHiFi;

// UI-04 / UI-06: editable, on-disk TEST authoring and debugger presentation.
// Execution is deliberately NOT simulated. The formal TEST executor is not wired here.
public sealed class UiTestProgram
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "自动测试项目";
    public List<UiTestCase> Cases { get; set; } = [];
    public List<UiTestCase> Flows { get; set; } = [];
}
public sealed class UiTestCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新建测试用例";
    public bool Enabled { get; set; } = true;
    public int Revision { get; set; } = 1;
    public List<UiTestStep> Steps { get; set; } = [];
}
public sealed class UiTestStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phase { get; set; } = "测试";
    public string Type { get; set; } = "等待";
    public string Name { get; set; } = "等待";
    public int Depth { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Breakpoint { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = [];
}

public partial class MainWindow
{
    private UiTestProgram? _testProgram;
    private UiDocument? _testProgramDocument;
    private string? _testProgramRoot;
    private string? _testCaseId;
    private string? _testStepId;
    private string _testPhase = "测试";
    private bool _debugPane;
    private readonly HashSet<string> _modifiedFlowIds = new();
    private readonly Stack<string> _testUndo = new();
    private readonly Stack<string> _testRedo = new();
    private readonly List<string> _debugEvents = [];
    private static readonly string[] StepTypes =
    [
        "CAN 单次发送", "CAN 周期发送", "CAN 信号判定", "等待 CAN 信号",
        "UDS 请求", "ECU 烧录", "Key 组合动作", "IGN 控制", "夹具控制",
        "波形采集", "波形判定", "等待", "设置变量", "变量判定",
        "IF", "ELSE", "SWITCH", "CASE", "循环", "异常处理", "调用公共流程", "备注"
    ];

    private UiTestCase? ActiveTestCase =>
        _testProgram?.Cases.Concat(_testProgram.Flows).FirstOrDefault(c => c.Id == _testCaseId);
    private bool ActiveIsFlow => _testProgram?.Flows.Any(c => c.Id == _testCaseId) == true;
    private UiTestStep? ActiveTestStep =>
        ActiveTestCase?.Steps.FirstOrDefault(s => s.Id == _testStepId);

    private static TextBlock EditorText(string text, bool heading = false) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.FromRgb(35, 52, 74)),
        FontSize = heading ? 15 : 12.5,
        FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, heading ? 14 : 7)
    };
    private static Button EditorButton(string label, Action action)
    {
        var b = new Button
        {
            Content = label, Height = 32, MinWidth = 68,
            Margin = new Thickness(0, 0, 7, 7), Padding = new Thickness(9, 3, 9, 3),
            Background = Brushes.White, Foreground = new SolidColorBrush(Color.FromRgb(29, 64, 104)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 222, 239)), BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        b.Click += (_, _) => action();
        return b;
    }
    private static Border EditorCard(UIElement child, Thickness margin) => new()
    {
        Child = child, Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(219, 229, 242)),
        BorderThickness = new Thickness(1), Padding = new Thickness(13),
        Margin = margin, CornerRadius = new CornerRadius(6)
    };
    private static TextBox EditorInput(StackPanel panel, string label, string value, Action<string> changed, bool multiline = false)
    {
        panel.Children.Add(EditorText(label));
        var input = new TextBox
        {
            Text = value, FontSize = 12.5, MinHeight = multiline ? 70 : 31,
            AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 222, 238))
        };
        input.TextChanged += (_, _) => changed(input.Text);
        panel.Children.Add(input);
        return input;
    }
    private void EnsureTestProgram()
    {
        if (!_workFiles.HasProject)
        {
            _testProgram = null; _testProgramDocument = null; _testProgramRoot = null;
            return;
        }
        if (_testProgram is not null && _testProgramRoot == _workFiles.Root) return;
        _testUndo.Clear(); _testRedo.Clear(); _modifiedFlowIds.Clear();
        _testProgramRoot = _workFiles.Root;
        _testProgram = new UiTestProgram();
        _testProgramDocument = null;
        var asset = _workFiles.Workspace!.Assets.FirstOrDefault(a =>
            a.Kind == "测试程序" && a.Name == "UI 测试编辑器");
        if (asset is null)
        {
            _testCaseId = null; _testStepId = null;
            return;
        }
        try
        {
            var doc = _workFiles.OpenAsset(asset);
            var program = JsonSerializer.Deserialize<UiTestProgram>(doc.Content);
            if (program?.Version != 1 || program.Cases is null || program.Flows is null)
                throw new InvalidDataException("测试编辑器文档结构无效。");
            _testProgram = program;
            _testProgramDocument = doc;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            StatusText = "测试编辑器文件读取失败：" + ex.Message;
        }
        _testCaseId = _testProgram.Cases.FirstOrDefault()?.Id ??
            _testProgram.Flows.FirstOrDefault()?.Id;
        _testStepId = null;
    }
    private void CaptureTestChange()
    {
        if (_testProgram is null) return;
        _testUndo.Push(JsonSerializer.Serialize(_testProgram));
        if (_testUndo.Count > 150)
        {
            var arr = _testUndo.Take(100).Reverse().ToArray();
            _testUndo.Clear();
            foreach (var value in arr) _testUndo.Push(value);
        }
        _testRedo.Clear();
        WriteTestContent();
    }
    private void WriteTestContent()
    {
        if (_testProgramDocument is not null && _testProgram is not null)
        {
            _testProgramDocument.Content = JsonSerializer.Serialize(
                _testProgram, new JsonSerializerOptions { WriteIndented = true });
            RefreshTabStrip();
        }
    }
    private void ChangeTest(Action mutation, bool render = true)
    {
        // Create an on-disk draft immediately on first edit so the existing
        // dirty-file confirmation protects edits on project switch and shutdown.
        if (_workFiles.HasProject && _testProgramDocument is null)
        {
            try
            {
                var asset = _workFiles.NewAsset("测试程序", "UI 测试编辑器");
                _testProgramDocument = _workFiles.OpenAsset(asset);
                _testProgramDocument.Content = JsonSerializer.Serialize(
                    _testProgram, new JsonSerializerOptions { WriteIndented = true });
                _workFiles.Save(_testProgramDocument);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
            {
                ShowFileError("创建测试编辑文档失败", ex);
                return;
            }
        }
        var editingFlowId = ActiveIsFlow ? _testCaseId : null;
        CaptureTestChange();
        mutation();
        if (editingFlowId is not null && _testProgram?.Flows.Any(x => x.Id == editingFlowId) == true)
            _modifiedFlowIds.Add(editingFlowId);
        WriteTestContent();
        if (render) RenderTestEditor();
    }
    private void UndoTest(bool redo)
    {
        if (_testProgram is null) return;
        var from = redo ? _testRedo : _testUndo;
        var to = redo ? _testUndo : _testRedo;
        if (from.Count == 0) { StatusText = "没有可" + (redo ? "重做" : "撤销") + "的测试编辑。"; return; }
        to.Push(JsonSerializer.Serialize(_testProgram));
        _testProgram = JsonSerializer.Deserialize<UiTestProgram>(from.Pop())!;
        WriteTestContent();
        RenderTestEditor();
    }
    private bool SaveTestProgram()
    {
        if (!_workFiles.HasProject || _testProgram is null)
        {
            StatusText = "请先创建或打开工程，然后编辑测试程序。";
            return false;
        }
        try
        {
            if (_testProgramDocument is null)
            {
                var asset = _workFiles.NewAsset("测试程序", "UI 测试编辑器");
                _testProgramDocument = _workFiles.OpenAsset(asset);
            }
            var previousRevisions = _testProgram.Flows
                .Where(x => _modifiedFlowIds.Contains(x.Id))
                .ToDictionary(x => x.Id, x => x.Revision);
            foreach (var flow in _testProgram.Flows.Where(x => previousRevisions.ContainsKey(x.Id)))
                flow.Revision++;
            WriteTestContent();
            try { _workFiles.Save(_testProgramDocument); }
            catch
            {
                foreach (var flow in _testProgram.Flows.Where(x => previousRevisions.ContainsKey(x.Id)))
                    flow.Revision = previousRevisions[flow.Id];
                WriteTestContent();
                throw;
            }
            _modifiedFlowIds.Clear();
            RefreshProjectTree(); RefreshTabStrip(); StoreSession();
            StatusText = "测试编辑器文件已保存（UI-only 文档，不是正式 TEST DSL）："+
                _testProgramDocument.Asset.RelativePath;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        { ShowFileError("保存测试程序失败", ex); return false; }
    }
    private void CreateTestCase()
    {
        if (_testProgram is null) return;
        var name = AskName(this, "新建测试用例", "新建用例");
        if (name is null) return;
        ChangeTest(() =>
        {
            var c = new UiTestCase { Name = name };
            _testProgram.Cases.Add(c); _testCaseId = c.Id; _testStepId = null;
        });
    }
    private void CreatePublicFlow()
    {
        if (_testProgram is null) return;
        var name = AskName(this, "新建公共流程", "新建公共流程");
        if (name is null) return;
        ChangeTest(() =>
        {
            var flow = new UiTestCase { Name = name };
            _testProgram.Flows.Add(flow);
            _testCaseId = flow.Id; _testStepId = null; _testPhase = "测试";
        });
    }
    private void AddTestStep()
    {
        var c = ActiveTestCase;
        if (c is null) { StatusText = "先新建或选择测试用例。"; return; }
        var menu = new ContextMenu();
        foreach (var type in StepTypes)
        {
            var selected = type;
            var item = new MenuItem { Header = selected };
            item.Click += (_, _) => ChangeTest(() =>
            {
                var step = new UiTestStep { Phase = _testPhase, Type = selected, Name = selected };
                var index = c.Steps.FindIndex(s => s.Id == _testStepId);
                if (index < 0) c.Steps.Add(step);
                else c.Steps.Insert(index + 1, step);
                _testStepId = step.Id;
            });
            menu.Items.Add(item);
        }
        menu.PlacementTarget = TestEditorHost;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
        menu.IsOpen = true;
    }
    private void MoveTestStep(int delta)
    {
        var c = ActiveTestCase; if (c is null || _testStepId is null) return;
        var index = c.Steps.FindIndex(s => s.Id == _testStepId);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= c.Steps.Count) return;
        ChangeTest(() => (c.Steps[index], c.Steps[target]) = (c.Steps[target], c.Steps[index]));
    }
    private void PreflightTest(string scope)
    {
        _debugPane = true;
        _debugEvents.Clear();
        var c = ActiveTestCase;
        if (!_workFiles.HasProject || c is null)
            _debugEvents.Add("未就绪：没有打开的测试工程或当前用例。");
        else
        {
            if (scope == "已启用用例")
            {
                var enabled = _testProgram!.Cases.Where(x => x.Enabled).ToArray();
                _debugEvents.Add("勾选/启用运行范围：" + enabled.Length + " 个用例。");
                foreach (var item in enabled)
                    _debugEvents.Add("· " + item.Name + "：" + item.Steps.Count(x => x.Enabled) + " 个已启用步骤");
            }
            var selected = scope == "当前步骤" ? ActiveTestStep : null;
            _debugEvents.Add("检查范围：" + scope + " · " + c.Name);
            if (scope == "当前步骤" && selected is null)
                _debugEvents.Add("阻断：未选择可执行步骤。");
            if (scope != "当前步骤" && !c.Steps.Any(s => s.Enabled))
                _debugEvents.Add("阻断：当前用例没有启用的步骤。");
            foreach (var step in c.Steps.Where(s => s.Enabled && (selected is null || s.Id == selected.Id)))
                if (step.Parameters.Count == 0 && step.Type != "备注")
                    _debugEvents.Add("待配置：" + step.Phase + " / " + step.Name + "（未填写步骤参数）");
            _debugEvents.Add("执行阻断：UI 独立验证工程尚未接入正式 TEST 执行器、真实设备会话和证据链。");
            _debugEvents.Add("没有启动硬件，也没有产生 PASS/FAIL 或任何模拟反馈。");
        }
        RenderTestEditor();
    }

    private void RenderTestEditor()
    {
        EnsureTestProgram();
        TestEditorHost.Visibility = Visibility.Visible;
        ToolDescriptionPanel.Visibility = Visibility.Collapsed;
        DocumentEditorPanel.Visibility = Visibility.Collapsed;
        WorkspaceTitle.Text = "自动测试 · 编辑与调试";
        WorkspaceSubtitle.Text = "UI-04 / UI-06 · 工程测试程序编辑、步骤参数与执行前检查";
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(247, 249, 253)) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        if (_testProgram is null)
        {
            root.ColumnDefinitions[0].Width = new GridLength(0);
            root.ColumnDefinitions[2].Width = new GridLength(0);
            var intro = new StackPanel();
            intro.Children.Add(EditorText("先创建或打开 UI 工程，再管理真实可保存的测试程序。", true));
            intro.Children.Add(EditorButton("新建工程…", () => NewProject_Click(this, new RoutedEventArgs())));
            intro.Children.Add(EditorButton("打开工程…", () => OpenProject_Click(this, new RoutedEventArgs())));
            var pane = EditorCard(intro, new Thickness(0));
            Grid.SetColumn(pane, 1); root.Children.Add(pane);
            TestEditorHost.Content = root;
            return;
        }
        var cases = new StackPanel();
        cases.Children.Add(EditorText("测试用例", true));
        cases.Children.Add(EditorButton("＋ 新建用例", CreateTestCase));
        foreach (var test in _testProgram.Cases)
        {
            var current = test;
            var btn = EditorButton((current.Id == _testCaseId ? "●  " : "    ") + current.Name,
                () => { _testCaseId = current.Id; _testStepId = null; RenderTestEditor(); });
            btn.HorizontalContentAlignment = HorizontalAlignment.Left;
            btn.Width = 174;
            cases.Children.Add(btn);
        }
        cases.Children.Add(EditorText("公共流程 · 单一连续步骤树", true));
        cases.Children.Add(EditorButton("＋ 新建公共流程", CreatePublicFlow));
        foreach (var flow in _testProgram.Flows)
        {
            var selectedFlow = flow;
            var btn = EditorButton((flow.Id == _testCaseId ? "●  " : "    ") + flow.Name,
                () => { _testCaseId = selectedFlow.Id; _testStepId = null;
                    _testPhase = "测试"; RenderTestEditor(); });
            btn.HorizontalContentAlignment = HorizontalAlignment.Left;
            btn.Width = 174;
            cases.Children.Add(btn);
        }
        var left = EditorCard(new ScrollViewer { Content = cases, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            new Thickness(0, 0, 8, 0));
        Grid.SetColumn(left, 0); root.Children.Add(left);

        var mid = new StackPanel();
        var bar = new WrapPanel();
        bar.Children.Add(EditorButton("＋ 添加步骤", AddTestStep));
        bar.Children.Add(EditorButton("↑ 上移", () => MoveTestStep(-1)));
        bar.Children.Add(EditorButton("↓ 下移", () => MoveTestStep(1)));
        bar.Children.Add(EditorButton("复制", () =>
        {
            var c = ActiveTestCase; var selected = ActiveTestStep;
            if (c is null || selected is null) return;
            ChangeTest(() =>
            {
                var copy = JsonSerializer.Deserialize<UiTestStep>(JsonSerializer.Serialize(selected))!;
                copy.Id = Guid.NewGuid().ToString("N");
                copy.Name += " 副本";
                c.Steps.Insert(c.Steps.IndexOf(selected) + 1, copy);
                _testStepId = copy.Id;
            });
        }));
        bar.Children.Add(EditorButton("删除", () =>
        {
            var c = ActiveTestCase; var selected = ActiveTestStep;
            if (c is null || selected is null) return;
            ChangeTest(() => { c.Steps.Remove(selected); _testStepId = null; });
        }));
        bar.Children.Add(EditorButton("撤销", () => UndoTest(false)));
        bar.Children.Add(EditorButton("重做", () => UndoTest(true)));
        bar.Children.Add(EditorButton("保存", () => SaveTestProgram()));
        mid.Children.Add(bar);
        var phaseBar = new WrapPanel();
        if (ActiveIsFlow) _testPhase = "测试";
        foreach (var phase in (ActiveIsFlow ? new[] { "测试" } : new[] { "准备", "测试", "清理" }))
        {
            var selectedPhase = phase;
            phaseBar.Children.Add(EditorButton((_testPhase == phase ? "● " : "") + phase,
                () => { _testPhase = selectedPhase; _testStepId = null; RenderTestEditor(); }));
        }
        mid.Children.Add(phaseBar);
        mid.Children.Add(EditorText("步骤结构 · " + (ActiveIsFlow ? "公共流程连续步骤" : _testPhase), true));
        var stepList = new ListBox
        {
            MinHeight = 200, MaxHeight = 440, BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(219, 229, 242)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        foreach (var step in ActiveTestCase?.Steps.Where(s => s.Phase == _testPhase) ?? [])
        {
            var current = step;
            var row = new ListBoxItem
            {
                Tag = current, Content = (step.Breakpoint ? "●  " : "   ") +
                    new string(' ', Math.Clamp(step.Depth, 0, 7) * 3) +
                    (step.Enabled ? "" : "[禁用] ") + step.Name + "  ·  " + step.Type,
                Padding = new Thickness(6), FontSize = 12.7,
                IsSelected = step.Id == _testStepId
            };
            var menu = new ContextMenu();
            var breakpoint = new MenuItem { Header = step.Breakpoint ? "取消断点" : "设置断点" };
            breakpoint.Click += (_, _) => ChangeTest(() => current.Breakpoint = !current.Breakpoint);
            menu.Items.Add(breakpoint);
            var run = new MenuItem { Header = "执行此步骤（检查执行前置条件）" };
            run.Click += (_, _) => { _testStepId = current.Id; PreflightTest("当前步骤"); };
            menu.Items.Add(run);
            row.ContextMenu = menu;
            stepList.Items.Add(row);
        }
        stepList.SelectionChanged += (_, _) =>
        {
            if (stepList.SelectedItem is ListBoxItem { Tag: UiTestStep step } &&
                step.Id != _testStepId)
            { _testStepId = step.Id; RenderTestEditor(); }
        };
        mid.Children.Add(stepList);
        var runBar = new WrapPanel();
        runBar.Children.Add(EditorButton("运行此步骤", () => PreflightTest("当前步骤")));
        runBar.Children.Add(EditorButton("运行此用例", () => PreflightTest("当前用例")));
        runBar.Children.Add(EditorButton("运行选中用例", () => PreflightTest("已启用用例")));
        runBar.Children.Add(EditorButton("单步 / 继续", () => PreflightTest("当前步骤")));
        runBar.Children.Add(EditorButton("运行到此处", () => PreflightTest("当前步骤")));
        runBar.Children.Add(EditorButton("停止", () => { _debugEvents.Add("停止：当前没有实际运行的 TEST 会话。"); _debugPane = true; RenderTestEditor(); }));
        mid.Children.Add(runBar);
        mid.Children.Add(EditorButton(_debugPane ? "收起调试信息" : "展开调试信息",
            () => { _debugPane = !_debugPane; RenderTestEditor(); }));
        if (_debugPane)
        {
            mid.Children.Add(EditorText("运行与调试 · 执行前检查", true));
            mid.Children.Add(EditorText("执行状态：未启动｜设备会话：未连接｜结果：无｜模式：真实执行前置检查"));
            var breakpointRows = ActiveTestCase?.Steps.Where(x => x.Breakpoint)
                .Select(x => x.Phase + " · " + x.Name).ToArray() ?? [];
            mid.Children.Add(EditorText("断点列表（" + breakpointRows.Length + "）", true));
            foreach (var breakpoint in breakpointRows) mid.Children.Add(EditorText("●  " + breakpoint));
            if (breakpointRows.Length == 0) mid.Children.Add(EditorText("当前用例未设置断点。"));
            mid.Children.Add(EditorText("变量监视", true));
            var variables = ActiveTestCase?.Steps.Where(x => x.Type == "设置变量")
                .Select(x => x.Parameters.GetValueOrDefault("变量名称", "未命名变量") +
                    " = " + x.Parameters.GetValueOrDefault("表达式", "尚未配置"))
                .ToArray() ?? [];
            foreach (var variable in variables) mid.Children.Add(EditorText(variable));
            if (variables.Length == 0) mid.Children.Add(EditorText("尚无变量定义；运行期变量值需由实际执行器提供。"));
            mid.Children.Add(EditorText("诊断与操作时间线", true));
            var text = new TextBox
            {
                IsReadOnly = true, MinHeight = 95, MaxHeight = 200,
                Text = string.Join(Environment.NewLine, _debugEvents),
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(249, 251, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 230, 242))
            };
            mid.Children.Add(text);
        }
        var middle = EditorCard(new ScrollViewer { Content = mid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            new Thickness(0, 0, 8, 0));
        Grid.SetColumn(middle, 1); root.Children.Add(middle);

        var inspector = new StackPanel();
        inspector.Children.Add(EditorText("用例与步骤参数", true));
        var testCase = ActiveTestCase;
        if (testCase is not null)
        {
            EditorInput(inspector, "用例名称", testCase.Name, value =>
            {
                if (testCase.Name == value) return;
                ChangeTest(() => testCase.Name = value, false);
            });
            inspector.Children.Add(EditorButton(testCase.Enabled ? "已启用" : "已禁用",
                () => ChangeTest(() => testCase.Enabled = !testCase.Enabled)));
            inspector.Children.Add(EditorButton(ActiveIsFlow ? "删除当前公共流程" : "删除当前用例", () =>
            {
                if (MessageBox.Show(this, "将当前用例从 UI 文件中移除？", "删除用例",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                ChangeTest(() =>
                {
                    if (ActiveIsFlow) _testProgram.Flows.Remove(testCase);
                    else _testProgram.Cases.Remove(testCase);
                    _testCaseId = _testProgram.Cases.FirstOrDefault()?.Id ??
                        _testProgram.Flows.FirstOrDefault()?.Id;
                    _testStepId = null;
                });
            }));
        }
        var stepSelected = ActiveTestStep;
        if (stepSelected is not null)
        {
            inspector.Children.Add(EditorText("当前步骤 · " + stepSelected.Type, true));
            EditorInput(inspector, "步骤名称", stepSelected.Name, value =>
            {
                if (stepSelected.Name == value) return;
                ChangeTest(() => stepSelected.Name = value, false);
            });
            var typeBox = new ComboBox { ItemsSource = StepTypes, SelectedItem = stepSelected.Type,
                MinHeight = 33, Margin = new Thickness(0, 0, 0, 12) };
            typeBox.SelectionChanged += (_, _) =>
            {
                if (typeBox.SelectedItem is string type && type != stepSelected.Type)
                    ChangeTest(() => stepSelected.Type = type);
            };
            inspector.Children.Add(EditorText("步骤类型"));
            inspector.Children.Add(typeBox);
            inspector.Children.Add(EditorButton(stepSelected.Enabled ? "禁用步骤" : "启用步骤",
                () => ChangeTest(() => stepSelected.Enabled = !stepSelected.Enabled)));
            inspector.Children.Add(EditorButton(stepSelected.Breakpoint ? "取消断点" : "设置断点",
                () => ChangeTest(() => stepSelected.Breakpoint = !stepSelected.Breakpoint)));
            inspector.Children.Add(EditorButton("增加缩进", () => ChangeTest(() => stepSelected.Depth = Math.Min(7, stepSelected.Depth + 1))));
            inspector.Children.Add(EditorButton("减少缩进", () => ChangeTest(() => stepSelected.Depth = Math.Max(0, stepSelected.Depth - 1))));
            if (stepSelected.Type.StartsWith("CAN ", StringComparison.Ordinal))
                inspector.Children.Add(EditorButton("从工程 DBC 选择报文 / 信号…",
                    () => OpenStepDbcPicker(stepSelected)));
            if (stepSelected.Type == "调用公共流程")
            {
                inspector.Children.Add(EditorText("选择公共流程资源", true));
                foreach (var flow in _testProgram.Flows)
                {
                    var target = flow;
                    inspector.Children.Add(EditorButton("引用 · " + flow.Name + " (Rev " + flow.Revision + ")", () =>
                        ChangeTest(() =>
                        {
                            stepSelected.Parameters["公共流程资源 ID"] = target.Id;
                            stepSelected.Parameters["固定修订"] = target.Revision.ToString();
                        })));
                }
            }
            foreach (var key in ParameterKeys(stepSelected.Type))
            {
                var parameter = key;
                EditorInput(inspector, key, stepSelected.Parameters.GetValueOrDefault(key, ""), value =>
                {
                    if (stepSelected.Parameters.GetValueOrDefault(parameter, "") == value) return;
                    ChangeTest(() => stepSelected.Parameters[parameter] = value, false);
                }, key == "备注");
            }
        }
        else inspector.Children.Add(EditorText("选中步骤后在这里直接修改参数。添加步骤后无需另开属性窗口。"));
        inspector.Children.Add(EditorText("UI 工作区未接线真实设备。运行命令只报告阻断条件，不生成模拟结果。"));
        var right = EditorCard(new ScrollViewer { Content = inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            new Thickness(0));
        Grid.SetColumn(right, 2); root.Children.Add(right);
        TestEditorHost.Content = root;
        WorkspaceStatusText.Text = "TEST 编辑 · " + (_testProgramDocument?.Dirty == true ? "● 未保存更改" : "UI 文件可保存") +
            " · 执行器未接线";
    }
    private void OpenStepDbcPicker(UiTestStep step)
    {
        var files = _workFiles.Workspace?.Assets.Where(a => a.Kind == "DBC").OrderBy(a => a.Name).ToArray() ?? [];
        if (files.Length == 0)
        {
            MessageBox.Show(this,
                "工程还没有 DBC 文件。可先从“工具 → DBC 编辑器”直接创建并定义报文/信号，再回来选择。",
                "选择 DBC 资源", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new Window
        {
            Owner = this, Title = "选择工程 DBC 报文与信号", Width = 870, Height = 560,
            MinWidth = 670, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White, FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        var shell = new Grid { Margin = new Thickness(15) };
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fileList = new ListBox { ItemsSource = files, DisplayMemberPath = "Name",
            Margin = new Thickness(0, 0, 10, 0) };
        var messageList = new ListBox { DisplayMemberPath = "Label", Margin = new Thickness(0, 0, 10, 0) };
        var signalList = new ListBox();
        Grid.SetColumn(fileList, 0); Grid.SetColumn(messageList, 1); Grid.SetColumn(signalList, 2);
        shell.Children.Add(fileList); shell.Children.Add(messageList); shell.Children.Add(signalList);
        UiDocument? selectedDoc = null;
        fileList.SelectionChanged += (_, _) =>
        {
            messageList.ItemsSource = null; signalList.ItemsSource = null;
            if (fileList.SelectedItem is not UiAsset asset) return;
            try
            {
                selectedDoc = _workFiles.OpenAsset(asset);
                messageList.ItemsSource = UiDbcEditing.Messages(selectedDoc.Content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { ShowFileError("读取 DBC 文件失败", ex); }
        };
        messageList.SelectionChanged += (_, _) =>
        {
            signalList.ItemsSource = null;
            if (selectedDoc is not null && messageList.SelectedItem is UiDbcEditing.Message message)
                signalList.ItemsSource = UiDbcEditing.SignalNames(selectedDoc.Content, message.EncodedId);
        };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(EditorButton("取消", () => dialog.Close()));
        actions.Children.Add(EditorButton("引用选中报文 / 信号", () =>
        {
            if (fileList.SelectedItem is not UiAsset asset ||
                messageList.SelectedItem is not UiDbcEditing.Message message)
            {
                MessageBox.Show(dialog, "请至少选择一个 DBC 文件及报文。", "引用 DBC");
                return;
            }
            var signal = signalList.SelectedItem as string;
            if (step.Type is "CAN 信号判定" or "等待 CAN 信号" && signal is null)
            {
                MessageBox.Show(dialog, "当前步骤需要选择一个真实 DBC 信号。", "引用 DBC");
                return;
            }
            ChangeTest(() =>
            {
                step.Parameters["DBC 文件"] = asset.RelativePath;
                step.Parameters["DBC 报文引用"] = message.Name;
                step.Parameters["报文"] = message.Name;
                step.Parameters["报文 ID"] = "0x" +
                    (message.EncodedId & 0x1FFFFFFFu).ToString("X");
                if (signal is not null) step.Parameters["信号"] = signal;
            });
            dialog.Close();
        }));
        Grid.SetRow(actions, 1); Grid.SetColumnSpan(actions, 3); shell.Children.Add(actions);
        dialog.Content = shell;
        fileList.SelectedItem = files.FirstOrDefault(a =>
            a.RelativePath == step.Parameters.GetValueOrDefault("DBC 文件")) ?? files[0];
        dialog.ShowDialog();
    }

    private static string[] ParameterKeys(string type) => type switch
    {
        "CAN 单次发送" or "CAN 周期发送" => ["报文 ID", "数据 HEX", "DBC 报文引用", "周期 ms"],
        "CAN 信号判定" or "等待 CAN 信号" => ["DBC 文件", "报文", "信号", "比较方式", "期望值", "超时 ms"],
        "UDS 请求" => ["请求 ID", "响应 ID", "服务与数据 HEX", "超时 ms"],
        "ECU 烧录" => ["烧录配置引用", "固件文件引用"],
        "Key 组合动作" => ["KeyMask (20位)", "保持时间 ms"],
        "IGN 控制" or "夹具控制" => ["目标状态", "超时 ms"],
        "波形采集" or "波形判定" => ["信号引用", "参考波形", "容差", "持续时间 ms"],
        "等待" => ["持续时间 ms"],
        "IF" or "ELSE" or "SWITCH" or "CASE" or "循环" or "异常处理" => ["条件表达式", "循环次数", "备注"],
        "调用公共流程" => ["公共流程资源 ID", "固定修订"],
        "设置变量" or "变量判定" => ["变量名称", "表达式"],
        _ => ["备注"]
    };
}
