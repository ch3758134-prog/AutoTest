using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Testing.Authoring;

/// <summary>All edits produce new trees. Failed edits leave history and current document untouched.</summary>
public sealed class StepEditTransaction
{
    private readonly Stack<TestDocument> _undo = new(), _redo = new();
    public TestDocument Current { get; private set; }
    public StepEditTransaction(TestDocument document) => Current = document;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    private void Apply(TestDocument next)
    {
        _undo.Push(Current); Current = next; _redo.Clear();
    }
    public void Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("No undo history");
        _redo.Push(Current); Current = _undo.Pop();
    }
    public void Redo()
    {
        if (!CanRedo) throw new InvalidOperationException("No redo history");
        _undo.Push(Current); Current = _redo.Pop();
    }
    public void Insert(TestPhase phase, StepId? parent, string? slot, int index, StepNode node) =>
        ChangeList(phase, parent, slot, nodes =>
        {
            if (index < 0 || index > nodes.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (TestDocument.WalkNodes([node]).Any(n => Current.Walk().Any(x => x.Id == n.Id)))
                throw new InvalidOperationException("StepId already exists; use Copy");
            return nodes.Insert(index, node);
        });
    /// <summary>
    /// Insert a user-selected operation and its automatically resolved object mapping
    /// as ONE undoable transaction. Never silently replace an existing binding revision.
    /// </summary>
    public void InsertWithMapping(TestPhase phase, StepId? parent, string? slot, int index,
        StepNode node, TestTargetMapping? mapping)
    {
        var baseDocument = WithMapping(Current, mapping);
        var existingIds = Current.Walk().Select(n => n.Id).ToHashSet();
        if (TestDocument.WalkNodes([node]).Any(n => existingIds.Contains(n.Id)))
            throw new InvalidOperationException("StepId already exists.");
        Apply(EditList(baseDocument, phase, parent, slot, nodes =>
        {
            if (index < 0 || index > nodes.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return nodes.Insert(index, node);
        }));
    }

    /// <summary>Existing step edit and a newly selected object use the same atomic history entry.</summary>
    public void ReplaceWithMapping(StepId id, StepNode edited, TestTargetMapping? mapping)
    {
        if (id != edited.Id) throw new ArgumentException("Step identity changed.");
        var baseDocument = WithMapping(Current, mapping);
        var (phase, parent, slot, index) = Find(id);
        Apply(EditList(baseDocument, phase, parent, slot, nodes => nodes.SetItem(index, edited)));
    }

    private static TestDocument WithMapping(TestDocument doc, TestTargetMapping? mapping)
    {
        if (mapping is null) return doc;
        mapping.Validate();
        var existing = doc.Mappings.FirstOrDefault(m =>
            string.Equals(m.Name, mapping.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing != mapping)
                throw new InvalidOperationException(
                    "同名操作对象引用的资源或修订不同。请先在对象管理中明确重新绑定：" + mapping.Name);
            return doc;
        }
        var updated = doc.Mappings.Add(mapping);
        var errors = TestTargetMappings.Validate(updated);
        if (errors.Length != 0) throw new ArgumentException(string.Join("; ", errors));
        return doc with { Mappings = updated };
    }

    public void Delete(StepId id)
    {
        var (phase, parent, slot, index) = Find(id);
        ChangeList(phase, parent, slot, nodes => nodes.RemoveAt(index));
    }
    public void Copy(StepId id, TestPhase targetPhase, StepId? parent, string? slot, int index) =>
        Insert(targetPhase, parent, slot, index,
            Current.Walk().First(x => x.Id == id).CloneNewIds());
    public void UpsertMapping(TestTargetMapping mapping)
    {
        mapping.Validate();
        var index = Array.FindIndex(Current.Mappings.ToArray(), x =>
            string.Equals(x.Name, mapping.Name, StringComparison.OrdinalIgnoreCase));
        var next = index < 0 ? Current.Mappings.Add(mapping)
            : Current.Mappings.SetItem(index, mapping);
        var errors = TestTargetMappings.Validate(next);
        if (errors.Length > 0) throw new ArgumentException(string.Join("; ", errors));
        Apply(Current with { Mappings = next });
    }
    public void RemoveMapping(string name)
    {
        if (Current.Walk().Any(n =>
            (n.Properties.TryGetValue("target", out var target) ||
             n.Properties.TryGetValue("profile", out target)) &&
            string.Equals(target, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Mapping is still referenced by a test step.");
        var index = Array.FindIndex(Current.Mappings.ToArray(), x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new KeyNotFoundException("Mapping not found.");
        Apply(Current with { Mappings = Current.Mappings.RemoveAt(index) });
    }
    public void Rename(StepId id, string name) => Update(id, x => x with { Name = name });
    public void SetProperty(StepId id, string key, string value) =>
        Update(id, x => x.WithProperty(key, value));
    /// <summary>One UI inspector Apply is one undoable authoring transaction.</summary>
    public void ApplyProperties(StepId id, string name, IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrWhiteSpace(name) || properties is null)
            throw new ArgumentException("Step name and properties are required");
        Update(id, x => x with { Name = name,
            Properties = properties.ToImmutableDictionary(StringComparer.Ordinal) });
    }
    public void Move(StepId id, TestPhase phase, StepId? parent, string? slot, int index)
    {
        var node = Current.Walk().First(x => x.Id == id);
        if (TestDocument.WalkNodes([node]).Any(x => x.Id == parent))
            throw new InvalidOperationException("Cannot move node into its own subtree");
        var (oldPhase, oldParent, oldSlot, oldIndex) = Find(id);
        var source = Current;
        try
        {
            // Remove+insert as one history entry, with index correction for same list.
            var removed = EditList(Current, oldPhase, oldParent, oldSlot, a => a.RemoveAt(oldIndex));
            if (phase == oldPhase && parent == oldParent && slot == oldSlot && index > oldIndex) index--;
            var moved = EditList(removed, phase, parent, slot, a => a.Insert(index, node));
            Apply(moved);
        }
        catch { Current = source; throw; }
    }
    private (TestPhase phase, StepId? parent, string? slot, int index) Find(StepId id)
    {
        foreach (var phase in Enum.GetValues<TestPhase>())
        {
            var found = FindIn(Current.Get(phase), id, null, null);
            if (found is { } x) return (phase, x.parent, x.slot, x.index);
        }
        throw new KeyNotFoundException($"Step {id} not found");
    }
    private static (StepId? parent, string? slot, int index)? FindIn(
        ImmutableArray<StepNode> nodes, StepId id, StepId? parent, string? slot)
    {
        for (var i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].Id == id) return (parent, slot, i);
            foreach (var child in nodes[i].Slots)
            {
                var result = FindIn(child.Value, id, nodes[i].Id, child.Key);
                if (result is not null) return result;
            }
        }
        return null;
    }
    private void Update(StepId id, Func<StepNode, StepNode> update)
    {
        var (phase, parent, slot, index) = Find(id);
        ChangeList(phase, parent, slot, a => a.SetItem(index, update(a[index])));
    }
    private void ChangeList(TestPhase phase, StepId? parent, string? slot,
        Func<ImmutableArray<StepNode>, ImmutableArray<StepNode>> edit) =>
        Apply(EditList(Current, phase, parent, slot, edit));
    private static TestDocument EditList(TestDocument doc, TestPhase phase, StepId? parent,
        string? slot, Func<ImmutableArray<StepNode>, ImmutableArray<StepNode>> edit)
    {
        if (parent is null) return doc with { Phases = doc.Phases.SetItem(phase, edit(doc.Get(phase))) };
        if (string.IsNullOrWhiteSpace(slot)) throw new ArgumentException("Child slot required");
        var found = false;
        ImmutableArray<StepNode> Change(ImmutableArray<StepNode> nodes) => nodes.Select(n =>
        {
            if (n.Id == parent)
            {
                if (!n.Slots.TryGetValue(slot, out var children))
                    throw new ArgumentException($"Missing child slot {slot}");
                found = true; return n with { Slots = n.Slots.SetItem(slot, edit(children)) };
            }
            return n with { Slots = n.Slots.ToImmutableDictionary(s => s.Key, s => Change(s.Value)) };
        }).ToImmutableArray();
        var updated = Change(doc.Get(phase));
        if (!found) throw new KeyNotFoundException("Parent not found in specified phase");
        return doc with { Phases = doc.Phases.SetItem(phase, updated) };
    }
}
