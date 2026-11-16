using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoTest.Desktop.Testing;

/// <summary>Native high-density TEST rows; inspector is hosted in the ONE Shell right rail.</summary>
public partial class TestEditorView : UserControl
{
    private readonly TestEditorViewModel _viewModel;
    private TestStepInspector? _inspector;
    private bool _inspectorInitialized;
    private bool _restoringStepRow;
    private IReadOnlyList<TestActionOption> _paletteActions = Array.Empty<TestActionOption>();
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
        _viewModel.ActionPaletteRequested += OpenActionPalette;
        _viewModel.AllowEditorAction = TryLeaveInspector;
        Loaded += (_, _) =>
        {
            // A document tab can be unloaded/reloaded whenever the user switches
            // workspaces. Re-loading must NEVER reset an in-progress Inspector.
            if (_inspectorInitialized) return;
            _inspectorInitialized = true;
            _viewModel.ResetInspector();
        };
    }

    public bool HasPendingInspectorDraft =>
        _inspector?.HasPendingEdits == true || _viewModel.HasNewStepDraft;

    /// <summary>
    /// Every navigation/command leaves this inspector only after its input is
    /// validated and applied to the undoable TEST draft. Invalid input remains
    /// visible and the previous step selection is preserved.
    /// </summary>
    public bool TryLeaveInspector()
    {
        if (_viewModel.IsApplyingInspector) return true;
        if (_viewModel.HasNewStepDraft || _inspector?.HasPendingEdits == true)
            return _inspector?.TryApplyChanges() == true;
        return true;
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
            if ((_inspector?.HasPendingEdits == true || _viewModel.HasNewStepDraft) &&
                _inspector?.TryApplyChanges() != true)
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
        editor.Cancelled += _viewModel.CancelInspectorEdit;
        editor.AppliedSuccessfully += () =>
        {
            // The VM may have rebuilt the STEP tree during Apply. Its early
            // request was correctly rejected while HasPendingEdits was true.
            // Issue a fresh request NOW that the draft was accepted, ensuring
            // the selected StepId and visible Inspector always agree.
            if (ReferenceEquals(_inspector, editor))
                _viewModel.ResetInspector();
        };
        _inspector = editor;
        InspectorHost.Content = editor;
        if (_focusNameOnNextInspector)
        {
            _focusNameOnNextInspector = false;
            editor.Loaded += (_, _) => editor.FocusName();
        }
    }

    private void OpenActionPalette(bool caseOnly)
    {
        _paletteActions = TestStepInspector.PaletteActions(caseOnly);
        PaletteHint.Text = caseOnly ? "SWITCH 的 CASE 区只能插入 CASE 分支" :
            "选择后立即加入可撤销的正式 TEST 草稿；右侧完成参数";
        PaletteSearch.Text = "";
        PaletteList.ItemsSource = _paletteActions;
        PaletteList.SelectedIndex = _paletteActions.Count > 0 ? 0 : -1;
        ActionPalette.IsOpen = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            PaletteSearch.Focus();
            PaletteSearch.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void PaletteSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (PaletteList is null) return;
        var query = PaletteSearch.Text.Trim();
        var filtered = _paletteActions.Where(x => query.Length == 0 ||
            (x.Title + " " + x.Category + " " + x.Description)
                .Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        PaletteList.ItemsSource = filtered;
        PaletteList.SelectedIndex = filtered.Length > 0 ? 0 : -1;
    }

    private void PaletteSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { ActionPalette.IsOpen = false; e.Handled = true; }
        else if (e.Key == Key.Enter && TryChoosePaletteItem()) e.Handled = true;
        else if (e.Key == Key.Down) { PaletteList.Focus(); e.Handled = true; }
    }

    private void PaletteList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && TryChoosePaletteItem()) e.Handled = true;
        else if (e.Key == Key.Escape) { ActionPalette.IsOpen = false; e.Handled = true; }
    }

    private void PaletteList_DoubleClick(object sender, MouseButtonEventArgs e) =>
        TryChoosePaletteItem();

    private void PaletteSelect_Click(object sender, RoutedEventArgs e) =>
        TryChoosePaletteItem();

    private void PaletteCancel_Click(object sender, RoutedEventArgs e) =>
        ActionPalette.IsOpen = false;

    private bool TryChoosePaletteItem()
    {
        if (PaletteList.SelectedItem is not TestActionOption chosen) return false;
        if (!_viewModel.ChooseNewStepAction(chosen.Key)) return false;
        ActionPalette.IsOpen = false;
        return true;
    }

    private void StepSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        e.Handled = true;
        if (_viewModel.NavigateSearch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) &&
            _viewModel.SelectedRow is { } row)
            StepsList.ScrollIntoView(row);
        StepSearch.Focus();
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
        if (_viewModel.SelectedItem?.Phase != row.Item.Phase ||
            _viewModel.SelectedItem?.Kind != row.Item.Kind) return;
        if (_viewModel.AddChildCommand.CanExecute())
            _viewModel.AddChildCommand.Execute();
    }

    private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringStepRow || !ReferenceEquals(e.OriginalSource, StepsList)) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(StepsList.SelectedItem, _viewModel.SelectedRow)) return;
            _restoringStepRow = true;
            try { StepsList.SelectedItem = _viewModel.SelectedRow; }
            finally { _restoringStepRow = false; }
        }), DispatcherPriority.Background);
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
        if (container?.DataContext is StepRowProjection clicked &&
            !ReferenceEquals(_viewModel.SelectedItem, clicked.Item) &&
            !TryLeaveInspector())
        {
            _dragSource = null;
            e.Handled = true; // stop WPF visually selecting a row that failed validation
            return;
        }
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
        if (container.DataContext is not StepRowProjection clicked) return;
        // If the previous Inspector contains invalid inputs, the selection is
        // deliberately rejected. The context menu must never edit a stale row.
        if (_viewModel.SelectedItem?.Step?.Id != clicked.Item.Step?.Id ||
            _viewModel.SelectedItem?.Kind != clicked.Item.Kind ||
            _viewModel.SelectedItem?.Phase != clicked.Item.Phase) return;
        var menu = new ContextMenu();
        void Item(string title, ICommand command)
        {
            menu.Items.Add(new MenuItem { Header = title, Command = command });
        }
        var target = clicked.Item;
        if (target.Kind is StepTreeItemKind.Phase or StepTreeItemKind.Slot)
            Item("＋ 在此阶段／分支末尾添加", _viewModel.AddChildCommand);
        if (target.Kind == StepTreeItemKind.Step)
        {
            Item("在当前步骤下方插入", _viewModel.AddAfterCommand);
            if (target.Children.Count > 0)
            {
                menu.Items.Add(new Separator());
                foreach (var branch in target.Children.Where(x => x.Kind == StepTreeItemKind.Slot))
                {
                    var slot = branch;
                    var item = new MenuItem
                    {
                        Header = "＋ 在 " + StepUiCatalog.SlotLabel(slot.SlotName ?? "") + " 中添加"
                    };
                    item.Click += (_, _) => _viewModel.BeginInsertInto(slot);
                    menu.Items.Add(item);
                }
            }
        }
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
