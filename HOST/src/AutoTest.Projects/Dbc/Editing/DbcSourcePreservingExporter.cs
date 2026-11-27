using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Dbc.Editing;

/// <summary>
/// Strict source-preserving parameter edits for DBCs containing comments and
/// attributes. Only existing BO_/SG_ records are replaced using the canonical
/// DbcParserAdapter exporter; every unrelated source line is copied verbatim.
/// Additions/deletions and renaming referenced signal/IDs require a separate
/// metadata-aware operation and are intentionally rejected here.
/// </summary>
internal static class DbcSourcePreservingExporter
{
    private static readonly Regex MessageHeader = new(
        @"^\s*BO_\s+(\d+)\s+\w+\s*:", RegexOptions.Compiled);
    private static readonly Regex SignalHeader = new(
        @"^\s*SG_\s+(\w+)\s*(?:M|m\d+)?\s*:", RegexOptions.Compiled);

    // References outside BO_/SG_ must never become orphaned after a delete.
    // Only the known source-preserving grammar is eligible; unfamiliar directives
    // are rejected by DbcWorkbenchDocument.Import before any mutation.
    internal static void AssertMayDeleteMessage(string original, uint messageId) =>
        AssertNoExternalReferences(original, messageId, null);

    internal static void AssertMayDeleteSignal(string original, uint messageId, string signalName) =>
        AssertNoExternalReferences(original, messageId, signalName);

    private static void AssertNoExternalReferences(string original, uint messageId, string? signalName)
    {
        var id = messageId.ToString(CultureInfo.InvariantCulture);
        var pattern = signalName is null
            ? @"\b(?:BO_|SG_|VAL_)\s+" + id + @"\b"
            : @"\b(?:SG_|VAL_)\s+" + id + @"\s+" +
                Regex.Escape(signalName) + @"(?:\s|;|$)";
        foreach (var line in original.Replace("\r\n", "\n").Split('\n'))
        {
            var text = line.TrimStart();
            if ((text.StartsWith("CM_ ", StringComparison.Ordinal) ||
                 text.StartsWith("BA_ ", StringComparison.Ordinal) ||
                 text.StartsWith("VAL_ ", StringComparison.Ordinal)) &&
                Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant))
                throw Conflict(signalName is null
                    ? "此报文仍被注释、枚举或属性引用，不能直接删除。"
                    : "此信号仍被注释、枚举或属性引用，不能直接删除。");
        }
    }

    internal static string Rewrite(string original, DbcDatabase draft)
    {
        var before = DbcParserAdapter.Parse(original, draft.Name,
            allowSignalOverlaps: true);
        var oldMessages = before.Messages.ToDictionary(m => m.Id);
        var newMessages = draft.Messages.ToDictionary(m => m.Id);

        foreach (var old in before.Messages)
        {
            if (!newMessages.TryGetValue(old.Id, out var edited))
            {
                AssertMayDeleteMessage(original, old.Id);
                continue;
            }
            foreach (var oldSignal in old.Signals)
                if (!edited.Signals.Any(s => s.Name == oldSignal.Name))
                    AssertMayDeleteSignal(original, old.Id, oldSignal.Name);
        }

        // Use the canonical exporter only for the BO_/SG_ records being edited.
        // Preserve VERSION, CM_, BA_, BA_DEF_, VAL_ and all other source lines.
        var exported = DbcParserAdapter.Export(draft);
        var messageLines = new Dictionary<uint, string>();
        var signalLines = new Dictionary<(uint MessageId, string Name), string>();
        uint? exportId = null;
        foreach (var line in exported.Split('\n'))
        {
            var message = MessageHeader.Match(line);
            if (message.Success)
            {
                exportId = uint.Parse(message.Groups[1].Value, CultureInfo.InvariantCulture);
                messageLines.Add(exportId.Value, line.TrimEnd('\r'));
                continue;
            }
            var signal = SignalHeader.Match(line);
            if (signal.Success && exportId is not null)
                signalLines.Add((exportId.Value, signal.Groups[1].Value), line.TrimEnd('\r'));
        }

        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var output = new StringBuilder(original.Length + 512);
        uint? currentId = null;
        var additionsWritten = false;

        void AddExtraSignals()
        {
            if (currentId is not uint id || !newMessages.TryGetValue(id, out var current))
                return;
            var originalNames = oldMessages[id].Signals.Select(s => s.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var signal in current.Signals)
                if (!originalNames.Contains(signal.Name))
                    output.Append(signalLines[(id, signal.Name)]).Append(newline);
        }

        void AddNewMessages()
        {
            if (additionsWritten) return;
            additionsWritten = true;
            foreach (var added in draft.Messages.Where(m => !oldMessages.ContainsKey(m.Id)))
            {
                output.Append(newline).Append(messageLines[added.Id]).Append(newline);
                foreach (var signal in added.Signals)
                    output.Append(signalLines[(added.Id, signal.Name)]).Append(newline);
            }
        }

        foreach (var part in Regex.Split(original, @"(?<=\n)"))
        {
            if (part.Length == 0) continue;
            var ending = part.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" :
                part.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "";
            var line = ending.Length == 0 ? part : part[..^ending.Length];
            var message = MessageHeader.Match(line);
            if (message.Success)
            {
                AddExtraSignals();
                currentId = uint.Parse(message.Groups[1].Value, CultureInfo.InvariantCulture);
                if (newMessages.TryGetValue(currentId.Value, out var edited))
                {
                    var old = oldMessages[currentId.Value];
                    output.Append(old.Name == edited.Name && old.Dlc == edited.Dlc &&
                        old.Transmitter == edited.Transmitter
                        ? line : messageLines[currentId.Value]).Append(ending);
                }
                continue;
            }
            var signal = SignalHeader.Match(line);
            if (signal.Success && currentId is uint id && oldMessages.ContainsKey(id))
            {
                if (newMessages.TryGetValue(id, out var edited))
                {
                    var newSignal = edited.Signals.FirstOrDefault(s => s.Name == signal.Groups[1].Value);
                    if (newSignal is not null)
                    {
                        var oldSignal = before.FindSignal(id, newSignal.Name);
                        output.Append(oldSignal == newSignal ? line :
                            signalLines[(id, newSignal.Name)]).Append(ending);
                    }
                }
                continue;
            }
            // First non-empty non-signal line after the last BO_/SG_ group:
            // insert new records BEFORE CM_/BA_/VAL_ reference metadata.
            if (currentId is not null && !string.IsNullOrWhiteSpace(line))
            {
                AddExtraSignals();
                AddNewMessages();
                currentId = null;
            }
            output.Append(line).Append(ending);
        }
        AddExtraSignals();
        AddNewMessages();
        return output.ToString();
    }

    private static ProjectDataException Conflict(string message) =>
        new("DBC_SOURCE_PRESERVE", message);
}
