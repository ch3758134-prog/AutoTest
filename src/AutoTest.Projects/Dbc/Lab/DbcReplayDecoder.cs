using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Dbc.Lab;

public sealed record DbcDecodedValue(uint MessageId, string MessageName, string SignalName,
    double PhysicalValue, string Unit, ulong RawValue, string? ValueDescription);

// Domain-level event for Integration/Wave to adapt into its single shared signal observation contract.
public sealed record DbcRevisionBinding(Guid DbcAssetId, int Revision, string Sha256);

public sealed record DbcReplayResult(Guid OperationId, string Source, string TimeQuality, DateTimeOffset ObservedAt,
    string DbcSha256, int DbcRevision, string RevisionQuality, uint MessageId, ImmutableArray<byte> RawFrame,
    long LostCount, ImmutableArray<DbcDecodedValue> Signals);

public static class DbcReplayDecoder
{
    public static DbcReplayResult Decode(DbcWorkbenchDocument document, uint messageId,
        ReadOnlySpan<byte> bytes, DateTimeOffset timestamp, long lostFrames = 0, DbcRevisionBinding? revision = null)
    {
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.ExportText()))).ToLowerInvariant();
        return DecodeWithVerifiedSourceHash(document, messageId, bytes, timestamp, lostFrames, revision, contentHash);
    }

    // Internal fast path for a frozen document snapshot whose SHA was checked once before a bounded archive loop.
    // Keeping this internal prevents a caller from supplying an arbitrary hash to the public Decode contract.
    internal static DbcReplayResult DecodeWithVerifiedSourceHash(DbcWorkbenchDocument document, uint messageId,
        ReadOnlySpan<byte> bytes, DateTimeOffset timestamp, long lostFrames,
        DbcRevisionBinding? revision, string contentHash)
    {
        if (timestamp == default || lostFrames < 0) throw new ProjectDataException("DBC_REPLAY_TIME", "Invalid Replay time or loss count.");
        if (revision is not null && (revision.DbcAssetId == Guid.Empty || revision.Revision < 1 ||
            !string.Equals(contentHash, revision.Sha256, StringComparison.OrdinalIgnoreCase)))
            throw new ProjectDataException("REFERENCE_STALE", "DBC asset revision/fingerprint mismatch.");
        var message = document.Model.FindMessage(messageId);
        if (bytes.Length != message.Dlc) throw new ProjectDataException("DBC_REPLAY_DLC", "Replay payload length does not match DBC DLC.");
        var decoded = ImmutableArray.CreateBuilder<DbcDecodedValue>();
        foreach (var signal in message.Signals)
        {
            // Multiplexed signals require a selector-aware decoder: refuse to guess which branch is active.
            if (!string.IsNullOrEmpty(signal.Multiplexer))
                throw new ProjectDataException("DBC_MUX_REPLAY", "Multiplexing requires selector-aware decode; unsupported in this slice.");
            var raw = DbcSignalCodec.DecodeRaw(signal, bytes);
            var physical = DbcSignalCodec.ToPhysical(signal, raw);
            var valueTable = document.ValueTables.FirstOrDefault(t => t.MessageId == messageId && t.SignalName == signal.Name);
            var key = signal.Signed ? DbcSignalCodec.ToSigned(raw, signal.BitLength) :
                raw <= long.MaxValue ? (long)raw : long.MinValue;
            var label = valueTable is not null && (raw <= long.MaxValue || signal.Signed) &&
                valueTable.Labels.TryGetValue(key, out var matched) ? matched : null;
            decoded.Add(new DbcDecodedValue(messageId, message.Name, signal.Name, physical, signal.Unit, raw, label));
        }
        return new(Guid.NewGuid(), "Replay", "ReplayTimestamp", timestamp, contentHash,
            revision?.Revision ?? 0, revision is null ? "Unbound" : "Bound", messageId, ImmutableArray.CreateRange(bytes.ToArray()), lostFrames,
            decoded.ToImmutable());
    }
    public static byte[] ParseHex(string hex)
    {
        var normalized = new string(hex.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != ':').ToArray());
        if (normalized.Length % 2 != 0 || normalized.Length > 16 || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new ProjectDataException("DBC_REPLAY_HEX", "Expected 0..8 classic CAN bytes in hex.");
        return Convert.FromHexString(normalized);
    }
}
