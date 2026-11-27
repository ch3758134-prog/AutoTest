using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using Xunit;

namespace AutoTest.Testing.Tests;

/// <summary>
/// Pure authoring/compile checks. These tests neither fake a CAN observation
/// nor authorize physical ECU commands.
/// </summary>
public sealed class SwitchFlowContractTests
{
    [Fact]
    public void SwitchRequiresGuaranteedUpstreamVariable()
    {
        var doc = Document(Switch(Case("1", Wait())));
        Assert.Contains(new PlanCompiler().Prepare(doc).Diagnostics,
            x => x.Contains("SWITCH 变量") && x.Contains("mode"));
    }

    [Fact]
    public void SwitchWithUniqueCasesAndKnownVariableCompiles()
    {
        var doc = Document(Mode(), Switch(Case("1", Wait()), Case("2", Wait())));
        Assert.Empty(new PlanCompiler().Prepare(doc).Diagnostics);
    }

    [Fact]
    public void EmptySwitchDoesNotCompileAsSuccessfulNoOp()
    {
        var doc = Document(Mode(), Switch());
        Assert.Contains(new PlanCompiler().Prepare(doc).Diagnostics,
            x => x.Contains("SWITCH 没有启用的 CASE"));
    }

    [Fact]
    public void DuplicateCaseAndDuplicateDefaultAreRejected()
    {
        var duplicate = Document(Mode(), Switch(Case("1", Wait()), Case("1", Wait())));
        Assert.Contains(new PlanCompiler().Prepare(duplicate).Diagnostics,
            x => x.Contains("重复 CASE"));
        var defaults = Document(Mode(), Switch(
            Case("default", Wait()), Case("DEFAULT", Wait())));
        Assert.Contains(new PlanCompiler().Prepare(defaults).Diagnostics,
            x => x.Contains("重复 CASE"));
    }

    [Fact]
    public void CaseValueMustBeLiteralAndCannotBeAtTopLevel()
    {
        var dynamicCase = Document(Mode(), Switch(Case("$anotherVariable", Wait())));
        Assert.Contains(new PlanCompiler().Prepare(dynamicCase).Diagnostics,
            x => x.Contains("固定字面值"));
        var outside = Document(Case("1", Wait()));
        Assert.Contains(new PlanCompiler().Prepare(outside).Diagnostics,
            x => x.Contains("Case outside Switch"));
    }

    [Fact]
    public void VariableOnlyAssignedInOneCaseIsNotAvailableAfterSwitch()
    {
        var doc = Document(Mode(), Switch(
            Case("1", AssignResult()), Case("2", Wait())),
            RequiresResult());
        Assert.Contains(new PlanCompiler().Prepare(doc).Diagnostics,
            x => x.Contains("IF 变量") && x.Contains("result"));
    }

    [Fact]
    public void WithoutDefaultUnmatchedPathCannotProduceGuaranteedVariable()
    {
        var doc = Document(Mode(), Switch(Case("1", AssignResult())),
            RequiresResult());
        Assert.Contains(new PlanCompiler().Prepare(doc).Diagnostics,
            x => x.Contains("IF 变量") && x.Contains("result"));
    }

    [Fact]
    public void EveryCaseIncludingDefaultCanProduceDownstreamVariable()
    {
        var doc = Document(Mode(), Switch(
            Case("1", AssignResult()), Case("default", AssignResult())),
            RequiresResult());
        Assert.Empty(new PlanCompiler().Prepare(doc).Diagnostics);
    }

    private static TestDocument Document(params StepNode[] nodes)
    {
        var tx = new StepEditTransaction(TestDocument.Create("Switch flow contract"));
        for (var i = 0; i < nodes.Length; ++i)
            tx.Insert(TestPhase.Test, null, null, i, nodes[i]);
        return tx.Current;
    }

    private static StepNode Mode() => StepNode.Create("SetVariable")
        .WithProperty("variable", "mode").WithProperty("value", "1");

    private static StepNode AssignResult() => StepNode.Create("SetVariable")
        .WithProperty("variable", "result").WithProperty("value", "1");

    private static StepNode Wait() => StepNode.Create("Wait")
        .WithProperty("milliseconds", "10");

    private static StepNode Case(string label, params StepNode[] children) =>
        StepNode.Create("Case").WithProperty("equals", label)
            .WithSlot("body", children);

    private static StepNode Switch(params StepNode[] arms) =>
        StepNode.Create("Switch").WithProperty("variable", "mode")
            .WithSlot("cases", arms);

    private static StepNode RequiresResult() => StepNode.Create("If")
        .WithProperty("variable", "result").WithProperty("operator", "==")
        .WithProperty("equals", "1")
        .WithSlot("then", Wait()).WithSlot("else", Wait());
}
