using System.Collections.Immutable;

namespace AutoTest.TestFixtures;

// Zero transport handles: models byte fragmentation and faults for later A1 adapter tests.
public sealed class FakeUsbByteLink
{
    public bool DisconnectInjected { get; set; }
    public bool ReverseChunks { get; set; }
    public bool DropLastChunk { get; set; }
    public ImmutableArray<ImmutableArray<byte>> Deliver(ReadOnlySpan<byte> data, int chunkSize)
    {
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (DisconnectInjected) throw new IOException("MOCK_USB_DISCONNECT");
        var chunks = ImmutableArray.CreateBuilder<ImmutableArray<byte>>();
        for (int offset = 0; offset < data.Length; offset += chunkSize)
            chunks.Add(ImmutableArray.CreateRange(data.Slice(offset,
                Math.Min(chunkSize, data.Length - offset)).ToArray()));
        if (DropLastChunk && chunks.Count > 0) chunks.RemoveAt(chunks.Count - 1);
        var immutable = chunks.ToImmutable();
        return ReverseChunks ? immutable.Reverse().ToImmutableArray() : immutable;
    }
}
