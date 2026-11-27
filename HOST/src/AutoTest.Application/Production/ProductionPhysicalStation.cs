using AutoTest.Core;

namespace AutoTest.Application.Production;

/// <summary>
/// A real, firmware-verified adapter for one station. The production
/// composition root must not register a UI bool, GPIO ACK, timer, or fake
/// hardware as this port. The current Desktop does not implement it.
/// </summary>
public interface IProductionPhysicalStation
{
    /// <summary>
    /// Request station-owned fixture press and return independently confirmed
    /// mechanical closure and safety-interlock evidence. No FLASH/TEST here.
    /// </summary>
    ValueTask<FixtureAdmissionEvidence> PressAndVerifyAsync(
        ProductionJobSnapshot job, CancellationToken ct);

    /// <summary>
    /// Observe a debounced, fresh real START edge on the same controller
    /// session, after verified fixture closure.
    /// </summary>
    ValueTask<PhysicalStartEvidence> WaitForStartAsync(
        ProductionJobSnapshot job, FixtureAdmissionEvidence fixture,
        CancellationToken ct);
}

public sealed record FixtureAdmissionEvidence(
    RunId Run, StationId Station, bool MechanicalClosureVerified,
    bool SafetyInterlockVerified, string EvidenceReference);

public sealed record PhysicalStartEvidence(
    RunId Run, StationId Station, bool FreshPhysicalEdgeVerified,
    string EvidenceReference);
