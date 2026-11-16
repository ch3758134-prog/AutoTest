using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTest.Desktop.Testing;

/// <summary>Native high-density TEST rows; inspector is hosted in the ONE Shell right rail.</summary>
public partial class TestEditorView : UserControl
{
    private readonly TestEditorViewModel _viewModel;
    private TestStepInspector? _inspector;
    private bool _focusNameOnNextInspector;
    private Point _dragStart;
    private StepRowProjection? _dragSource;
    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };

    public TestEditorView(TestEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.InspectorRequested += ShowInspector;
        _viewModel.AllowEditorAction = TryLeaveInspector;
        Loaded += (_, _) => _viewModel.ResetInspector();
    }

    public bool HasPendingInspectorDraft => _inspector?.HasPendingEdits == true;

    /// <summary>
    /// Every navigation/command leaves this inspector only after its input is
    /// validated and applied to the undoable TEST draft. Invalid input remains
    /// visible and the previous step selection is preserved.
    /// </summary>
    public bool TryLeaveInspector()
    {
        if (_viewModel.IsApplyingInspector || _inspector?.HasPendingEdits != true) return true;
        return _inspector.TryApplyChanges();
    }

    /// <summary>
    /// Explicit save-on-close first validates/applies pending Inspector edits, then
    /// awaits real TEST document persistence. On failure both drafts stay in place.
    /// </summary>
    public async Task<bool> SaveForCloseAsync()
    {
        try
        {
            if (_inspector?.HasPendingEdits == true && !_inspector.TryApplyChanges())
                return false;
            return await _viewModel.SaveForCloseAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // An unexpected persistence or editor exception must never crash a
            // WPF async-void Save button/keyboard handler or silently exit.
            System.Diagnostics.Trace.TraceError("TEST SaveForClose failed: " + error);
            MessageBox.Show(Window.GetWindow(this), "步骤或测试程序保存失败，草稿仍在本窗口中：\n" +
                error.Message, "保存未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void ShowInspector(StepInspectorContext context)
    {
        if (_inspector?.HasPendingEdits == true && !_viewModel.IsApplyingInspector)
            return; // stale async completion may not overwrite an active draft
        var editor = new TestStepInspector(context.Choices, context.ExistingMappings,
            context.Original, context.InitialActionKey, context.CaseOnly);
        editor.ApplyRequested = _viewModel.ApplyInspector;
        editor.Cancelled += _viewModel.ResetInspector;
        _inspector = editor;
        InspectorHost.Content = editor;
        if (_focusNameOnNextInspector)
        {
            _focusNameOnNextInspector = false;
            editor.Loaded += (_, _) => editor.FocusName();
        }
    }

    private async void SaveEditor_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await SaveForCloseAsync();
    }

    private void AddAtRow_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: StepRowProjection row } || !row.IsPhase) return;
        _viewModel.SelectItem(row.Item);
        if (_viewModel.AddChildCommand.CanExecute())
            _viewModel.AddChildCommand.Execute();
    }

    private void StepExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StepRowProjection row } &&
            row.ExpandMark.Length > 0)
        {
            _viewModel.ToggleExpansion(row);
            e.Handled = true;
        }
    }

    private void QuickField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: StepRowProjection row })
            _viewModel.CommitQuickValue(row);
    }

    private void QuickField_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key is Key.Enter or Key.Return)
        {
            Keyboard.ClearFocus(); e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (box.DataContext is StepRowProjection row)
            {
                row.QuickValue = row.OriginalQuickValue;
                box.Text = row.OriginalQuickValue;
            }
            Keyboard.ClearFocus(); e.Handled = true;
        }
    }

    private static bool IsTextEditorHit(DependencyObject? start)
    {
        var parent = start;
        while (parent is not null && parent is not ListBoxItem)
        {
            if (parent is TextBoxBase) return true;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return false;
    }

    private void StepsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(StepsList);
        var origin = e.OriginalSource as DependencyObject;
        if (IsTextEditorHit(origin)) { _dragSource = null; return; }
        var container = origin is null ? null :
            ItemsControl.ContainerFromElement(StepsList, origin) as ListBoxItem;
        _dragSource = (container?.DataContext as StepRowProjection) is { IsStep: true } row
            ? row : null;
    }

    private void StepsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSource is null || e.LeftButton != MouseButtonState.Pressed) return;
        var now = e.GetPosition(StepsList);
        if (Math.Abs(now.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var source = _dragSource;
        _dragSource = null;
        DragDrop.DoDragDrop(StepsList, new DataObject("AutoTest.StepRow", source),
            DragDropEffects.Move);
    }

    private void StepsList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("AutoTest.StepRow") ?
            DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void StepsList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("AutoTest.StepRow") is not StepRowProjection moving) return;
        var origin = e.OriginalSource as DependencyObject;
        var container = origin is null ? null :
            ItemsControl.ContainerFromElement(StepsList, origin) as ListBoxItem;
        if (container?.DataContext is StepRowProjection target)
        {
            _viewModel.MoveStepAfter(moving, target);
            e.Handled = true;
        }
    }

    private void StepsList_RightClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var container = source is null ? null :
            ItemsControl.ContainerFromElement(StepsList, source) as ListBoxItem;
        if (container is null) return;
        StepsList.SelectedItem = container.DataContext;
        var menu = new ContextMenu();
        void Item(string title, ICommand command)
        {
            menu.Items.Add(new MenuItem { Header = title, Command = command });
        }
        Item("＋ 添加步骤 / 子步骤", _viewModel.AddChildCommand);
        Item("在该步骤下方插入", _viewModel.AddAfterCommand);
        menu.Items.Add(new Separator());
        Item("编辑步骤属性", _viewModel.EditParametersCommand);
        Item("复制步骤及子分支", _viewModel.DuplicateCommand);
        Item("复制到当前用例剪贴板（Ctrl+C）", _viewModel.CopyStepCommand);
        Item("粘贴到当前位置（Ctrl+V）", _viewModel.PasteStepCommand);
        Item("上移", _viewModel.MoveUpCommand);
        Item("下移", _viewModel.MoveDownCommand);
        Item("删除", _viewModel.DeleteCommand);
        menu.Items.Add(new Separator());
        Item("撤销", _viewModel.UndoCommand);
        Item("重做", _viewModel.RedoCommand);
        Item("保存", _viewModel.SaveCommand);
        container.ContextMenu = menu;
    }

    private void StepsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedRow?.IsStep != true || e.ChangedButton != MouseButton.Left) return;
        if (_viewModel.EditParametersCommand.CanExecute())
        {
            _focusNameOnNextInspector = true;
            _viewModel.EditParametersCommand.Execute();
            e.Handled = true;
        }
    }

    private async void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await SaveForCloseAsync(); // Ctrl+S applies the right-side Inspector first.
            return;
        }
        var focused = Keyboard.FocusedElement;
        bool typing = focused is TextBoxBase or PasswordBox or ComboBox;
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.F && !typing)
        {
            StepSearch.Focus(); StepSearch.SelectAll(); e.Handled = true; return;
        }
        if (typing) return; // text editing owns Delete and text Undo/Redo
        ICommand? command = null;
        if (ctrl && e.Key == Key.Z) command = _viewModel.UndoCommand;
        else if (ctrl && e.Key == Key.Y) command = _viewModel.RedoCommand;
        else if (ctrl && e.Key == Key.C) command = _viewModel.CopyStepCommand;
        else if (ctrl && e.Key == Key.V) command = _viewModel.PasteStepCommand;
        else if (ctrl && e.Key == Key.Space) command = _viewModel.AddChildCommand;
        else if (e.Key == Key.Delete) command = _viewModel.DeleteCommand;
        else if (e.Key == Key.F9)
        {
            _viewModel.ToggleBreakpoint(); e.Handled = true; return;
        }
        else if (e.Key == Key.F2)
        {
            if (_viewModel.EditParametersCommand.CanExecute())
            {
                _focusNameOnNextInspector = true;
                _viewModel.EditParametersCommand.Execute();
                e.Handled = true;
            }
            return;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Up)
            command = _viewModel.MoveUpCommand;
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Down)
            command = _viewModel.MoveDownCommand;
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null); e.Handled = true;
        }
    }
}
