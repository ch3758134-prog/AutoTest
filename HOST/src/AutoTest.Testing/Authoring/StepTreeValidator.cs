using System.Collections.Immutable;
using AutoTest.Core;

namespace AutoTest.Testing.Authoring;

public static class StepTreeValidator
{
    public static ImmutableArray<string> ValidateTree(TestDocument document,
        StepDescriptorCatalog catalog, ISet<string>? availableAssets = null,
        bool allowIncompleteSteps = false)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        errors.AddRange(TestTargetMappings.Validate(document.Mappings));
        if (document.FormatVersion != TestDocument.CurrentFormat) errors.Add("Unsupported format version");
        if (document.Id.Value == Guid.Empty) errors.Add("Invalid document identity");
        var ids = new HashSet<StepId>();
        var active = new HashSet<StepNode>(ReferenceEqualityComparer.Instance);
        void Visit(StepNode n, string location, string? parent = null)
        {
            if (!active.Add(n)) { errors.Add($"{location}: reference cycle"); return; }
            if (n.Id.Value == Guid.Empty || !ids.Add(n.Id)) errors.Add($"{location}: empty/duplicate StepId");
            // Disabled steps remain editable and are excluded from execution;
            // re-enabling subjects them to the same formal TEST validation.
            if (!StepExecutionPolicy.IsEnabled(n))
            {
                active.Remove(n);
                return;
            }
            var d = catalog.Lookup(n.Type);
            if (d is null) errors.Add($"{location}: unknown node {n.Type}");
            else
            {
                foreach (var slot in d.RequiredSlots)
                    if (!n.Slots.ContainsKey(slot)) errors.Add($"{location}: missing slot {slot}");
                foreach (var slot in n.Slots.Keys)
                    if (!d.RequiredSlots.Contains(slot)) errors.Add($"{location}: illegal slot {slot}");
                errors.AddRange(catalog.ValidateParameters(n, location, allowIncompleteSteps));
            }
            // CallSubroutine.target is a TEST subroutine identity, not an asset mapping.
            // Only physical/signal operations use TestTargetMapping.
            if (!allowIncompleteSteps &&
                n.Type is "Write" or "Read" or "WaitUntil" or "AssertSignal" or "WaveStart" &&
                n.Properties.TryGetValue("target", out var targetName) &&
                !document.Mappings.Any(m => string.Equals(m.Name, targetName,
                    StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{location}: unmapped target {targetName}");
            if (!allowIncompleteSteps && n.Type == "Write")
            {
                var writeTargetName = n.Properties.GetValueOrDefault("target", "");
                var mapping = document.Mappings.FirstOrDefault(m =>
                    string.Equals(m.Name, writeTargetName, StringComparison.OrdinalIgnoreCase));
                if (mapping is not null)
                {
                    if (mapping.Kind is not (TestTargetKind.RawCan or TestTargetKind.CanMessage or
                        TestTargetKind.RawKey or TestTargetKind.Button or TestTargetKind.Ignition))
                        errors.Add($"{location}: Write 目标类型 {mapping.Kind} 不支持真实硬件操作");
                    if (mapping.Kind == TestTargetKind.CanMessage &&
                        mapping.Role != AutoTest.Contracts.BusRole.DutCan)
                        errors.Add($"{location}: DBC 发送报文必须绑定 ECU CAN");
                }
                int period = 0;
                if (n.Properties.TryGetValue("periodMs", out var periodText) &&
                    (!int.TryParse(periodText, out period) || period != 0 && period is < 20 or > 600000))
                    errors.Add($"{location}: CAN 周期必须是 0 或 20～600000ms");
                if (n.Properties.TryGetValue("durationMs", out var durationText) &&
                    (!int.TryParse(durationText, out var duration) || duration is < 0 or > 1800000))
                    errors.Add($"{location}: 周期持续时间必须是 0～1800000ms");
                if (period > 0 && mapping is not null &&
                    mapping.Kind is not (TestTargetKind.RawCan or TestTargetKind.CanMessage))
                    errors.Add($"{location}: 非 CAN 步骤不能设置 CAN 周期");
            }
            if (!allowIncompleteSteps && n.Type is "WaitUntil" or "Read")
            {
                var readTargetName = n.Properties.GetValueOrDefault("target", "");
                var mapping = document.Mappings.FirstOrDefault(m =>
                    string.Equals(m.Name, readTargetName, StringComparison.OrdinalIgnoreCase));
                if (mapping is not null && mapping.Kind != TestTargetKind.Signal)
                    errors.Add($"{location}: 信号读取/等待必须绑定 DBC Signal");
                if (!int.TryParse(n.Properties.GetValueOrDefault("timeoutMs", ""), out var timeout) ||
                    timeout is < 1 or > 600000)
                    errors.Add($"{location}: 信号等待超时必须是 1～600000ms");
                if (n.Type == "Read")
                {
                    if (n.Properties.GetValueOrDefault("mode", "") is not ("Next" or "Latest" or
                        "next" or "latest"))
                        errors.Add($"{location}: Read 模式只能为 Latest 或 Next");
                    if (!int.TryParse(n.Properties.GetValueOrDefault("maxAgeMs", ""), out var age) ||
                        age is < 0 or > 600000)
                        errors.Add($"{location}: Read 最大数据年龄必须是 0～600000ms");
                }
            }
            if (!allowIncompleteSteps && n.Type == "If" &&
                n.Properties.TryGetValue("operator", out var ifComparator) &&
                ifComparator is not ("==" or "!=" or "<" or "<=" or ">" or ">="))
                errors.Add($"{location}: IF 条件比较运算符无效");
            if (!allowIncompleteSteps && n.Type is "WaitUntil" or "Expect")
            {
                if (n.Properties.GetValueOrDefault("operator", "") is not
                    ("==" or "!=" or "<" or "<=" or ">" or ">="))
                    errors.Add($"{location}: 比较运算符无效");
            }
            if (!allowIncompleteSteps && n.Type == "Calculate" &&
                n.Properties.GetValueOrDefault("operator", "") is not
                    ("+" or "-" or "*" or "/"))
                errors.Add($"{location}: 算术运算符无效");
            if (!allowIncompleteSteps && n.Type == "AssertSignal")
            {
                var signalTarget = n.Properties.GetValueOrDefault("target", "");
                var mapping = document.Mappings.FirstOrDefault(m =>
                    string.Equals(m.Name, signalTarget, StringComparison.OrdinalIgnoreCase));
                if (mapping is not null && mapping.Kind != TestTargetKind.Signal)
                    errors.Add($"{location}: CAN 信号判定必须选择有效的 DBC 信号。");
                var comparator = n.Properties.GetValueOrDefault("operator", "");
                if (comparator is not ("==" or "!=" or "<" or "<=" or ">" or ">="))
                    errors.Add($"{location}: CAN 信号判定的比较方式无效。");
                if (!int.TryParse(n.Properties.GetValueOrDefault("timeoutMs", ""), out var timeout) ||
                    timeout is < 1 or > 600000)
                    errors.Add($"{location}: CAN 信号判定超时应在 1～600000 毫秒内。");
            }
            if (!allowIncompleteSteps && n.Type is ("WaveAnalyze" or "WaveCompare") &&
                string.IsNullOrWhiteSpace(n.Properties.GetValueOrDefault("profile", "")))
                errors.Add($"{location}: wave analysis requires an explicitly reviewed and mapped WAVE profile");
            if (!allowIncompleteSteps && n.Type is ("WaveAnalyze" or "WaveCompare") &&
                n.Properties.TryGetValue("profile", out var profileName) &&
                !string.IsNullOrWhiteSpace(profileName) &&
                !document.Mappings.Any(m => m.Kind == TestTargetKind.WaveProfile &&
                    string.Equals(m.Name, profileName, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{location}: unmapped wave analysis profile {profileName}");
            // Wave criteria are edited and versioned in the WAVE profile,
            // not duplicated in free-form STEP properties. The actual profile
            // is checked against immutable project resources before execution.
            if (!allowIncompleteSteps && n.Type == "Switch")
            {
                var cases = n.Slots.TryGetValue("cases", out var switchCases)
                    ? switchCases.Where(StepExecutionPolicy.IsEnabled).ToArray()
                    : [];
                if (cases.Length == 0)
                    errors.Add($"{location}: SWITCH 没有启用的 CASE；请添加至少一个匹配分支。");
                if (cases.Length > 64)
                    errors.Add($"{location}: SWITCH 最多允许 64 个 CASE。");
                foreach (var group in cases.GroupBy(
                             x => x.Properties.GetValueOrDefault("equals", "").Trim(),
                             StringComparer.OrdinalIgnoreCase))
                {
                    if (group.Count() > 1)
                        errors.Add($"{location}: SWITCH 存在重复 CASE：{group.Key}；每个值只能命中一个分支。");
                    if (group.Key.StartsWith("$", StringComparison.Ordinal))
                        errors.Add($"{location}: CASE 比较值必须是固定字面值，不能使用未冻结的变量引用。");
                }
            }
            if (n.Type == "Case" && parent != "Switch") errors.Add($"{location}: Case outside Switch");
            if (!allowIncompleteSteps && n.Type == "Loop" && (!n.Properties.TryGetValue("count", out var count) ||
                !int.TryParse(count, out var loops) || loops < 0 || loops > 1000))
                errors.Add($"{location}: Loop count outside 0..1000");
            if (!allowIncompleteSteps && n.Type == "Wait" && (!n.Properties.TryGetValue("milliseconds", out var ms) ||
                !int.TryParse(ms, out var delay) || delay < 0 || delay > 60000))
                errors.Add($"{location}: Wait outside 0..60000ms");
            if (!allowIncompleteSteps && n.Properties.TryGetValue("asset", out var asset) &&
                (availableAssets is null || !availableAssets.Contains(asset)))
                errors.Add($"{location}: unresolved asset {asset}");
            foreach (var slot in n.Slots)
            {
                if (n.Type == "Switch" && slot.Key == "cases" && slot.Value.Any(x => x.Type != "Case"))
                    errors.Add($"{location}: Switch cases must contain Case nodes");
                foreach (var child in slot.Value) Visit(child, $"{location}/{slot.Key}/{child.Id}", n.Type);
            }
            active.Remove(n);
        }
        var enabledFlash = TestRunStepScope.EnabledSteps(document)
            .Where(n => n.Type == "Flash").ToArray();
        if (enabledFlash.Length > 1)
            errors.Add("首版测试用例只允许一条 Flash 步骤，防止重复擦写。");
        var topLevelTestSteps = document.Get(TestPhase.Test).Select(n => n.Id).ToHashSet();
        foreach (var flash in enabledFlash)
        {
            if (!topLevelTestSteps.Contains(flash.Id))
                errors.Add($"{flash.Id}: Flash 仅能在测试阶段顶层运行，禁止置于循环、分支或清理。");
            if (flash.Properties.ContainsKey("flashConsent") ||
                flash.Properties.ContainsKey("typedConsent"))
                errors.Add($"{flash.Id}: 实体 FLASH 授权不得保存在 TEST 文件。");
        }
        foreach (var phase in Enum.GetValues<TestPhase>())
            foreach (var n in document.Get(phase)) Visit(n, $"{phase}/{n.Id}");
        return errors.ToImmutable();
    }
}
