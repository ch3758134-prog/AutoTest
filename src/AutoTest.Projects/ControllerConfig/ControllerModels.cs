using System.Collections.Immutable;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.ControllerConfig;

// These are authored configuration facts, never measurements or physical actuator confirmations.
public enum ControlInteraction { Momentary, Latched, SpringReturn, Exclusive }
public enum MeasurementStatus { NotMeasured, Valid, OutOfRange, Invalid, Unqualified }
public enum ObservationOrigin { Manual, Mock, Replay, Physical }

/// <summary>
/// Fixed operator-like button timing. These are configuration values, not device
/// measurements. Zero hold duration means a pulse must supply a duration at the
/// test-step call site; it never authorizes an instantaneous physical operation.
/// </summary>
public sealed record ButtonActionTiming(int DelayBeforePressMs, int DefaultPressDurationMs,
    int DelayAfterReleaseMs)
{
    public static ButtonActionTiming Unspecified { get; } = new(0, 0, 0);
    public ButtonActionTiming Validate()
    {
        if (DelayBeforePressMs < 0 || DefaultPressDurationMs < 0 || DelayAfterReleaseMs < 0)
            throw new ProjectDataException("CTRL_ACTION_TIMING", "Button timing must be nonnegative.");
        return this;
    }
}

public sealed record VoltageWindow(int Channel, double? TargetVolts, double? MinimumVolts, double? MaximumVolts)
{
    public VoltageWindow Validate()
    {
        if (Channel is < 1 or > 5) throw new ProjectDataException("CTRL_CHANNEL", "Voltage channel must be in 1..5.");
        if ((TargetVolts.HasValue && !double.IsFinite(TargetVolts.Value)) ||
            (MinimumVolts.HasValue && !double.IsFinite(MinimumVolts.Value)) ||
            (MaximumVolts.HasValue && !double.IsFinite(MaximumVolts.Value)))
            throw new ProjectDataException("CTRL_VOLTAGE", "Non-finite voltage is not allowed.");
        if (MinimumVolts.HasValue != MaximumVolts.HasValue)
            throw new ProjectDataException("CTRL_WINDOW", "Both limits must be specified or neither.");
        if (MinimumVolts > MaximumVolts || (TargetVolts.HasValue && MinimumVolts.HasValue &&
            (TargetVolts < MinimumVolts || TargetVolts > MaximumVolts)))
            throw new ProjectDataException("CTRL_WINDOW", "Voltage target/limits are inconsistent.");
        return this;
    }
}

public sealed record VoltageMeasurement(Guid Id, int Channel, double ValueVolts, DateTimeOffset CapturedAt,
    ObservationOrigin Origin, MeasurementStatus Status, string EvidenceRef, string Operator,
    int ConfigurationRevision)
{
    public VoltageMeasurement Validate()
    {
        if (Id == Guid.Empty || Channel is < 1 or > 5 || !double.IsFinite(ValueVolts) ||
            ConfigurationRevision < 1 || CapturedAt == default || string.IsNullOrWhiteSpace(EvidenceRef) ||
            string.IsNullOrWhiteSpace(Operator) || !Enum.IsDefined(Origin) || !Enum.IsDefined(Status))
            throw new ProjectDataException("CTRL_MEASUREMENT", "Measurement requires valid value, source, evidence and configuration revision.");
        if (Origin == ObservationOrigin.Physical && string.IsNullOrWhiteSpace(EvidenceRef))
            throw new ProjectDataException("CTRL_EVIDENCE", "Physical measurements require a trace reference.");
        return this;
    }
}

public sealed record ControllerState(Guid Id, string Name, int Order, ControlInteraction Interaction,
    uint KeyMask, Guid? ReturnStateId, string? ExclusiveGroup,
    ImmutableArray<VoltageWindow> Windows, ImmutableArray<VoltageMeasurement> Measurements)
{
    /// <summary>Optional for backwards-compatible controller configuration files.</summary>
    public ButtonActionTiming ActionTiming { get; init; } = ButtonActionTiming.Unspecified;

    public static ControllerState Create(string name, uint mask, int order) =>
        new ControllerState(Guid.NewGuid(), name, order, ControlInteraction.Momentary, mask, null, null,
            ImmutableArray<VoltageWindow>.Empty, ImmutableArray<VoltageMeasurement>.Empty).Validate();

    public ControllerState Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Order < 0 || KeyMask > 0xFFFFF ||
            !Enum.IsDefined(Interaction) || Windows.IsDefault || Measurements.IsDefault)
            throw new ProjectDataException("CTRL_STATE", "State identity, order, mask or collection is invalid.");
        if (Windows.Any(w => w is null) || Measurements.Any(m => m is null))
            throw new ProjectDataException("CTRL_STATE", "Null channel data is invalid.");
        (ActionTiming ?? throw new ProjectDataException("CTRL_ACTION_TIMING", "Button timing is missing.")).Validate();
        foreach (var window in Windows) window.Validate();
        foreach (var measurement in Measurements) measurement.Validate();
        if (Windows.GroupBy(w => w.Channel).Any(g => g.Count() > 1))
            throw new ProjectDataException("CTRL_CHANNEL_DUPLICATE", "Voltage channel occurs more than once.");
        if (Measurements.GroupBy(m => m.Id).Any(g => g.Count() > 1))
            throw new ProjectDataException("CTRL_MEASUREMENT_DUPLICATE", "Duplicate measurement ID.");
        if (Interaction == ControlInteraction.SpringReturn && ReturnStateId is null)
            throw new ProjectDataException("CTRL_RETURN", "Spring return requires a target state.");
        if (Interaction == ControlInteraction.Exclusive && string.IsNullOrWhiteSpace(ExclusiveGroup))
            throw new ProjectDataException("CTRL_EXCLUSIVE", "Exclusive control requires a group name.");
        return this;
    }
}

