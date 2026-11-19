using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Uds;

public interface IUdsMessageChannel
{
    ValueTask SendPduAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    ValueTask<byte[]> ReceivePduAsync(TimeSpan timeout, CancellationToken ct);
}

public sealed class IsoTpUdsMessageChannel(IsoTpConversation conversation) : IUdsMessageChannel
{
    public ValueTask SendPduAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => conversation.SendPduAsync(data,ct);
    public ValueTask<byte[]> ReceivePduAsync(TimeSpan timeout, CancellationToken ct) => conversation.ReceivePduAsync(timeout,ct);
}

// Read-only UDS boundary. State-changing requests remain denied until an explicitly
// reviewed physical programming transport and authorization path is integrated.
public sealed class UdsClient : IUdsClient
{
    private readonly IUdsMessageChannel _channel;
    private readonly UdsServiceCatalog _catalog;
    private readonly SemaphoreSlim _exchangeLock = new(1,1);
    public UdsTrace Trace { get; }
    public int MaxResponsePending { get; init; } = 8;
    public UdsClient(IUdsMessageChannel channel, UdsServiceCatalog? catalog = null, UdsTrace? trace = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _catalog = catalog ?? new UdsServiceCatalog(); Trace = trace ?? new UdsTrace();
    }
    public async ValueTask<UdsExchangeResult> RequestAsync(UdsRequest request,
        UdsEndpointProfile profile, CancellationToken ct)
    {
        UdsEndpointProfileValidator.ValidateAddressing(profile);
        if (request.Pdu.IsDefaultOrEmpty || request.CorrelationId == Guid.Empty)
            throw new ArgumentException("Nonempty UDS PDU and correlation ID required.");
        var pdu = request.Pdu.ToArray();
        var service = _catalog.Get(pdu[0]);
        if (pdu.Length < service.MinimumRequestLength)
            throw new ArgumentException($"Malformed {service.Name} request.");
        if (MaxResponsePending < 0) throw new InvalidOperationException("Invalid pending limit.");
        if (service.Risk != UdsRiskLevel.ReadOnly)
            return Result(Outcome.Rejected,pdu,[],null);
        if (!await _exchangeLock.WaitAsync(0,ct).ConfigureAwait(false))
        {
            Trace.AppendError(request.CorrelationId,"UDS_ENDPOINT_BUSY");
            return Result(Outcome.Rejected,pdu,[],null);
        }
        try
        {
            Trace.AppendRequest(request.CorrelationId,pdu);
            await _channel.SendPduAsync(pdu,ct).ConfigureAwait(false);
            var timeout = profile.P2;
            for (int pending = 0; ;)
            {
                var response = await _channel.ReceivePduAsync(timeout,ct).ConfigureAwait(false);
                Trace.AppendResponse(request.CorrelationId,response);
                if (response.Length == 3 && response[0] == 0x7F && response[1] == pdu[0])
                {
                    if (response[2] == 0x78)
                    {
                        if (++pending > MaxResponsePending)
                            throw new TimeoutException("Exceeded bounded ResponsePending count.");
                        timeout = profile.P2Star;
                        continue;
                    }
                    return Result(Outcome.Rejected,pdu,response,response[2]);
                }
                // Suppressed positive responses require an explicit separate no-response
                // policy; never misinterpret timeout as an acknowledged ECU response.
                if (response.Length < 1 || response[0] != (byte)(pdu[0]+0x40))
                    throw new IsoTpProtocolException("Response SID/NRC does not match request.");
                return Result(Outcome.Accepted,pdu,response,null);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
        {
            Trace.AppendError(request.CorrelationId,ex.GetType().Name + ": " + ex.Message);
            return Result(Outcome.Uncertain,pdu,[],null);
        }
        finally { _exchangeLock.Release(); }
    }
    private static UdsExchangeResult Result(Outcome outcome, byte[] request, byte[] response, byte? nrc)
    {
        var point = TimePoint.FromHostArrival(DateTimeOffset.UtcNow, 0);
        return new(outcome, request.ToImmutableArray(), response.ToImmutableArray(), nrc,
            ImmutableArray<EvidenceRef>.Empty, point);
    }
}
