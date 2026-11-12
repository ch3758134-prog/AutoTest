using System.Collections.ObjectModel;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using Prism.Mvvm;

namespace AutoTest.Desktop.Testing;

public enum StepTreeItemKind { Phase, Slot, Step }

/// <summary>Tree coordinates are presentation-only; IDs are canonical StepId.</summary>
public sealed class StepTreeItem : BindableBase
{
    private bool _isSelected;
    public StepTreeItemKind Kind { get; }
    public TestPhase Phase { get; }
    public StepNode? Step { get; }
    public StepTreeItem? Parent { get; }
    public string? SlotName { get; }
    public int Index { get; }
    public ObservableCollection<StepTreeItem> Children { get; } = new();

    public StepTreeItem(StepTreeItemKind kind, TestPhase phase, string label,
        StepTreeItem? parent = null, StepNode? step = null,
        string? slot = null, int index = 0)
    {
        Kind = kind;
        Phase = phase;
        Label = label;
        Parent = parent;
        Step = step;
        SlotName = slot;
        Index = index;
    }

    public string Label { get; }
    public string Icon => Kind switch
    {
        StepTreeItemKind.Phase => "▣",
        StepTreeItemKind.Slot => "↳",
        _ => Step?.Type switch
        {
            "If" => "◇",
            "Loop" => "↻",
            "Wait" => "◷",
            "Assert" => "✓",
            _ => "·"
        }
    };
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public (StepId? ParentId, string? Slot, int Index) CoordinatesForAppend()
    {
        if (Kind == StepTreeItemKind.Phase)
            return (null, null, Children.Count);
        if (Kind == StepTreeItemKind.Slot && Parent?.Step is { } container)
            return (container.Id, SlotName, Children.Count);
        throw new InvalidOperationException("请先选择阶段或子分支，再添加子步骤。");
    }

    public (StepId? ParentId, string? Slot, int Index) CoordinatesForSibling(int delta = 1)
    {
        if (Kind != StepTreeItemKind.Step || Parent is null)
            throw new InvalidOperationException("请选择一个步骤。");
        return Parent.Kind switch
        {
            StepTreeItemKind.Phase => (null, null, Index + delta),
            StepTreeItemKind.Slot => (Parent.Parent?.Step?.Id, Parent.SlotName, Index + delta),
            _ => throw new InvalidOperationException("步骤父层级不合法。")
        };
    }

    public static IEnumerable<StepTreeItem> Walk(IEnumerable<StepTreeItem> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Walk(node.Children)) yield return child;
        }
    }
}
