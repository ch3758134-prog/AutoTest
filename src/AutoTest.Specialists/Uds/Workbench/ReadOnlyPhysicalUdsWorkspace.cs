using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Uds.Workbench;

// UDS-owned port boundary for Issue #17. DEVICE owns the single real USB/CAN
// session, lease, Tx authorization and trace. INTEGRATION injects its provider.
// No UDS object opens WinUSB, creates a hub, or grants physical permissions.
public sealed record ReadOnlyDiagnosticSessionSpec(
    string StationIdentity, long ExpectedSessionGeneration,
    UdsEndpointProfile Endpoint, IReadOnlySet<byte> AllowedReadOnlySids,
    TimeSpan MaxRequestDuration);

public sealed record ReadOnlyPhysicalConnection(
    string StationIdentity, long SessionGeneration, UdsEndpointProfile Endpoint,
    bool ReadOnlyTxAuthorized, bool RawTraceReady, DateTimeOffset ExpiresUtc);

public sealed record PhysicalUdsBusEvent(
    DateTimeOffset Utc, string Direction, string Layer, BusRole Role,
    uint CanId, bool ExtendedId, string RawHex, string TraceRef,
    long SessionGeneration, long LostFrames, bool TxAcknowledgedByDevice = false);

public interface IReadOnlyPhysicalUdsSession : IAsyncDisposable
{
    ReadOnlyPhysicalConnection Connection { get; }
    IUdsClient Client { get; } // Existing UdsClient/IsoTpConversation, never a new stack.
    IReadOnlyList<PhysicalUdsBusEvent> SnapshotCan(Guid correlationId);
    IReadOnlyList<UdsTraceEntry> SnapshotUds(Guid correlationId);
}

public interface IReadOnlyPhysicalUdsSessionProvider
{
    // Must reflect the existing single station/USB Session; no UI-owned handle.
    ReadOnlyPhysicalConnection? Current { get; }
    ValueTask<IReadOnlyPhysicalUdsSession> OpenAsync(
        ReadOnlyDiagnosticSessionSpec spec, CancellationToken ct);
}

public sealed record PhysicalReadOnlyUdsExchange(
    Guid CorrelationId, UdsExchangeResult Result,
    ReadOnlyPhysicalConnection Connection,
    IReadOnlyList<PhysicalUdsBusEvent> CanTrace,
    IReadOnlyList<UdsTraceEntry> UdsTrace,
    bool RawBusEvidenceVerified, string DiagnosticCode);

public sealed class ReadOnlyPhysicalUdsWorkspace(IReadOnlyPhysicalUdsSessionProvider provider)
{
    private readonly SemaphoreSlim _singleOperation = new(1,1);
    private static readonly IReadOnlySet<byte> AllowedSids = ImmutableHashSet.Create<byte>(0x19,0x22);

    public ReadOnlyPhysicalConnection? Current => provider.Current;

    public static void ValidateRequest(UdsRequest request, UdsEndpointProfile endpoint)
    {
        UdsEndpointProfileValidator.ValidateAddressing(endpoint);
        if (request.CorrelationId == Guid.Empty || request.Pdu.IsDefaultOrEmpty ||
            request.Pdu.Length > 64)
            throw new ArgumentException("P1_READONLY_INVALID_REQUEST");
        byte sid=request.Pdu[0];
        if (!AllowedSids.Contains(sid))
            throw new UnauthorizedAccessException($"P1_READONLY_SID_DENIED_0x{sid:X2}");
        // 0x22 carries one or more two-byte DIDs. 0x19 includes a subfunction.
        if ((sid == 0x22 && (request.Pdu.Length < 3 ||
            (request.Pdu.Length-1)%2 != 0)) ||
            (sid == 0x19 && request.Pdu.Length < 2))
            throw new ArgumentException("P1_READONLY_PDU_MALFORMED");
    }

