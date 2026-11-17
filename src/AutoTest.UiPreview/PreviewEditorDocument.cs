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

    public void Rebuild(string? search)
    {
        Rows.Clear();
        foreach (var phase in Phases)
        {
            Rows.Add(new PreviewRow { Phase = phase });
            if (!phase.Collapsed) Append(phase, phase.Steps, null, 0);
        }
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
}
