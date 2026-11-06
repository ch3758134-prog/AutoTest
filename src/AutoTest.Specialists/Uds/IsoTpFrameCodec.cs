using System;

namespace AutoTest.Specialists.Uds;

public enum IsoTpFrameKind { Single, First, Consecutive, FlowControl }
public enum IsoTpFlowStatus : byte { Continue = 0, Wait = 1, Overflow = 2 }
public sealed class IsoTpProtocolException : IOException
{
    public IsoTpProtocolException(string message) : base(message) { }
}
public sealed record IsoTpFirstFrame(int Length, byte[] InitialData);
public sealed record IsoTpConsecutiveFrame(byte Sequence, byte[] Data);
public sealed record IsoTpFlowControl(IsoTpFlowStatus Status, byte BlockSize, TimeSpan StMin);

// ISO 15765-2 classic-CAN normal addressing. Every encoded payload is unpadded.
public static class IsoTpFrameCodec
{
    public const int MaxClassicPdu = 4095;
    public static IsoTpFrameKind Kind(ReadOnlySpan<byte> frame)
    {
        if (frame.Length is < 1 or > 8) throw new IsoTpProtocolException("Invalid classic CAN payload length.");
        return (frame[0] >> 4) switch
        {
            0 => IsoTpFrameKind.Single, 1 => IsoTpFrameKind.First,
            2 => IsoTpFrameKind.Consecutive, 3 => IsoTpFrameKind.FlowControl,
            _ => throw new IsoTpProtocolException("Unknown ISO-TP PCI type.")
        };
    }
    public static byte[] EncodeSingle(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(pdu));
        var frame = new byte[pdu.Length + 1]; frame[0] = (byte)pdu.Length;
        pdu.CopyTo(frame.AsSpan(1)); return frame;
    }
    public static byte[] ParseSingle(ReadOnlySpan<byte> frame)
    {
        if (Kind(frame) != IsoTpFrameKind.Single) throw new IsoTpProtocolException("Expected SF.");
        int count = frame[0] & 0xF;
        if (count is < 1 or > 7 || frame.Length < 1 + count)
            throw new IsoTpProtocolException("Invalid SF length.");
        return frame.Slice(1, count).ToArray();
    }
    public static byte[] EncodeFirst(int totalLength, ReadOnlySpan<byte> pdu)
    {
        if (totalLength is < 8 or > MaxClassicPdu || pdu.Length < 6)
            throw new ArgumentOutOfRangeException(nameof(totalLength));
        var frame = new byte[8]; frame[0] = (byte)(0x10 | (totalLength >> 8));
        frame[1] = (byte)totalLength; pdu.Slice(0, 6).CopyTo(frame.AsSpan(2)); return frame;
    }
    public static IsoTpFirstFrame ParseFirst(ReadOnlySpan<byte> frame)
    {
        if (Kind(frame) != IsoTpFrameKind.First || frame.Length != 8)
            throw new IsoTpProtocolException("Invalid FF length.");
        int count = ((frame[0] & 0xF) << 8) | frame[1];
        if (count < 8) throw new IsoTpProtocolException("Invalid 12-bit FF data length.");
        return new(count, frame.Slice(2, 6).ToArray());
    }
    public static byte[] EncodeConsecutive(byte sequence, ReadOnlySpan<byte> data)
    {
        if (sequence > 15 || data.Length is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(data));
        var result = new byte[data.Length + 1]; result[0] = (byte)(0x20 | sequence);
        data.CopyTo(result.AsSpan(1)); return result;
    }
    public static IsoTpConsecutiveFrame ParseConsecutive(ReadOnlySpan<byte> frame)
    {
        if (Kind(frame) != IsoTpFrameKind.Consecutive || frame.Length < 2)
            throw new IsoTpProtocolException("Invalid CF.");
        return new((byte)(frame[0] & 0xF), frame[1..].ToArray());
    }
    public static byte[] EncodeFlow(IsoTpFlowStatus status, byte blockSize, byte stMinRaw)
    {
        if ((byte)status > 2) throw new ArgumentOutOfRangeException(nameof(status));
        _ = DecodeStMin(stMinRaw);
        return [(byte)(0x30 | (byte)status), blockSize, stMinRaw];
    }
    public static IsoTpFlowControl ParseFlow(ReadOnlySpan<byte> frame)
    {
        if (Kind(frame) != IsoTpFrameKind.FlowControl || frame.Length < 3 || (frame[0] & 0xF) > 2)
            throw new IsoTpProtocolException("Invalid FC.");
        return new((IsoTpFlowStatus)(frame[0] & 0xF), frame[1], DecodeStMin(frame[2]));
    }
    public static TimeSpan DecodeStMin(byte raw) => raw switch
    {
        <= 0x7F => TimeSpan.FromMilliseconds(raw),
        >= 0xF1 and <= 0xF9 => TimeSpan.FromTicks((raw - 0xF0) * 1000L),
        _ => throw new IsoTpProtocolException("Reserved STmin encoding.")
    };
}
