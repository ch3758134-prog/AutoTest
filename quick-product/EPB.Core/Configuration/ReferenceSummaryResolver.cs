namespace EPB.Core.Configuration;

/// <summary>把被引用对象转换成可直接显示在步骤卡片上的摘要。</summary>
public static class ReferenceSummaryResolver
{
    public static string Resolve(ConfigurationSequence sequence)
    {
        var items = sequence.Steps
            .Select(ResolveStep)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        return items.Length == 0 ? "空流程" : string.Join("；", items);
    }

    private static string ResolveStep(ConfigurationStep step)
    {
        if (step.Kind == ConfigurationStepKind.组合)
        {
            var children = step.Children.Select(ResolveStep).Where(x => !string.IsNullOrWhiteSpace(x));
            return string.Join("；", children);
        }

        return $"{step.Name}：{step.Summary}";
    }
}
