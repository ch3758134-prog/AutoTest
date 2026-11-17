using System.Collections.ObjectModel;

namespace AutoTest.UiPreview;

/// <summary>
/// UI-only test editor document projection. Owns phase data, row flattening,
/// and search highlighting; no WPF view, devices, or runtime services.
/// </summary>
public sealed class PreviewEditorDocument
{
    public List<DemoPhase> Phases { get; } = new();
    public ObservableCollection<PreviewRow> Rows { get; } = new();
    public bool IsDirty { get; private set; }
    public void MarkDirty() => IsDirty = true;
    public void AcceptSaved() => IsDirty = false;
    public DemoStep? SelectedStep { get; private set; }
    public PreviewRow? SelectedRow { get; private set; }

    public void SelectStep(DemoStep? step)
    {
        SelectedStep = step;
        SelectedRow = Rows.FirstOrDefault(row => row.IsStep && row.Step?.Id == step?.Id);
    }
    public void SelectRow(PreviewRow? row)
    {
        SelectedRow = row;
        SelectedStep = row?.Step;
    }

    public void Rebuild(string? search)
    {
        Rows.Clear();
        foreach (var phase in Phases)
        {
            Rows.Add(new PreviewRow { Phase = phase });
            if (!phase.Collapsed) Append(phase, phase.Steps, null, 0);
        }
        // New projection rows preserve selection through the stable step ID.
        SelectedRow = Rows.FirstOrDefault(row => row.IsStep && row.Step?.Id == SelectedStep?.Id);
        var query = search?.Trim() ?? string.Empty;
        foreach (var row in Rows)
            row.IsMatch = row.IsStep && query.Length > 0 &&
                (row.Name + " " + row.Summary + " " + row.Resource)
                .Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void Append(DemoPhase phase, ObservableCollection<DemoStep> steps,
        DemoStep? owner, int depth)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            Rows.Add(new PreviewRow
            {
                Phase = phase, Step = step, Owner = owner, Depth = depth, Index = i
            });
            foreach (var branch in step.Branches)
            {
                Rows.Add(new PreviewRow
                {
                    Phase = phase, Branch = branch, Owner = step, Depth = depth + 1
                });
                Append(phase, branch.Steps, step, depth + 1);
            }
        }
    }

    public IEnumerable<DemoStep> AllSteps()
    {
        foreach (var phase in Phases)
            foreach (var step in Traverse(phase.Steps)) yield return step;
    }

    public static IEnumerable<DemoStep> Traverse(IEnumerable<DemoStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var branch in step.Branches)
                foreach (var child in Traverse(branch.Steps)) yield return child;
        }
    }
    // Editing operations belong to the in-memory document, not the WPF window.
    public DemoStep InsertAfter(PreviewRow? target, DemoStep step)
    {
        var phase = target?.Phase ?? Phases.First(p => p.Key == "test");
        ObservableCollection<DemoStep> items;
        var index = -1;
        if (target?.IsBranch == true && target.Branch is not null)
            items = target.Branch.Steps;
        else if (target?.IsStep == true && target.Step is not null)
        {
            var found = Locate(target.Step);
            items = found?.Items ?? phase.Steps;
            index = (found?.Index ?? items.Count - 1) + 1;
        }
        else
            items = phase.Steps;

        if (index < 0 || index > items.Count) items.Add(step);
        else items.Insert(index, step);
        phase.Collapsed = false;
        SelectStep(step);
        MarkDirty();
        return step;
    }

    public (ObservableCollection<DemoStep> Items, int Index)? Locate(DemoStep selected)
    {
        foreach (var phase in Phases)
        {
            var found = LocateIn(phase.Steps, selected);
            if (found is not null) return found;
        }
        return null;
    }

    private static (ObservableCollection<DemoStep> Items, int Index)? LocateIn(
        ObservableCollection<DemoStep> items, DemoStep selected)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], selected)) return (items, i);
            foreach (var branch in items[i].Branches)
            {
                var found = LocateIn(branch.Steps, selected);
                if (found is not null) return found;
            }
        }
        return null;
    }

    public DemoStep? Duplicate(DemoStep selected)
    {
        var found = Locate(selected);
        if (found is null) return null;
        var copy = selected.Copy();
        found.Value.Items.Insert(found.Value.Index + 1, copy);
        SelectStep(copy);
        MarkDirty();
        return copy;
    }

    public DemoStep? Delete(DemoStep selected)
    {
        var found = Locate(selected);
        if (found is null) return null;
        found.Value.Items.RemoveAt(found.Value.Index);
        var replacement = found.Value.Items.ElementAtOrDefault(
            Math.Min(found.Value.Index, found.Value.Items.Count - 1));
        SelectStep(replacement);
        MarkDirty();
        return replacement;
    }

    public bool CanMove(DemoStep selected, int delta)
    {
        var found = Locate(selected);
        if (found is null) return false;
        var next = found.Value.Index + delta;
        return next >= 0 && next < found.Value.Items.Count;
    }

    public void Move(DemoStep selected, int delta)
    {
        var found = Locate(selected);
        if (found is null) return;
        var next = found.Value.Index + delta;
        if (next < 0 || next >= found.Value.Items.Count) return;
        found.Value.Items.Move(found.Value.Index, next);
        SelectStep(selected);
        MarkDirty();
    }

}
