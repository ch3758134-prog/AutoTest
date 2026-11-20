using System.IO;
namespace AutoTest.TestEditorPreview;

/// <summary>
/// Startup invariants for the synthetic UI fixture. These are NOT hardware, business
/// execution or Windows visual regression tests. They catch corrupted test content
/// before a reviewer mistakes it for a meaningful editing example.
/// </summary>
public static class PreviewSelfCheck
{
    public static void Verify(EditorDocument doc)
    {
        if (doc.Cases.Count != 12)
            throw new InvalidDataException("设计样本必须包含 12 个用例。");
        var c = doc.Cases[2];
        if (c.CountSteps != 40 || c.Phases[0].Children.Count != 6 ||
            c.Phases[1].Children.Count < 10 || c.Phases[2].Children.Count != 5)
            throw new InvalidDataException("TEST 设计样本 40 步/三阶段结构与 V02-02 不一致。");
        var all = c.Phases.SelectMany(Walk).ToArray();
        if (all.Select(n => n.Id).Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new InvalidDataException("存在重复的 UI 节点身份。");
        foreach (var phase in c.Phases)
            foreach (var node in Walk(phase))
                foreach (var child in node.Children)
                    if (!ReferenceEquals(child.Parent, node))
                        throw new InvalidDataException("样本有错误的结构父子关系。");
        var issues = all.SelectMany(n => n.Fields).Count(f => f.IsInvalid);
        if (issues != 3)
            throw new InvalidDataException("V02-02 样本应该恰有 3 个可定位字段问题，而不是 " + issues + " 个。");
        if (c.RunSelected)
            throw new InvalidDataException("当前编辑用例不能因此被强制勾选本次运行。");
        var expected = new[] { "T09", "T18", "T29" };
        var actual = all.Where(n => n.LocalIssues > 0).Select(n => n.Code).Order().ToArray();
        if (!expected.SequenceEqual(actual))
            throw new InvalidDataException("设计样本预期的问题步骤 T09/T18/T29 不一致。");
        var roundTrip = EditorDocument.Load(doc.Serialize());
        if (roundTrip.Cases[2].CountSteps != 40 ||
            roundTrip.Cases[2].Find(doc.ActiveNodeId) is null ||
            roundTrip.Cases[2].Phases.SelectMany(Walk).SelectMany(n => n.Fields).Count(f => f.IsInvalid) != 3)
            throw new InvalidDataException("UI JSON 草稿无法保留树结构、当前对象或无效输入。");
        var original = all.First(n => n.Code == "T07");
        var copy = original.CloneWithFreshIds();
        if (copy.Id == original.Id || copy.Children.Count != original.Children.Count ||
            copy.Children[0].Id == original.Children[0].Id)
            throw new InvalidDataException("复制流程子树没有保留结构或更新 UI 身份。");
    }
    private static IEnumerable<EditorNode> Walk(EditorNode root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var nested in Walk(child))
                yield return nested;
    }
}
