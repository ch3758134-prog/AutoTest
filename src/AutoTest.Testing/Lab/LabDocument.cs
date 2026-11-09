using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoTest.Testing.Lab;

// This isolated LAB candidate never takes over the existing production TestDocument/Runner.
// INTEGRATION must map it to the authoritative TEST document after 0098b9f reconciliation.
public enum LabPhase { Prepare, Test, Cleanup }
public enum LabSource { Mock, Replay, Physical }
public enum LabQuality { Observed, Missing, Invalid, Stale }
public enum LabVerdict { Pass, Fail, Inconclusive, Error, Skipped }

public sealed record LabNode(Guid Id, string Type, string Name,
    Dictionary<string, string> Parameters, Dictionary<string, List<LabNode>> Slots)
{
    public static LabNode New(string type, string? name = null) =>
        new(Guid.NewGuid(), type, name ?? type, new(StringComparer.Ordinal), new(StringComparer.Ordinal));

    public LabNode Copy(bool newIds = false) => new(newIds ? Guid.NewGuid() : Id, Type, Name,
        new Dictionary<string, string>(Parameters, StringComparer.Ordinal),
        Slots.ToDictionary(p => p.Key, p => p.Value.Select(n => n.Copy(newIds)).ToList(), StringComparer.Ordinal));
}

public sealed record LabDocument(Guid Id, int FormatVersion, string Name,
    Dictionary<LabPhase, List<LabNode>> Phases)
{
    public const int CurrentFormat = 1;
    public static LabDocument New(string name = "New program") =>
        new(Guid.NewGuid(), CurrentFormat, name,
            Enum.GetValues<LabPhase>().ToDictionary(p => p, _ => new List<LabNode>()));
    public LabDocument Copy() => new(Id, FormatVersion, Name,
        Phases.ToDictionary(p => p.Key, p => p.Value.Select(n => n.Copy()).ToList()));
    public IEnumerable<LabNode> Walk() => Phases.Values.SelectMany(Walk);
    public static IEnumerable<LabNode> Walk(IEnumerable<LabNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var child in n.Slots.Values.SelectMany(Walk)) yield return child;
        }
    }
}

public static class LabDocumentCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };
    public static string Save(LabDocument document) => JsonSerializer.Serialize(document, Options);
    public static LabDocument Open(string json)
    {
        var document = JsonSerializer.Deserialize<LabDocument>(json, Options)
            ?? throw new FormatException("Empty document");
        if (document.FormatVersion != LabDocument.CurrentFormat)
            throw new NotSupportedException("Lab document schema requires explicit migration");
        if (document.Id == Guid.Empty || document.Phases is null ||
            Enum.GetValues<LabPhase>().Any(p => !document.Phases.ContainsKey(p)))
            throw new FormatException("Invalid document identity or missing phases");
        if (document.Phases.Count != 3 || document.Phases.Values.Any(v => v is null))
            throw new FormatException("Invalid phase content");
        var ids = new HashSet<Guid>();
        var visited = 0;
        var stack = new Stack<(LabNode? node, int depth)>();
        foreach (var node in document.Phases.Values.SelectMany(v => v)) stack.Push((node, 0));
        while (stack.TryPop(out var item))
        {
            var (node, depth) = item;
            if (node is null || node.Id == Guid.Empty || !ids.Add(node.Id))
                throw new FormatException("Empty or duplicate node identity");
            if (node.Parameters is null || node.Slots is null ||
                node.Slots.Values.Any(v => v is null)) throw new FormatException("Invalid node content");
            if (++visited > 10000 || depth > 32) throw new FormatException("LAB document exceeds size/depth limit");
            foreach (var child in node.Slots.Values.SelectMany(v => v)) stack.Push((child, depth + 1));
        }
        return document.Copy();
    }
    public static string Digest(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
}

public sealed class LabDocumentEditor
{
    private readonly Stack<LabDocument> _undo = new();
    private readonly Stack<LabDocument> _redo = new();
    private LabDocument _current;
    public LabDocumentEditor(LabDocument document) => _current = document.Copy();
    public LabDocument Snapshot => _current.Copy();
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;

