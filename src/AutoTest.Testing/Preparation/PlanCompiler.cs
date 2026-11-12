using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Testing.Authoring;

namespace AutoTest.Testing.Preparation;

/// <summary>Deep-copy via JSON codec to isolate edits from the executing plan.</summary>
public sealed record PreparedTestPlan(string Fingerprint, string CanonicalJson,
    ImmutableArray<string> Diagnostics)
{
    public TestDocument RestoreDocument() => TestDocumentCodec.Deserialize(CanonicalJson);
    public bool ValidateFingerprint() => string.Equals(Fingerprint,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson))).ToLowerInvariant(),
        StringComparison.Ordinal);
}

public sealed class PlanCompiler
{
    private readonly StepDescriptorCatalog _catalog;
    public PlanCompiler(StepDescriptorCatalog? catalog = null) =>
        _catalog = catalog ?? StepDescriptorCatalog.Builtins();
    private static readonly HashSet<string> RealTypes = new(StringComparer.Ordinal)
    {
        "Write", "Read", "WaitUntil", "Calculate", "Expect",
        "WaveStart", "WaveStop", "WaveAnalyze"
    };

    public PreparedTestPlan Prepare(TestDocument document, ISet<string>? availableAssets = null,
        bool allowRealOperations = false)
    {
        var errors = StepTreeValidator.ValidateTree(document, _catalog, availableAssets).ToBuilder();
        foreach (var node in document.Walk())
            if (_catalog.Lookup(node.Type) is { ExecutableV1: false } &&
                !(allowRealOperations && RealTypes.Contains(node.Type)))
                errors.Add($"{node.Id}: {node.Type} not executable in V1");
        var json = TestDocumentCodec.Serialize(document);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new PreparedTestPlan(hash, json, errors.ToImmutable());
    }
}
