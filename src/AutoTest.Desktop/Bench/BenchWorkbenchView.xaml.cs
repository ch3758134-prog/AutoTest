using System;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Bench;

/// <summary>
/// Real hardware bench view. The shared Station handles the only WinUSB
/// connection; opening this view never silently creates another session.
/// </summary>
public partial class BenchWorkbenchView : UserControl
{
    private BenchKeyActionEditorWindow? _editorWindow;
    public event EventHandler? OpenStationRequested;
    public event EventHandler? OpenCanRequested;
    public event EventHandler? OpenDbcRequested;
    public BenchWorkbenchView(BenchWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BenchWorkbenchViewModel vm) return;
        vm.CanMonitor.Start();
        await vm.InitializeAsync();
        await vm.RefreshConnectionAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BenchWorkbenchViewModel vm) vm.CanMonitor.Stop();
    }

    private async void ReloadKeyActions_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BenchWorkbenchViewModel vm) return;
        if (vm.KeyActions.HasUnsavedChanges)
        {
            if (MessageBox.Show(
                "当前按键动作存在未保存修改。放弃这些修改并重新加载当前工程的按键动作？",
                "确认放弃按键草稿", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            await vm.KeyActions.DiscardDraftAndReloadAsync();
        }
        else await vm.KeyActions.LoadAsync();
    }

    private async void ReloadCanMessages_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BenchWorkbenchViewModel vm) return;
        if (vm.CanMessages.Dirty)
        {
            if (MessageBox.Show(
                "CAN 发送表存在未保存修改。放弃这些修改并重新加载当前工程的 CAN 报文？",
                "确认放弃 CAN 草稿", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            await vm.CanMessages.DiscardDraftAndReloadAsync();
        }
        else await vm.CanMessages.LoadAsync();
    }

    private BenchKeyActionEditorViewModel? Actions =>
        (DataContext as BenchWorkbenchViewModel)?.KeyActions;

    private void SelectCard(BenchKeyActionItem action)
    {
        if (Actions is { } vm && vm.Actions.Contains(action))
            vm.SelectedAction = action;
    }

    private async void KeyActionTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: BenchKeyActionItem action }) return;
        SelectCard(action);
        if (Actions is { IsActionRunning: false } vm)
            await vm.RunAsync(); // The real DEVICE adapter prompts before output.
    }

    private void CreateKeyAction_Click(object sender, RoutedEventArgs e)
    {
        if (Actions is not { CanEdit: true } vm) return;
        vm.NewActionCommand.Execute();
        if (vm.SelectedAction is not null)
            OpenActionEditor(vm.SelectedAction, focusName: true);
    }

    private void EditSelectedKeyAction_Click(object sender, RoutedEventArgs e)
    {
        if (Actions?.SelectedAction is { } action)
            OpenActionEditor(action);
        else
            MessageBox.Show("请先新建或选择一个按键动作按钮。", "按键动作",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // WPF ContextMenu is outside the ordinary visual tree. Retrieve the
    // originating card from PlacementTarget rather than using its stale
    // inherited DataContext (which can point to another ECU action).
    private static BenchKeyActionItem? MenuAction(object sender)
    {
        return sender is MenuItem { Parent: ContextMenu menu } &&
               menu.PlacementTarget is Button { DataContext: BenchKeyActionItem action }
            ? action : null;
    }

    private void KeyActionMenu_Run_Click(object sender, RoutedEventArgs e)
    {
        if (MenuAction(sender) is { } action)
            _ = RunCardActionAsync(action);
    }

    private async Task RunCardActionAsync(BenchKeyActionItem action)
    {
        SelectCard(action);
        if (Actions is { IsActionRunning: false } vm)
            await vm.RunAsync();
    }

    private void KeyActionMenu_Edit_Click(object sender, RoutedEventArgs e)
    {
        if (MenuAction(sender) is { } action) OpenActionEditor(action);
    }

    private void KeyActionMenu_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (MenuAction(sender) is { } action)
            OpenActionEditor(action, focusName: true);
    }

    private void KeyActionMenu_Copy_Click(object sender, RoutedEventArgs e)
    {
        if (MenuAction(sender) is not { } action || Actions is not { CanEdit: true } vm) return;
        SelectCard(action);
        vm.CopyActionCommand.Execute();
        if (vm.SelectedAction is { } copied)
            OpenActionEditor(copied, focusName: true);
    }

    private void KeyActionMenu_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MenuAction(sender) is not { } action || Actions is not { CanEdit: true } vm) return;
        if (MessageBox.Show($"删除动作“{action.Name}”？\\n这将修改当前编辑草稿；保存后才会更新工程配置。",
                "确认删除按键动作", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        SelectCard(action);
        vm.DeleteActionCommand.Execute();
    }

    private void OpenActionEditor(BenchKeyActionItem action, bool focusName = false)
    {
        if (Actions is not { } vm || !vm.Actions.Contains(action)) return;
        SelectCard(action);
        if (_editorWindow is { IsVisible: true } existing)
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            if (focusName) existing.FocusActionName();
            return;
        }

        var dialog = new BenchKeyActionEditorWindow(vm)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_editorWindow, dialog))
                _editorWindow = null;
        };
        _editorWindow = dialog;
        dialog.Show();
        if (focusName) dialog.FocusActionName();
    }

    private void ChooseDbcCanMessage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BenchWorkbenchViewModel bench) return;
        var editor = bench.CanMessages;
        if (!editor.CanEdit) return;
        editor.RefreshDbcCatalog();

        if (!editor.HasDbc)
        {
            var answer = MessageBox.Show(Window.GetWindow(this),
                "当前工程还没有可选的 DBC 报文。\\n" +
                "需要先在 DBC 工具中导入文件并加入当前工程。\\n\\n现在打开 DBC 工具吗？",
                "从工程 DBC 选择发送报文",
                MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Yes)
                OpenDbcRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var picker = new BenchDbcMessagePickerWindow(editor.DbcMessages.ToArray(), editor.DbcStatus)
        {
            Owner = Window.GetWindow(this)
        };
        if (picker.ShowDialog() == true && picker.PickedMessage is { } message)
            editor.TryAddFromDbc(message);
    }

    private void OpenDbc_Click(object sender, RoutedEventArgs e) =>
        OpenDbcRequested?.Invoke(this, EventArgs.Empty);

    private void OpenCan_Click(object sender, RoutedEventArgs e) =>
        OpenCanRequested?.Invoke(this, EventArgs.Empty);

    private void OpenStation_Click(object sender, RoutedEventArgs e) =>
        OpenStationRequested?.Invoke(this, EventArgs.Empty);
}
