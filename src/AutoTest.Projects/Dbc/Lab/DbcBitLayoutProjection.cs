using System.Collections.Immutable;
using System.Globalization;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Dbc.Lab;

/// <summary>
/// Read-only DBC bit allocation projection for the PROJECT diagnostic editor.
/// Bit positions reuse DbcSignalCodec.BitPositions (the same Intel/Motorola
/// convention as decoding). A layout preview never claims observed raw bits.
/// </summary>
public sealed record DbcByteLayoutRow(int ByteIndex, string RawHex,
    string Bit7, string Bit6, string Bit5, string Bit4,
    string Bit3, string Bit2, string Bit1, string Bit0);

public sealed record DbcSignalLayoutRow(string SignalName, int StartBit, int Length,
    string ByteOrder, string Signedness, string BitPositions);

public sealed record DbcBitLayoutSnapshot(uint MessageId, int Dlc, string Source,
    string Quality, ImmutableArray<DbcByteLayoutRow> Bytes,
    ImmutableArray<DbcSignalLayoutRow> Signals);

public static class DbcBitLayoutProjection
{
    public static DbcBitLayoutSnapshot Preview(DbcMessage message) => Create(message, null);

    public static DbcBitLayoutSnapshot FromReplay(DbcMessage message, ReadOnlySpan<byte> raw)
    {
        if (raw.Length != message.Dlc)
            throw new ProjectDataException("DBC_REPLAY_DLC", "Raw replay DLC does not match the selected DBC frame.");
        return Create(message, raw.ToArray());
    }

    private static DbcBitLayoutSnapshot Create(DbcMessage message, byte[]? raw)
    {
        message.Validate();
        var occupants = new List<string>[message.Dlc * 8];
        var signalRows = ImmutableArray.CreateBuilder<DbcSignalLayoutRow>();
        foreach (var signal in message.Signals)
        {
            var positions = DbcSignalCodec.BitPositions(signal);
            signalRows.Add(new DbcSignalLayoutRow(signal.Name, signal.StartBit, signal.BitLength,
                signal.Intel ? "Intel / little-endian" : "Motorola / big-endian",
                signal.Signed ? "signed" : "unsigned", string.Join(",", positions)));
            foreach (var pos in positions)
            {
                occupants[pos] ??= [];
                occupants[pos].Add(signal.Name + (signal.Multiplexer is null ? "" : "(" + signal.Multiplexer + ")"));
            }
        }

        string DescribeBit(int pos)
        {
            var names = occupants[pos];
            var label = names is null || names.Count == 0 ? "·" : string.Join("/", names);
            // Unknown raw bits remain ?, even when a signal is assigned to the bit.
            var bit = raw is null ? "?" : ((raw[pos / 8] >> (pos % 8)) & 1).ToString(CultureInfo.InvariantCulture);
            return label + ":" + bit;
        }

        var rows = ImmutableArray.CreateBuilder<DbcByteLayoutRow>();
        for (var b = 0; b < message.Dlc; b++)
        {
            var start = b * 8;
            rows.Add(new DbcByteLayoutRow(b, raw is null ? "??" : raw[b].ToString("X2", CultureInfo.InvariantCulture),
                DescribeBit(start + 7), DescribeBit(start + 6), DescribeBit(start + 5), DescribeBit(start + 4),
                DescribeBit(start + 3), DescribeBit(start + 2), DescribeBit(start + 1), DescribeBit(start)));
        }
        var mux = message.Signals.Any(s => s.Multiplexer is not null);
        var quality = mux ? "MUX_BRANCH_UNRESOLVED (layout only; no branch selected)" :
            raw is null ? "DEFINITION_ONLY (no observed bits)" : "REPLAY_INPUT (not physical CAN RX)";
        return new DbcBitLayoutSnapshot(message.Id, message.Dlc,
            raw is null ? "DBC_DEFINITION" : "REPLAY", quality, rows.ToImmutable(), signalRows.ToImmutable());
    }
}