public sealed record ControllerDefinition(Guid Id, string Name, int Order, string Presentation,
    Guid DefaultStateId, ImmutableArray<ControllerState> States)
{
    public ControllerDefinition Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Order < 0 || string.IsNullOrWhiteSpace(Presentation) ||
            States.IsDefault || States.Length == 0 || States.Any(s => s is null))
            throw new ProjectDataException("CTRL_CONTROLLER", "Controller requires a name, presentation and states.");
        foreach (var state in States) state.Validate();
        if (States.GroupBy(s => s.Id).Any(g => g.Count() > 1) || States.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
            States.GroupBy(s => s.Order).Any(g => g.Count() > 1))
            throw new ProjectDataException("CTRL_STATE_DUPLICATE", "State ID, name or order is duplicated.");
        if (!States.Any(s => s.Id == DefaultStateId))
            throw new ProjectDataException("CTRL_DEFAULT", "Default state does not exist.");
        foreach (var state in States)
            if (state.ReturnStateId.HasValue && !States.Any(s => s.Id == state.ReturnStateId))
                throw new ProjectDataException("CTRL_RETURN", "Return state does not exist.");
        return this;
    }
}

public sealed record EcuControlProfile(Guid ProfileId, Guid EcuId, string EcuName,
    Guid HardwareProfileId, string HardwareModel, int Revision,
    ImmutableArray<ControllerDefinition> Controllers)
{
    /// <summary>
    /// Explicit station-key resource policy for this ECU configuration. Null keeps
    /// old files readable, but prevents physical button execution until configured.
    /// </summary>
    public HardwareKeyPolicy? KeyHardwarePolicy { get; init; }

    public static EcuControlProfile Create(string ecuName, string hardwareModel, Guid hardwareProfileId) =>
        new EcuControlProfile(Guid.NewGuid(), Guid.NewGuid(), ecuName, hardwareProfileId, hardwareModel, 1,
            ImmutableArray<ControllerDefinition>.Empty).Validate();

    public EcuControlProfile Validate()
    {
        if (ProfileId == Guid.Empty || EcuId == Guid.Empty || HardwareProfileId == Guid.Empty ||
            string.IsNullOrWhiteSpace(EcuName) || string.IsNullOrWhiteSpace(HardwareModel) ||
            Revision < 1 || Controllers.IsDefault || Controllers.Any(c => c is null))
            throw new ProjectDataException("CTRL_PROFILE", "ECU or board binding is missing or invalid.");
        if (KeyHardwarePolicy is { } policy)
        {
            policy.Validate();
            if (policy.HardwareProfileId != HardwareProfileId)
                throw new ProjectDataException("CTRL_HARDWARE_STALE",
                    "Key hardware policy belongs to a different hardware profile.");
        }
        foreach (var controller in Controllers) controller.Validate();
        if (KeyHardwarePolicy is { } activePolicy)
            foreach (var state in Controllers.SelectMany(c => c.States))
                if ((state.KeyMask & ~activePolicy.SupportedMask) != 0)
                    throw new ProjectDataException("CTRL_STATE_UNSUPPORTED_KEY",
                        "Configured ECU operation uses a Key outside the available hardware policy: " + state.Name);
        if (Controllers.GroupBy(c => c.Id).Any(g => g.Count() > 1) ||
            Controllers.GroupBy(c => c.Order).Any(g => g.Count() > 1) ||
            Controllers.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new ProjectDataException("CTRL_CONTROLLER_DUPLICATE", "Controller ID, name or order duplicated.");
        return this;
    }
    public ControllerState ResolveState(Guid controllerId, Guid stateId, int expectedRevision)
    {
        if (expectedRevision != Revision)
            throw new ProjectDataException("REFERENCE_STALE", "Configuration revision changed; TEST mapping is stale.");
        var controller = Controllers.FirstOrDefault(c => c.Id == controllerId) ??
            throw new ProjectDataException("CTRL_NOT_FOUND", "Controller is missing.");
        return controller.States.FirstOrDefault(s => s.Id == stateId) ??
            throw new ProjectDataException("CTRL_STATE_NOT_FOUND", "State is missing.");
    }
    public ApprovedStateMapping ApprovedMapping(Guid controllerId, Guid stateId, int expectedRevision)
    {
        var state = ResolveState(controllerId, stateId, expectedRevision);
        return new(ProfileId, Revision, HardwareProfileId, controllerId, stateId, state.KeyMask, state.Interaction);
    }
}

// Name is a DTO role only: this is a validated *configuration*, not authorization to drive a real board.
public sealed record ApprovedStateMapping(Guid ProfileId, int Revision, Guid HardwareProfileId,
    Guid ControllerId, Guid StateId, uint KeyMask, ControlInteraction Interaction);

public sealed record StateReference(Guid ProfileId, Guid ControllerId, Guid StateId, int Revision)
{
    public ControllerState Resolve(EcuControlProfile profile)
    {
        if (ProfileId != profile.ProfileId) throw new ProjectDataException("REFERENCE_STALE", "Profile identity changed.");
        return profile.ResolveState(ControllerId, StateId, Revision);
    }
}



