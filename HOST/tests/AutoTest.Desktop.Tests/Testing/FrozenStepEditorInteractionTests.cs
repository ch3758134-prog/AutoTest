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

        editor.Undo(); // Cancel must undo insertion, not create a second Delete.
        Assert.Empty(editor.Current.Get(TestPhase.Test));
        Assert.True(editor.CanRedo);
        Assert.Equal(TestDocumentCodec.Serialize(original),
            TestDocumentCodec.Serialize(editor.Current));
        editor.Redo(); // The caller must still mark the restored node provisional.
        Assert.Equal(draft.Id, Assert.Single(editor.Current.Get(TestPhase.Test)).Id);
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
    public void InlineQuickEditIsOptInAndCanRestoreOriginalDraftWithoutMutation()
    {
        var phase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        var wait = StepUiCatalog.NewNode("Wait");
        var row = new StepRowProjection(new StepTreeItem(StepTreeItemKind.Step,
            TestPhase.Test, wait.Name, phase, wait), 0);
        Assert.True(row.IsQuickEditable);
        Assert.True(row.ShowQuickSummary);
        Assert.False(row.ShowInlineEditor);
        row.IsInlineEditing = true;
        Assert.True(row.ShowInlineEditor);
        row.QuickValue = "invalid";
        row.CancelQuickEdit();
        Assert.Equal(row.OriginalQuickValue, row.QuickValue);
        Assert.True(row.ShowQuickSummary);
        Assert.False(row.ShowInlineEditor);
    }

    [Fact]
    public void PaletteAvoidsSecondCanSendActionButStillCanEditLegacyCanSend()
    {
        var choices = TestStepInspector.PaletteActions(false);
        Assert.Single(choices.Where(option => option.Key == "can"));
        Assert.DoesNotContain(choices, option => option.Key == "can-send");
        Assert.Contains(choices, option => option.Key == "can-wait");
        Assert.Contains(choices, option => option.Key == "key" && option.Title == "按键动作");
    }

    [Fact]
    public void CanSendStepSummaryShowsTargetPeriodAndStopCondition()
    {
        var phase = new StepTreeItem(StepTreeItemKind.Phase, TestPhase.Test, "测试");
        var can = StepNode.Create("Write", "车辆环境")
            .WithProperty("target", "DBC.VCU_1")
            .WithProperty("value", "Mode=1;State=2")
            .WithProperty("periodMs", "65")
            .WithProperty("durationMs", "5000");
        var row = new StepRowProjection(new StepTreeItem(StepTreeItemKind.Step,
            TestPhase.Test, can.Name, phase, can), 0);
        Assert.Contains("DBC.VCU_1", row.Summary);
        Assert.Contains("65 ms", row.Summary);
        Assert.Contains("5000 ms", row.Summary);
        var raw = can.WithProperty("value", "HEX:0102030405060708")
            .WithProperty("periodMs", "0").WithProperty("durationMs", "0");
        var rawRow = new StepRowProjection(new StepTreeItem(StepTreeItemKind.Step,
            TestPhase.Test, raw.Name, phase, raw), 0);
        Assert.Contains("DBC.VCU_1", rawRow.Summary);
        Assert.Contains("原始数据", rawRow.Summary);
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
