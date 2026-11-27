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
    public PreparedTestPlan Prepare(TestDocument document, ISet<string>? availableAssets = null,
        bool allowRealOperations = false,
        IReadOnlyCollection<string>? inheritedVariables = null)
    {
        var errors = StepTreeValidator.ValidateTree(document, _catalog, availableAssets).ToBuilder();
        // A branch must not authorize downstream physical operations using
        // a variable that no preceding execution path could have created.
        errors.AddRange(TestVariableFlowValidator.Validate(document, inheritedVariables));
        try { _ = document.ReportSettings.Validate(); }
        catch (FormatException e) { errors.Add("Test report: " + e.Message); }
        void CheckEnabledNodes(IEnumerable<StepNode> steps)
        {
            foreach (var node in steps)
            {
                if (!StepExecutionPolicy.IsEnabled(node)) continue;
                try { _ = StepResultRecordingOptions.Read(node); }
                catch (FormatException e) { errors.Add(node.Id + ": " + e.Message); }
                var descriptor = _catalog.Lookup(node.Type);
                if (descriptor is null ||
                    descriptor.Execution == StepExecutionCapability.Unsupported ||
                    descriptor.Execution == StepExecutionCapability.Hardware && !allowRealOperations)
                    errors.Add($"{node.Id}: {node.Type} is not available in this execution mode.");
                if (node.Type == "If" &&
                    (!node.Slots.TryGetValue("then", out var thenArm) ||
                     !thenArm.Any(StepExecutionPolicy.IsEnabled)) &&
                    (!node.Slots.TryGetValue("else", out var elseArm) ||
                     !elseArm.Any(StepExecutionPolicy.IsEnabled)))
                    errors.Add(node.Id + ": IF 的 THEN/ELSE 均无启用步骤；请先添加分支动作或删除无效 IF。");
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