    public void Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("No undo state");
        _redo.Push(_current); _current = _undo.Pop();
    }
    public void Redo()
    {
        if (!CanRedo) throw new InvalidOperationException("No redo state");
        _undo.Push(_current); _current = _redo.Pop();
    }
    private void Change(Action<LabDocument> edit)
    {
        var candidate = _current.Copy();
        edit(candidate);
        // Re-serialize on every commit so no reference escaping through the editor.
        candidate = LabDocumentCodec.Open(LabDocumentCodec.Save(candidate));
        _undo.Push(_current); _current = candidate; _redo.Clear();
    }
    private static List<LabNode> GetSlot(LabDocument doc, LabPhase phase, Guid? parent, string? slot)
    {
        if (!doc.Phases.TryGetValue(phase, out var root)) throw new ArgumentOutOfRangeException(nameof(phase));
        if (parent is null)
        {
            if (slot is not null) throw new ArgumentException("Root cannot have slot");
            return root;
        }
        var node = LabDocument.Walk(root).SingleOrDefault(n => n.Id == parent)
            ?? throw new KeyNotFoundException("Parent not found in phase");
        if (string.IsNullOrEmpty(slot) || !node.Slots.TryGetValue(slot, out var nodes))
            throw new ArgumentException("Existing child slot is required");
        return nodes;
    }
    private static (List<LabNode> list, int index, LabNode node) Find(LabDocument doc, Guid id)
    {
        foreach (var roots in doc.Phases.Values)
        {
            var result = Search(roots, id);
            if (result is { } found) return found;
        }
        throw new KeyNotFoundException("Step not found");
    }
    private static (List<LabNode>, int, LabNode)? Search(List<LabNode> list, Guid id)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Id == id) return (list, i, list[i]);
            foreach (var child in list[i].Slots.Values)
                if (Search(child, id) is { } found) return found;
        }
        return null;
    }
    public void Insert(LabPhase phase, Guid? parent, string? slot, int index, LabNode node) => Change(doc =>
    {
        var target = GetSlot(doc, phase, parent, slot);
        if (index < 0 || index > target.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var ids = doc.Walk().Select(n => n.Id).ToHashSet();
        if (LabDocument.Walk(new[] { node }).Any(n => ids.Contains(n.Id)))
            throw new InvalidOperationException("StepId must be unique. Use Copy.");
        target.Insert(index, node.Copy());
    });
    public void Delete(Guid id) => Change(doc =>
    {
        var (list, index, _) = Find(doc, id);
        list.RemoveAt(index);
    });
    public void Copy(Guid id, LabPhase phase, Guid? parent, string? slot, int index)
    {
        var clone = Find(_current, id).node.Copy(newIds: true);
        Insert(phase, parent, slot, index, clone);
    }
    public void Move(Guid id, LabPhase phase, Guid? parent, string? slot, int index) => Change(doc =>
    {
        var (source, oldIndex, node) = Find(doc, id);
        if (LabDocument.Walk(new[] { node }).Any(n => n.Id == parent))
            throw new InvalidOperationException("Cannot move node into itself or descendant");
        source.RemoveAt(oldIndex);
        var target = GetSlot(doc, phase, parent, slot);
        if (ReferenceEquals(source, target) && index > oldIndex) index--;
        if (index < 0 || index > target.Count) throw new ArgumentOutOfRangeException(nameof(index));
        target.Insert(index, node);
    });
    public void Rename(Guid id, string name) => Change(doc =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required");
        var (list, index, node) = Find(doc, id);
        list[index] = node with { Name = name.Trim() };
    });
    public void SetParameter(Guid id, string key, string value) => Change(doc =>
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Parameter key required");
        var (_, _, node) = Find(doc, id);
        node.Parameters[key] = value;
    });
    public void EnsureSlot(Guid id, string slot) => Change(doc =>
    {
        if (string.IsNullOrWhiteSpace(slot)) throw new ArgumentException("Slot required");
        var (_, _, node) = Find(doc, id);
        node.Slots.TryAdd(slot, new List<LabNode>());
    });
}

