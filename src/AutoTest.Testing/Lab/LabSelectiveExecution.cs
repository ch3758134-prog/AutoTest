using System.Collections.Immutable;

namespace AutoTest.Testing.Lab;

/// <summary>
/// Test case = a top-level TEST group, not a CAN/UDS/IGN action step.
/// Legacy programs remain loadable: their ungrouped TEST steps are exposed as
/// one selectable compatibility case (using the program ID as its stable key).
/// </summary>
public sealed record LabTestCaseDefinition(Guid Id, string Name, int StepCount, bool IsLegacy);

/// <summary>
/// The selection belongs only to a particular Run, never to a published TEST.
/// The selected plan has its own SHA/digest, and the source program SHA remains
/// available for traceability. Production/barcode runs use the full plan.
/// </summary>
public sealed record LabSelectiveRunPlan(
    LabPreparedPlan Plan,
    string SourceProgramDigest,
    ImmutableArray<Guid> SelectedCaseIds);

public static class LabSelectiveExecution
{
    public const string TestCaseType = "TestCase";

    public static ImmutableArray<LabTestCaseDefinition> Discover(LabDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var testNodes = source.Phases[LabPhase.Test];
        var builder = ImmutableArray.CreateBuilder<LabTestCaseDefinition>();
        var ungrouped = new List<LabNode>();
        foreach (var node in testNodes)
        {
            if (node.Type == TestCaseType)
            {
                if (node.Id == source.Id)
                    throw new InvalidOperationException("TESTCASE_ID_CONFLICTS_WITH_PROGRAM_ID");
                builder.Add(new LabTestCaseDefinition(node.Id, node.Name,
                    LabDocument.Walk(node.Slots.GetValueOrDefault("body", new())).Count(), false));
            }
            else if (node.Type != "Subroutine")
            {
                ungrouped.Add(node);
            }
        }

        if (ungrouped.Count != 0)
            builder.Add(new LabTestCaseDefinition(source.Id,
                builder.Count == 0 ? "原测试程序（全部测试步骤）" : "未分组的测试步骤",
                LabDocument.Walk(ungrouped).Count(), true));
        return builder.ToImmutable();
    }

    /// <summary>
    /// Fail closed: validate the *entire* published program before selecting,
    /// then compile a detached snapshot with only the selected TEST cases.
    /// Prepare and Cleanup remain unchanged even for a single selected case.
    /// Top-level subroutine definitions stay accessible to selected cases.
    /// </summary>
    public static LabSelectiveRunPlan Prepare(
        LabDocument source, IEnumerable<Guid> selectedCaseIds, LabPlanCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selectedCaseIds);
        ArgumentNullException.ThrowIfNull(compiler);
        var fullPlan = compiler.Prepare(source);
        if (!fullPlan.Ready)
            throw new InvalidOperationException(
                "TEST_PROGRAM_INVALID: " + string.Join("; ", fullPlan.Diagnostics));
        var selection = selectedCaseIds.ToImmutableHashSet();
        var available = Discover(source);
        if (selection.IsEmpty)
            throw new InvalidOperationException("NO_TEST_CASE_SELECTED");
        var availableIds = available.Select(x => x.Id).ToImmutableHashSet();
        if (selection.Any(x => !availableIds.Contains(x)))
            throw new InvalidOperationException("UNKNOWN_TEST_CASE_SELECTION");
        var selected = available.Where(x => selection.Contains(x.Id)).Select(x => x.Id).ToImmutableArray();

        var detached = source.Copy();
        detached.Phases[LabPhase.Test] = detached.Phases[LabPhase.Test]
            .Where(node => node.Type switch
            {
                "Subroutine" => true,
                TestCaseType => selection.Contains(node.Id),
                _ => selection.Contains(source.Id)
            })
            .ToList();

        var plan = compiler.Prepare(detached);
        if (!plan.Ready)
            throw new InvalidOperationException(
                "SELECTED_TEST_CASE_INVALID: " + string.Join("; ", plan.Diagnostics));
        return new LabSelectiveRunPlan(plan, fullPlan.Digest, selected);
    }
}
