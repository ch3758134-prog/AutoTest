using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTest.Desktop.Bench;

// An engineering action is an ordered physical KEY_SIM instruction program.
// SetKeys publishes the COMPLETE twenty-switch state; Hold does not touch hardware.
// Neither action names nor a "neutral"/"released" switch mask are hardcoded.
public enum BenchActionStepKind { SetKeys, Hold }

public sealed record BenchActionStep(BenchActionStepKind Kind, uint Mask, int DurationMs)
{
    public static BenchActionStep SetKeys(uint mask) => new(BenchActionStepKind.SetKeys, mask, 0);
    public static BenchActionStep Hold(int milliseconds) => new(BenchActionStepKind.Hold, 0, milliseconds);
}

public sealed record BenchKeyAction(string Id, string Name, IReadOnlyList<BenchActionStep> Steps);

public sealed record BenchActionProgress(int StepNumber, int StepCount, string Description);
public sealed record BenchActionExecution(bool Completed, bool Cancelled,
    bool OutputKnown, string Detail);

public static class BenchActionValidation
{
    public const uint MaximumKeyMask = 0xFFFFF;
    public const int MaximumHoldMilliseconds = 600000;
    public const int MaximumSteps = 256;

    public static void Validate(BenchKeyAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!Guid.TryParse(action.Id, out var id) || id == Guid.Empty)
            throw new ArgumentException("动作 ID 无效。");
        if (string.IsNullOrWhiteSpace(action.Name) || action.Name.Trim().Length > 100)
            throw new ArgumentException("动作名称必须为 1～100 个字符。");
        if (action.Steps is null || action.Steps.Count is < 1 or > MaximumSteps)
            throw new ArgumentException("动作必须包含 1～256 个步骤。");
        if (action.Steps[0].Kind != BenchActionStepKind.SetKeys)
            throw new ArgumentException("动作必须以设置完整 Key 状态开始，不能先保持未知状态。");
        foreach (var step in action.Steps)
        {
            if (step is null) throw new ArgumentException("动作步骤不能为空。");
            switch (step.Kind)
            {
                case BenchActionStepKind.SetKeys when step.Mask <= MaximumKeyMask && step.DurationMs == 0:
                    break; // Zero is a valid, explicit all-switches-open request.
                case BenchActionStepKind.Hold when step.Mask == 0 &&
                    step.DurationMs is >= 1 and <= MaximumHoldMilliseconds:
                    break;
                default:
                    throw new ArgumentException("步骤无效：Key 必须为完整 20-bit Mask，保持须为 1～600000 ms。");
            }
        }
    }

    public static void ValidateAll(IReadOnlyList<BenchKeyAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count > 500) throw new ArgumentException("工程动作数量超过 500。");
        foreach (var action in actions) Validate(action);
        if (actions.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
            actions.GroupBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new ArgumentException("工程中存在重复的动作 ID 或动作名称。");
    }
}
