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
    private readonly StepEditTransaction _transaction;
    private readonly TreeView _tree = new();
    private readonly TextBlock _status = new();
    private TestStepInspector? _inspector;
    private bool _refreshing, _dirty, _busy;
    private StepId? _newStepId;
    private int _requestVersion;

    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };

    public bool IsDirty => _dirty || _newStepId is not null ||
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
        toolbar.Children.Add(new TextBlock
        {
            Text = "公共流程 · " + session.AssetName,
            FontSize = 15, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        });
        toolbar.Children.Add(MakeButton("＋ 添加步骤", (s, _) => ShowPalette(s as Button)));
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
        layout.Children.Add(_tree);

        Grid.SetRow(_status, 2);
        _status.Text = "公共流程为连续步骤树；选择节点后在右侧编辑。运行由调用它的测试用例负责。";
        _status.Padding = new Thickness(14, 6, 14, 6);
        _status.Foreground = new SolidColorBrush(Color.FromRgb(86, 111, 143));
        layout.Children.Add(_status);
        Content = layout;
        RefreshTree(null);
    }

    private static Button MakeButton(string title, RoutedEventHandler handler)
    {
        var button = new Button { Content = title, MinHeight = 29,
            Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(9, 3, 9, 3) };
        button.Click += handler;
        return button;
    }

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
        if (_refreshing || item?.Tag is not Selection selected) return;
        if (!TryFinishInspector())
        {
            _status.Text = "当前步骤参数未完成，先修正或取消，再切换。";
            return;
        }
        if (selected.Node is { } node)
            _ = ShowInspectorAsync(node);
        else
            InspectorHost.Content = new TextBlock
            {
                Text = "已选择" + (selected.IsSlot ? "子分支" : "流程根步骤") +
                    "；点击“添加步骤”插入到该位置。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14)
            };
    }

    private bool TryFinishInspector()
    {
        if (_newStepId is not null)
            return _inspector?.TryApplyChanges() == true;
        return _inspector?.HasPendingEdits != true ||
               _inspector.TryApplyChanges();
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
        palette.PlacementTarget = owner ?? _tree;
        palette.IsOpen = true;
    }

    private void AddStep(TestActionOption action, Selection place)
    {
        if (_busy || !TryFinishInspector()) return;
        try
        {
            var node = StepUiCatalog.NewNode(action.StepType) with { Name = action.Title };
            var parent = place.IsSlot ? place.Parent : place.Parent;
            var slot = place.Slot;
            var index = place.IsSlot ? place.Index : place.Node is not null
                ? place.Index + 1 : _transaction.Current.Get(TestPhase.Test).Length;
            _transaction.Insert(TestPhase.Test, parent, slot, index, node);
            _newStepId = node.Id;
            _dirty = true;
            RefreshTree(node.Id);
            _ = ShowInspectorAsync(node, action.Key);
        }
        catch (Exception error) { _status.Text = "添加步骤失败：" + error.Message; }
    }

    private async Task ShowInspectorAsync(StepNode node, string? actionKey = null)
    {
        var request = ++_requestVersion;
        var assetKind = node.Type;
        var needsCatalog = actionKey is "can" or "read" or "wait-signal" or
            "wave-start" or "wave-analyze" ||
            assetKind is "Read" or "WaitUntil" or "WaveStart" or
                "WaveAnalyze" or "CanWait" or "CanSend" ||
            assetKind == "Write" && actionKey is not ("ign" or "key");
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
                _dirty = true;
                RefreshTree(node.Id);
                _status.Text = "已更新当前流程步骤，保存后更新工程资源修订。";
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
            if (ReferenceEquals(_inspector, inspector))
                _ = ShowInspectorAsync(_transaction.Current.Walk()
                    .First(x => x.Id == node.Id));
        };
        inspector.Cancelled += () =>
        {
            if (_newStepId == node.Id && _transaction.CanUndo)
            {
                _transaction.Undo();
                _newStepId = null;
                RefreshTree(null);
            }
            else _ = ShowInspectorAsync(node);
        };
        _inspector = inspector;
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
        _transaction.Redo(); _newStepId = null; _dirty = true; RefreshTree(null);
    }

    public bool ConfirmClose()
    {
        if (_busy) return false;
        if (!IsDirty) return true;
        return MessageBox.Show(Window.GetWindow(this),
            "公共流程有未保存修改，确定放弃吗？", "关闭公共流程",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public async Task<bool> SaveForCloseAsync()
    {
        if (_busy || !TryFinishInspector()) return false;
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
            _status.Text = "公共流程已保存 · 修订 " + _session.AssetRevision;
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
