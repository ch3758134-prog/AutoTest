using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Testing.Authoring;

public static class StepTreeValidator
{
    public static ImmutableArray<string> ValidateTree(TestDocument document,
        StepDescriptorCatalog catalog, ISet<string>? availableAssets = null,
        bool allowIncompleteSteps = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalog);
        _ = availableAssets; // Resource closure is validated by the project application use case.

        var errors = ImmutableArray.CreateBuilder<string>();
        errors.AddRange(TestTargetMappings.Validate(document.Mappings));
        if (document.FormatVersion != TestDocument.CurrentFormat)
            errors.Add("TEST 文档版本不受支持，必须使用当前格式重新创建。");
        if (document.Id.Value == Guid.Empty) errors.Add("TEST 文档标识无效。");

        var ids = new HashSet<StepId>();
        var active = new HashSet<StepNode>(ReferenceEqualityComparer.Instance);

        void Visit(StepNode node, string location, string? parent = null)
        {
            if (!active.Add(node))
            {
                errors.Add($"{location}: 步骤树存在循环引用。");
                return;
            }

            if (node.Id.Value == Guid.Empty || !ids.Add(node.Id))
                errors.Add($"{location}: 步骤标识为空或重复。");
            if (string.IsNullOrWhiteSpace(node.Name))
                errors.Add($"{location}: 步骤名称不能为空。");

            var enabled = StepExecutionPolicy.IsEnabled(node);
            if (node.Properties.TryGetValue("enabled", out var enabledText) &&
                !bool.TryParse(enabledText, out _))
                errors.Add($"{location}: 步骤执行状态只能是 true 或 false。");
            if (node.Properties.TryGetValue("editor.breakpoint", out var breakpointText) &&
                !bool.TryParse(breakpointText, out _))
                errors.Add($"{location}: 断点状态只能是 true 或 false。");

            var descriptor = catalog.Lookup(node.Type);
            if (descriptor is null)
            {
                errors.Add($"{location}: 未知 TEST 步骤类型 {node.Type}。");
            }
            else
            {
                foreach (var slot in descriptor.RequiredSlots)
                    if (!node.Slots.ContainsKey(slot))
                        errors.Add($"{location}: 缺少必要分支 {slot}。");
                foreach (var slot in node.Slots.Keys)
                    if (!descriptor.RequiredSlots.Contains(slot, StringComparer.Ordinal))
                        errors.Add($"{location}: 存在不支持的分支 {slot}。");

                // Disabled steps may be incomplete, but they are not exempt
                // from schema, value-type, choice or structural validation.
                errors.AddRange(catalog.ValidateParameters(node, location,
                    allowIncompleteSteps || !enabled));

                var allowed = descriptor.Parameters.Select(x => x.Key)
                    .ToHashSet(StringComparer.Ordinal);
                allowed.Add("enabled");
                allowed.Add("editor.breakpoint");
                foreach (var key in node.Properties.Keys)
                    if (!allowed.Contains(key))
                        errors.Add($"{location}: 未定义的步骤参数 {key}。");
            }

            if (enabled && !allowIncompleteSteps &&
                node.Type is "Write" or "Read" or "WaitUntil" or "AssertSignal" or "WaveStart")
            {
                var targetName = node.Properties.GetValueOrDefault("target", "");
                var mapping = document.Mappings.FirstOrDefault(x =>
                    string.Equals(x.Name, targetName, StringComparison.OrdinalIgnoreCase));
                if (mapping is null)
                    errors.Add($"{location}: 操作对象未绑定到当前工程资源。");
                else
                {
                    var expectedKind = node.Type switch
                    {
                        "Read" or "WaitUntil" or "AssertSignal" or "WaveStart"
                            => TestTargetKind.Signal,
                        _ => (TestTargetKind?)null
                    };
                    if (expectedKind is { } expected && mapping.Kind != expected)
                        errors.Add($"{location}: 当前步骤必须绑定 {expected} 类型的资源。");

                    if (node.Type == "Write")
                    {
                        if (mapping.Kind is not (TestTargetKind.RawCan or TestTargetKind.CanMessage or
                            TestTargetKind.RawKey or TestTargetKind.Button or TestTargetKind.Ignition))
                            errors.Add($"{location}: 此操作对象不支持真实硬件写入。");
                        if (mapping.Kind == TestTargetKind.CanMessage &&
                            mapping.Role != AutoTest.Contracts.BusRole.DutCan)
                            errors.Add($"{location}: DBC 报文必须绑定 ECU CAN。");

                        var periodValid = int.TryParse(
                            node.Properties.GetValueOrDefault("periodMs", "0"),
                            System.Globalization.CultureInfo.InvariantCulture, out var period);
                        var durationValid = int.TryParse(
                            node.Properties.GetValueOrDefault("durationMs", "0"),
                            System.Globalization.CultureInfo.InvariantCulture, out var duration);
                        if (periodValid && durationValid)
                        {
                            if (period > 0 && mapping.Kind is not
                                (TestTargetKind.RawCan or TestTargetKind.CanMessage))
                                errors.Add($"{location}: 只有 ECU CAN 报文允许周期发送。");
                            if (period == 0 && duration > 0)
                                errors.Add($"{location}: 设置周期持续时间前必须先设置 CAN 周期。");
                        }
                    }
                }
            }

            if (node.Type == "Case" && parent != "Switch")
                errors.Add($"{location}: CASE 只能位于 SWITCH 分支中。");

            if (node.Type == "Switch")
            {
                var cases = node.Slots.TryGetValue("cases", out var switchCases)
                    ? switchCases.Where(StepExecutionPolicy.IsEnabled).ToArray()
                    : [];
                if (enabled && !allowIncompleteSteps && cases.Length == 0)
                    errors.Add($"{location}: SWITCH 至少需要一个有效 CASE。");
                if (cases.Length > 64)
                    errors.Add($"{location}: SWITCH 最多允许 64 个 CASE。");

                foreach (var group in cases.GroupBy(
                    x => x.Properties.GetValueOrDefault("equals", "").Trim(),
                    StringComparer.OrdinalIgnoreCase))
                {
                    if (group.Count() > 1)
                        errors.Add($"{location}: SWITCH 存在重复 CASE 值 {group.Key}。");
                    if (group.Key.StartsWith("$", StringComparison.Ordinal))
                        errors.Add($"{location}: CASE 匹配值必须是固定值，不能引用运行时变量。");
                }
            }

            foreach (var slot in node.Slots)
            {
                if (node.Type == "Switch" && slot.Key == "cases" &&
                    slot.Value.Any(child => child.Type != "Case"))
                    errors.Add($"{location}: SWITCH 的 CASE 列表只能包含 CASE 节点。");
                foreach (var child in slot.Value)
                    Visit(child, $"{location}/{slot.Key}/{child.Id}", node.Type);
            }

            active.Remove(node);
        }

        foreach (var phase in Enum.GetValues<TestPhase>())
            foreach (var node in document.Get(phase))
                Visit(node, $"{phase}/{node.Id}");

        return errors.ToImmutable();
    }
}