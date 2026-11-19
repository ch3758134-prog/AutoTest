using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace AutoTest.Frontend;

public partial class MainWindow : Window
{
    private readonly UiWorkbenchState _state;
    private readonly Dictionary<string, ToolWindow> _toolWindows = new(StringComparer.Ordinal);
    public MainWindow()
    {
        InitializeComponent();
        _state = UiWorkbenchState.Load();
        DataContext = _state;
        RunWorkspaceView.RequestEdit += (_, _) => WorkTabs.SelectedIndex = 1;
        EditorWorkspaceView.RequestRun += (_, _) => WorkTabs.SelectedIndex = 0;
        Closing += (_, _) => _state.SaveNow();
    }

    private void ShowTools_Click(object sender, RoutedEventArgs e)
    {
        ToolsMenu.PlacementTarget = ToolsButton;
        ToolsMenu.IsOpen = true;
    }

    private void OpenTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string key }) OpenTool(key);
    }
    private void OpenRecords_Click(object sender, RoutedEventArgs e) => OpenTool("records");
    private void OpenProduction_Click(object sender, RoutedEventArgs e) => OpenTool("production");

    private void OpenTool(string key)
    {
        if (_toolWindows.TryGetValue(key, out var existing) && existing.IsVisible)
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var tool = new ToolWindow(key, _state)
        {
            Owner = this,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        _toolWindows[key] = tool;
        tool.Closed += (_, _) => _toolWindows.Remove(key);
        tool.Show();
    }

    private void SaveDraft_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _state.SaveNow();
        e.Handled = true;
    }

    private void AddCase_Click(object sender, RoutedEventArgs e)
    {
        var entry = new CaseDraft { Name = $"新测试用例 {(_state.Cases.Count + 1)}" };
        _state.Cases.Add(entry);
        _state.SelectedCase = entry;
        WorkTabs.SelectedIndex = 1;
    }

    private void FileTree_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is CaseDraft c) _state.SelectedCase = c;
        else if (e.NewValue is DbcMessageDraft) { /* single click only selects resource */ }
    }

    private void FileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        switch (FileTree.SelectedItem)
        {
            case CaseDraft test:
                _state.SelectedCase = test;
                WorkTabs.SelectedIndex = 1;
                break;
            case DbcMessageDraft:
                OpenTool("dbc");
                break;
            case WaveSignalDraft:
                OpenTool("wave");
                break;
            case InputStateDraft:
                OpenTool("inputs");
                break;
        }
    }

    private void FileSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (FileSearchBox is null || FileTree is null) return;
        var term = FileSearchBox.Text.Trim();
        foreach (var category in FileTree.Items)
        {
            if (category is not TreeViewItem tree) continue;
            ApplyFilter(tree, term);
        }
    }
    private static bool ApplyFilter(TreeViewItem node, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            node.Visibility = Visibility.Visible;
            foreach (var item in node.Items)
                if (item is TreeViewItem child) ApplyFilter(child, term);
            return true;
        }
        var self = (node.Header?.ToString() ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);
        var desc = false;
        foreach (var entry in node.Items)
        {
            if (entry is TreeViewItem child) desc |= ApplyFilter(child, term);
            else if (entry is CaseDraft c) desc |= c.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            else if (entry is DbcMessageDraft d) desc |= d.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            else if (entry is WaveSignalDraft w) desc |= w.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            else if (entry is InputStateDraft x) desc |= x.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
        }
        node.Visibility = self || desc ? Visibility.Visible : Visibility.Collapsed;
        if (desc) node.IsExpanded = true;
        return self || desc;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "导入前端界面草稿（非正式工程）",
            Filter = "AutoTest 界面草稿 (*.ui-draft.json)|*.ui-draft.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        if (MessageBox.Show(this,
                "此操作会替换当前 UI 设计草稿，不会打开正式 ECU 工程。是否继续？",
                "导入界面草稿", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        try { _state.Import(dlg.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                        or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "草稿读取失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出前端界面草稿（非正式工程）",
            Filter = "AutoTest 界面草稿 (*.ui-draft.json)|*.ui-draft.json",
            FileName = "AutoTest-UI.ui-draft.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try { _state.Export(dlg.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "草稿导出失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
