using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Uds;

// Exclusive CAN transmit lease is held for the full session. Device owns the
// lease implementation; this module never owns the hardware USB/Station session.
public sealed class UdsSessionService(ITransmitLeaseService leases, ICanFrameHub hub)
{
    public async ValueTask<UdsSession> StartSessionAsync(UdsEndpointProfile endpoint,
        string owner, CancellationToken ct)
    {
        UdsEndpointProfileValidator.ValidateAddressing(endpoint);
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner required.", nameof(owner));
        var acquired = await leases.AcquireAsync(endpoint.Role,owner,TimeSpan.FromMinutes(2),ct).ConfigureAwait(false);
        if (acquired.Outcome != Outcome.Accepted)
            throw new InvalidOperationException($"UDS CAN lease unavailable: {acquired.FailureCode}");
        LeaseToken lease = acquired.Value;
        try
        {
            var link = new CanHubIsoTpLink(hub,endpoint,lease);
            var session = new UdsSession(new UdsClient(new IsoTpUdsMessageChannel(
                new IsoTpConversation(link))), link, leases, lease, endpoint);
            return session;
        }
        catch { await leases.ReleaseAsync(lease,ct).ConfigureAwait(false); throw; }
    }
}

public sealed class UdsSession : IAsyncDisposable
{
    private readonly CanHubIsoTpLink _link;
    private readonly ITransmitLeaseService _leases;
    private readonly LeaseToken _token;
    private readonly UdsEndpointProfile _endpoint;
    private int _disposed;
    public UdsClient Client { get; }
    internal UdsSession(UdsClient client, CanHubIsoTpLink link, ITransmitLeaseService leases,
        LeaseToken token, UdsEndpointProfile endpoint)
    { Client = client; _link = link; _leases = leases; _token = token; _endpoint = endpoint; }
    public ValueTask<UdsExchangeResult> RequestAsync(byte[] pdu, CancellationToken ct) =>
        Client.RequestAsync(new(pdu.ToImmutableArray(),Guid.NewGuid()),_endpoint,ct);
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed,1) != 0) return;
        await _link.DisposeAsync().ConfigureAwait(false);
        var released = await _leases.ReleaseAsync(_token,CancellationToken.None).ConfigureAwait(false);
        if (released.Outcome != Outcome.Accepted)
            throw new IOException($"CAN lease release failed: {released.FailureCode}");
    }
}
