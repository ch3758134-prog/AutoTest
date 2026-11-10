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
public sealed record DbcMessage(uint Id, string Name, int Dlc, string Transmitter, ImmutableArray<DbcSignal> Signals, bool IsExtended = false)
{
    public void Validate()
    {
        if (Id > 0x1FFFFFFF || (!IsExtended && Id > 0x7FF) || string.IsNullOrWhiteSpace(Name) || Dlc is < 0 or > 8 || Signals.IsDefault)
            throw new ProjectDataException("DBC_MESSAGE", $"Invalid DBC message: {Name}.");
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

    public DbcMessage FindMessage(uint id, bool? extended = null)
    {
        var found = Messages.Where(m => m.Id == id && (!extended.HasValue || m.IsExtended == extended.Value)).ToArray();
        return found.Length switch
        {
            1 => found[0],
            0 => throw new ProjectDataException("DBC_MESSAGE_MISSING", $"CAN frame {id:X}/{extended} not found."),
            _ => throw new ProjectDataException("DBC_FRAME_AMBIGUOUS", $"ID 0x{id:X} is both 11/29-bit; specify frame format.")
        };
    }
    public DbcSignal FindSignal(uint id, string signal, bool? extended = null) =>
        FindMessage(id, extended).Signals.FirstOrDefault(s => s.Name == signal) ??
        throw new ProjectDataException("DBC_SIGNAL_MISSING", $"Signal {signal} not found.");
    public DbcDatabase Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Revision < 1 || Messages.IsDefault || Nodes.IsDefault)
            throw new ProjectDataException("DBC_DATABASE", "Invalid DBC database.");
        if (Messages.GroupBy(m => (m.Id, m.IsExtended)).Any(g => g.Count() > 1))
            throw new ProjectDataException("DBC_MESSAGE_DUPLICATE", "Duplicate CAN frame id.");
        foreach (var message in Messages) message.Validate();
        return this;
    }
}
