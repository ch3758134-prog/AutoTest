using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoTest.Frontend;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public ObservableCollection<CaseDraft> Cases { get; } = [];
    public ObservableCollection<CanDraft> CanRows { get; } = [];
    public ObservableCollection<KeyActionDraft> KeyActions { get; } = [];

    private CaseDraft? _selectedCase;
    public CaseDraft? SelectedCase
    {
        get => _selectedCase;
        set { _selectedCase = value; Changed(); SelectedStep = null; }
    }
    private StepDraft? _selectedStep;
    public StepDraft? SelectedStep
    {
        get => _selectedStep;
        set { _selectedStep = value; Changed(); }
    }
    private CanDraft? _selectedCan;
    public CanDraft? SelectedCan
    {
        get => _selectedCan;
        set { _selectedCan = value; Changed(); }
    }
    private KeyActionDraft? _selectedAction;
    public KeyActionDraft? SelectedAction
    {
        get => _selectedAction;
        set { _selectedAction = value; Changed(); SelectedKeyStep = null; }
    }
    private KeyStepDraft? _selectedKeyStep;
    public KeyStepDraft? SelectedKeyStep
    {
        get => _selectedKeyStep;
        set { _selectedKeyStep = value; Changed(); }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ShowTest_Click(this, new RoutedEventArgs());
    }

    private void ShowTest_Click(object sender, RoutedEventArgs e)
    {
        TestWorkspace.Visibility = Visibility.Visible;
        BenchWorkspace.Visibility = Visibility.Collapsed;
        TestNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        BenchNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        WorkspaceHeading.Text = "自动测试";
        WorkspaceSubheading.Text = "真实操作体验设计 · 暂无执行器";
        StatusText.Text = "自动测试编辑器为 UI 草稿，不属于正式 TEST 工程。";
    }

    private void ShowBench_Click(object sender, RoutedEventArgs e)
    {
        TestWorkspace.Visibility = Visibility.Collapsed;
        BenchWorkspace.Visibility = Visibility.Visible;
        TestNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        BenchNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        WorkspaceHeading.Text = "手动台架";
        WorkspaceSubheading.Text = "CAN 发送 / KEY 动作 / 真实监视工作区设计";
        StatusText.Text = "手动台架当前只可编辑界面草稿，不会发送 CAN、KEY 或 GPIO 控制。";
    }

    private void AddCase_Click(object sender, RoutedEventArgs e)
    {
        var entry = new CaseDraft { Name = "测试用例 " + (Cases.Count + 1) };
        Cases.Add(entry);
        SelectedCase = entry;
        CaseList.ScrollIntoView(entry);
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCase is null) AddCase_Click(sender, e);
        if (SelectedCase is null) return;
        var phase = (PhasePicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "测试";
        var step = new StepDraft { Phase = phase, Name = "新步骤 " + (SelectedCase.Steps.Count + 1) };
        SelectedCase.Steps.Add(step);
        SelectedStep = step;
        StepsGrid.ScrollIntoView(step);
    }
    private void CopyStep_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCase is null || SelectedStep is null) return;
        var source = SelectedStep;
        var copy = new StepDraft {
            Phase = source.Phase, Kind = source.Kind, Name = source.Name + " - 副本",
            Target = source.Target, Value = source.Value, Note = source.Note, Enabled = source.Enabled
        };
        SelectedCase.Steps.Insert(SelectedCase.Steps.IndexOf(source) + 1, copy);
        SelectedStep = copy;
    }
    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCase is null || SelectedStep is null) return;
        SelectedCase.Steps.Remove(SelectedStep);
        SelectedStep = null;
    }

    private void AddCan_Click(object sender, RoutedEventArgs e)
    {
        var row = new CanDraft { Name = "报文 " + (CanRows.Count + 1) };
        CanRows.Add(row);
        SelectedCan = row;
        CanGrid.ScrollIntoView(row);
    }
    private void CopyCan_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCan is null) return;
        var copy = new CanDraft
        {
            Name = SelectedCan.Name + " - 副本", Id = SelectedCan.Id,
            Data = SelectedCan.Data, Included = SelectedCan.Included
        };
        CanRows.Insert(CanRows.IndexOf(SelectedCan) + 1, copy);
        SelectedCan = copy;
    }
    private void DeleteCan_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCan is null) return;
        CanRows.Remove(SelectedCan);
        SelectedCan = null;
    }

    private void AddKeyAction_Click(object sender, RoutedEventArgs e)
    {
        var action = new KeyActionDraft { Name = "按键动作 " + (KeyActions.Count + 1) };
        KeyActions.Add(action);
        SelectedAction = action;
        ActionList.ScrollIntoView(action);
    }
    private void AddKeyStep_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAction is null) AddKeyAction_Click(sender, e);
        if (SelectedAction is null) return;
        var step = new KeyStepDraft();
        SelectedAction.Steps.Add(step);
        SelectedKeyStep = step;
    }

    private static JsonSerializerOptions JsonOptions => new() { WriteIndented = true };
    private void SaveDraft_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存纯前端界面草稿（非正式 AutoTest 工程）",
            Filter = "前端界面草稿 (*.ui-draft.json)|*.ui-draft.json",
            FileName = "AutoTest.UI.ui-draft.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var doc = new FrontendDraftDocument
            {
                Cases = Cases.ToList(),
                CanRows = CanRows.ToList(),
                KeyActions = KeyActions.ToList()
            };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(doc, JsonOptions));
            StatusText.Text = "界面草稿已保存；不是正式 ECU 项目文件，也没有任何实体执行。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, ex.Message, "草稿保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void OpenDraft_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开纯前端界面草稿",
            Filter = "前端界面草稿 (*.ui-draft.json)|*.ui-draft.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var doc = JsonSerializer.Deserialize<FrontendDraftDocument>(File.ReadAllText(dialog.FileName));
            if (doc is null) throw new InvalidDataException("空界面草稿。");
            Cases.Clear();
            CanRows.Clear();
            KeyActions.Clear();
            foreach (var entry in doc.Cases) Cases.Add(entry);
            foreach (var row in doc.CanRows) CanRows.Add(row);
            foreach (var action in doc.KeyActions) KeyActions.Add(action);
            SelectedCase = Cases.FirstOrDefault();
            SelectedAction = KeyActions.FirstOrDefault();
            SelectedCan = CanRows.FirstOrDefault();
            StatusText.Text = "界面草稿已载入。未连接 PROJECT、TEST 或硬件服务。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, ex.Message, "界面草稿打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
