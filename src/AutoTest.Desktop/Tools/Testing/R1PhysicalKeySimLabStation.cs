using System.IO;
using AutoTest.Application.V1;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Devices.Diagnostics;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Testing.Lab;

namespace AutoTest.Desktop.Tools.Testing;

/// <summary>Actual P05 controller profile resolver for the one R1 TEST model.</summary>
public sealed class R1KeySimControllerCatalog(Func<EcuControlProfile?> current)
    : ILabControllerCatalog
{
    public LabControllerResolution Resolve(LabControllerRef reference)
    {
        var profile = current();
        if (profile is null ||
            !Guid.TryParse(reference.ControllerId, out var controller) ||
            !Guid.TryParse(reference.StateId, out var state) ||
            !int.TryParse(reference.Revision, out var revision))
            return new(false, profile?.Revision.ToString() ?? "not loaded");
        try
        {
            _ = profile.ResolveState(controller, state, revision);
            return new(true, profile.Revision.ToString());
        }
        catch { return new(false, profile.Revision.ToString()); }
    }
}

/// <summary>
/// A commanded held key must have a release on a guaranteed Cleanup path.
/// Release hidden inside IF/CASE/WHEN is not guaranteed to execute.
/// Only unconditional top-level steps or Sequence bodies qualify.
/// This is a software recovery policy, not a demand for analog readback.
/// </summary>
public static class R1KeySimCleanupPolicy
{
    public static bool HasUnconditionalRelease(LabDocument document, EcuControlProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profile);
        if (!document.Phases.TryGetValue(LabPhase.Cleanup, out var cleanup))
            return false;
        return WalkGuaranteed(cleanup).Any(n =>
        {
            if (n.Type != "ControllerState" ||
                !Guid.TryParse(n.Parameters.GetValueOrDefault("controllerId"), out var controller) ||
                !Guid.TryParse(n.Parameters.GetValueOrDefault("stateId"), out var state) ||
                !int.TryParse(n.Parameters.GetValueOrDefault("revision"), out var revision))
                return false;
            try { return profile.ResolveState(controller, state, revision).KeyMask == 0; }
            catch { return false; }
        });
    }

    private static IEnumerable<LabNode> WalkGuaranteed(IEnumerable<LabNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Type == "ControllerState") yield return node;
            if (node.Type == "Sequence" &&
                node.Slots.TryGetValue("body", out var body))
                foreach (var child in WalkGuaranteed(body)) yield return child;
        }
    }
}

/// <summary>
/// Physical R1 test station: real CAN1 signal observations + exactly
/// pre-approved KEY_SIM 20-bit masks on CAN0. Per-run approval covers the
/// immutable TEST plan; the device still grants only one USB CAN_SEND at a time.
/// An ACK is the main-controller transport outcome, not switch/voltage readback.
/// </summary>
public sealed class R1PhysicalKeySimLabStation : IAuthorizedPhysicalControllerLabStation
{
    private readonly R2PhysicalCanReadOnlyLabStation _signals;
    private readonly HardwareBenchController _hardware;
    private readonly EcuControlProfile _profile;
    private readonly Func<bool> _profileStillCurrent;
    private readonly HashSet<LabControllerRef> _approved;
    private readonly long _generation;

