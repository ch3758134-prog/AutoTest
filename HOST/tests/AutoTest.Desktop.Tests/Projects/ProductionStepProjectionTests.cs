using AutoTest.Desktop.Shell;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class ProductionStepProjectionTests
{
    [Fact]
    public void IfDisplaysActualPredicateAndThenBeforeElse()
    {
        var branch = StepNode.Create("If", "驻车状态分派")
            .WithProperty("variable", "brakeState")
            .WithProperty("operator", ">=")
            .WithProperty("equals", "1")
            .WithSlot("then", StepNode.Create("Wait", "执行成立动作")
                .WithProperty("milliseconds", "100"))
            .WithSlot("else", StepNode.Create("Wait", "执行否则动作")
                .WithProperty("milliseconds", "200"));
        var doc = TestDocument.Create("生产用例");
        var tx = new StepEditTransaction(doc);
        tx.Insert(TestPhase.Test, null, null, 0, branch);
        var displayed = Assert.Single(AutomaticTestPlanService.BuildCaseSteps(tx.Current));
        Assert.Contains("brakeState >= 1", displayed.ActionSummary);
        Assert.Equal(2, displayed.Children.Count);
        Assert.StartsWith("〔条件成立〕", displayed.Children[0].Name);
        Assert.StartsWith("〔否则〕", displayed.Children[1].Name);
    }

    [Fact]
    public void SignalAssertionsHaveReadableJudgeWithoutRuntimeClaims()
    {
        var compare = StepNode.Create("AssertSignal", "驻车电流判定")
            .WithProperty("target", "DUT.Current")
            .WithProperty("operator", "<=")
            .WithProperty("expected", "10")
            .WithProperty("timeoutMs", "2500");
        var tx = new StepEditTransaction(TestDocument.Create("运行观察"));
        tx.Insert(TestPhase.Test, null, null, 0, compare);
        var displayed = Assert.Single(AutomaticTestPlanService.BuildCaseSteps(tx.Current));
        Assert.Contains("<= 10", displayed.ActionSummary);
        Assert.Contains("2500", displayed.ActionSummary);
        Assert.Equal("未执行", displayed.State);
    }
}
