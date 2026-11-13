using System.Collections.Immutable;
using System.Text.Json;
using AutoTest.Core;
using AutoTest.Contracts;

namespace AutoTest.Testing.Authoring;

public static class TestDocumentCodec
{
    private sealed record NodeData(string Id, string Type, string Name,
        Dictionary<string, string> Properties, Dictionary<string, NodeData[]> Slots);
    // Explicit primitive storage: strongly typed AssetId has no public JSON constructor.
    private sealed record MappingData(string Name, string Kind, Guid AssetId, int Revision,
        string Sha256, string Role, uint CanId, bool Extended, string SignalName, string Unit,
        Guid ControllerId, Guid StateId);
    private sealed record DocumentData(int FormatVersion, string Id, string Name,
        Dictionary<string, NodeData[]> Phases, MappingData[]? Mappings = null);
    private static MappingData EncodeMapping(TestTargetMapping x) => new(
        x.Name, x.Kind.ToString(), x.Asset.Value, x.AssetRevision, x.AssetSha256,
        x.Role.ToString(), x.CanId, x.Extended, x.SignalName, x.Unit,
        x.ControllerId, x.StateId);
    private static TestTargetMapping DecodeMapping(MappingData x) =>
        new TestTargetMapping(x.Name, Enum.Parse<TestTargetKind>(x.Kind, false),
            x.Kind == nameof(TestTargetKind.Ignition) && x.AssetId == Guid.Empty
                ? default : AssetId.Create(x.AssetId),
            x.Revision, x.Sha256, Enum.Parse<BusRole>(x.Role, false),
            x.CanId, x.Extended, x.SignalName, x.Unit, x.ControllerId, x.StateId).Validate();
    private static NodeData Encode(StepNode node) => new(node.Id.ToString(), node.Type, node.Name,
        node.Properties.ToDictionary(), node.Slots.ToDictionary(s => s.Key,
            s => s.Value.Select(Encode).ToArray()));
    private static StepNode Decode(NodeData node) => new(
        StepId.TryParse(node.Id, out var id) ? id : throw new FormatException("Invalid StepId"),
        node.Type, node.Name, (node.Properties ?? throw new FormatException("Missing properties")).ToImmutableDictionary(),
        (node.Slots ?? throw new FormatException("Missing slots")).ToImmutableDictionary(s => s.Key,
            s => s.Value.Select(Decode).ToImmutableArray()));
    public static string Serialize(TestDocument doc) => JsonSerializer.Serialize(new DocumentData(
        doc.FormatVersion, doc.Id.ToString(), doc.Name,
        Enum.GetValues<TestPhase>().ToDictionary(p => p.ToString(), p => doc.Get(p).Select(Encode).ToArray()),
        doc.Mappings.IsDefault ? [] : doc.Mappings.Select(EncodeMapping).ToArray()),
        new JsonSerializerOptions { WriteIndented = true });
    public static TestDocument Deserialize(string json)
    {
        var data = JsonSerializer.Deserialize<DocumentData>(json) ?? throw new FormatException("Empty document");
        if (data.FormatVersion != TestDocument.CurrentFormat)
            throw new NotSupportedException($"Format {data.FormatVersion} requires explicit migration");
        if (!TestDocumentId.TryParse(data.Id, out var id)) throw new FormatException("Invalid document ID");
        if (data.Phases is null || data.Phases.Count != 3 ||
            data.Phases.Keys.Any(k => !Enum.TryParse<TestPhase>(k, out _)))
            throw new FormatException("All three stages are required");
        var phases = Enum.GetValues<TestPhase>().ToImmutableDictionary(p => p,
            p => data.Phases.TryGetValue(p.ToString(), out var nodes)
                ? nodes.Select(Decode).ToImmutableArray()
                : throw new FormatException($"Missing phase {p}"));
        var doc = new TestDocument(id, data.Name, data.FormatVersion, phases)
        {
            Mappings = (data.Mappings ?? []).Select(DecodeMapping).ToImmutableArray()
        };
        var issues = StepTreeValidator.ValidateTree(doc, StepDescriptorCatalog.Builtins());
        // Asset references may be resolved only at preparation, but structural errors block loading.
        var structural = issues.Where(x => !x.Contains("unresolved asset", StringComparison.Ordinal));
        if (structural.Any()) throw new FormatException(string.Join("; ", structural));
        return doc;
    }
}
