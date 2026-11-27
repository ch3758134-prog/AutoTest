using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// Single-body authoring view for the G3 public-flow asset. It deliberately
/// reuses TEST StepNode, StepEditTransaction and TestStepInspector; it is NOT a
/// second step engine and has no independent hardware run button.
/// </summary>
public sealed class PublicFlowEditorView : UserControl
{
    private sealed record Selection(StepId? Parent, string? Slot,
        int Index, StepNode? Node, bool IsSlot = false);

    private readonly ProjectWorkspaceService _workspace;
    private PublicFlowEditorSession _session;
    private StepEditTransaction _transaction;
    private readonly TreeView _tree = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _flowHeading = new();
    private TestStepInspector? _inspector;
    private bool _refreshing, _dirty, _busy;
    private StepId? _newStepId;
    private int _requestVersion;
    private readonly Grid _propertyOverlay = new() { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(64, 241, 245, 249)) };
    private readonly Border _propertyCard = new() { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(221, 230, 237)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Margin = new Thickness(18) };
    public bool IsPropertyEditorOpen => _propertyOverlay.Visibility == Visibility.Visible;
    private StepId? _cancelledStepId;
    private string? _cancelledActionKey;
    private string? _newActionKey;

    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };

    public bool IsDirty => _dirty || IsPropertyEditorOpen || _newStepId is not null ||
                           _inspector?.HasPendingEdits == true;
    public bool IsBusy => _busy;

    public PublicFlowEditorView(ProjectWorkspaceService workspace,
        PublicFlowEditorSession session)
    {
        _workspace = workspace;
        _session = session;
        var draft = TestDocument.Create(session.Document.Name);
        _transaction = new StepEditTransaction(draft with
        {
            Phases = draft.Phases.SetItem(TestPhase.Test, session.Document.Body),
            Mappings = session.Document.Mappings
        });

        Background = Brushes.White;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 12;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 10, 12, 10) };
        _flowHeading.Text = "公共流程 · " + session.AssetName;
        _flowHeading.FontSize = 15;
        _flowHeading.FontWeight = FontWeights.SemiBold;
        _flowHeading.VerticalAlignment = VerticalAlignment.Center;
        _flowHeading.Margin = new Thickness(0, 0, 16, 0);
        toolbar.Children.Add(_flowHeading);
        toolbar.Children.Add(MakeButton("重命名", async (_, _) => await RenameFlowAsync()));
        toolbar.Children.Add(MakeButton("查看引用", async (_, _) => await ShowUsagesAsync()));
        toolbar.Children.Add(MakeButton("＋ 添加步骤", (s, _) => ShowPalette(s as Button)));
        toolbar.Children.Add(MakeButton("编辑步骤", (_, _) => EditSelected()));
        toolbar.Children.Add(MakeButton("删除", (_, _) => DeleteSelected()));
        toolbar.Children.Add(MakeButton("上移", (_, _) => MoveSelected(-1)));
        toolbar.Children.Add(MakeButton("下移", (_, _) => MoveSelected(1)));
        toolbar.Children.Add(MakeButton("撤销", (_, _) => Undo()));
        toolbar.Children.Add(MakeButton("重做", (_, _) => Redo()));
        toolbar.Children.Add(MakeButton("保存", async (_, _) => await SaveForCloseAsync()));
        layout.Children.Add(toolbar);

        Grid.SetRow(_tree, 1);
        _tree.Margin = new Thickness(12, 2, 12, 4);
        _tree.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 228, 239));
        _tree.SelectedItemChanged += (_, args) => SelectedChanged(args.NewValue as TreeViewItem);
        _tree.MouseDoubleClick += (_, args) => { if (CurrentSelection().Node is not null) { EditSelected(); args.Handled = true; } };
        _tree.PreviewKeyDown += (_, args) => { if (args.Key == Key.F2) { EditSelected(); args.Handled = true; } };
        layout.Children.Add(_tree);

        Grid.SetRow(_status, 2);
        _status.Text = "双击编辑步骤 · 公共流程使用与 TEST 相同的步骤定义和执行器。";
        _status.Padding = new Thickness(14, 6, 14, 6);
        _status.Foreground = new SolidColorBrush(Color.FromRgb(86, 111, 143));
        layout.Children.Add(_status);
        var cardLayout = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(14, 8, 14, 4) };
        var close = MakeButton("完成", (_, _) => TryDismissPropertyEditor());
        var cancel = MakeButton("取消", (_, _) => CancelPropertyEditor());
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        DockPanel.SetDock(cancel, Dock.Right);
        header.Children.Add(cancel);
        header.Children.Add(new TextBlock { Text = "步骤属性", FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(header, Dock.Top);
        cardLayout.Children.Add(header);
        cardLayout.Children.Add(InspectorHost);
        _propertyCard.Child = cardLayout;
        _propertyOverlay.Children.Add(_propertyCard);
        _propertyOverlay.MouseLeftButtonDown += (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, _propertyOverlay)) return;
            // Clicking the surrounding dimmed surface never applies or
            // discards step edits. Only explicit 完成/取消 can leave the editor.
            args.Handled = true;
        };
        Grid.SetRowSpan(_propertyOverlay, 3);
        Panel.SetZIndex(_propertyOverlay, 100);
        layout.Children.Add(_propertyOverlay);
        PreviewKeyDown += (_, args) =>
        {
            if (IsPropertyEditorOpen && args.Key == Key.Escape)
            { CancelPropertyEditor(); args.Handled = true; }
        };
        Content = layout;
        RefreshTree(null);
    }

    private static Button MakeButton(string title, RoutedEventHandler handler)
    {
        var button = new Button { Content = title, MinHeight = 33,
            Margin = new Thickness(0, 0, 7, 0), Padding = new Thickness(11, 5, 11, 5),
            Background = Brushes.White, BorderBrush = new SolidColorBrush(
                Color.FromRgb(217, 229, 239)), Foreground = new SolidColorBrush(
                Color.FromRgb(39, 67, 90)) };
        button.Click += handler;
        return button;
    }

    private async Task RenameFlowAsync()
    {
        if (_busy || !TryFinishInspector() || !await SaveForCloseAsync()) return;
        var nameDialog = new ResourceNameDialog("重命名公共流程", _session.AssetName);
        if (Window.GetWindow(this) is { } owner) nameDialog.Owner = owner;
        if (nameDialog.ShowDialog() != true) return;
        var name = nameDialog.ResourceName;
        if (name == _session.AssetName) return;
        _busy = true;
        try
        {
            // Rename only the user-visible resource, not its body, StepIds or
            // content fingerprint. Existing TEST calls remain valid.
            await _workspace.RenameAssetAsync(_session.AssetId, name);
            _session = await _workspace.OpenPublicFlowEditorAsync(_session.AssetId);
            _flowHeading.Text = "公共流程 · " + _session.AssetName;
            _status.Text = "已重命名公共流程；已有用例调用保持不变。";
        }
        catch (Exception error)
        {
            _status.Text = "重命名失败：" + error.Message;
        }
        finally { _busy = false; }
    }

    private async Task ShowUsagesAsync()
    {
        if (_busy || !TryFinishInspector()) return;
        _busy = true;
        try
        {
            var context = _workspace.Current;
            if (context is null) return;
            var usages = new List<string>();
            foreach (var asset in context.Definition.Assets)
            {
                bool used = false;
                if (asset.Kind == ProjectWorkspaceService.TestProgramKind)
                {
                    var opened = await _workspace.OpenTestEditorAsync(asset.Id);
                    used = opened.Document.Walk().Any(CallsThisFlow);
                }
                else if (asset.Kind == ProjectWorkspaceService.PublicFlowKind &&
                         asset.Id != _session.AssetId)
                {
                    var opened = await _workspace.OpenPublicFlowEditorAsync(asset.Id);
                    used = opened.Document.Walk().Any(CallsThisFlow);
                }
                if (used) usages.Add(asset.Name);
            }
            _status.Text = usages.Count == 0
                ? "当前工程还没有调用这个公共流程。"
                : "当前调用位置：" + string.Join("、", usages);
        }
        catch (Exception error)
        {
            _status.Text = "引用检查失败：" + error.Message;
        }
        finally { _busy = false; }
    }

    private bool CallsThisFlow(StepNode node) =>
        node.Type == "CallPublicFlow" &&
        Guid.TryParse(node.Properties.GetValueOrDefault("flowAssetId", ""), out var id) &&
        id == _session.AssetId.Value;

    private Selection CurrentSelection()
    {
        if (_tree.SelectedItem is TreeViewItem item && item.Tag is Selection selected)
            return selected;
        return new Selection(null, null, _transaction.Current.Get(TestPhase.Test).Length, null);
    }

    private void RefreshTree(StepId? focus)
    {
        _refreshing = true;
        _tree.Items.Clear();
        var body = _transaction.Current.Get(TestPhase.Test);
        for (var index = 0; index < body.Length; index++)
            _tree.Items.Add(BuildStep(body[index], null, null, index, focus));
        // WPF may ignore IsSelected set before an item joins the live tree.
        // Restore the actual selection after all nested containers are attached.
        if (focus is not null)
        {
            static TreeViewItem? Find(IEnumerable<object> items, StepId id)
            {
                foreach (var candidate in items.OfType<TreeViewItem>())
                {
                    if (candidate.Tag is Selection { Node: { } node } && node.Id == id)
                        return candidate;
                    var nested = Find(candidate.Items.Cast<object>(), id);
                    if (nested is not null) return nested;
                }
                return null;
            }
            var selected = Find(_tree.Items.Cast<object>(), focus.Value);
            if (selected is not null) selected.IsSelected = true;
        }
        _refreshing = false;
        if (focus is null)
            InspectorHost.Content = new TextBlock
            {
                Text = "选中流程步骤即可编辑；选择分支节点可以向该分支添加步骤。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14)
            };
    }

    private TreeViewItem BuildStep(StepNode node, StepId? parent,
        string? slot, int index, StepId? focus)
    {
        var item = new TreeViewItem
        {
            Header = node.Name + "   ·   " + StepUiCatalog.Label(node.Type),
            Tag = new Selection(parent, slot, index, node),
            IsExpanded = true
        };
        if (focus == node.Id)
            item.IsSelected = true;
        foreach (var childSlot in node.Slots.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var branch = new TreeViewItem
            {
                Header = StepUiCatalog.SlotLabel(childSlot.Key),
                Tag = new Selection(node.Id, childSlot.Key, childSlot.Value.Length,
                    null, true), IsExpanded = true
            };
            for (var i = 0; i < childSlot.Value.Length; i++)
                branch.Items.Add(BuildStep(childSlot.Value[i], node.Id, childSlot.Key, i, focus));
            item.Items.Add(branch);
        }
        return item;
    }

    private void SelectedChanged(TreeViewItem? item)
    {
        if (_refreshing || IsPropertyEditorOpen) return;
        _requestVersion++;
    }

    private void EditSelected()
    {
        if (_busy || !TryFinishInspector() || CurrentSelection().Node is not { } node) return;
        _ = ShowInspectorAsync(node);
    }

    private bool TryFinishInspector()
    {
        if (!IsPropertyEditorOpen && _newStepId is null) return true;
        _inspector?.ShowPendingWarning();
        return false;
    }

    public bool TryDismissPropertyEditor()
    {
        if (!IsPropertyEditorOpen) return _newStepId is null;
        if (_busy) return false;
        if (_inspector is not { } editor)
        {
            if (_newStepId is not null) return false;
            ClosePropertyEditor(); // invalidates outstanding catalog loads
            return true;
        }
        if (!editor.HasPendingEdits && _newStepId is null)
        {
            ClosePropertyEditor();
            return true;
        }
        return editor.TryApplyChanges();
    }

    private void ClosePropertyEditor()
    {
        _requestVersion++;
        _propertyOverlay.Visibility = Visibility.Collapsed;
        InspectorHost.Content = null;
        _inspector = null;
        _tree.Focus();
    }

    private void CancelPropertyEditor()
    {
        ClosePropertyEditor();
        if (_newStepId is { } id && _transaction.CanUndo)
        {
            _transaction.Undo();
            _cancelledStepId = id;
            _cancelledActionKey = _newActionKey;
            _newStepId = null;
            _newActionKey = null;
            RefreshTree(null);
        }
    }

    private void ShowPalette(Button? owner)
    {
        if (_busy || !TryFinishInspector()) return;
        var chosenPlace = CurrentSelection();
        var caseOnly = chosenPlace.IsSlot && chosenPlace.Slot == "cases";
        var palette = new ContextMenu();
        foreach (var action in TestStepInspector.PaletteActions(caseOnly))
        {
            var selectedAction = action;
            var item = new MenuItem { Header = selectedAction.DisplayName };
            item.Click += (_, _) => AddStep(selectedAction, chosenPlace);
            palette.Items.Add(item);
        }
        palette.PlacementTarget = (UIElement?)owner ?? _tree;
        palette.IsOpen = true;
    }

    private void AddStep(TestActionOption action, Selection place)
    {
        if (_busy || !TryFinishInspector()) return;
        try
        {
            var node = StepUiCatalog.NewNode(action.StepType) with { Name = action.Title };
            if (action.Key == "can") node = node.WithProperty("value", "HEX:0000000000000000");
            var parent = place.IsSlot ? place.Parent : place.Parent;
            var slot = place.Slot;
            var index = place.IsSlot ? place.Index : place.Node is not null
                ? place.Index + 1 : _transaction.Current.Get(TestPhase.Test).Length;
            _transaction.Insert(TestPhase.Test, parent, slot, index, node);
            _newStepId = node.Id;
            _newActionKey = action.Key;
            _cancelledStepId = null;
            _cancelledActionKey = null;
            _dirty = true;
            RefreshTree(node.Id);
            _ = ShowInspectorAsync(node, action.Key);
        }
        catch (Exception error) { _status.Text = "添加步骤失败：" + error.Message; }
    }

    private async Task ShowInspectorAsync(StepNode node, string? actionKey = null)
    {
        var request = ++_requestVersion;
        _propertyCard.MaxWidth = 680;
        _propertyCard.MaxHeight = 350;
        _propertyOverlay.Visibility = Visibility.Visible;
        _inspector = null;
        InspectorHost.Content = new TextBlock { Text = "正在读取步骤属性…", Margin = new Thickness(18) };
        var assetKind = node.Type;
        var namedTarget = node.Properties.GetValueOrDefault("target", "");
        var bound = _transaction.Current.Mappings.FirstOrDefault(mapping =>
            string.Equals(mapping.Name, namedTarget, StringComparison.OrdinalIgnoreCase));
        var needsCatalog = actionKey is "can" or "read" or "wait-signal" or
            "wave-start" or "wave-analyze" or "assert-signal" ||
            assetKind is "Read" or "WaitUntil" or "AssertSignal" or
                "WaveStart" or "WaveAnalyze" or "CanWait" or "CanSend" ||
            assetKind == "Write" && actionKey is not ("ign" or "key") &&
                bound?.Kind is not (TestTargetKind.Ignition or TestTargetKind.RawKey or
                    TestTargetKind.Button);
        IReadOnlyList<MappingChoice> choices = [];
        if (needsCatalog)
        {
            InspectorHost.Content = new TextBlock { Text = "正在读取报文与信号目录…",
                Margin = new Thickness(14) };
            try { choices = await TestTargetCatalog.LoadAsync(_workspace); }
            catch (Exception error)
            {
                _status.Text = "资源目录读取失败（草稿未丢失）：" + error.Message;
            }
        }
        if (request != _requestVersion) return;
        var flows = _workspace.Current?.Definition.Assets
            .Where(a => a.Kind == ProjectWorkspaceService.PublicFlowKind &&
                a.Id != _session.AssetId)
            .Select(a => new PublicFlowChoice(a.Id.Value,
                a.Revision, a.Sha256, a.Name)).ToArray() ?? [];
        var inspector = new TestStepInspector(choices,
            _transaction.Current.Mappings.ToArray(), node, actionKey, false, flows);
        inspector.ApplyRequested = result =>
        {
            try
            {
                _transaction.ReplaceWithMapping(node.Id, result.Step, result.Mapping);
                _newStepId = null;
                _newActionKey = null;
                _dirty = true;
                RefreshTree(node.Id);
                _status.Text = "已更新当前流程步骤，修改将自动保存。";
                return true;
            }
            catch (Exception error)
            {
                _status.Text = "应用失败：" + error.Message;
                return false;
            }
        };
        inspector.AppliedSuccessfully += () =>
        {
            if (ReferenceEquals(_inspector, inspector)) ClosePropertyEditor();
        };
        inspector.Cancelled += () =>
        {
            if (ReferenceEquals(_inspector, inspector)) CancelPropertyEditor();
        };
        _inspector = inspector;
        _propertyCard.MaxWidth = inspector.PreferredEditorWidth;
        _propertyCard.MaxHeight = inspector.PreferredEditorHeight;
        InspectorHost.Content = inspector;
    }

    private void DeleteSelected()
    {
        if (_busy || !TryFinishInspector()) return;
        var step = CurrentSelection().Node;
        if (step is null) return;
        try { _transaction.Delete(step.Id); _dirty = true; RefreshTree(null); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void MoveSelected(int offset)
    {
        if (_busy || !TryFinishInspector()) return;
        var place = CurrentSelection();
        if (place.Node is null) return;
        var target = place.Index + offset;
        if (target < 0) return;
        try
        {
            _transaction.Move(place.Node.Id, TestPhase.Test,
                place.Parent, place.Slot, offset > 0 ? target + 1 : target);
            _dirty = true;
            RefreshTree(place.Node.Id);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void Undo()
    {
        if (_busy || !TryFinishInspector() || !_transaction.CanUndo) return;
        _transaction.Undo(); _newStepId = null; _dirty = true; RefreshTree(null);
    }

    private void Redo()
    {
        if (_busy || !TryFinishInspector() || !_transaction.CanRedo) return;
        _transaction.Redo(); _newStepId = null; _dirty = true;
        if (_cancelledStepId is { } id && _transaction.Current.Walk().FirstOrDefault(x => x.Id == id) is { } draft)
        {
            _newStepId = id;
            _newActionKey = _cancelledActionKey;
            _cancelledStepId = null;
            _cancelledActionKey = null;
            RefreshTree(id);
            _ = ShowInspectorAsync(draft, _newActionKey);
            return;
        }
        RefreshTree(null);
    }

    public async Task SynchronizeSavedRevisionAsync()
    {
        var context = _workspace.Current;
        var asset = context?.Definition.Assets.FirstOrDefault(a => a.Id == _session.AssetId);
        if (_busy || asset is null || context!.Definition.Id != _session.ProjectId ||
            asset.Revision == _session.AssetRevision) return;
        if (IsDirty)
        {
            _status.Text = "公共流程磁盘修订已变化；草稿仍保留，不得覆盖新版本。";
            return;
        }
        _busy = true;
        try
        {
            _session = await _workspace.OpenPublicFlowEditorAsync(_session.AssetId);
            var draft = TestDocument.Create(_session.Document.Name);
            _transaction = new StepEditTransaction(draft with
            {
                Phases = draft.Phases.SetItem(TestPhase.Test, _session.Document.Body),
                Mappings = _session.Document.Mappings
            });
            RefreshTree(null);
            _status.Text = "已读取工程自动更新的公共流程修订。";
        }
        catch (Exception error) { _status.Text = "无法读取新修订：" + error.Message; }
        finally { _busy = false; }
    }

    public bool ConfirmClose()
    {
        if (_busy || !TryFinishInspector()) return false;
        if (!IsDirty) return true;
        return MessageBox.Show(Window.GetWindow(this),
            "公共流程有未保存修改，确定放弃吗？", "关闭公共流程",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public async Task<bool> SaveForCloseAsync()
    {
        if (_busy || !TryDismissPropertyEditor()) return false;
        if (!_dirty) return true;
        _busy = true;
        try
        {
            var body = _transaction.Current.Get(TestPhase.Test);
            var pins = new List<PublicFlowReference>();
            foreach (var step in TestDocument.WalkNodes(body)
                .Where(x => x.Type == "CallPublicFlow"))
            {
                if (Guid.TryParse(step.Properties.GetValueOrDefault("flowAssetId", ""), out var id) &&
                    int.TryParse(step.Properties.GetValueOrDefault("flowRevision", ""), out var revision))
                {
                    var sha = step.Properties.GetValueOrDefault("flowSha", "");
                    var reference = new PublicFlowReference(id, revision, sha);
                    if (!pins.Contains(reference)) pins.Add(reference);
                }
            }
            var next = _session.Document with
            {
                Body = body,
                Mappings = _transaction.Current.Mappings,
                References = pins.ToImmutableArray()
            };
            _session = await _workspace.SavePublicFlowEditorAsync(_session, next);
            _dirty = false;
            _status.Text = "公共流程已保存";
            return true;
        }
        catch (Exception ex)
        {
            _status.Text = "公共流程保存失败，修改仍保留：" + ex.Message;
            MessageBox.Show(Window.GetWindow(this), ex.Message, "保存未完成",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally { _busy = false; }
    }
}
