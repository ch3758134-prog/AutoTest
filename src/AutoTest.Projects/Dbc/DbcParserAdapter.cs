using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoTest.Projects.Project;
namespace AutoTest.Projects.Dbc;

public static class DbcParserAdapter
{
    private static readonly Regex Message = new(@"^BO_\s+(\d+)\s+(\w+)\s*:\s*(\d+)\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex Signal = new("^SG_\\s+(\\w+)\\s*(M|m\\d+)?\\s*:\\s*(\\d+)\\|(\\d+)@([01])([+-])\\s*\\(([^,]+),([^)]+)\\)\\s*\\[([^|]+)\\|([^]]+)\\]\\s*\"([^\"]*)\"\\s*(.*)$", RegexOptions.Compiled);
    public static DbcDatabase Parse(string text, string name = "Imported")
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ProjectDataException("DBC_PARSE", "Empty DBC source.");
        var messages = new List<DbcMessage>();
        var nodes = new List<string>();
        DbcMessage? current = null;
        var lineNumber = 0;
        foreach (var sourceLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            lineNumber++;
            var line = sourceLine.Trim();
            if (line.StartsWith("BU_:", StringComparison.Ordinal))
            {
                nodes.AddRange(line[4..].Split(' ', StringSplitOptions.RemoveEmptyEntries)); continue;
            }
            if (line.StartsWith("BO_ ", StringComparison.Ordinal))
            {
                var m = Message.Match(line);
                if (!m.Success) throw new ProjectDataException("DBC_PARSE", $"Malformed BO_ at line {lineNumber}.");
                current = new DbcMessage(uint.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    m.Groups[2].Value, int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), m.Groups[4].Value,
                    ImmutableArray<DbcSignal>.Empty);
                messages.Add(current); continue;
            }
            if (line.StartsWith("SG_ ", StringComparison.Ordinal))
            {
                var m = Signal.Match(line);
                if (!m.Success || current is null) throw new ProjectDataException("DBC_PARSE", $"Malformed or orphan SG_ at line {lineNumber}.");
                double num(int index) => double.Parse(m.Groups[index].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                var signal = new DbcSignal(m.Groups[1].Value, int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture), m.Groups[5].Value == "1", m.Groups[6].Value == "-",
                    num(7), num(8), num(9), num(10), m.Groups[11].Value,
                    m.Groups[12].Value.Split(',')[0].Trim(), m.Groups[2].Success ? m.Groups[2].Value : null);
                current = current with { Signals = current.Signals.Add(signal) };
                messages[^1] = current;
            }
        }
        if (messages.Count == 0) throw new ProjectDataException("DBC_PARSE", "No BO_ messages found.");
        return new DbcDatabase(name, 1, nodes.Distinct().ToImmutableArray(), messages.ToImmutableArray()).Validate();
    }
    public static bool TryParse(string text, out DbcDatabase? database, out string? error)
    {
        try { database = Parse(text); error = null; return true; }
        catch (Exception e) when (e is ProjectDataException or FormatException or OverflowException)
        { database = null; error = e.Message; return false; }
    }
    public static string Export(DbcDatabase database)
    {
        database.Validate();
        var b = new StringBuilder("VERSION \"\"\n\nNS_ :\n\nBS_:\n\nBU_: ");
        b.AppendLine(string.Join(" ", database.Nodes)); b.AppendLine();
        foreach (var m in database.Messages)
        {
            b.AppendLine($"BO_ {m.Id} {m.Name}: {m.Dlc} {m.Transmitter}");
            foreach (var s in m.Signals)
            {
                string number(double x) => x.ToString("R", CultureInfo.InvariantCulture);
                b.AppendLine($" SG_ {s.Name}{(s.Multiplexer is null ? "" : " " + s.Multiplexer)} : {s.StartBit}|{s.BitLength}@{(s.Intel ? 1 : 0)}{(s.Signed ? '-' : '+')} ({number(s.Factor)},{number(s.Offset)}) [{number(s.Minimum)}|{number(s.Maximum)}] \"{s.Unit}\" {s.Receiver}");
            }
            b.AppendLine();
        }
        return b.ToString();
    }
}
