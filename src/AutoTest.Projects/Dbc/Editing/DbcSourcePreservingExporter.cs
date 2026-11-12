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

    internal static string Rewrite(string original, DbcDatabase draft)
    {
        var before = DbcParserAdapter.Parse(original, draft.Name);
        if (before.Messages.Length != draft.Messages.Length ||
            !before.Messages.Select(m => m.Id).Order().SequenceEqual(draft.Messages.Select(m => m.Id).Order()))
            throw Conflict("添加、删除或修改 CAN ID 不受保留原文模式支持。");

        foreach (var oldMessage in before.Messages)
        {
            var editedMessage = draft.Messages.Single(m => m.Id == oldMessage.Id);
            if (oldMessage.Signals.Length != editedMessage.Signals.Length ||
                !oldMessage.Signals.Select(s => s.Name).Order().SequenceEqual(
                    editedMessage.Signals.Select(s => s.Name).Order()))
                throw Conflict("新增、删除或重命名信号需要同时更新注释和引用，当前禁止。");
        }

        // All newly generated BO_/SG_ lines come from the one existing serializer.
        var exported = DbcParserAdapter.Export(draft);
        var messageLines = new Dictionary<uint, string>();
        var signalLines = new Dictionary<(uint MessageId, string Name), string>();
        uint? currentExportId = null;
        foreach (var line in exported.Split('\n'))
        {
            var message = MessageHeader.Match(line);
            if (message.Success)
            {
                currentExportId = uint.Parse(message.Groups[1].Value, CultureInfo.InvariantCulture);
                messageLines.Add(currentExportId.Value, line);
                continue;
            }
            var signal = SignalHeader.Match(line);
            if (signal.Success && currentExportId is not null)
                signalLines.Add((currentExportId.Value, signal.Groups[1].Value), line);
        }

        var outText = new StringBuilder(original.Length + 128);
        uint? currentId = null;
        foreach (var part in Regex.Split(original, @"(?<=\n)"))
        {
            if (part.Length == 0) continue;
            var end = part.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" :
                      part.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "";
            var content = end.Length == 0 ? part : part[..^end.Length];
            var updated = content;
            var message = MessageHeader.Match(content);
            if (message.Success)
            {
                currentId = uint.Parse(message.Groups[1].Value, CultureInfo.InvariantCulture);
                var old = before.FindMessage(currentId.Value);
                var draftMessage = draft.FindMessage(currentId.Value);
                if (old.Name != draftMessage.Name || old.Dlc != draftMessage.Dlc ||
                    old.Transmitter != draftMessage.Transmitter)
                    updated = messageLines[currentId.Value];
            }
            else
            {
                var signal = SignalHeader.Match(content);
                if (signal.Success && currentId is not null)
                {
                    var oldSignal = before.FindSignal(currentId.Value, signal.Groups[1].Value);
                    var editedSignal = draft.FindSignal(currentId.Value, signal.Groups[1].Value);
                    if (oldSignal != editedSignal)
                        updated = signalLines[(currentId.Value, signal.Groups[1].Value)];
                }
            }
            outText.Append(updated).Append(end);
        }
        return outText.ToString();
    }

    private static ProjectDataException Conflict(string message) =>
        new("DBC_SOURCE_PRESERVE", message);
}
