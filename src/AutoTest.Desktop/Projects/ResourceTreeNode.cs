using System.Collections.ObjectModel;
using System.ComponentModel;
using AutoTest.Projects.Resources;

namespace AutoTest.Desktop.Projects;

/// <summary>Presentation-only tree item. Empty category nodes are NOT persisted as assets.</summary>
public sealed class ResourceTreeNode : INotifyPropertyChanged
{
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label { get; }
    public string Icon { get; }
    public string Description { get; }
    public string Category { get; }
    public AssetEntry? Asset { get; }
    public bool IsPlaceholder { get; }
    public ObservableCollection<ResourceTreeNode> Children { get; } = new();

    public ResourceTreeNode(string label, string icon, string description, string category = "",
        AssetEntry? asset = null, bool isPlaceholder = false)
    {
        Label = label;
        Icon = icon;
        Description = description;
        Category = category;
        Asset = asset;
        IsPlaceholder = isPlaceholder;
    }
}
