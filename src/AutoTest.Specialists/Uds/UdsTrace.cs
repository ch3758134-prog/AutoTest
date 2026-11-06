namespace AutoTest.Specialists.Uds;

public enum UdsTraceDirection { Request, Response, Error }
public sealed record UdsTraceEntry(Guid CorrelationId, UdsTraceDirection Direction,
    DateTimeOffset Utc, byte[] Data, string Note);

public sealed class UdsTrace
{
    private readonly object _sync = new();
    private readonly List<UdsTraceEntry> _entries = [];
    public void AppendRequest(Guid correlation, ReadOnlySpan<byte> pdu) =>
        Append(correlation,UdsTraceDirection.Request,pdu,"REQUEST");
    public void AppendResponse(Guid correlation, ReadOnlySpan<byte> pdu) =>
        Append(correlation,UdsTraceDirection.Response,pdu,"RESPONSE");
    public void AppendError(Guid correlation, string reason) =>
        Append(correlation,UdsTraceDirection.Error,[],reason);
    private void Append(Guid id, UdsTraceDirection direction, ReadOnlySpan<byte> pdu, string note)
    {
        lock (_sync) _entries.Add(new(id,direction,DateTimeOffset.UtcNow,pdu.ToArray(),note));
    }
    public IReadOnlyList<UdsTraceEntry> Snapshot()
    {
        lock (_sync) return _entries.Select(e=>e with { Data=e.Data.ToArray() }).ToArray();
    }
}
