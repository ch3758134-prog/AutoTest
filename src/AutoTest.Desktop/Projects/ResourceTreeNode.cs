using System.Collections.ObjectModel;
using AutoTest.Projects.Resources;

namespace AutoTest.Desktop.Projects;

/// <summary>Presentation-only tree item. Empty category nodes are NOT persisted as assets.</summary>
public sealed class ResourceTreeNode
{
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
