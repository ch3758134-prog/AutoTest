using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Uds;

// Testing owns the node/step model; this adapter exposes one well-bounded
// action without introducing duplicate TEST contracts.
public sealed class UdsStepHandler(IUdsClient client)
{
    public async ValueTask<OperationResult<UdsExchangeResult>> ExecuteAsync(
        ReadOnlyMemory<byte> requestPdu, UdsEndpointProfile endpoint, CancellationToken ct)
    {
        if (requestPdu.IsEmpty) return OperationResult<UdsExchangeResult>.Rejected("UDS_EMPTY","Empty UDS request.");
        var exchange = await client.RequestAsync(new UdsRequest(
            ImmutableArray.CreateRange(requestPdu.ToArray()),Guid.NewGuid()),endpoint,ct).ConfigureAwait(false);
        return exchange.Outcome switch
        {
            Outcome.Accepted => OperationResult<UdsExchangeResult>.Accepted(exchange),
            Outcome.Rejected => OperationResult<UdsExchangeResult>.Rejected("UDS_REJECTED",
                exchange.NegativeResponseCode is byte nrc ? $"NRC=0x{nrc:X2}" : "Policy rejected request."),
            _ => OperationResult<UdsExchangeResult>.Uncertain("UDS_UNCERTAIN","UDS response timed out or was invalid.",
                sideEffectMayHaveOccurred: false)
        };
    }
}
