using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutoTest.Desktop.Shell;

namespace AutoTest.Desktop.Testing;

/// <summary>Native high-density TEST rows with explicit transactional property overlay.</summary>
public partial class TestEditorView : UserControl
{
    private readonly TestEditorViewModel _viewModel;
    private TestStepInspector? _inspector;
    private bool _inspectorInitialized;
    private bool _restoringStepRow;
    private IReadOnlyList<TestActionOption> _paletteActions = Array.Empty<TestActionOption>();
    // These names MUST match TestStepInspector's authored action groups.
    // A previous UI-only taxonomy made all non-favorite categories empty.
    private static readonly string[] PaletteGroups =
        ["常用步骤", "外部操作", "读取与观察", "分析与判断", "流程控制"];
    private bool _focusNameOnNextInspector;
    private bool _committingQuickValue;
    private StepRowProjection? _activeQuickRow;
    private Point _dragStart;
    private StepRowProjection? _dragSource;
    private readonly ListCollectionView? _canMonitorRows;
    private readonly DispatcherTimer _canFollowTimer =
        new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _canMonitorVisible = true;
    public bool IsPropertyEditorOpen => StepPropertyOverlay.Visibility == Visibility.Visible;

    public TestEditorView(TestEditorViewModel viewModel,
        MainWindowViewModel? liveCanSource = null)
    {
        InitializeComponent();
        PaletteCategories.ItemsSource = PaletteGroups;
        if (liveCanSource is not null)
        {
            // The shell already subscribes to the genuine USB CAN observation
            // stream. This is a separate filtered view, not a second USB
            // reader, simulator or shared filter of the normal CAN tool.
            _canMonitorRows = new ListCollectionView(liveCanSource.CanRecords);
            CanMonitorTable.ItemsSource = _canMonitorRows;
            CanMonitorFeedStatus.DataContext = liveCanSource;
            CanMonitorFeedStatus.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(MainWindowViewModel.CanMonitorDisplayStatus)));
        }
        else
        {
            _canMonitorVisible = false;
            SetCanMonitorVisibility(false);
            ToggleCanMonitorButton.Visibility = Visibility.Collapsed;
        }
        _canFollowTimer.Tick += (_, _) => FollowLatestCanObservation();
        Loaded += (_, _) => _canFollowTimer.Start();
        Unloaded += (_, _) => _canFollowTimer.Stop();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.InspectorRequested += ShowInspector;
        _viewModel.InspectorLoading += ShowInspectorLoading;
        _viewModel.ActionPaletteRequested += OpenActionPalette;
        _viewModel.AllowEditorAction = TryLeaveInspector;
        _viewModel.CanAutoPersist = () =>
            !IsPropertyEditorOpen && _activeQuickRow?.IsInlineEditing != true;
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
        IsPropertyEditorOpen || _inspector?.HasPendingEdits == true || _viewModel.HasNewStepDraft;

    /// <summary>
    /// Every navigation/command leaves this inspector only after its input is
    /// validated and applied to the undoable TEST draft. Invalid input remains
    /// visible and the previous step selection is preserved.
    /// </summary>
    public bool TryLeaveInspector()
    {
        if (_viewModel.IsApplyingInspector) return true;
        // An invalid inline value must block changing rows, tabs or searching;
        // the edit remains visibly in place instead of silently disappearing.
        if (!_committingQuickValue && _activeQuickRow is { IsInlineEditing: true } row &&
            !CommitInlineQuickValue(row))
            return false;
        if (IsPropertyEditorOpen)
            return TryDismissPropertyEditor();
        if (_viewModel.HasNewStepDraft) return false;
        return true;
    }

    /// <summary>
    /// Explicit save-on-close first validates/applies pending Inspector edits, then
    /// awaits real TEST document persistence. On failure both drafts stay in place.
    /// </summary>
    public async Task<bool> SaveForCloseAsync()
    {
        try
        {
            if (_activeQuickRow is { IsInlineEditing: true } row &&
                !CommitInlineQuickValue(row))
                return false;
            // Explicit save/close may finish a valid property draft. The
            // shell's idle autosave still skips documents with open overlays.
            if (!TryDismissPropertyEditor()) return false;
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

    private void ShowInspectorLoading(string selectedStepName)
    {
        _inspector = null;
        StepPropertyCard.MaxWidth = 680;
        StepPropertyCard.MaxHeight = 350;
        StepPropertyHost.Content = new TextBlock
        {
            Text = selectedStepName + "\n\n正在载入工程资源…",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13, Margin = new Thickness(18)
        };
        StepPropertyOverlay.Visibility = Visibility.Visible;
        StepPropertyOverlay.Focus();
    }

    private void ShowInspector(StepInspectorContext context)
    {
        if (context.Original is null) return;
        if (_inspector?.HasPendingEdits == true && !_viewModel.IsApplyingInspector) return;
        var editor = new TestStepInspector(context.Choices, context.ExistingMappings,
            context.Original, context.InitialActionKey, context.CaseOnly,
            context.PublicFlows, context.IfVariables);
        editor.ApplyRequested = _viewModel.ApplyInspector;
        editor.ApplyFailureReason = () => _viewModel.StatusMessage;
        editor.Cancelled += () =>
        {
            if (!ReferenceEquals(_inspector, editor)) return;
            ClosePropertyOverlay();
            _viewModel.CancelInspectorEdit();
        };
        editor.AppliedSuccessfully += () =>
        {
            if (!ReferenceEquals(_inspector, editor)) return;
            // Canonical document is already updated. Close queues one debounced
            // persist; a direct Run or Ctrl+S awaits the same save contract.
            // Do not race a second async save from this synchronous event.
            ClosePropertyOverlay();
        };
        _inspector = editor;
        StepPropertyCard.MaxWidth = editor.PreferredEditorWidth;
        StepPropertyCard.MaxHeight = editor.PreferredEditorHeight;
        StepPropertyHost.Content = editor;
        StepPropertyOverlay.Visibility = Visibility.Visible;
        if (_focusNameOnNextInspector)
        {
            _focusNameOnNextInspector = false;
            Dispatcher.BeginInvoke(new Action(editor.FocusName), DispatcherPriority.Input);
        }
        else StepPropertyOverlay.Focus();
    }

    private void ClosePropertyOverlay()
    {
        StepPropertyOverlay.Visibility = Visibility.Collapsed;
        StepPropertyHost.Content = null;
        _inspector = null;
        _focusNameOnNextInspector = false;
        _viewModel.QueueAutoSave();
        StepsList.Focus();
    }

    // Property editing is a deliberate transaction. Clicking the backdrop
    // must never implicitly apply, cancel or dismiss edits.
    private void StepPropertyOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, StepPropertyOverlay))
        {
            // Backdrop is not a Save/Cancel button. Keep the transaction open
            // and avoid losing the current field focus or an invalid draft.
            e.Handled = true;
        }
    }

    private void FinishPropertyOverlay_Click(object sender, RoutedEventArgs e) =>
        TryDismissPropertyEditor();

    public bool TryDismissPropertyEditor()
    {
        if (!IsPropertyEditorOpen) return !_viewModel.HasNewStepDraft;
        if (_viewModel.IsApplyingInspector) return false;
        if (_inspector is not { } editor)
        {
            // No editor exists yet, so there is nothing to apply. Keep a
            // provisional new step open until its editor is ready or cancelled.
            if (_viewModel.HasNewStepDraft) return false;
            ClosePropertyOverlay();
            _viewModel.CancelInspectorEdit(); // invalidate the outstanding request
            return true;
        }
        if (!editor.HasPendingEdits && !_viewModel.HasNewStepDraft)
        {
            ClosePropertyOverlay();
            return true;
        }
        // Reuse exactly the same validation and transaction as Apply.
        // On failure leave the editor and the original step intact.
        return editor.TryApplyChanges();
    }

    private void CancelPropertyOverlay_Click(object sender, RoutedEventArgs e)
    {
        ClosePropertyOverlay();
        _viewModel.CancelInspectorEdit();
    }

    private void EditStepRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StepRowProjection row }) return;
        _viewModel.SelectItem(row.Item);
        if (_viewModel.SelectedItem?.Step?.Id != row.Item.Step?.Id) return;
        if (_viewModel.EditParametersCommand.CanExecute())
            _viewModel.EditParametersCommand.Execute();
        e.Handled = true;
    }

    private void OpenActionPalette(bool caseOnly)
    {
        var shell = Window.GetWindow(this);
        var width = shell?.ActualWidth > 0 ? shell.ActualWidth : SystemParameters.WorkArea.Width;
        var height = shell?.ActualHeight > 0 ? shell.ActualHeight : SystemParameters.WorkArea.Height;
        // Respect actual available editor DIPs and Windows work area.
        // The chooser must stay wholly reachable on 1366x768 at 150% DPI.
        PaletteSurface.Width = Math.Clamp(
            Math.Min(width - 50, Math.Max(400, ActualWidth - 18)), 400, 690);
        PaletteSurface.MaxHeight = Math.Clamp(
            Math.Min(height - 85, SystemParameters.WorkArea.Height - 65), 280, 530);
        PaletteList.MaxHeight = Math.Max(165, PaletteSurface.MaxHeight - 175);
        _paletteActions = TestStepInspector.PaletteActions(caseOnly);
        PaletteCategories.SelectedIndex = 0;
        var target = _viewModel.SelectedItem;
        PaletteHint.Text = caseOnly
            ? "当前插入位置：SWITCH / CASE 分支"
            : target is null ? "先选择目标阶段或流程分支"
            : "当前插入位置：" + StepUiCatalog.PhaseLabel(target.Phase) +
              " / " + target.Label;
        PaletteSearch.Text = "";
        RefreshPaletteItems("");
        PaletteList.SelectedItem = _paletteActions.FirstOrDefault();
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
        RefreshPaletteItems(query);
    }

    private void PaletteCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PaletteCategories?.SelectedItem is not string) return;
        if (PaletteSearch is not null && PaletteSearch.Text.Length > 0)
            PaletteSearch.Clear();
        else if (PaletteList is not null)
            RefreshPaletteItems("");
    }

    private void RefreshPaletteItems(string query)
    {
        var category = PaletteCategories?.SelectedItem as string ?? "常用步骤";
        string[] favorites =
            ["can", "stop-can", "key", "ign", "read", "assert-signal",
             "wait", "if", "loop", "uds", "wave-start", "wave-analyze"];
        var filtered = _paletteActions
            .Where(x => _paletteActions.Count == 1 && x.Key == "case" ||
                query.Length > 0
                ? (x.Title + " " + x.Group + " " + x.Category + " " + x.Description)
                    .Contains(query, StringComparison.CurrentCultureIgnoreCase)
                : category == "常用步骤" ? favorites.Contains(x.Key)
                : category == x.Group)
            .OrderBy(x => query.Length == 0 && category == "常用步骤"
                ? Array.IndexOf(favorites, x.Key) : 0)
            .ThenBy(x => x.Title, StringComparer.CurrentCulture)
            .ToArray();
        PaletteList.ItemsSource = filtered;
        PaletteList.SelectedItem = filtered.FirstOrDefault();
    }

    // HTML palette selects a type in one click; the optional confirm button
    // remains for keyboard users. Keyboard arrow selection does not insert.
    private void PaletteList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var origin = e.OriginalSource as DependencyObject;
        var item = origin is null ? null :
            ItemsControl.ContainerFromElement(PaletteList, origin) as ListBoxItem;
        if (item?.DataContext is not TestActionOption option) return;
        PaletteList.SelectedItem = option;
        if (TryChoosePaletteItem()) e.Handled = true;
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

    private void PaletteCancel_Click(object sender, RoutedEventArgs e) =>
        ActionPalette.IsOpen = false;

    private bool TryChoosePaletteItem()
    {
        if (PaletteList.SelectedItem is not TestActionOption chosen) return false;
        // The Inspector can be published synchronously when resource choices
        // are cached, so request focus BEFORE committing the selected type.
        _focusNameOnNextInspector = true;
        if (!_viewModel.ChooseNewStepAction(chosen.Key))
        {
            _focusNameOnNextInspector = false;
            return false;
        }
        ActionPalette.IsOpen = false;
        Dispatcher.BeginInvoke(new Action(() => _inspector?.FocusName()),
            DispatcherPriority.Input);
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

    private void InsertAfterRow_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: StepRowProjection { IsStep: true } row }) return;
        var expected = StepSelectionKey.Of(row.Item);
        _viewModel.SelectItem(row.Item);
        if (StepSelectionKey.Of(_viewModel.SelectedItem) != expected) return;
        if (_viewModel.AddAfterCommand.CanExecute())
            _viewModel.AddAfterCommand.Execute();
    }

    private void CanMonitorFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (_canMonitorRows is null || sender is not RadioButton { Tag: string filter })
            return;
        _canMonitorRows.Filter = value => value is MainWindowViewModel.CanMonitorRow row &&
            (filter == "All" ||
             filter == "Ecu" && row.Port.Contains("ECU", StringComparison.OrdinalIgnoreCase) ||
             filter == "Key" && row.Port.Contains("KEY", StringComparison.OrdinalIgnoreCase));
        FollowLatestCanObservation();
    }

    private void ToggleCanMonitor_Click(object sender, RoutedEventArgs e) =>
        SetCanMonitorVisibility(!_canMonitorVisible);

    private void SetCanMonitorVisibility(bool visible)
    {
        _canMonitorVisible = visible;
        CanMonitorPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MonitorDivider.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MonitorDividerColumn.Width = new GridLength(visible ? 5 : 0);
        MonitorColumn.Width = new GridLength(visible ? 420 : 0);
        ToggleCanMonitorButton.Content = visible ? "隐藏 CAN 监视" : "显示 CAN 监视";
    }

    private void FollowLatestCanObservation()
    {
        if (!_canMonitorVisible || CanAutoFollow?.IsChecked != true ||
            !CanMonitorTable.IsVisible || _canMonitorRows is null ||
            _canMonitorRows.IsEmpty) return;
        var tail = _canMonitorRows.Cast<MainWindowViewModel.CanMonitorRow>()
            .LastOrDefault();
        if (tail is not null) CanMonitorTable.ScrollIntoView(tail);
    }

    private void AddAtRow_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: StepRowProjection row } ||
            !(row.IsPhase || row.IsSlot)) return;
        var expected = StepSelectionKey.Of(row.Item);
        _viewModel.SelectItem(row.Item);
        if (StepSelectionKey.Of(_viewModel.SelectedItem) != expected) return;
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

    private void QuickSummary_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not TextBlock text ||
            text.DataContext is not StepRowProjection row || !row.IsQuickEditable) return;
        _viewModel.SelectItem(row.Item);
        if (_viewModel.SelectedItem?.Step?.Id != row.Item.Step?.Id) return;
        row.IsInlineEditing = true;
        _activeQuickRow = row;
        e.Handled = true;
        if (VisualTreeHelper.GetParent(text) is Grid grid)
        {
            var box = grid.Children.OfType<TextBox>().FirstOrDefault();
            if (box is not null)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    box.Focus();
                    box.SelectAll();
                }), DispatcherPriority.Input);
        }
    }

    private bool CommitInlineQuickValue(StepRowProjection row)
    {
        if (_committingQuickValue || !row.IsInlineEditing) return false;
        _committingQuickValue = true;
        try
        {
            if (!_viewModel.TryCommitQuickValue(row)) return false;
            row.IsInlineEditing = false;
            if (ReferenceEquals(_activeQuickRow, row)) _activeQuickRow = null;
            return true;
        }
        finally { _committingQuickValue = false; }
    }

    private void QuickField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: StepRowProjection row } &&
            row.IsInlineEditing && !_committingQuickValue)
            CommitInlineQuickValue(row);
    }

    private void QuickField_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not StepRowProjection row) return;
        if (e.Key is Key.Enter or Key.Return)
        {
            if (CommitInlineQuickValue(row)) Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.CancelQuickEdit();
            if (ReferenceEquals(_activeQuickRow, row)) _activeQuickRow = null;
            Keyboard.ClearFocus();
            e.Handled = true;
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
        if (StepSelectionKey.Of(_viewModel.SelectedItem) !=
            StepSelectionKey.Of(clicked.Item)) return;
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
            Item("▶ 运行此步骤（真实 ECU 诊断）", _viewModel.RunSelectedStepCommand);
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
        Item("确认当前用例", _viewModel.PrepareRunApprovalCommand);
        Item("▶ 运行当前用例", _viewModel.RunCommand);
        menu.Items.Add(new Separator());
        Item("编辑步骤属性", _viewModel.EditParametersCommand);
        Item("复制步骤及子分支", _viewModel.DuplicateCommand);
        if (target.Kind == StepTreeItemKind.Step)
            Item("复制为公共流程…", _viewModel.CopyAsPublicFlowCommand);
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
        if (e.ChangedButton != MouseButton.Left || IsPropertyEditorOpen) return;
        var origin = e.OriginalSource as DependencyObject;
        // Inline inputs and dedicated action buttons own their gestures.
        if (origin is null) return;
        for (var ancestor = origin; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is Button or TextBox) return;
        var container = ItemsControl.ContainerFromElement(StepsList, origin) as ListBoxItem;
        if (container?.DataContext is not StepRowProjection { IsStep: true } row) return;
        _viewModel.SelectItem(row.Item);
        if (StepSelectionKey.Of(_viewModel.SelectedItem) != StepSelectionKey.Of(row.Item)) return;
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
        if (IsPropertyEditorOpen)
        {
            if (e.Key == Key.Escape)
            {
                CancelPropertyOverlay_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Do not let step-list Delete, Ctrl+Z, F2 etc. affect the underlying
            // TEST document while a property transaction is active.
            return;
        }
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await SaveForCloseAsync(); // Ctrl+S saves only applied properties.
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
