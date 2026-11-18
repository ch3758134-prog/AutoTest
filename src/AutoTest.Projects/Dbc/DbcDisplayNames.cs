using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Dbc;

/// <summary>
/// User-facing DBC names. The BO_/SG_ identifiers remain the only protocol keys.
/// Explicit aliases use standard DBC BA_DEF_/BA_ attributes; existing Chinese CM_
/// comments can provide a read-only fallback. No automatic translation is attempted.
/// </summary>
public sealed class DbcDisplayNames
{
    private const string MessageAttribute = "AutoTestMessageAlias";
    private const string SignalAttribute = "AutoTestSignalAlias";
    private static readonly Regex MessageAliasLine = new(
        @"^\s*BA_\s+""AutoTestMessageAlias""\s+BO_\s+(\d+)\s+""((?:\\.|[^""\\])*)""\s*;\s*$",
        RegexOptions.Compiled);
    private static readonly Regex SignalAliasLine = new(
        @"^\s*BA_\s+""AutoTestSignalAlias""\s+SG_\s+(\d+)\s+(\w+)\s+""((?:\\.|[^""\\])*)""\s*;\s*$",
        RegexOptions.Compiled);
    private static readonly Regex MessageComment = new(
        @"^\s*CM_\s+BO_\s+(\d+)\s+""((?:\\.|[^""\\])*)""\s*;\s*$",
        RegexOptions.Compiled);
    private static readonly Regex SignalComment = new(
        @"^\s*CM_\s+SG_\s+(\d+)\s+(\w+)\s+""((?:\\.|[^""\\])*)""\s*;\s*$",
        RegexOptions.Compiled);
    private static readonly Regex OwnedDefinition = new(
        @"^\s*BA_DEF_\s+(?:BO_|SG_)\s+""AutoTest(?:Message|Signal)Alias""\s+STRING\s*;\s*$",
        RegexOptions.Compiled);

    private readonly Dictionary<uint, string> _explicitMessages = new();
    private readonly Dictionary<(uint Id, string Signal), string> _explicitSignals = new();
    private readonly Dictionary<uint, string> _commentMessages = new();
    private readonly Dictionary<(uint Id, string Signal), string> _commentSignals = new();