    private static bool IsValidClassicFrame(string hex)
    {
        try
        {
            var bytes=UdsHex.Parse(hex);
            return bytes.Length is >=1 and <=8;
        }
        catch (FormatException) { return false; }
    }

    public bool IsReady(UdsEndpointProfile endpoint)
    {
        var current=provider.Current;
        if (current is null || !current.ReadOnlyTxAuthorized ||
            !current.RawTraceReady || string.IsNullOrWhiteSpace(current.StationIdentity) ||
            current.SessionGeneration <= 0 || current.ExpiresUtc<=DateTimeOffset.UtcNow)
            return false;
        return current.Endpoint == endpoint && endpoint.Role == BusRole.DutCan;
    }

    public async Task<PhysicalReadOnlyUdsExchange> RequestAsync(
        UdsRequest request, UdsEndpointProfile endpoint,
        CancellationToken ct = default)
    {
        // Block every non-read-only SID BEFORE provider acquisition or CAN frames.
        ValidateRequest(request,endpoint);
        if (!IsReady(endpoint))
            throw new UnauthorizedAccessException("P1_PHYSICAL_SESSION_NOT_AUTHORIZED");
        if (!await _singleOperation.WaitAsync(0,ct).ConfigureAwait(false))
            throw new InvalidOperationException("P1_PHYSICAL_REQUEST_BUSY");
        try
        {
            var current=provider.Current;
            if (current is null || !IsReady(endpoint))
                throw new UnauthorizedAccessException("P1_PHYSICAL_SESSION_CHANGED");
            var spec=new ReadOnlyDiagnosticSessionSpec(current.StationIdentity,
                current.SessionGeneration,endpoint,AllowedSids,
                TimeSpan.FromMilliseconds(Math.Clamp(
                    endpoint.P2Star.TotalMilliseconds * 9,1000,120000)));
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(spec.MaxRequestDuration);
            await using var session=await provider.OpenAsync(spec,timeout.Token)
                .ConfigureAwait(false);
            if (session.Connection != current || !IsReady(endpoint))
                throw new UnauthorizedAccessException("P1_PHYSICAL_BOUND_SESSION_MISMATCH");
            UdsExchangeResult result=await session.Client.RequestAsync(request,endpoint,
                timeout.Token).ConfigureAwait(false);
            // A device session change invalidates the entire exchange, even if a
            // stale ECU PDU appears to be positive.
            if (provider.Current != current || DateTimeOffset.UtcNow >= current.ExpiresUtc)
                throw new IOException("P1_PHYSICAL_SESSION_GENERATION_CHANGED");
            var can=session.SnapshotCan(request.CorrelationId).ToArray();
            var uds=session.SnapshotUds(request.CorrelationId).ToArray();
            bool canVerified=can.Any(x=>x.Direction=="TX" &&
                    x.TxAcknowledgedByDevice && x.CanId==endpoint.RequestCanId) &&
                can.Any(x=>x.Direction=="RX" && x.CanId==endpoint.ResponseCanId) &&
                can.All(x=>x.SessionGeneration==current.SessionGeneration &&
                    x.LostFrames==0 && x.Role==BusRole.DutCan &&
                    x.ExtendedId==(x.CanId>0x7FFu) &&
                    ((x.Direction=="TX" && x.CanId==endpoint.RequestCanId) ||
                     (x.Direction=="RX" && x.CanId==endpoint.ResponseCanId)) &&
                    !string.IsNullOrWhiteSpace(x.TraceRef) &&
                    IsValidClassicFrame(x.RawHex));
            if (result.Outcome == Outcome.Accepted && !canVerified)
                result=result with { Outcome=Outcome.Uncertain };
            string code=canVerified ? "PHYSICAL_CAN_TRACE_BOUND" :
                "PHYSICAL_RAW_CAN_EVIDENCE_INCOMPLETE";
            return new(request.CorrelationId,result,current,can,uds,canVerified,code);
        }
        finally { _singleOperation.Release(); }
    }
}
