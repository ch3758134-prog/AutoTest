namespace AutoTest.Devices.Usb;
// Single read-loop owner; bounded buffer and byte-at-a-time resynchronization on corruption.
public sealed class UsbFrameDecoder
{
    private readonly List<byte> _buffer = new();
    public long InvalidCandidates { get; private set; }
    public long DiscardedBytes { get; private set; }
    public void Reset() => _buffer.Clear();
    public IReadOnlyList<UsbResponse> Feed(ReadOnlySpan<byte> input)
    {
        var found = new List<UsbResponse>();
        foreach (var b in input) _buffer.Add(b);
        while(_buffer.Count != 0)
        {
            int start = _buffer.IndexOf(UsbWireCodec.Start);
            if(start < 0) { DiscardedBytes += _buffer.Count; _buffer.Clear(); break; }
            if(start > 0) { _buffer.RemoveRange(0,start); DiscardedBytes += start; }
            if(_buffer.Count < UsbWireCodec.ResponseOverhead) break;
            int count = UsbWireCodec.ResponseOverhead + _buffer[5];
            if(_buffer.Count < count) break;
            if(UsbWireCodec.TryDecodeResponse(_buffer.GetRange(0,count).ToArray(),out var response))
            { found.Add(response); _buffer.RemoveRange(0,count); }
            else { InvalidCandidates++; DiscardedBytes++; _buffer.RemoveAt(0); }
        }
        // Even a malicious indefinite partial packet cannot grow indefinitely.
        if(_buffer.Count > 264) { DiscardedBytes += _buffer.Count; _buffer.Clear(); InvalidCandidates++; }
        return found;
    }
}