    public static DbcDisplayNames Parse(string text)
    {
        var names = new DbcDisplayNames();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            var match = MessageComment.Match(line);
            if (match.Success && TryId(match.Groups[1].Value, out var id))
            {
                var label = Decode(match.Groups[2].Value);
                if (ContainsChinese(label)) names._commentMessages[id] = label.Trim();
                continue;
            }
            match = SignalComment.Match(line);
            if (match.Success && TryId(match.Groups[1].Value, out id))
            {
                var label = Decode(match.Groups[3].Value);
                if (ContainsChinese(label))
                    names._commentSignals[(id, match.Groups[2].Value)] = label.Trim();
                continue;
            }
            match = MessageAliasLine.Match(line);
            if (match.Success && TryId(match.Groups[1].Value, out id))
            {
                names._explicitMessages[id] = Decode(match.Groups[2].Value).Trim();
                continue;
            }
            match = SignalAliasLine.Match(line);
            if (match.Success && TryId(match.Groups[1].Value, out id))
                names._explicitSignals[(id, match.Groups[2].Value)] =
                    Decode(match.Groups[3].Value).Trim();
        }
        return names;
    }

    public string MessageAlias(uint encodedMessageId) =>
        _explicitMessages.TryGetValue(encodedMessageId, out var value) ? value :
        _commentMessages.GetValueOrDefault(encodedMessageId, "");

    public string SignalAlias(uint encodedMessageId, string signalName) =>
        _explicitSignals.TryGetValue((encodedMessageId, signalName), out var value) ? value :
        _commentSignals.GetValueOrDefault((encodedMessageId, signalName), "");

    public string MessageLabel(DbcMessage message)
    {
        var chinese = MessageAlias(message.Id);
        return string.IsNullOrWhiteSpace(chinese) ? message.Name : chinese + " · " + message.Name;
    }

    public string SignalLabel(DbcMessage message, DbcSignal signal)
    {
        var chinese = SignalAlias(message.Id, signal.Name);
        return string.IsNullOrWhiteSpace(chinese) ? signal.Name : chinese + " · " + signal.Name;
    }

    public void SetMessageAlias(uint messageId, string? alias)
    {
        var normalized = Normalize(alias);
        if (normalized.Length == 0) _explicitMessages.Remove(messageId);
        else _explicitMessages[messageId] = normalized;
    }

    public void SetSignalAlias(uint messageId, string signalName, string? alias)
    {
        if (string.IsNullOrWhiteSpace(signalName))
            throw new ArgumentException("请先选择信号。", nameof(signalName));
        var normalized = Normalize(alias);
        if (normalized.Length == 0) _explicitSignals.Remove((messageId, signalName));
        else _explicitSignals[(messageId, signalName)] = normalized;
    }

    /// <summary>Rewrites only owned alias records; unrelated DBC lines remain unchanged.</summary>
    public string Rewrite(string source)
    {
        var output = new StringBuilder(source.Length + 256);
        // Preserve the byte-level newline convention and every unrelated line.
        // Removing an AutoTest-owned attribute never rewrites CM_, VAL_, BO_ or SG_.
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        foreach (var segment in Regex.Split(source, @"(?<=\n)"))
        {
            if (segment.Length == 0) continue;
            var line = segment.TrimEnd('\r', '\n');
            if (OwnedDefinition.IsMatch(line) ||
                MessageAliasLine.IsMatch(line) || SignalAliasLine.IsMatch(line))
                continue;
            output.Append(segment);
        }
        if ((_explicitMessages.Count > 0 || _explicitSignals.Count > 0) &&
            output.Length > 0 && output[^1] != '\n')
            output.Append(newline);
        if (_explicitMessages.Count > 0)
        {
            output.Append("BA_DEF_ BO_ \"AutoTestMessageAlias\" STRING ;").Append(newline);
            foreach (var pair in _explicitMessages.OrderBy(x => x.Key))
                output.Append("BA_ \"").Append(MessageAttribute).Append("\" BO_ ")
                    .Append(pair.Key.ToString(CultureInfo.InvariantCulture)).Append(" \"")
                    .Append(Encode(pair.Value)).Append("\";").Append(newline);
        }
        if (_explicitSignals.Count > 0)
        {
            output.Append("BA_DEF_ SG_ \"AutoTestSignalAlias\" STRING ;").Append(newline);
            foreach (var pair in _explicitSignals.OrderBy(x => x.Key.Id).ThenBy(x => x.Key.Signal,
                StringComparer.Ordinal))
                output.Append("BA_ \"").Append(SignalAttribute).Append("\" SG_ ")
                    .Append(pair.Key.Id.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(pair.Key.Signal).Append(" \"").Append(Encode(pair.Value))
                    .Append("\";").Append(newline);
        }
        return output.ToString();
    }

    public bool SameExplicitNames(DbcDisplayNames other) =>
        _explicitMessages.Count == other._explicitMessages.Count &&
        _explicitSignals.Count == other._explicitSignals.Count &&
        _explicitMessages.All(x => other._explicitMessages.TryGetValue(x.Key, out var v) && v == x.Value) &&
        _explicitSignals.All(x => other._explicitSignals.TryGetValue(x.Key, out var v) && v == x.Value);

    private static string Normalize(string? input)
    {
        var value = (input ?? "").Trim();
        if (value.Length > 100 || value.Any(c => char.IsControl(c)))
            throw new ProjectDataException("DBC_ALIAS", "中文别名最长 100 字，不允许换行或控制字符。");
        return value;
    }

    private static bool ContainsChinese(string label) => label.Any(c => c is >= '\u3400' and <= '\u9FFF');
    private static bool TryId(string text, out uint id) =>
        uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    private static string Decode(string value) => value.Replace("\\\"", "\"").Replace("\\\\", "\\");
    private static string Encode(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
