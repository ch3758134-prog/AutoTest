using AutoTest.Core;
using AutoTest.Desktop.Testing;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Testing;

public sealed class TestEditorInteractionStateTests
{
    [Fact]
    public void StableStepAndBranchIdentitySurviveTreeRebuild()
    {
        var phase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        var step = StepNode.Create("If", "条件").WithSlot("then").WithSlot("else");
        var original = new StepTreeItem(StepTreeItemKind.Step, TestPhase.Test, "IF",
            phase, step, index: 0);
        phase.Children.Add(original);
        var then = new StepTreeItem(StepTreeItemKind.Slot, TestPhase.Test, "THEN",
            original, slot: "then");
        original.Children.Add(then);
        var key = StepSelectionKey.Of(then)!.Value;

        var recreatedPhase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        var recreatedStep = new StepTreeItem(StepTreeItemKind.Step, TestPhase.Test, "IF",
            recreatedPhase, step, index: 0);
        recreatedPhase.Children.Add(recreatedStep);
        var recreatedThen = new StepTreeItem(StepTreeItemKind.Slot, TestPhase.Test, "THEN",
            recreatedStep, slot: "then");
        recreatedStep.Children.Add(recreatedThen);

        Assert.Same(recreatedThen, key.FindIn([recreatedPhase]));
    }

    [Fact]
    public void ValidationFailureDoesNotMoveSelection()
    {
        var state = new TestEditorInteractionState();
        var first = new StepSelectionKey(TestPhase.Prepare, StepTreeItemKind.Phase);
        var next = new StepSelectionKey(TestPhase.Test, StepTreeItemKind.Phase);
        Assert.True(state.TryNavigate(first, () => true));
        Assert.False(state.TryNavigate(next, () => false));
        Assert.Equal(first, state.Selection);
    }

    [Fact]
    public void OlderInspectorLoadCannotReplaceNewerChoice()
    {
        var state = new TestEditorInteractionState();
        var old = state.NextInspectorRequest();
        var current = state.NextInspectorRequest();
        Assert.False(state.IsCurrentInspectorRequest(old));
        Assert.True(state.IsCurrentInspectorRequest(current));
    }

    [Fact]
    public void CollapsedStateSurvivesProjectionRefresh()
    {
        var phase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        var state = new TestEditorInteractionState();
        state.ToggleCollapsed(phase);
        Assert.True(state.IsCollapsed(new StepTreeItem(
            StepTreeItemKind.Phase, TestPhase.Test, "测试")));
    }
}
