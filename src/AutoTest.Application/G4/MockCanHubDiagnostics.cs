using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Devices.Can;
using AutoTest.Specialists.Flash;
using AutoTest.Specialists.Uds;

namespace AutoTest.Application.G4;

/// <summary>ONE in-process CAN hub for WAVE (replay RX) and UDS (leased TX/RX). Zero USB/device drivers.</summary>
public sealed class MockCanHubRouter : IDisposable
{
    private readonly MockBootloader _ecu = new("G4-MOCK-ECU");
    private readonly CanTransmitGate _leases = new(1);
    private int _requests;
    public CanFrameHub Hub { get; }
    public ITransmitLeaseService Leases => _leases;
    public int MockRequestCount => Volatile.Read(ref _requests);
    public MockCanHubRouter()
    {
        Hub = new CanFrameHub(TransmitMockAsync);
    }
    private ValueTask<OperationResult<CanTxReceipt>> TransmitMockAsync(
        CanTxRequest request, LeaseToken lease, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_leases.Validate(request.Frame.Role, lease))
            return ValueTask.FromResult(OperationResult<CanTxReceipt>.Rejected("CAN_LEASE_INVALID", "Invalid TX lease"));
        if (request.Frame.Role != BusRole.DutCan || request.Frame.Id != 0x7E0)
            return ValueTask.FromResult(OperationResult<CanTxReceipt>.Rejected("MOCK_UDS_ENDPOINT_ONLY", "No device TX"));
        try
        {
            var pdu = IsoTpFrameCodec.ParseSingle(request.Frame.Data.AsSpan());
            // This hub path is intentionally READ-ONLY. All programming goes to an isolated MockBootloader.
            if (pdu[0] is not (0x19 or 0x22))
                return ValueTask.FromResult(OperationResult<CanTxReceipt>.Rejected("MOCK_CAN_READ_ONLY", "UDS state change denied"));
            var response = _ecu.ReceiveRequest(pdu);
            if (response.Length > 7)
                return ValueTask.FromResult(OperationResult<CanTxReceipt>.Rejected("MOCK_ONLY_SINGLE_FRAME", "Use a dedicated ISO-TP fixture"));
            Interlocked.Increment(ref _requests);
            var frame = CanFrame.Create(BusRole.DutCan, false, 0x7E8,
                IsoTpFrameCodec.EncodeSingle(response));
            Hub.Publish(new CanObservation(frame,
                TimePoint.FromSimulation(DateTimeOffset.UtcNow, MockRequestCount),
                EvidenceQuality.Derived, lease.SessionGeneration, 0, EvidenceRef.New()));
            var receipt = new CanTxReceipt(request.CorrelationId, AckKind.SimulatorAcknowledged,
                TimePoint.FromSimulation(DateTimeOffset.UtcNow, MockRequestCount), EvidenceRef.New());
            return ValueTask.FromResult(OperationResult<CanTxReceipt>.Accepted(receipt));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or IsoTpProtocolException)
        {
            return ValueTask.FromResult(OperationResult<CanTxReceipt>.Uncertain("MOCK_BUS_ERROR", ex.Message));
        }
    }
    public void Dispose() => Hub.Dispose();
}

public sealed record MockUdsOutcome(Outcome Outcome, string Code, byte[] Response,
    bool Simulated, bool PhysicalEffectVerified, int SharedHubRequests);

public sealed class MockHubDiagnosticWorkbench(MockCanHubRouter bus)
{
    private readonly UdsEndpointProfile _endpoint = new(BusRole.DutCan, 0x7E0, 0x7E8,
        TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(900), "Normal");

    public async Task<MockUdsOutcome> ReadDtcMockAsync(CancellationToken ct = default)
    {
        var sessions = new UdsSessionService(bus.Leases, bus.Hub);
        await using var session = await sessions.StartSessionAsync(_endpoint, "G4-MOCK-UDS", ct).ConfigureAwait(false);
        // Subscription task starts asynchronously. Reject rather than race a missing response.
        for (var i = 0; bus.Hub.SubscriberCount == 0 && i < 1000; i++)
            await Task.Delay(1, ct).ConfigureAwait(false);
        if (bus.Hub.SubscriberCount == 0)
            return new(Outcome.Uncertain, "UDS_SUBSCRIPTION_TIMEOUT", [], true, false, bus.MockRequestCount);
        var response = await session.RequestAsync([0x19, 0x02], ct).ConfigureAwait(false);
        return new(response.Outcome, response.Outcome == Outcome.Accepted ? "MOCK_DTC_RESPONSE" :
            "UDS_REJECTED_OR_UNCERTAIN", response.ResponsePdu.ToArray(), true, false, bus.MockRequestCount);
    }

    public async Task<MockUdsOutcome> RejectStateChangeOnHubAsync(CancellationToken ct = default)
    {
        var sessions = new UdsSessionService(bus.Leases, bus.Hub);
        await using var session = await sessions.StartSessionAsync(_endpoint, "G4-MOCK-UDS", ct).ConfigureAwait(false);
        var response = await session.RequestAsync([0x34, 0x00, 0x00], ct).ConfigureAwait(false);
        return new(response.Outcome, "PHYSICAL_PROGRAMMING_BLOCKED", response.ResponsePdu.ToArray(),
            true, false, bus.MockRequestCount);
    }
}
