using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Contracts;
using AutoTest.Testing.Authoring;

namespace AutoTest.Testing.Preparation;

/// <summary>Deep-copy via official JSON codec to isolate edits from the executing plan.</summary>
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

    /// <summary>
    /// A non-null PROJECT resolver is required whenever any step declares a typed assetId,
    /// assetRevision and assetSha256. Unresolved resources block execution, including Mock.
    /// </summary>
    public PreparedTestPlan Prepare(TestDocument document, ISet<string>? availableAssets = null,
        IAssetResolver? assetResolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = StepTreeValidator.ValidateTree(document, _catalog, availableAssets).ToBuilder();
        // Do not traverse/serialize an in-memory reference cycle or an unbounded tree.
        // Invalid plans have no runnable fingerprint and can only be displayed in Preflight.
        if (errors.Any(x => x.Contains("reference cycle", StringComparison.Ordinal) ||
                            x.Contains("exceeds 10000", StringComparison.Ordinal) ||
                            x.Contains("depth exceeds 32", StringComparison.Ordinal)))
            return new PreparedTestPlan("", "", errors.ToImmutable());
        errors.AddRange(TestResourceBinding.Validate(document, assetResolver));
        foreach (var node in document.Walk())
            if (_catalog.Lookup(node.Type) is { ExecutableV1: false })
                errors.Add($"{node.Id}: {node.Type} not executable in V1");
        // RunCoordinator cleanup supports only safe Mock SetVariable/Wait/Sequence/Log nodes.
        void CheckCleanup(IEnumerable<StepNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Type is not ("SetVariable" or "Wait" or "Sequence" or "Log"))
                    errors.Add($"Cleanup/{node.Id}: {node.Type} cannot run in Mock cleanup");
                foreach (var children in node.Slots.Values) CheckCleanup(children);
            }
        }
        CheckCleanup(document.Get(TestPhase.Cleanup));
        var json = TestDocumentCodec.Serialize(document);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new PreparedTestPlan(hash, json, errors.Distinct(StringComparer.Ordinal).ToImmutableArray());
    }
}
