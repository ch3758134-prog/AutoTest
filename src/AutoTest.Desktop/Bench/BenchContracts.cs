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
    // Identity at editor load time, not a Controller Profile name. Null is used
    // by nonphysical test ports; the real adapter always reads the live PROJECT.
    string? ActiveProjectId => null;
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

    // Project-scoped editable action sequences; default ports do not persist or actuate.
    Task<IReadOnlyList<BenchKeyAction>> LoadKeyActionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BenchKeyAction>>(Array.Empty<BenchKeyAction>());
    Task SaveKeyActionsAsync(IReadOnlyList<BenchKeyAction> actions,
        CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("工程按键动作存储尚未接线。"));
    Task<BenchActionExecution> RunKeyActionAsync(BenchKeyAction action,
        IProgress<BenchActionProgress>? progress, CancellationToken cancellationToken) =>
        Task.FromResult(new BenchActionExecution(false, false, false,
            "实际 KEY_SIM 连续动作执行接口未接线，不允许仿真。"));

    Task<IReadOnlyList<BenchCanMessage>> LoadCanMessagesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BenchCanMessage>>(Array.Empty<BenchCanMessage>());
    Task SaveCanMessagesAsync(IReadOnlyList<BenchCanMessage> messages, CancellationToken ct) =>
        Task.FromException(new NotSupportedException("工程 CAN 报文配置未接线。"));
    Task<BenchOperationResult> SendCanOnceAsync(BenchCanMessage message, CancellationToken ct) =>
        Task.FromResult(new BenchOperationResult(false, null, null, null, null, null,
            null, "CAN1_UNAVAILABLE", "真实 DUT CAN 单发接口未接线。",
            BenchEvidenceSource.Physical, null));
    Task<BenchActionExecution> RunCanPeriodicAsync(BenchCanMessage message,
        IProgress<BenchCanPeriodicProgress>? progress, CancellationToken ct) =>
        Task.FromResult(new BenchActionExecution(false, false, false,
            "真实 DUT CAN 周期发送尚未接线，不允许仿真。"));

    Task<BenchActionExecution> RunCanPeriodicBatchAsync(
        IReadOnlyList<BenchCanMessage> checkedMessages,
        IProgress<BenchCanPeriodicProgress>? progress, CancellationToken ct) =>
        Task.FromResult(new BenchActionExecution(false, false, false,
            "真实 DUT CAN1 多报文周期发送尚未接线，禁止仿真。"));

    Task SaveManualMeasurementAsync(BenchManualMeasurement measurement,
        CancellationToken cancellationToken);
}
