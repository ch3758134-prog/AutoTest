using AutoTest.Testing.Authoring;

namespace AutoTest.Testing.Lab;

/// <summary>
/// Non-lossy bridge from the authoritative PROJECT TestDocument to the richer
/// LAB executor. Never creates a second independent test-program identity.
/// Unsupported node types fail explicitly rather than being dropped or simulated.
/// </summary>
public static class ProjectTestProgramAdapter
{
    public static LabDocument ToLabDocument(TestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FormatVersion != TestDocument.CurrentFormat ||
            document.Id.Value == Guid.Empty)
            throw new InvalidDataException("PROJECT_TEST_DOCUMENT_INVALID");
        var supported = LabPlanCompiler.ExecutableNodes.ToHashSet(StringComparer.Ordinal);
        LabNode Convert(StepNode step)
        {
            if (!supported.Contains(step.Type))
                throw new NotSupportedException(
                    $"PROJECT_TEST_NODE_UNSUPPORTED: {step.Type} / {step.Id.Value:D}");
            var parameters = step.Properties.ToDictionary(x => x.Key, x => x.Value,
                StringComparer.Ordinal);
            var slots = step.Slots.ToDictionary(x => x.Key,
                x => x.Value.Select(Convert).ToList(), StringComparer.Ordinal);
            return new LabNode(step.Id.Value, step.Type, step.Name, parameters, slots);
        }
        var phases = Enum.GetValues<LabPhase>().ToDictionary(phase => phase, phase =>
            document.Get(phase switch
            {
                LabPhase.Prepare => TestPhase.Prepare,
                LabPhase.Test => TestPhase.Test,
                LabPhase.Cleanup => TestPhase.Cleanup,
                _ => throw new InvalidDataException("UNKNOWN_TEST_PHASE")
            }).Select(Convert).ToList());
        return LabDocumentCodec.Open(LabDocumentCodec.Save(new LabDocument(
            document.Id.Value, LabDocument.CurrentFormat, document.Name, phases)));
    }

    public static LabPreparedPlan Prepare(TestDocument document,
        ILabControllerCatalog? controllers = null, ILabAssetCatalog? assets = null) =>
        new LabPlanCompiler(controllers, assets).Prepare(ToLabDocument(document));
}
