namespace AutoTest.TestHiFi;

/// <summary>
/// Checks the design fixture, not ECU execution. This is deliberately narrow:
/// a real Windows build and a screenshot remain mandatory before UX acceptance.
/// </summary>
internal static class PreviewInvariants
{
    public static void Assert(IReadOnlyCollection<DemoCase> cases,
                              IReadOnlyCollection<DemoStep> steps,
                              IReadOnlyCollection<DemoIssue> issues)
    {
        if (cases.Count != 12) throw new InvalidOperationException("视觉样本用例数量不是 12。");
        if (cases.All(c => c.Id != "TC-003"))
            throw new InvalidOperationException("缺少高保真默认用例 TC-003。");
        if (steps.Count(s => s.Phase == "准备") != 6 ||
            steps.Count(s => s.Phase == "测试") != 29 ||
            steps.Count(s => s.Phase == "清理") != 5)
            throw new InvalidOperationException("三阶段设计样本数量应为 6 / 29 / 5。");
        if (steps.Where(s => s.Phase == "测试").Select(s => s.Number).Distinct().Count() != 29)
            throw new InvalidOperationException("测试阶段步骤编号不唯一。");
        if (issues.Count != 3 || issues.Count(i => i.Severity == "错误") != 2 ||
            issues.Count(i => i.Severity == "警告") != 1)
            throw new InvalidOperationException("视觉稿三条问题状态与档案不一致。");
        foreach (var issue in issues)
            if (!steps.Any(s => s.Phase == "测试" && s.Number == issue.StepNumber))
                throw new InvalidOperationException("问题不能定位到对应研究步骤。");
    }
}
