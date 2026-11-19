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
    private const string MessageSummaryAttribute = "AutoTestMessageSummary";
    private const string SignalSummaryAttribute = "AutoTestSignalSummary";
    private static readonly Regex MessageAliasLine = new(
        @"^\s*BA_\s+""AutoTestMessageAlias""\s+BO_\s+(\d+)\s+""((?:\\.|[^""\\])*)""\s*;\s*$",
        RegexOptions.Compiled);
    private static readonly Regex SignalAliasLine = new(
        @"^\s*BA_\s+""AutoTestSignalAlias""\s+SG_\s+(\d+)\s+(\w+)\s+""((?:\\.|[^""\\])*)""\s*;\s*$",
        RegexOptions.Compiled);
    private static readonly Regex MessageSummaryLine = new(
        @"^\s*BA_\s+""AutoTestMessageSummary""\s+BO_\s+(\d+)\s+""((?:\\.|[^""\\])*)""\s*;\s*$",
        RegexOptions.Compiled);
    private static readonly Regex SignalSummaryLine = new(
        @"^\s*BA_\s+""AutoTestSignalSummary""\s+SG_\s+(\d+)\s+(\w+)\s+""((?:\\.|[^""\\])*)""\s*;\s*$",
        RegexOptions.Compiled);
    private static readonly Regex MessageComment = new(
        @"^\s*CM_\s+BO_\s+(\d+)\s+""((?:\\.|[^""\\])*)""\s*;\s*$",
        RegexOptions.Compiled);
    private static readonly Regex SignalComment = new(
        @"^\s*CM_\s+SG_\s+(\d+)\s+(\w+)\s+""((?:\\.|[^""\\])*)""\s*;\s*$",
        RegexOptions.Compiled);
    private static readonly Regex OwnedDefinition = new(
        @"^\s*BA_DEF_\s+(?:BO_|SG_)\s+""AutoTest(?:Message|Signal)(?:Alias|Summary)""\s+STRING\s*;\s*$",
        RegexOptions.Compiled);

    private readonly Dictionary<uint, string> _explicitMessages = new();
    private readonly Dictionary<(uint Id, string Signal), string> _explicitSignals = new();
    private readonly Dictionary<uint, string> _commentMessages = new();
    private readonly Dictionary<(uint Id, string Signal), string> _commentSignals = new();
    private readonly Dictionary<uint, string> _explicitMessageSummaries = new();
    private readonly Dictionary<(uint Id, string Signal), string> _explicitSignalSummaries = new();
    private readonly Dictionary<uint, string> _messageDescriptions = new();
    // Keep edits separate from imported CM_ statements: a save must touch
    // only comments explicitly changed by the operator, never unrelated source.
    private readonly Dictionary<uint, string> _editedMessageComments = new();
    private readonly Dictionary<(uint Id, string Signal), string> _editedSignalComments = new();
    private readonly Dictionary<(uint Id, string Signal), string> _signalDescriptions = new();

    public static DbcDisplayNames Parse(string text)
    {
        var names = new DbcDisplayNames();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            var match = MessageComment.Match(line);
            if (match.Success && TryId(match.Groups[1].Value, out var id))
            {
                var label = DecodeComment(match.Groups[2].Value);
                names._messageDescriptions[id] = label;
                if (LooksLikeAlias(label)) names._commentMessages[id] = label.Trim();
                continue;
            }
            match = SignalComment.Match(line);
            if (match.Success && TryId(match.Groups[1].Value, out id))
            {
                var label = DecodeComment(match.Groups[3].Value);
                names._signalDescriptions[(id, match.Groups[2].Value)] = label;
                if (LooksLikeAlias(label))
                    names._commentSignals[(id, match.Groups[2].Value)] = label.Trim();
                continue;
            }
            match = MessageSummaryLine.Match(line);
            if (match.Success && TryId(match.Groups[1].Value, out id))
            {
                names._explicitMessageSummaries[id] = Decode(match.Groups[2].Value).Trim();
                continue;
            }
            match = SignalSummaryLine.Match(line);
            if (match.Success && TryId(match.Groups[1].Value, out id))
            {
                names._explicitSignalSummaries[(id, match.Groups[2].Value)] =
                    Decode(match.Groups[3].Value).Trim();
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

    // The short UI summary is independent from the full raw CM_ definition.
    // Existing DBC files can still display a read-only CM_ first sentence.
    private static string FirstSentence(string source)
    {
        var first = source.Split(['。', '；', '\n', '\r'], StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.Length > 0) ?? "";
        return first.Length <= 90 ? first : first[..90] + "…";
    }
    public string MessageCommentPreview(uint id) =>
        FirstSentence(MessageDescription(id)) is { Length: > 0 } preview
            ? preview : MessageSummary(id);
    public string SignalCommentPreview(uint id, string signalName) =>
        FirstSentence(SignalDescription(id, signalName)) is { Length: > 0 } preview
            ? preview : SignalSummary(id, signalName);

    public void SetMessageDescription(uint id, string? comment)
    {
        var content = NormalizeComment(comment);
        _messageDescriptions[id] = content;
        _editedMessageComments[id] = content;
        // A long description is not a name. Remove stale legacy CM_ alias
        // fallback and retain it only for short, name-like imported comments.
        if (LooksLikeAlias(content)) _commentMessages[id] = content.Trim();
        else _commentMessages.Remove(id);
    }

    public void SetSignalDescription(uint id, string signalName, string? comment)
    {
        if (string.IsNullOrWhiteSpace(signalName))
            throw new ArgumentException("请先选择信号。", nameof(signalName));
        var content = NormalizeComment(comment);
        _signalDescriptions[(id, signalName)] = content;
        _editedSignalComments[(id, signalName)] = content;
        if (LooksLikeAlias(content)) _commentSignals[(id, signalName)] = content.Trim();
        else _commentSignals.Remove((id, signalName));
    }

    public bool SameEditedComments(DbcDisplayNames other) =>
        _editedMessageComments.All(edit =>
            other.MessageDescription(edit.Key) == edit.Value) &&
        _editedSignalComments.All(edit =>
            other.SignalDescription(edit.Key.Id, edit.Key.Signal) == edit.Value);

    public string MessageSummary(uint id) =>
        _explicitMessageSummaries.TryGetValue(id, out var summary) ? summary :
            FirstSentence(MessageDescription(id));
    public string SignalSummary(uint id, string signalName) =>
        _explicitSignalSummaries.TryGetValue((id, signalName), out var summary) ? summary :
            FirstSentence(SignalDescription(id, signalName));
    public string ExplicitMessageSummary(uint id) =>
        _explicitMessageSummaries.GetValueOrDefault(id, "");
    public string ExplicitSignalSummary(uint id, string name) =>
        _explicitSignalSummaries.GetValueOrDefault((id, name), "");

    public string MessageDescription(uint encodedMessageId) =>
        _messageDescriptions.GetValueOrDefault(encodedMessageId, "");
    public string SignalDescription(uint encodedMessageId, string signalName) =>
        _signalDescriptions.GetValueOrDefault((encodedMessageId, signalName), "");

    public string MessageAlias(uint encodedMessageId) =>
        _explicitMessages.TryGetValue(encodedMessageId, out var value) ? value :
        _commentMessages.GetValueOrDefault(encodedMessageId, "");

    public string SignalAlias(uint encodedMessageId, string signalName) =>
        _explicitSignals.TryGetValue((encodedMessageId, signalName), out var value) ? value :
        _commentSignals.GetValueOrDefault((encodedMessageId, signalName), "");

    public string MessageLabel(DbcMessage message)
    {
        var chinese = MessageAlias(message.Id);
        return string.IsNullOrWhiteSpace(chinese) ? message.Name : chinese;
    }

    public string SignalLabel(DbcMessage message, DbcSignal signal)
    {
        var chinese = SignalAlias(message.Id, signal.Name);
        return string.IsNullOrWhiteSpace(chinese) ? signal.Name : chinese;
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

    public void SetMessageSummary(uint id, string? value)
    {
        var text = NormalizeSummary(value);
        if (text.Length == 0) _explicitMessageSummaries.Remove(id);
        else _explicitMessageSummaries[id] = text;
    }
    public void SetSignalSummary(uint id, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("请先选择信号。", nameof(name));
        var text = NormalizeSummary(value);
        if (text.Length == 0) _explicitSignalSummaries.Remove((id, name));
        else _explicitSignalSummaries[(id, name)] = text;
    }
    /// <summary>Rewrites only AutoTest-owned labels and summaries, preserving
    /// all raw CM_, VAL_, BO_, SG_ and other unowned DBC content.</summary>
    public string Rewrite(string source)
    {
        source = RewriteEditedComments(source);
        var output = new StringBuilder(source.Length + 256);
        // Preserve the byte-level newline convention and every unrelated line.
        // Removing an AutoTest-owned attribute never rewrites CM_, VAL_, BO_ or SG_.
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        foreach (var segment in Regex.Split(source, @"(?<=\n)"))
        {
            if (segment.Length == 0) continue;
            var line = segment.TrimEnd('\r', '\n');
            if (OwnedDefinition.IsMatch(line) ||
                MessageAliasLine.IsMatch(line) || SignalAliasLine.IsMatch(line) ||
                MessageSummaryLine.IsMatch(line) || SignalSummaryLine.IsMatch(line))
                continue;
            output.Append(segment);
        }
        if ((_explicitMessages.Count > 0 || _explicitSignals.Count > 0 ||
            _explicitMessageSummaries.Count > 0 || _explicitSignalSummaries.Count > 0) &&
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
        if (_explicitMessageSummaries.Count > 0)
        {
            output.Append("BA_DEF_ BO_ \"AutoTestMessageSummary\" STRING ;").Append(newline);
            foreach (var pair in _explicitMessageSummaries.OrderBy(x => x.Key))
                output.Append("BA_ \"").Append(MessageSummaryAttribute).Append("\" BO_ ")
                    .Append(pair.Key.ToString(CultureInfo.InvariantCulture)).Append(" \"")
                    .Append(Encode(pair.Value)).Append("\";").Append(newline);
        }
        if (_explicitSignalSummaries.Count > 0)
        {
            output.Append("BA_DEF_ SG_ \"AutoTestSignalSummary\" STRING ;").Append(newline);
            foreach (var pair in _explicitSignalSummaries.OrderBy(x => x.Key.Id)
                         .ThenBy(x => x.Key.Signal, StringComparer.Ordinal))
                output.Append("BA_ \"").Append(SignalSummaryAttribute).Append("\" SG_ ")
                    .Append(pair.Key.Id.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(pair.Key.Signal).Append(" \"").Append(Encode(pair.Value))
                    .Append("\";").Append(newline);
        }
        return output.ToString();
    }


    // CM_ BO_/SG_ are native DBC documentation, unlike short AutoTest summaries.
    // Rewrite exact recognised statements only. Unknown/multiline source forms
    // are deliberately rejected for the edited object rather than damaged.
    private string RewriteEditedComments(string source)
    {
        if (_editedMessageComments.Count == 0 && _editedSignalComments.Count == 0)
            return source;
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var output = new StringBuilder(source.Length + 512);
        var seenMessages = new HashSet<uint>();
        var seenSignals = new HashSet<(uint Id, string Signal)>();

        foreach (var segment in Regex.Split(source, @"(?<=\n)"))
        {
            if (segment.Length == 0) continue;
            var content = segment.TrimEnd('\r', '\n');
            var suffix = segment[content.Length..];
            var match = MessageComment.Match(content);
            if (match.Success && TryId(match.Groups[1].Value, out var msgId) &&
                _editedMessageComments.TryGetValue(msgId, out var msgComment))
            {
                if (!seenMessages.Add(msgId))
                    throw new ProjectDataException("DBC_COMMENT_DUPLICATE", "报文存在重复 CM_，不能自动覆盖：" + msgId);
                if (msgComment.Length != 0)
                    output.Append("CM_ BO_ ").Append(msgId.ToString(CultureInfo.InvariantCulture))
                        .Append(" \"").Append(EncodeComment(msgComment)).Append("\";").Append(suffix);
                continue;
            }
            match = SignalComment.Match(content);
            if (match.Success && TryId(match.Groups[1].Value, out msgId))
            {
                var key = (msgId, match.Groups[2].Value);
                if (_editedSignalComments.TryGetValue(key, out var sigComment))
                {
                    if (!seenSignals.Add(key))
                        throw new ProjectDataException("DBC_COMMENT_DUPLICATE", "信号存在重复 CM_：" + key);
                    if (sigComment.Length != 0)
                        output.Append("CM_ SG_ ").Append(msgId.ToString(CultureInfo.InvariantCulture))
                            .Append(' ').Append(key.Item2).Append(" \"")
                            .Append(EncodeComment(sigComment)).Append("\";").Append(suffix);
                    continue;
                }
            }
            // Fail closed when the selected object's comment does not have a
            // recognised single-statement form (e.g. raw embedded newlines).
            if (content.TrimStart().StartsWith("CM_ BO_", StringComparison.Ordinal) ||
                content.TrimStart().StartsWith("CM_ SG_", StringComparison.Ordinal))
            {
                var keyMatch = Regex.Match(content, @"^\s*CM_\s+BO_\s+(\d+)\b");
                if (keyMatch.Success && TryId(keyMatch.Groups[1].Value, out var id) &&
                    _editedMessageComments.ContainsKey(id) && !MessageComment.IsMatch(content))
                    throw new ProjectDataException("DBC_COMMENT_UNSUPPORTED",
                        "原报文注释使用了不支持的原始格式，未覆盖原文：" + id);
                keyMatch = Regex.Match(content, @"^\s*CM_\s+SG_\s+(\d+)\s+(\w+)\b");
                if (keyMatch.Success && TryId(keyMatch.Groups[1].Value, out id) &&
                    _editedSignalComments.ContainsKey((id, keyMatch.Groups[2].Value)) &&
                    !SignalComment.IsMatch(content))
                    throw new ProjectDataException("DBC_COMMENT_UNSUPPORTED",
                        "原信号注释使用了不支持的原始格式，未覆盖原文：" + keyMatch.Groups[2].Value);
            }
            output.Append(segment);
        }
        foreach (var edit in _editedMessageComments.OrderBy(x => x.Key))
        {
            if (seenMessages.Contains(edit.Key) || edit.Value.Length == 0) continue;
            if (output.Length > 0 && output[^1] != '\n') output.Append(newline);
            output.Append("CM_ BO_ ").Append(edit.Key.ToString(CultureInfo.InvariantCulture))
                .Append(" \"").Append(EncodeComment(edit.Value)).Append("\";").Append(newline);
        }
        foreach (var edit in _editedSignalComments.OrderBy(x => x.Key.Id)
                     .ThenBy(x => x.Key.Signal, StringComparer.Ordinal))
        {
            if (seenSignals.Contains(edit.Key) || edit.Value.Length == 0) continue;
            if (output.Length > 0 && output[^1] != '\n') output.Append(newline);
            output.Append("CM_ SG_ ").Append(edit.Key.Id.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(edit.Key.Signal).Append(" \"")
                .Append(EncodeComment(edit.Value)).Append("\";").Append(newline);
        }
        return output.ToString();
    }

    private static string NormalizeComment(string? comment)
    {
        var normalized = (comment ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        if (normalized.Length > 16000 || normalized.Any(c => char.IsControl(c) && c is not '\n' and not '\t'))
            throw new ProjectDataException("DBC_COMMENT_INVALID",
                "DBC 注释最长 16000 字，允许换行和制表符，不允许其他控制字符。");
        return normalized;
    }

    private static string DecodeComment(string value)
    {
        var output = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                output.Append(value[i]);
                continue;
            }
            var next = value[++i];
            output.Append(next switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '"' => '"',
                '\\' => '\\',
                _ => '\\'
            });
            if (next is not ('n' or 'r' or 't' or '"' or '\\')) output.Append(next);
        }
        return output.ToString();
    }

    private static string EncodeComment(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\n", "\\n").Replace("\t", "\\t");

    private static bool LooksLikeAlias(string comment) =>
        comment.Length is > 0 and <= 48 && !comment.Contains('\n') &&
        !comment.Contains('。') && !comment.Contains('；') && ContainsChinese(comment);

    public bool SameExplicitNames(DbcDisplayNames other) =>
        _explicitMessages.Count == other._explicitMessages.Count &&
        _explicitSignals.Count == other._explicitSignals.Count &&
        _explicitMessageSummaries.Count == other._explicitMessageSummaries.Count &&
        _explicitSignalSummaries.Count == other._explicitSignalSummaries.Count &&
        _explicitMessages.All(x => other._explicitMessages.TryGetValue(x.Key, out var v) && v == x.Value) &&
        _explicitSignals.All(x => other._explicitSignals.TryGetValue(x.Key, out var v) && v == x.Value) &&
        _explicitMessageSummaries.All(x => other._explicitMessageSummaries.TryGetValue(x.Key, out var v) && v == x.Value) &&
        _explicitSignalSummaries.All(x => other._explicitSignalSummaries.TryGetValue(x.Key, out var v) && v == x.Value);

    private static string Normalize(string? input)
    {
        var value = (input ?? "").Trim();
        if (value.Length > 100 || value.Any(c => char.IsControl(c)))
            throw new ProjectDataException("DBC_ALIAS", "中文别名最长 100 字，不允许换行或控制字符。");
        return value;
    }

    private static string NormalizeSummary(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length > 180 || text.Any(char.IsControl))
            throw new ProjectDataException("DBC_SUMMARY",
                "功能摘要最多 180 字且不可换行；原始技术定义仍保存在 CM_ 注释中。");
        return text;
    }

    private static bool ContainsChinese(string label) => label.Any(c => c is >= '\u3400' and <= '\u9FFF');
    private static bool TryId(string text, out uint id) =>
        uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    private static string Decode(string value) => value.Replace("\\\"", "\"").Replace("\\\\", "\\");
    private static string Encode(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
