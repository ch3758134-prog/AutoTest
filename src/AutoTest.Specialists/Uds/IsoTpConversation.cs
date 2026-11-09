using System;
using System.IO;

namespace AutoTest.Specialists.Uds;

// A frame link carries only the 0..8 CAN data bytes; IDs, leases and routing belong to Device.
public interface IIsoTpFrameLink
{
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct);
}

public sealed record IsoTpOptions(TimeSpan FrameTimeout, byte ReceiveBlockSize = 8,
    byte ReceiveStMin = 0, int MaxWaitFrames = 3)
{
    public static IsoTpOptions Default { get; } = new(TimeSpan.FromSeconds(1));
    public void Validate()
    {
        if (FrameTimeout <= TimeSpan.Zero || MaxWaitFrames < 0) throw new ArgumentOutOfRangeException(nameof(FrameTimeout));
        _ = IsoTpFrameCodec.DecodeStMin(ReceiveStMin);
    }
}

// Instance is a single-owner ISO-TP conversation: simultaneous Send/Receive on one
// conversation is forbidden; UdsClient serializes entire request/response exchanges.
public sealed class IsoTpConversation
{
    private readonly IIsoTpFrameLink _link;
    private readonly IsoTpOptions _options;
    public IsoTpConversation(IIsoTpFrameLink link, IsoTpOptions? options = null)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _options = options ?? IsoTpOptions.Default; _options.Validate();
    }
    private async ValueTask<byte[]> NextAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            var data = await _link.ReceiveAsync(limit.Token).ConfigureAwait(false);
            if (data.Length is < 1 or > 8) throw new IsoTpProtocolException("CAN payload length invalid.");
            return data.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("ISO-TP frame/flow-control timeout."); }
    }
    public async ValueTask SendPduAsync(ReadOnlyMemory<byte> pdu, CancellationToken ct = default)
    {
        if (pdu.Length is < 1 or > IsoTpFrameCodec.MaxClassicPdu) throw new ArgumentOutOfRangeException(nameof(pdu));
        if (pdu.Length <= 7)
        {
            await _link.SendAsync(IsoTpFrameCodec.EncodeSingle(pdu.Span), ct).ConfigureAwait(false);
            return;
        }
        await _link.SendAsync(IsoTpFrameCodec.EncodeFirst(pdu.Length, pdu.Span), ct).ConfigureAwait(false);
        int offset = 6, nextSeq = 1, waitFrames = 0;
        while (offset < pdu.Length)
        {
            IsoTpFlowControl flow;
            do
            {
                var raw = await NextAsync(_options.FrameTimeout, ct).ConfigureAwait(false);
                flow = IsoTpFrameCodec.ParseFlow(raw);
                if (flow.Status == IsoTpFlowStatus.Overflow) throw new IsoTpProtocolException("Receiver overflow.");
                if (flow.Status == IsoTpFlowStatus.Wait && ++waitFrames > _options.MaxWaitFrames)
                    throw new IsoTpProtocolException("Exceeded ISO-TP wait-frame limit.");
            } while (flow.Status == IsoTpFlowStatus.Wait);
            waitFrames = 0;
            int sent = 0;
            while (offset < pdu.Length && (flow.BlockSize == 0 || sent < flow.BlockSize))
            {
                if (flow.StMin > TimeSpan.Zero) await Task.Delay(flow.StMin, ct).ConfigureAwait(false);
                var count = Math.Min(7, pdu.Length - offset);
                await _link.SendAsync(IsoTpFrameCodec.EncodeConsecutive((byte)nextSeq,
                    pdu.Span.Slice(offset, count)), ct).ConfigureAwait(false);
                nextSeq = (nextSeq + 1) & 0xF; offset += count; sent++;
            }
        }
    }
    public async ValueTask<byte[]> ReceivePduAsync(TimeSpan firstFrameTimeout, CancellationToken ct = default)
    {
        if (firstFrameTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(firstFrameTimeout));
        var first = await NextAsync(firstFrameTimeout, ct).ConfigureAwait(false);
        if (IsoTpFrameCodec.Kind(first) == IsoTpFrameKind.Single) return IsoTpFrameCodec.ParseSingle(first);
        var ff = IsoTpFrameCodec.ParseFirst(first);
        var result = new byte[ff.Length]; ff.InitialData.CopyTo(result, 0);
        int offset = ff.InitialData.Length, expectedSeq = 1, inBlock = 0;
        await _link.SendAsync(IsoTpFrameCodec.EncodeFlow(IsoTpFlowStatus.Continue,
            _options.ReceiveBlockSize, _options.ReceiveStMin), ct).ConfigureAwait(false);
        while (offset < result.Length)
        {
            var cf = IsoTpFrameCodec.ParseConsecutive(await NextAsync(_options.FrameTimeout, ct).ConfigureAwait(false));
            if (cf.Sequence != expectedSeq) throw new IsoTpProtocolException("Consecutive-frame sequence mismatch.");
            int count = Math.Min(result.Length - offset, cf.Data.Length);
            if (count < 1) throw new IsoTpProtocolException("Empty CF payload.");
            cf.Data.AsSpan(0, count).CopyTo(result.AsSpan(offset));
            offset += count; expectedSeq = (expectedSeq + 1) & 0xF; inBlock++;
            if (offset < result.Length && _options.ReceiveBlockSize != 0 && inBlock == _options.ReceiveBlockSize)
            {
                await _link.SendAsync(IsoTpFrameCodec.EncodeFlow(IsoTpFlowStatus.Continue,
                    _options.ReceiveBlockSize, _options.ReceiveStMin), ct).ConfigureAwait(false);
                inBlock = 0;
            }
        }
        return result;
    }
}
