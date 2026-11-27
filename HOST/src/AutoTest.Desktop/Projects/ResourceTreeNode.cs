using System.Collections.ObjectModel;
using System.ComponentModel;
using AutoTest.Projects.Resources;

namespace AutoTest.Desktop.Projects;

/// <summary>Visual-only navigation. AssetEntry is the canonical resource identity.</summary>
public sealed class ResourceTreeNode : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isSearchMatch;
    private string _count;
    public ResourceTreeNode(string label, string icon, string description, string category = "",
        AssetEntry? asset = null, bool isPlaceholder = false, string role = "Asset",
        string count = "", ProjectRunSelectionItem? runSelection = null)
    {
        Label = label; Icon = icon; Description = description; Category = category;
        Asset = asset; IsPlaceholder = isPlaceholder; Role = role;
        _count = count; RunSelection = runSelection;
    }
    public string Label { get; }
    public string Icon { get; }
    public string Description { get; }
    public string Category { get; }
    public string Role { get; }
    public AssetEntry? Asset { get; }
    public bool IsPlaceholder { get; }
    public ProjectRunSelectionItem? RunSelection { get; }
    public bool IsRunSelectable => RunSelection is not null;
    public string CountLabel
    {
        get => _count;
        set
        {
            if (_count == value) return;
            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountLabel)));
        }
    }
    /// <summary>Transient navigation highlight. Search never edits or opens an asset.</summary>
    public bool IsSearchMatch
    {
        get => _isSearchMatch;
        set
        {
            if (_isSearchMatch == value) return;
            _isSearchMatch = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSearchMatch)));
        }
    }

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
    public bool IsCreatableCategory => Asset is null && !IsPlaceholder &&
        Category is (ProjectWorkspaceService.PublicFlowKind or
        ProjectWorkspaceService.TestProgramKind or ProjectWorkspaceService.TestProjectKind or
        ProjectWorkspaceService.DbcKind or ProjectWorkspaceService.ControllerKind or
        ProjectWorkspaceService.WaveSettingsKind or ProjectWorkspaceService.WaveKind or
        ProjectWorkspaceService.FlashSettingsKind or ProjectWorkspaceService.FirmwareKind);
    public string IconPath => Role switch
    {
        "Root" => "M3 3 H21 V21 H3 Z M3 9 H21 M9 9 V21",
        "Project" or "Folder" => "M3 7 V20 H21 V7 H12 L10 5 H5 A2 2 0 0 0 3 7 Z",
        "Case" => "M7 3 H14 L19 8 V21 H7 A2 2 0 0 1 5 19 V5 A2 2 0 0 1 7 3 Z M14 3 V8 H19 M9 12 H16 M9 16 H16",
        _ => Category switch
        {
            ProjectWorkspaceService.DbcKind => "M3 5 A9 3 0 0 0 21 5 A9 3 0 0 0 3 5 Z M3 5 V19 C3 23 21 23 21 19 V5 M3 12 C3 16 21 16 21 12",
            ProjectWorkspaceService.ControllerKind => "M4 7 H20 M4 17 H20 M9 4 V10 M16 14 V20",
            ProjectWorkspaceService.WaveKind or ProjectWorkspaceService.WaveSettingsKind => "M2 12 H6 L9 5 L14 19 L18 12 H22",
            _ => "M7 3 H14 L19 8 V21 H7 A2 2 0 0 1 5 19 V5 A2 2 0 0 1 7 3 Z M14 3 V8 H19"
        }
    };
    public ObservableCollection<ResourceTreeNode> Children { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
}
