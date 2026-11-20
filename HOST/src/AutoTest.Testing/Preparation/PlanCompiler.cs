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
        "Write", "Read", "WaitUntil", "AssertSignal", "CanStopPeriodic", "Calculate", "Expect",
        "WaveStart", "WaveStop", "WaveAnalyze", "Flash"
    };

    public PreparedTestPlan Prepare(TestDocument document, ISet<string>? availableAssets = null,
        bool allowRealOperations = false)
    {
        var errors = StepTreeValidator.ValidateTree(document, _catalog, availableAssets).ToBuilder();
        void CheckEnabledNodes(IEnumerable<StepNode> steps)
        {
            foreach (var node in steps)
            {
                if (!StepExecutionPolicy.IsEnabled(node)) continue;
                if (_catalog.Lookup(node.Type) is { ExecutableV1: false } &&
                    !(allowRealOperations && RealTypes.Contains(node.Type)))
                    errors.Add($"{node.Id}: {node.Type} not executable in V1");
                foreach (var slot in node.Slots.Values) CheckEnabledNodes(slot);
            }
        }
        foreach (var phase in Enum.GetValues<TestPhase>())
            CheckEnabledNodes(document.Get(phase));
        var json = TestDocumentCodec.Serialize(document);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new PreparedTestPlan(hash, json, errors.ToImmutable());
    }
}
