using System.Collections.ObjectModel;

namespace AutoTest.Desktop.Shell;

/// <summary>
/// Owns document identity and active-tab transitions independently of WPF
/// controls or project persistence. A rejected leave guard never opens a hidden
/// tab, and Close/Discard never runs that guard for a second time.
/// </summary>
public sealed class DocumentWorkspaceState<T> where T : class
{
    public ObservableCollection<T> Tabs { get; } = new();
    public T? Active { get; private set; }

    public bool TryActivate(T? target, Func<T?, bool> canLeave)
    {
        if (ReferenceEquals(Active, target)) return true;
        if (target is not null && !Tabs.Contains(target)) return false;
        if (!canLeave(Active)) return false;
        Active = target;
        return true;
    }

    public bool TryOpen(T tab, Func<T?, bool> canLeave)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (Tabs.Contains(tab)) return TryActivate(tab, canLeave);
        if (!canLeave(Active)) return false;
        Tabs.Add(tab);
        Active = tab;
        return true;
    }

    /// <summary>Caller already obtained an explicit Save/Discard/Cancel decision.</summary>
    public bool RemoveApproved(T tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return false;
        var wasActive = ReferenceEquals(Active, tab);
        Tabs.RemoveAt(index);
        if (wasActive)
            Active = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        return true;
    }

    /// <summary>Only after project switch passed the same close guard.</summary>
    public void ClearApproved()
    {
        Tabs.Clear();
        Active = null;
    }
}