    public R1PhysicalKeySimLabStation(V1Workspace workspace,
        HardwareBenchController hardware, EcuControlProfile savedProfile,
        Func<bool> profileStillCurrent, IEnumerable<LabControllerRef> approvedBindings,
        bool operatorApproved)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _profile = savedProfile ?? throw new ArgumentNullException(nameof(savedProfile));
        _profileStillCurrent = profileStillCurrent ??
            throw new ArgumentNullException(nameof(profileStillCurrent));
        if (!operatorApproved)
            throw new UnauthorizedAccessException("KEYSIM_TEST_RUN_NOT_APPROVED");
        _profile.Validate();
        _approved = approvedBindings.ToHashSet();
        if (_approved.Count == 0)
            throw new InvalidOperationException("KEYSIM_TEST_HAS_NO_APPROVED_STEPS");
        foreach (var binding in _approved) _ = Resolve(binding);
        _signals = new R2PhysicalCanReadOnlyLabStation(workspace, hardware, true);
        _generation = _signals.SessionGeneration;
        if (!ControllerActionsAuthorized)
            throw new IOException("KEYSIM_CONTROLLER_PROFILE_OR_SESSION_STALE");
    }

    public long SessionGeneration => _generation;
    public bool IsReadOnlyAuthorized => _signals.IsReadOnlyAuthorized;
    public bool ControllerActionsAuthorized => IsReadOnlyAuthorized &&
        _hardware.Generation == _generation &&
        _profileStillCurrent();

    private uint Resolve(LabControllerRef binding)
    {
        if (!Guid.TryParse(binding.ControllerId, out var controller) ||
            !Guid.TryParse(binding.StateId, out var state) ||
            !int.TryParse(binding.Revision, out var revision))
            throw new InvalidOperationException("KEYSIM_CONTROLLER_REFERENCE_INVALID");
        return _profile.ResolveState(controller, state, revision).KeyMask;
    }

    public ValueTask<LabObservation> ObserveAsync(string name, Guid runId,
        LabSource source, CancellationToken cancellation) =>
        _signals.ObserveAsync(name, runId, source, cancellation);

    public async ValueTask<LabActionReceipt> SetControllerAsync(
        LabControllerRef binding, Guid runId, LabSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (source != LabSource.Physical || !ControllerActionsAuthorized ||
            !_approved.Contains(binding))
            return new(false, "KEYSIM_PHYSICAL_STEP_NOT_AUTHORIZED");
        uint mask;
        try { mask = Resolve(binding); }
        catch (Exception ex) { return new(false, "KEYSIM_REFERENCE_STALE: " + ex.Message); }
        if (mask > KeySimCanCodec.MaxMask)
            return new(false, "KEYSIM_MASK_OUT_OF_RANGE");
        var sent = await _hardware.SetPhysicalKeySimMaskAsync(mask,
            HardwareBenchController.PhysicalKeySimConfirmation(mask, _generation), ct);
        if (sent.Outcome != Outcome.Accepted ||
            sent.Value is not { Ack: AutoTest.Contracts.AckKind.DeviceAcknowledged })
            return new(false, sent.FailureCode ?? "KEYSIM_CAN0_ACK_UNKNOWN",
                sent.OperationId.ToString("D"));
        // Real TX fact in the DEVICE JSONL trace has this operation identifier.
        // ECU signal assertions remain distinct subsequent TEST steps.
        return new(true, null, sent.OperationId.ToString("D"));
    }
}


/// <summary>
/// Deterministic authorization preflight for actual KEY_SIM operations.
/// Conditional releases are not accepted as a recovery guarantee.
/// </summary>
public static class R1KeySimCleanupPolicy
{
    public static void RequireUnconditionalRelease(
        LabDocument program, EcuControlProfile profile,
        IEnumerable<LabControllerRef> approvedBindings)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(approvedBindings);
        uint MaskRef(LabControllerRef reference)
        {
            if (!Guid.TryParse(reference.ControllerId, out var controller) ||
                !Guid.TryParse(reference.StateId, out var state) ||
                !int.TryParse(reference.Revision, out var revision))
                throw new InvalidOperationException("KEYSIM_CONTROLLER_REFERENCE_INVALID");
            return profile.ResolveState(controller, state, revision).KeyMask;
        }
        uint? NodeMask(LabNode node)
        {
            if (node.Type != "ControllerState") return null;
            return MaskRef(new LabControllerRef(
                node.Parameters.GetValueOrDefault("controllerId") ?? "",
                node.Parameters.GetValueOrDefault("stateId") ?? "",
                node.Parameters.GetValueOrDefault("revision") ?? ""));
        }
        if (!approvedBindings.Any(x => MaskRef(x) != 0)) return;
        var cleanup = program.Phases[LabPhase.Cleanup];
        if (cleanup.Count == 0 || NodeMask(cleanup[0]) != 0)
            throw new InvalidOperationException(
                "KEYSIM_CLEANUP_FIRST_RELEASE_REQUIRED: Cleanup 首步必须为无条件掩码0的 ControllerState。");
        if (cleanup.Skip(1)
            .SelectMany(node => LabDocument.Walk(new[] { node }))
            .Select(NodeMask).Any(mask => mask.HasValue && mask.Value != 0))
            throw new InvalidOperationException(
                "KEYSIM_CLEANUP_REACTIVATION_FORBIDDEN: Cleanup 全部释放后不可重新激活按键。");
    }
}
