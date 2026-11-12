using System.Collections.Immutable;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.ControllerConfig;

/// <summary>
/// A test action is an operation on a configured ECU input state, not a raw 20-key
/// hardware primitive. The station adapter alone may convert this plan to a real
/// CAN0/KEY_SIM command after validating the active hardware session.
/// </summary>
public enum ButtonActuation { Pulse, Hold, Release }

public sealed record ButtonActionRequest(StateReference State, ButtonActuation Actuation,
    ButtonActionTiming? TimingOverride = null);

/// <summary>
/// Pure, physical-side-effect-free plan. For Pulse, MaskAfterOperation restores the
/// active combinations from before the request; for Hold it remains active until
/// a later explicit Release. Other active configurations are never cleared blindly.
/// </summary>
public sealed record ButtonActionPlan(ButtonActuation Actuation, uint MaskBefore,
    uint MaskDuringOperation, uint MaskAfterOperation,
    ButtonActionTiming Timing, ImmutableArray<StateReference> ActiveAfterOperation);

public static class ButtonActionPlanner
{
    public static ButtonActionPlan Resolve(EcuControlProfile profile, HardwareKeyPolicy hardware,
        IEnumerable<StateReference> currentlyActive, ButtonActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(currentlyActive);
        ArgumentNullException.ThrowIfNull(request);
        if (request.State is null || !Enum.IsDefined(request.Actuation))
            throw new ProjectDataException("CTRL_ACTION", "Invalid configured button operation.");
        profile.Validate();
        hardware.Validate();
        var target = request.State.Resolve(profile);
        var timing = (request.TimingOverride ?? target.ActionTiming).Validate();
        if (request.Actuation == ButtonActuation.Pulse && timing.DefaultPressDurationMs == 0)
            throw new ProjectDataException("CTRL_ACTION_TIMING",
                "A timed press must specify a nonzero duration in the configuration or test step.");

        // Distinct functional states may share a physical analog output. The
        // hardware policy, not a fixed count of functional buttons, governs
        // which resistor combinations may be held simultaneously.
        var previous = currentlyActive.Distinct().ToImmutableArray();
        var beforeMask = hardware.ValidateCombinedMask(profile, previous);
        ImmutableArray<StateReference> during;
        ImmutableArray<StateReference> after;

        if (request.Actuation == ButtonActuation.Release)
        {
            during = previous.Where(r => r != request.State).ToImmutableArray();
            after = during;
        }
        else
        {
            during = previous.Contains(request.State)
                ? previous
                : previous.Add(request.State);
            after = request.Actuation == ButtonActuation.Pulse ? previous : during;
        }

        var duringMask = hardware.ValidateCombinedMask(profile, during);
        var afterMask = hardware.ValidateCombinedMask(profile, after);
        // Release does not accidentally incur the pre-press human reaction delay;
        // a held action does not yet incur post-release delay.
        var effectiveTiming = request.Actuation switch
        {
            ButtonActuation.Release => new ButtonActionTiming(0, 0, timing.DelayAfterReleaseMs),
            ButtonActuation.Hold => new ButtonActionTiming(timing.DelayBeforePressMs, 0, 0),
            _ => timing
        };
        return new ButtonActionPlan(request.Actuation, beforeMask, duringMask,
            afterMask, effectiveTiming, after);
    }
}
