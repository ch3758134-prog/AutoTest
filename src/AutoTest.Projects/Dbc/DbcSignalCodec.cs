using AutoTest.Projects.Project;
namespace AutoTest.Projects.Dbc;

public static class DbcSignalCodec
{
    public static int[] BitPositions(DbcSignal signal)
    {
        var positions = new int[signal.BitLength];
        if (signal.Intel)
        {
            for (var i = 0; i < positions.Length; i++) positions[i] = signal.StartBit + i;
        }
        else
        {
            // DBC Motorola sawtooth convention: start bit is MSB; bit 0 is byte LSB.
            var pos = signal.StartBit;
            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] = pos;
                pos = pos % 8 == 0 ? pos + 15 : pos - 1;
            }
        }
        return positions;
    }
    public static ulong DecodeRaw(DbcSignal signal, ReadOnlySpan<byte> data)
    {
        signal.Validate(data.Length);
        ulong raw = 0;
        var bits = BitPositions(signal);
        for (int i = 0; i < bits.Length; i++)
        {
            var pos = bits[i];
            var value = (ulong)((data[pos / 8] >> (pos % 8)) & 1);
            raw |= value << (signal.Intel ? i : bits.Length - 1 - i);
        }
        return raw;
    }
    public static long ToSigned(ulong raw, int width)
    {
        if (width is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(width));
        if (width == 64) return unchecked((long)raw);
        var mask = (1UL << width) - 1;
        raw &= mask;
        return (raw & (1UL << (width - 1))) != 0 ? unchecked((long)(raw | ~mask)) : (long)raw;
    }
    public static double ToPhysical(DbcSignal signal, ulong raw) =>
        (signal.Signed ? ToSigned(raw, signal.BitLength) : (double)raw) * signal.Factor + signal.Offset;
    public static double DecodePhysical(DbcSignal signal, ReadOnlySpan<byte> bytes) => ToPhysical(signal, DecodeRaw(signal, bytes));
    public static void EncodeRaw(DbcSignal signal, ulong raw, Span<byte> bytes)
    {
        signal.Validate(bytes.Length);
        if (signal.BitLength < 64 && raw >> signal.BitLength != 0)
            throw new ProjectDataException("DBC_VALUE_RANGE", "Raw value exceeds signal bit length.");
        var bits = BitPositions(signal);
        for (var i = 0; i < bits.Length; i++)
        {
            var pos = bits[i]; var mask = (byte)(1 << (pos % 8));
            var bit = (raw >> (signal.Intel ? i : bits.Length - 1 - i)) & 1;
            if (bit != 0) bytes[pos / 8] |= mask; else bytes[pos / 8] &= unchecked((byte)~mask);
        }
    }
    public static void EncodePhysical(DbcSignal signal, double physical, Span<byte> bytes)
    {
        var value = (physical - signal.Offset) / signal.Factor;
        if (!double.IsFinite(value) || value != Math.Round(value))
            throw new ProjectDataException("DBC_VALUE_RANGE", "Physical value is not representable exactly.");
        var min = signal.Signed ? -Math.Pow(2, signal.BitLength - 1) : 0;
        var max = signal.Signed ? Math.Pow(2, signal.BitLength - 1) - 1 : Math.Pow(2, signal.BitLength) - 1;
        if (value < min || value > max) throw new ProjectDataException("DBC_VALUE_RANGE", "Physical value exceeds raw range.");
        ulong raw = signal.Signed ? unchecked((ulong)checked((long)value)) : checked((ulong)value);
        if (signal.BitLength < 64) raw &= (1UL << signal.BitLength) - 1;
        EncodeRaw(signal, raw, bytes);
    }
}
