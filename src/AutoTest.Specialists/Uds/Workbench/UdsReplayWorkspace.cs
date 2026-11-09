using System.Text.Json;

namespace AutoTest.Specialists.Uds.Workbench;

// Replay is an offline comparison against captured messages. It does not
// transmit, does not claim a live ECU response, and does not re-time a CAN bus.
public sealed record UdsReplayRecord(string RequestHex, List<string> Responses,
    List<UdsCanTraceRow> Frames);
public sealed record UdsReplayDocument(int Schema, List<UdsReplayRecord> Exchanges);

public sealed class UdsReplayWorkspace
{
    private IReadOnlyList<UdsReplayRecord> _records = [];
    private int _index;
    public int Count => _records.Count;
    public int Position => _index;
    public void Load(UdsReplayDocument document)
    {
        if (document.Schema != 1 || document.Exchanges is null || document.Exchanges.Count > 10000)
            throw new InvalidDataException("Invalid UDS replay document.");
        foreach (var exchange in document.Exchanges)
        {
            if (exchange is null || exchange.Responses is null || exchange.Frames is null ||
                exchange.Responses.Count > 256 || exchange.Frames.Count > 100000)
                throw new InvalidDataException("Invalid replay exchange.");
            var request=UdsHex.Parse(exchange.RequestHex);
            bool finalSeen=false;
            int pendingCount=0;
            foreach (var responseText in exchange.Responses)
            {
                var response=UdsHex.Parse(responseText);
                if (finalSeen)
                    throw new InvalidDataException("Replay response exists after a final UDS response.");
                if (response[0] == 0x7F)
                {
                    if (response.Length != 3 || response[1] != request[0])
                        throw new InvalidDataException("Replay negative response SID mismatch.");
                    if (response[2] == 0x78)
                    {
                        if (++pendingCount > 8)
                            throw new InvalidDataException("Replay exceeds bounded NRC 0x78 pending limit.");
                    }
                    else finalSeen=true;
                }
                else
                {
                    if (response[0] != (byte)(request[0]+0x40))
                        throw new InvalidDataException("Replay positive response SID mismatch.");
                    finalSeen=true;
                }
            }
            foreach(var frame in exchange.Frames)
            {
                if (string.IsNullOrWhiteSpace(frame.Direction))
                    throw new InvalidDataException("Replay frame requires direction.");
                var raw=UdsHex.Parse(frame.Hex);
                if (raw.Length is < 1 or > 8)
                    throw new InvalidDataException("Replay classical CAN frame DLC must be 1..8.");
            }
        }
        // Keep a private immutable-in-practice copy of validated replay data.
        // Caller edits after Load must not rewrite expected UDS responses or
        // let unvalidated frames enter a previously accepted replay session.
        _records=document.Exchanges.Select(x=>new UdsReplayRecord(
            UdsHex.Format(UdsHex.Parse(x.RequestHex)),
            x.Responses.Select(y=>UdsHex.Format(UdsHex.Parse(y))).ToList(),
            x.Frames.Select(y=>new UdsCanTraceRow(
                y.Direction,UdsHex.Format(UdsHex.Parse(y.Hex)))).ToList()
        )).ToArray();
        _index=0;
    }
    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file=new FileInfo(path);
        if (!file.Exists || file.Length>8*1024*1024)
            throw new InvalidDataException("Replay file missing or exceeds 8 MiB.");
        var doc=JsonSerializer.Deserialize<UdsReplayDocument>(
            await File.ReadAllBytesAsync(path,ct).ConfigureAwait(false))
            ?? throw new InvalidDataException("Invalid replay JSON.");
        Load(doc);
    }
    public UdsToolExchange Consume(string requestText,string expectedText="", Guid? correlationId=null)
    {
        var request=UdsHex.Parse(requestText);
        if (correlationId == Guid.Empty) throw new ArgumentException("Correlation ID must be nonempty.");
        if (_index>=_records.Count) throw new EndOfStreamException("REPLAY_EXHAUSTED");
        var record=_records[_index];
        byte[] stored=UdsHex.Parse(record.RequestHex);
        if (!stored.SequenceEqual(request))
        {
            // Replay files may contain SecurityAccess keys or firmware bytes.
            // Never echo the expected sensitive request into UI errors/log export.
            bool sensitive=stored[0] is 0x27 or 0x29 or 0x2E or 0x2F
                or 0x34 or 0x35 or 0x36 or 0x37;
            throw new InvalidOperationException(sensitive
                ? "REPLAY_REQUEST_MISMATCH: sensitive expected PDU redacted"
                : "REPLAY_REQUEST_MISMATCH: expected " + UdsHex.Format(stored));
        }
        byte[]? expected=string.IsNullOrWhiteSpace(expectedText)?null:UdsHex.Parse(expectedText);
        _index++;
        var responses=record.Responses.Select(UdsHex.Parse).ToList();
        byte[] final=responses.LastOrDefault(r=> !(r.Length==3 && r[0]==0x7F &&
            r[1]==request[0] && r[2]==0x78))??[];
        string outcome=final.Length==0 ? "Uncertain" : final[0]==0x7F?"Rejected":"Accepted";
        byte? nrc=final.Length==3 && final[0]==0x7F? final[2]:null;
        return new("Replay",UdsHex.Format(request),UdsHex.Format(final),outcome,
            nrc.HasValue?$"0x{nrc.Value:X2}":"—",
            expected is null?null:final.SequenceEqual(expected),correlationId ?? Guid.NewGuid(),
            record.Frames.ToArray(),final.Length==0?"REPLAY_NO_FINAL_RESPONSE":"")
            {
                ResponseSequence = responses.Select(UdsHex.Format).ToArray(),
                FailureLayer = final.Length == 0 ? "REPLAY_NO_FINAL_RESPONSE" :
                    nrc.HasValue ? "UDS_ECU_NEGATIVE_RESPONSE" : "NONE",
                DiagnosticDetail = final.Length == 0 ?
                    "Replay ended without a final UDS response; not a live ECU timeout." :
                    nrc.HasValue ? $"Replay negative response NRC=0x{nrc.Value:X2}" : ""
            };
    }
}
