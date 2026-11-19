using AutoTest.Core;
using AutoTest.Desktop.Testing;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Testing;

public sealed class StepInsertionTargetTests
{
    [Fact]
    public void AfterStep_UsesItsSiblingCoordinatesNotChildCoordinates()
    {
        var phase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        var node = StepNode.Create("Wait", "等待");
        var child = new StepTreeItem(StepTreeItemKind.Step, TestPhase.Test,
            "等待", phase, node, index: 0);
        phase.Children.Add(child);

        var insertion = StepInsertionTarget.Resolve(child, StepInsertionMode.After);
        Assert.Equal(TestPhase.Test, insertion.Phase);
        Assert.Null(insertion.ParentId);
        Assert.Null(insertion.Slot);
        Assert.Equal(1, insertion.Index);
        Assert.False(insertion.RequiresCase);
    }

    [Fact]
    public void IfThenAndElse_AreDifferentExplicitInsertionTargets()
    {
        var ifNode = StepNode.Create("If", "条件").WithSlot("then").WithSlot("else");
        var phase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        var conditional = new StepTreeItem(StepTreeItemKind.Step, TestPhase.Test,
            "条件", phase, ifNode, index: 0);
        phase.Children.Add(conditional);
        var then = new StepTreeItem(StepTreeItemKind.Slot, TestPhase.Test,
            "THEN", conditional, slot: "then");
        var otherwise = new StepTreeItem(StepTreeItemKind.Slot, TestPhase.Test,
            "ELSE", conditional, slot: "else");
        conditional.Children.Add(then);
        conditional.Children.Add(otherwise);

        Assert.False(StepInsertionTarget.CanAppend(conditional));
        Assert.Throws<InvalidOperationException>(() =>
            StepInsertionTarget.Resolve(conditional, StepInsertionMode.Append));
        var left = StepInsertionTarget.Resolve(then, StepInsertionMode.Append);
        var right = StepInsertionTarget.Resolve(otherwise, StepInsertionMode.Append);
        Assert.Equal(ifNode.Id, left.ParentId);
        Assert.Equal("then", left.Slot);
        Assert.Equal(ifNode.Id, right.ParentId);
        Assert.Equal("else", right.Slot);
        Assert.Equal(0, left.Index);
        Assert.Equal(0, right.Index);
    }

    [Fact]
    public void SwitchCases_RejectsNormalStepsAndCaseOutsideSwitch()
    {
        var switchNode = StepNode.Create("Switch", "分派").WithSlot("cases");
        var conditional = new StepTreeItem(StepTreeItemKind.Step, TestPhase.Test,
            "SWITCH", step: switchNode);
        var cases = new StepTreeItem(StepTreeItemKind.Slot, TestPhase.Test,
            "CASE", conditional, slot: "cases");
        conditional.Children.Add(cases);
        var place = StepInsertionTarget.Resolve(cases, StepInsertionMode.Append);
        Assert.True(place.RequiresCase);
        Assert.Equal(switchNode.Id, place.ParentId);
        Assert.Throws<ArgumentException>(() => place.ValidateNode(
            StepNode.Create("Wait", "普通等待")));
        place.ValidateNode(StepNode.Create("Case", "条件分支"));

        var phase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        var root = StepInsertionTarget.Resolve(phase, StepInsertionMode.Append);
        Assert.Throws<ArgumentException>(() => root.ValidateNode(
            StepNode.Create("Case", "孤立 CASE")));
    }
}
