using AutoTest.Testing.Authoring;
using AutoTest.Testing.Preparation;
using Xunit;

namespace AutoTest.Testing.Tests;

public sealed class IfFlowContractTests
{
    [Fact]
    public void IfRequiresUpstreamVariableEvenWithTwoValidSlots()
    {
        var branch = StepUiCatalogNotRequired();
        var tx = new StepEditTransaction(TestDocument.Create("No value"));
        tx.Insert(TestPhase.Test, null, null, 0, branch);
        Assert.Contains(new PlanCompiler().Prepare(tx.Current).Diagnostics,
            x => x.Contains("IF 变量") && x.Contains("brakeState"));
    }

    [Fact]
    public void IfAcceptsPreparedVariableAndLegacyEqualityWithoutOperator()
    {
        var tx = new StepEditTransaction(TestDocument.Create("Prepared variable"));
        tx.Insert(TestPhase.Prepare, null, null, 0,
            StepNode.Create("SetVariable").WithProperty("variable", "brakeState")
                .WithProperty("value", "1"));
        var branch = StepUiCatalogNotRequired();
        tx.Insert(TestPhase.Test, null, null, 0, branch);
        Assert.Empty(new PlanCompiler().Prepare(tx.Current).Diagnostics);
        Assert.Contains(TestVariableFlowValidator.Before(tx.Current, branch.Id),
            x => x.Name == "brakeState");
    }

    [Fact]
    public void OnlyOneIfArmProducesVariable_IsNotGuaranteedAfterIf()
    {
        var branch = StepNode.Create("If").WithProperty("variable", "mode")
            .WithProperty("equals", "1")
            .WithSlot("then", StepNode.Create("SetVariable")
                .WithProperty("variable", "brakeState").WithProperty("value", "1"))
            .WithSlot("else");
        var tx = new StepEditTransaction(TestDocument.Create("Branch join"));
        tx.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("SetVariable").WithProperty("variable", "mode")
                .WithProperty("value", "1"));
        tx.Insert(TestPhase.Test, null, null, 1, branch);
        tx.Insert(TestPhase.Test, null, null, 2, StepUiCatalogNotRequired());
        Assert.Contains(new PlanCompiler().Prepare(tx.Current).Diagnostics,
            x => x.Contains("IF 变量") && x.Contains("brakeState"));
    }

    [Fact]
    public void InvalidIfComparatorIsRejectedButOldFileDefaultsToEquality()
    {
        var doc = TestDocument.Create("Comparator");
        var tx = new StepEditTransaction(doc);
        tx.Insert(TestPhase.Test, null, null, 0,
            StepNode.Create("SetVariable").WithProperty("variable", "brakeState")
                .WithProperty("value", "1"));
        var condition = StepUiCatalogNotRequired().WithProperty("operator", "<>");
        tx.Insert(TestPhase.Test, null, null, 1, condition);
        Assert.Contains(new PlanCompiler().Prepare(tx.Current).Diagnostics,
            x => x.Contains("IF 条件比较运算符无效"));
    }

    private static StepNode StepUiCatalogNotRequired() =>
        StepNode.Create("If", "条件判断").WithProperty("variable", "brakeState")
            .WithProperty("equals", "1").WithSlot("then").WithSlot("else");
}
