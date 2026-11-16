using AutoTest.Desktop.Testing;
using AutoTest.Testing.Authoring;
using Xunit;

namespace AutoTest.Desktop.Tests.Testing;

public sealed class FrozenStepEditorInteractionTests
{
    [Fact]
    public void ChoosingStepTypeCreatesUndoableTransactionAndCancelReturnsDocumentToOriginal()
    {
        var original = TestDocument.Create("连续编辑");
        var editor = new StepEditTransaction(original);
        var phase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        var target = StepInsertionTarget.Resolve(phase, StepInsertionMode.Append);
        var draft = StepUiCatalog.NewNode("Wait");
        target.ValidateNode(draft);
        editor.Insert(target.Phase, target.ParentId, target.Slot, target.Index, draft);
        Assert.Single(editor.Current.Get(TestPhase.Test));
        Assert.True(editor.CanUndo);

        var visual = new StepRowProjection(new StepTreeItem(StepTreeItemKind.Step,
            TestPhase.Test, "等待", phase, draft), 0, newDraft: true);
        Assert.True(visual.IsNewDraft);
        Assert.Equal("待完善", visual.State);
        Assert.Equal("500 ms", visual.Summary);

        editor.Delete(draft.Id); // Cancel removes the only provisional STEP.
        Assert.Empty(editor.Current.Get(TestPhase.Test));
        Assert.Equal(TestDocumentCodec.Serialize(original),
            TestDocumentCodec.Serialize(editor.Current));
    }

    [Fact]
    public void SearchMarksMatchWithoutRemovingOtherSteps()
    {
        var phase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        var first = StepNode.Create("Wait", "等待驻车").WithProperty("milliseconds", "500");
        var second = StepNode.Create("Read", "读取释放电流")
            .WithProperty("variable", "motorCurrent");
        var left = new StepRowProjection(new StepTreeItem(StepTreeItemKind.Step,
            TestPhase.Test, first.Name, phase, first), 0, search: "驻车");
        var right = new StepRowProjection(new StepTreeItem(StepTreeItemKind.Step,
            TestPhase.Test, second.Name, phase, second), 0, search: "驻车");
        Assert.True(left.IsSearchMatch);
        Assert.False(right.IsSearchMatch);
        Assert.Equal("500 ms", left.Summary);
    }

    [Fact]
    public void SwitchCasePickerOffersOnlyCaseAndOrdinaryPickerExcludesIt()
    {
        Assert.All(TestStepInspector.PaletteActions(true),
            option => Assert.Equal("Case", option.StepType));
        Assert.DoesNotContain(TestStepInspector.PaletteActions(false),
            option => option.StepType == "Case");
    }

    [Fact]
    public void KnownStepsHaveConsistentTypedIconsWithoutFakeRunVerdicts()
    {
        var phase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        StepRowProjection Row(string type) => new(
            new StepTreeItem(StepTreeItemKind.Step, TestPhase.Test, type,
                phase, StepUiCatalog.NewNode(type)), 0);
        Assert.Equal("Flow", Row("Loop").Category);
        Assert.Equal("Uds", Row("UdsRequest").Category);
        Assert.Equal("Wave", Row("WaveAnalyze").Category);
        Assert.Equal("执行未接线", Row("UdsRequest").State);
    }
}
