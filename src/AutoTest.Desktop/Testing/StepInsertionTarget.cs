using AutoTest.Core;
using AutoTest.Testing.Authoring;

namespace AutoTest.Desktop.Testing;

/// <summary>
/// Immutable position captured before the right-side Inspector opens.
/// Never infer insertion coordinates from a different/updated selection on Apply.
/// The Switch.cases slot only accepts Case nodes.
/// </summary>
public enum StepInsertionMode { Append, After }

public sealed record StepInsertionTarget(
    TestPhase Phase, StepId? ParentId, string? Slot, int Index, bool RequiresCase)
{
    public static bool CanAppend(StepTreeItem? node) =>
        node?.Kind is StepTreeItemKind.Phase or StepTreeItemKind.Slot ||
        node is { Kind: StepTreeItemKind.Step, Children.Count: 1 };

    public static StepInsertionTarget Resolve(StepTreeItem node, StepInsertionMode mode)
    {
        if (mode == StepInsertionMode.After && node.Kind != StepTreeItemKind.Step)
            throw new InvalidOperationException("只有已存在的步骤可以在下方插入。");
        StepTreeItem coordinateNode = node;
        if (mode == StepInsertionMode.Append && node.Kind == StepTreeItemKind.Step)
        {
            if (node.Children.Count == 0)
                throw new InvalidOperationException("该步骤没有子分支；请选择在下方插入。");
            if (node.Children.Count != 1)
                throw new InvalidOperationException("该步骤有多个子分支，请明确选择 THEN、ELSE、CASE 等目标分支。");
            coordinateNode = node.Children[0];
        }
        var (parent, slot, index) = mode == StepInsertionMode.After
            ? coordinateNode.CoordinatesForSibling()
            : coordinateNode.CoordinatesForAppend();
        var caseOnly = slot == "cases";
        return new StepInsertionTarget(node.Phase, parent, slot, index, caseOnly);
    }

    public void ValidateNode(StepNode node)
    {
        if (RequiresCase && node.Type != "Case")
            throw new ArgumentException("SWITCH 的 CASE 区域只能添加 CASE 分支。");
        if (!RequiresCase && node.Type == "Case")
            throw new ArgumentException("CASE 只能在 SWITCH 的 CASE 区域中插入。");
    }
}
