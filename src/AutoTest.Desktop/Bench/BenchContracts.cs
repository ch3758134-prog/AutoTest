using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTest.Desktop.Bench;

// UI-local port: INTEGRATION maps this onto the ONE shared Project/DEVICE/RECORD composition.
// It is NOT a new USB protocol, device session, or public Contracts DTO.
public enum BenchEvidenceSource { Unknown, Mock, Replay, Physical }
public enum BenchInteraction { Latching, Momentary, ReturnToDefault }
public enum BenchConnection { Disconnected, Discovering, Connecting, Connected, Faulted }

public sealed record BenchDevice(string Identity, string DisplayName);
public sealed record BenchStateDefinition(string StateId, string Name, uint KeyMask,
    BenchInteraction Interaction, string? Hint);
public sealed record BenchControllerDefinition(string ControllerId, string Name,
    IReadOnlyList<BenchStateDefinition> States);
public sealed record BenchConfiguration(string ProjectName, string EcuProfileName,
    string Revision, IReadOnlyList<BenchControllerDefinition> Controllers);

public sealed record BenchConnectionSnapshot(BenchConnection State, string? DeviceIdentity,
    long? SessionGeneration, string? FailureStage, int? NativeErrorCode, string? Message);
public sealed record BenchOperationResult(bool Accepted, string? OperationId, string? CorrelationId,
    long? SessionGeneration, string? RawRequest, string? RawResponse,
    string? TraceReference, string? ErrorCode, string? ErrorMessage,
    BenchEvidenceSource Source, string? AckDescription);
public sealed record BenchManualVoltage(int Channel, decimal? Volts, string? Operator,
    DateTimeOffset? MeasuredAt, string? EvidenceReference);
public sealed record BenchManualMeasurement(string ProjectRevision, string ControllerId,
    string StateId, string? OperationId, BenchEvidenceSource Source, IReadOnlyList<BenchManualVoltage> Voltages,
    string? Comment);

public interface IBenchWorkbenchPort
{
    // No transport is created here. These methods MUST delegate to the existing shared session.
    Task<BenchConfiguration?> LoadConfigurationAsync(CancellationToken cancellationToken);
    Task<BenchConnectionSnapshot> GetConnectionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BenchDevice>> DiscoverCompatibleDevicesAsync(CancellationToken cancellationToken);
    Task<BenchConnectionSnapshot> ConnectAsync(string identity, CancellationToken cancellationToken);
    Task<BenchConnectionSnapshot> DisconnectAsync(CancellationToken cancellationToken);
    // The backend is the final authority. Physical outputs must be denied by default there.
    Task<BenchOperationResult> ApplyStateAsync(string revision, string controllerId,
        string stateId, BenchEvidenceSource source, CancellationToken cancellationToken);
    // Explicit safety recovery action: real CAN0 0x730 all-keys-release,
    // not a Mock operation. Existing test fixture ports fail closed.
    Task<BenchOperationResult> ReleaseAllPhysicalKeysAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BenchOperationResult(false, null, null, null, null, null, null,
            "PHYSICAL_RELEASE_UNSUPPORTED", "No physical release service connected.",
            BenchEvidenceSource.Physical, null));
    // Independent physical 20-bit resistor-switch mask. Does not require
    // importing a Controller Profile or pretending the five output voltages
    // are automatically measured.
    Task<BenchOperationResult> ApplyPhysicalKeyMaskAsync(uint mask,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BenchOperationResult(false, null, null, null, null,
            null, null, "DIRECT_KEYSIM_UNSUPPORTED",
            "The connected bench adapter does not support physical KEY_SIM masks.",
            BenchEvidenceSource.Physical, null));

    Task<BenchOperationResult> SetIgnAsync(bool on,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BenchOperationResult(false, null, null, null,
            null, null, null, "IGN_PHYSICAL_UNAVAILABLE",
            "Actual IGN output command has not been enabled on this bench adapter.",
            BenchEvidenceSource.Physical, null));

    Task<BenchOperationResult> SetSolenoidAsync(byte action,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BenchOperationResult(false, null, null, null,
            null, null, null, "SOL_PHYSICAL_UNAVAILABLE",
            "No physical SET_SOL controller is connected to this bench port.",
            BenchEvidenceSource.Physical, null));

    Task<BenchOperationResult> ReadSolenoidAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BenchOperationResult(false, null, null, null,
            null, null, null, "SOL_READ_UNAVAILABLE",
            "No physical GET_SOL controller is connected to this bench port.",
            BenchEvidenceSource.Physical, null));

    Task SaveManualMeasurementAsync(BenchManualMeasurement measurement,
        CancellationToken cancellationToken);
}
