using AutoTest.Core;

namespace AutoTest.Contracts;

public enum StationConnectionState { Disconnected, Connecting, Ready, Faulted }
public enum StationAction { MockProbe, Ignition, Fixture, Keypad, Programming }
public enum AuthorizationScope { None, MockOnly, PhysicalCommand, Programming }
public enum MockScenario { Normal, Timeout, Disconnect, DelayedResponse }
public enum AckKind { None, SimulatorAcknowledged, DeviceAcknowledged }
public enum PhysicalState { Unknown, VerifiedOn, VerifiedOff }

public sealed record SessionInfo(StationId Station, long Generation, string Transport,
    string DeviceIdentity, string FirmwareIdentity, StationConnectionState State);

// Authorization is scoped to the simulation environment; no real-token issuer exists in V0.
public sealed record Authorization(AuthorizationScope Scope, StationId Station, long Generation,
    DateTimeOffset ExpiresUtc, string Issuer)
{
    public static Authorization MockOnly(SessionInfo session, DateTimeOffset utc) =>
        new(AuthorizationScope.MockOnly, session.Station, session.Generation,
            utc.AddMinutes(1), "V0_MOCK_ISSUER");
}
public sealed record SafetyAuthorization(Authorization Authorization, string ApprovedScope,
    string ApprovalIdentity);
public sealed record StationCommand(StationAction Action, MockScenario Scenario,
    Guid CorrelationId);
public sealed record CommandReceipt(Guid OperationId, AckKind Ack, PhysicalState PhysicalState,
    long SessionGeneration, TimePoint AcknowledgedAt, EvidenceRef AckEvidence);

public sealed record MockObservation(Guid OperationId, string Indicator, long SessionGeneration,
    TimePoint Time, EvidenceRef Evidence, EvidenceQuality Quality);

public interface IStationSession
{
    ValueTask<OperationResult<SessionInfo>> ConnectAsync(StationId station, CancellationToken ct);
    ValueTask<OperationResult<Unit>> DisconnectAsync(CancellationToken ct);
}
public interface IStationControl
{
    ValueTask<OperationResult<CommandReceipt>> ExecuteAsync(StationCommand command,
        Authorization authorization, CancellationToken ct);
}
// Explicitly simulation-only extension; no real-device observational claim.
public interface IMockObservationSource
{
    ValueTask<OperationResult<MockObservation>> ObserveMockAsync(Guid operationId,
        long sessionGeneration, CancellationToken ct);
}
