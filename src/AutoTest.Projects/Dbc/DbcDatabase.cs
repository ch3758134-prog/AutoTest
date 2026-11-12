using System.Collections.Immutable;
using AutoTest.Projects.Project;
namespace AutoTest.Projects.Dbc;

public sealed record DbcSignal(string Name, int StartBit, int BitLength, bool Intel, bool Signed,
    double Factor, double Offset, double Minimum, double Maximum, string Unit, string Receiver,
    string? Multiplexer = null)
{
    public void Validate(int dlc)
    {
        if (string.IsNullOrWhiteSpace(Name) || BitLength is < 1 or > 64 || StartBit is < 0 or > 63 ||
            !double.IsFinite(Factor) || Factor == 0 || !double.IsFinite(Offset) ||
            !double.IsFinite(Minimum) || !double.IsFinite(Maximum) || Minimum > Maximum)
            throw new ProjectDataException("DBC_SIGNAL", $"Invalid signal definition: {Name}.");
        var positions = DbcSignalCodec.BitPositions(this);
        if (positions.Any(bit => bit < 0 || bit >= dlc * 8))
            throw new ProjectDataException("DBC_SIGNAL_RANGE", $"Signal {Name} exceeds message payload.");
    }
}
public sealed record DbcMessage(uint Id, string Name, int Dlc, string Transmitter, ImmutableArray<DbcSignal> Signals)
{
    // Preserve the exact BO_ integer, including Vector's 0x80000000 extended-frame
    // marker. Never confuse it with the physical 29-bit CAN arbitration ID.
    public bool IsExtended => (Id & 0x80000000u) != 0 || Id > 0x7FF;
    public uint ArbitrationId => Id & 0x1FFFFFFFu;

    public void Validate()
    {
        if ((Id & 0x60000000u) != 0)
            throw new ProjectDataException("DBC_MESSAGE_ID",
                $"Invalid DBC identifier 0x{Id:X8} for {Name}: bits 29 and 30 must be zero.");
        if (string.IsNullOrWhiteSpace(Name) || Dlc is < 0 or > 8 || Signals.IsDefault)
            throw new ProjectDataException("DBC_MESSAGE",
                $"Invalid DBC message: {Name} (encoded ID=0x{Id:X8}, DLC={Dlc}).");
        foreach (var signal in Signals) signal.Validate(Dlc);
        if (Signals.GroupBy(s => s.Name, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ProjectDataException("DBC_SIGNAL_DUPLICATE", "Duplicate signal name.");
        // Overlap is legal only when both signals belong to distinct multiplexed branches.
        for (int i = 0; i < Signals.Length; i++)
        for (int j = 0; j < i; j++)
            if (DbcSignalCodec.BitPositions(Signals[i]).Intersect(DbcSignalCodec.BitPositions(Signals[j])).Any() &&
                !(Signals[i].Multiplexer?.StartsWith('m') == true && Signals[j].Multiplexer?.StartsWith('m') == true &&
                  Signals[i].Multiplexer != Signals[j].Multiplexer))
                throw new ProjectDataException("DBC_OVERLAP", $"Signals {Signals[i].Name} and {Signals[j].Name} overlap.");
    }
}
public sealed record DbcDatabase(string Name, int Revision, ImmutableArray<string> Nodes, ImmutableArray<DbcMessage> Messages)
{
    public static DbcDatabase Create(string name) => new(name, 1, ImmutableArray<string>.Empty, ImmutableArray<DbcMessage>.Empty);
    public DbcMessage FindMessage(uint id)
    {
        // DBC metadata (VAL_, CM_, BA_) uses the encoded BO_ integer; live CAN
        // observations use the unflagged arbitration ID. Support both without
        // interpreting 0x98FF5027 as a wire-frame identifier.
        var exact = Messages.FirstOrDefault(m => m.Id == id);
        if (exact is not null) return exact;
        var physicalMatches = Messages.Where(m => m.ArbitrationId == id).Take(2).ToArray();
        if (physicalMatches.Length > 1)
            throw new ProjectDataException("DBC_MESSAGE_AMBIGUOUS",
                $"Frame 0x{id:X} matches multiple standard/extended DBC messages.");
        return physicalMatches.FirstOrDefault() ??
            throw new ProjectDataException("DBC_MESSAGE_MISSING", $"CAN frame {id:X} not found.");
    }
    public DbcSignal FindSignal(uint id, string signal) => FindMessage(id).Signals.FirstOrDefault(s => s.Name == signal) ??
        throw new ProjectDataException("DBC_SIGNAL_MISSING", $"Signal {signal} not found.");
    public DbcDatabase Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Revision < 1 || Messages.IsDefault || Nodes.IsDefault)
            throw new ProjectDataException("DBC_DATABASE", "Invalid DBC database.");
        if (Messages.GroupBy(m => m.Id).Any(g => g.Count() > 1))
            throw new ProjectDataException("DBC_MESSAGE_DUPLICATE", "Duplicate CAN frame id.");
        foreach (var message in Messages) message.Validate();
        return this;
    }
}
