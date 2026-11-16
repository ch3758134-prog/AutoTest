using AutoTest.Core;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// Stable presentation identity across immutable StepEditTransaction updates.
/// WPF ListBoxItem/StepTreeItem references are never the source of truth.
/// This is not a second persistent TEST model.
/// </summary>
public readonly record struct StepSelectionKey(
    TestPhase Phase, StepTreeItemKind Kind, StepId? StepId = null,
    StepId? ParentStepId = null, string? Slot = null)
{
    public static StepSelectionKey? Of(StepTreeItem? item)
    {
        if (item is null) return null;
        return item.Kind switch
        {
            StepTreeItemKind.Phase => new(item.Phase, item.Kind),
            StepTreeItemKind.Step => new(item.Phase, item.Kind, item.Step?.Id),
            StepTreeItemKind.Slot => new(item.Phase, item.Kind,
                null, item.Parent?.Step?.Id, item.SlotName),
            _ => null
        };
    }

    public StepTreeItem? FindIn(IEnumerable<StepTreeItem> roots) =>
        StepTreeItem.Walk(roots).FirstOrDefault(node => Of(node) == this);
}

/// <summary>
/// Owns TEST UI navigation, fold state and the latest Inspector request.
/// The document and undo history remain exclusively in StepEditTransaction.
/// </summary>
public sealed class TestEditorInteractionState
{
    private int _inspectorRequest;
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    public StepSelectionKey? Selection { get; private set; }
    public int NextInspectorRequest() => ++_inspectorRequest;
    public bool IsCurrentInspectorRequest(int request) => request == _inspectorRequest;

    public bool TryNavigate(StepSelectionKey? desired, Func<bool> applyPreviousDraft)
    {
        if (desired == Selection) return true;
        if (!applyPreviousDraft()) return false;
        Selection = desired;
        return true;
    }

    /// <summary>Used only when canonical TEST nodes were recreated by a transaction.</summary>
    public void Restore(StepSelectionKey? selected) => Selection = selected;

    private static string FoldKey(StepTreeItem item) => item.Kind switch
    {
        StepTreeItemKind.Phase => "phase:" + item.Phase,
        StepTreeItemKind.Slot => "slot:" + item.Parent?.Step?.Id + ":" + item.SlotName,
        _ => "step:" + item.Step?.Id
    };
    public bool IsCollapsed(StepTreeItem item) => _collapsed.Contains(FoldKey(item));
    public void ToggleCollapsed(StepTreeItem item)
    {
        var key = FoldKey(item);
        if (!_collapsed.Add(key)) _collapsed.Remove(key);
    }
}
