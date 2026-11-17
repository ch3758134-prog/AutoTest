namespace AutoTest.UiPreview;

/// <summary>
/// Undo and redo history of an in-memory edit document. Snapshots copy step trees,
/// including branch nesting and IDs; no persistence or runtime execution.
/// </summary>
public sealed class PreviewEditHistory
{
    private sealed record Snapshot(List<DemoPhase> Phases, Guid? Selected);
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Remember(IReadOnlyList<DemoPhase> phases, Guid? selected)
    {
        _undo.Push(Capture(phases, selected));
        _redo.Clear();
    }

    public bool TryUndo(List<DemoPhase> phases, Guid? selected, out Guid? restoredSelection)
    {
        if (!CanUndo) { restoredSelection = selected; return false; }
        _redo.Push(Capture(phases, selected));
        Restore(phases, _undo.Pop(), out restoredSelection);
        return true;
    }

    public bool TryRedo(List<DemoPhase> phases, Guid? selected, out Guid? restoredSelection)
    {
        if (!CanRedo) { restoredSelection = selected; return false; }
        _undo.Push(Capture(phases, selected));
        Restore(phases, _redo.Pop(), out restoredSelection);
        return true;
    }

    private static Snapshot Capture(IReadOnlyList<DemoPhase> phases, Guid? selected)
        => new(CopyPhases(phases), selected);

    private static void Restore(List<DemoPhase> target, Snapshot snapshot, out Guid? selected)
    {
        target.Clear();
        target.AddRange(CopyPhases(snapshot.Phases));
        selected = snapshot.Selected;
    }

    private static List<DemoPhase> CopyPhases(IReadOnlyList<DemoPhase> phases)
    {
        var copy = new List<DemoPhase>(phases.Count);
        foreach (var source in phases)
        {
            var phase = new DemoPhase(source.Key, source.Title) { Collapsed = source.Collapsed };
            foreach (var step in source.Steps) phase.Steps.Add(CopyStep(step));
            copy.Add(phase);
        }
        return copy;
    }

    private static DemoStep CopyStep(DemoStep src)
    {
        var copy = new DemoStep
        {
            Id = src.Id, Kind = src.Kind, Name = src.Name,
            Enabled = src.Enabled, Breakpoint = src.Breakpoint
        };
        foreach (var pair in src.Parameters) copy.Parameters.Add(pair.Key, pair.Value);
        foreach (var branch in src.Branches)
        {
            var fork = new DemoBranch(branch.Name);
            foreach (var step in branch.Steps) fork.Steps.Add(CopyStep(step));
            copy.Branches.Add(fork);
        }
        return copy;
    }
}
