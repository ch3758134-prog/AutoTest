using System.Collections.Immutable;
using AutoTest.Core;
using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;

namespace AutoTest.Application.TestAuthoring;

/// <summary>
/// Application use cases for the single canonical TEST authoring model.
/// Desktop views request actions and validation outcomes; they do not decide
/// which TEST node can be created or which runtime mode may compile it.
/// </summary>
public sealed class TestAuthoringUseCases
{
    private readonly StepDescriptorCatalog _catalog;
    private readonly PlanCompiler _compiler;

    public TestAuthoringUseCases()
        : this(StepDescriptorCatalog.Builtins(), new PlanCompiler())
    {
    }

    public TestAuthoringUseCases(StepDescriptorCatalog catalog, PlanCompiler compiler)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    }

    public IReadOnlyList<StepActionDefinition> AvailableActions => _catalog.Actions;

    public StepNode CreateStepDraft(string actionKey) =>
        _catalog.CreateNodeForAction(actionKey);

    public bool SupportsEditing(string stepType) =>
        _catalog.Lookup(stepType) is not null;

    public IReadOnlyList<TestVariableFlowValidator.AvailableVariable> AvailableVariables(
        TestDocument document, StepId step) =>
        TestVariableFlowValidator.Before(document, step);

    public ImmutableArray<string> ValidateEditableDocument(TestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return StepTreeValidator.ValidateTree(document, _catalog,
            allowIncompleteSteps: true);
    }

    public PreparedTestPlan PrepareForExecution(TestDocument document,
        IReadOnlyCollection<string>? inheritedVariables = null) =>
        _compiler.Prepare(document, allowRealOperations: true,
            inheritedVariables: inheritedVariables);
}