using System.ComponentModel;

namespace AutoTest.Desktop.Projects;

/// <summary>
/// The transient engineer selection is separate from persisted TestProjectEntry.Enabled.
/// Navigation and project editor observe the same entry identity.
/// </summary>
public sealed class ProjectRunSelectionState
{
    private readonly Dictionary<(Guid Project, Guid Entry), ProjectRunSelectionItem> _items = new();
    public event Action<Guid>? Changed;
    public ProjectRunSelectionItem Get(Guid projectId, Guid entryId, bool enabled)
    {
        var key = (projectId, entryId);
        if (_items.TryGetValue(key, out var item)) return item;
        item = new ProjectRunSelectionItem(enabled);
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProjectRunSelectionItem.IsSelected))
                Changed?.Invoke(projectId);
        };
        _items[key] = item;
        return item;
    }
}
public sealed class ProjectRunSelectionItem : INotifyPropertyChanged
{
    private bool _selected;
    public ProjectRunSelectionItem(bool selected) => _selected = selected;
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
