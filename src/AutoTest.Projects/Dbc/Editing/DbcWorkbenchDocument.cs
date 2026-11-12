using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AutoTest.Projects.Project;

namespace AutoTest.Projects.Dbc.Editing;

public sealed record DbcValueTable(uint MessageId, string SignalName, ImmutableDictionary<long, string> Labels)
{
    public DbcValueTable Validate(DbcDatabase model)
    {
        model.FindSignal(MessageId, SignalName);
        if (Labels.IsEmpty || Labels.Any(p => string.IsNullOrWhiteSpace(p.Value)))
            throw new ProjectDataException("DBC_VAL", "Value description entries are missing.");
        return this;
    }
}

/// <summary>
/// A deliberately fail-closed DBC authoring boundary: full source is retained unchanged for read-only
/// imports. Edits are enabled only for the explicitly supported grammar (BO_, SG_, BU_, VAL_).
/// Unsupported constructs are never silently removed by a save/export.
/// </summary>
public sealed class DbcWorkbenchDocument
{
    private static readonly Regex ValHeader = new(@"^VAL_\s+(\d+)\s+(\w+)\s+(.+);\s*$", RegexOptions.Compiled);
    private static readonly Regex SupportedSignal = new(@"^SG_\s+\w+\s*(?:M|m\d+)?\s*:\s*\d+\|\d+@[01][+-]\s*\([^,]+,[^)]+\)\s*\[[^|]+\|[^]]+\]\s*""[^""]*""\s*\w+$", RegexOptions.Compiled);
    private static readonly Regex ValItem = new("\\s*(-?\\d+)\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
    private readonly string _original;
    private readonly string _name;
    private DbcEditSession _editing;
    private ImmutableArray<DbcValueTable> _tables;
    private bool _dirty;
    public string SourceSha256 { get; }
    public IReadOnlyList<string> UnsupportedDirectives { get; }
    public bool CanEdit => UnsupportedDirectives.Count == 0;
    public bool IsDirty => _dirty;
    public DbcDatabase Model => _editing.Draft;
    public IReadOnlyList<DbcValueTable> ValueTables => _tables;

    private DbcWorkbenchDocument(string name, string original, DbcDatabase model,
        ImmutableArray<DbcValueTable> tables, IReadOnlyList<string> unsupported)
    {
        _original = original;
        _name = name;
        _editing = new DbcEditSession(model);
        _tables = tables;
        UnsupportedDirectives = unsupported;
        SourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))).ToLowerInvariant();
    }

    public static DbcWorkbenchDocument Import(string name, string text)
    {
        var model = DbcParserAdapter.Parse(text, name);
        var unsupported = new List<string>();
        var tables = ImmutableArray.CreateBuilder<DbcValueTable>();
        var inNs = false;
        foreach (var source in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = source.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("NS_ :", StringComparison.Ordinal)) { inNs = true; continue; }
            if (line.StartsWith("BS_:", StringComparison.Ordinal))
            {
                inNs = false;
                if (line != "BS_:") unsupported.Add("BS_ nonempty");
                continue;
            }
            if (inNs) continue;
            if (line.StartsWith("VAL_ ", StringComparison.Ordinal))
            {
                var match = ValHeader.Match(line);
                if (!match.Success) { unsupported.Add("VAL_ syntax"); continue; }
                if (!uint.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                { unsupported.Add("VAL_ id"); continue; }
                var remainder = match.Groups[3].Value;
                var labels = ImmutableDictionary.CreateBuilder<long, string>();
                var cursor = 0;
                foreach (Match item in ValItem.Matches(remainder))
                {
                    if (remainder[cursor..item.Index].Trim().Length > 0 ||
                        !long.TryParse(item.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var key) ||
                        labels.ContainsKey(key)) { unsupported.Add("VAL_ contents"); break; }
                    labels.Add(key, item.Groups[2].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"));
                    cursor = item.Index + item.Length;
                }
                if (remainder[cursor..].Trim().Length > 0 || labels.Count == 0)
                    unsupported.Add("VAL_ contents");
                else
                {
                    try { tables.Add(new DbcValueTable(id, match.Groups[2].Value, labels.ToImmutable()).Validate(model)); }
                    catch (ProjectDataException) { unsupported.Add("VAL_ unresolved reference"); }
                }
                continue;
            }
            if (line.StartsWith("VERSION ", StringComparison.Ordinal))
            {
                if (line != "VERSION \"\"") unsupported.Add("VERSION nonempty");
                continue;
            }
            if (line.StartsWith("BU_:", StringComparison.Ordinal)) continue;
            if (line.StartsWith("BO_ ", StringComparison.Ordinal))
            {
                if (!Regex.IsMatch(line, @"^BO_\s+\d+\s+\w+\s*:\s*[0-8]\s+\w+$"))
                    unsupported.Add("BO_ trailing/unsupported fields");
                continue;
            }
            if (line.StartsWith("SG_ ", StringComparison.Ordinal))
            {
                var lastQuote = line.LastIndexOf('"');
                if (lastQuote < 0 || !SupportedSignal.IsMatch(line))
                    unsupported.Add("SG_ multi-receiver/unsupported extension");
                continue;
            }
            unsupported.Add(line.Split(' ', '\t', ':')[0]);
        }
        if (inNs) unsupported.Add("NS_ block without BS_: terminator");
        if (model.Messages.Any(m => m.Id > 0x7FF))
            unsupported.Add("extended-frame policy not verified");
        return new(name, text, model, tables.ToImmutable(), unsupported.Distinct(StringComparer.Ordinal).ToArray());
    }

    public void AddNode(string name) => Mutate(e => e.AddNode(name));
    public void AddMessage(DbcMessage message) => Mutate(e => e.AddMessage(message));
    public void UpdateMessage(uint previousId, DbcMessage replacement)
    {
        RequireEditable();
        if (_tables.Any(t => t.MessageId == previousId) && previousId != replacement.Id)
            throw new ProjectDataException("DBC_VAL_REFERENCE", "Remove value tables before changing this frame ID.");
        Mutate(e =>
        {
            var old = e.Draft.FindMessage(previousId);
            var index = Array.FindIndex(e.Draft.Messages.ToArray(), m => m.Id == previousId);
            e.Apply(e.Draft with { Messages = e.Draft.Messages.SetItem(index, replacement with { Signals = old.Signals }) });
        });
    }
    public void DeleteMessage(uint id)
    {
        Mutate(e => e.RemoveMessage(id));
        _tables = _tables.RemoveAll(v => v.MessageId == id);
    }
    public void AddSignal(uint messageId, DbcSignal signal) => Mutate(e => e.AddSignal(messageId, signal));
    public void UpdateSignal(uint messageId, string previousName, DbcSignal updated)
    {
        if (_tables.Any(t => t.MessageId == messageId && t.SignalName == previousName) && updated.Name != previousName)
            throw new ProjectDataException("DBC_VAL_REFERENCE", "Remove value description before renaming the signal.");
        Mutate(e => e.EditSignal(messageId, previousName, updated));
    }
    public void DeleteSignal(uint messageId, string name)
    {
        Mutate(e => e.RemoveSignal(messageId, name));
        _tables = _tables.RemoveAll(v => v.MessageId == messageId && v.SignalName == name);
    }
    public void SetValueDescription(uint messageId, string name, long raw, string label)
    {
        RequireEditable();
        Model.FindSignal(messageId, name);
        if (string.IsNullOrWhiteSpace(label)) throw new ProjectDataException("DBC_VAL", "Label required.");
        var index = Array.FindIndex(_tables.ToArray(), t => t.MessageId == messageId && t.SignalName == name);
        var previous = index < 0 ? ImmutableDictionary<long, string>.Empty : _tables[index].Labels;
        var next = new DbcValueTable(messageId, name, previous.SetItem(raw, label)).Validate(Model);
        _tables = index < 0 ? _tables.Add(next) : _tables.SetItem(index, next);
        _dirty = true;
    }
    public string ExportText()
    {
        if (!_dirty) return _original;
        RequireEditable();
        var output = new StringBuilder(DbcParserAdapter.Export(Model));
        foreach (var value in _tables.OrderBy(t => t.MessageId).ThenBy(t => t.SignalName, StringComparer.Ordinal))
        {
            output.Append("VAL_ ").Append(value.MessageId).Append(' ').Append(value.SignalName).Append(' ');
            foreach (var item in value.Labels.OrderBy(p => p.Key))
                output.Append(item.Key).Append(" \"").Append(item.Value.Replace("\\", "\\\\").Replace("\"", "\\\""))
                    .Append("\" ");
            output.AppendLine(";");
        }
        var serialized = output.ToString();
        var roundTrip = Import(_name, serialized);
        if (!roundTrip.CanEdit || !Equivalent(Model, roundTrip.Model) ||
            !ValueTablesMatch(ValueTables, roundTrip.ValueTables))
            throw new ProjectDataException("DBC_ROUNDTRIP", "DBC export changes the supported semantic model.");
        return serialized;
    }
    private static bool ValueTablesMatch(IReadOnlyList<DbcValueTable> a, IReadOnlyList<DbcValueTable> b) =>
        a.Count == b.Count && a.All(x => b.Any(y => y.MessageId == x.MessageId &&
            y.SignalName == x.SignalName && x.Labels.Count == y.Labels.Count &&
            x.Labels.All(kv => y.Labels.TryGetValue(kv.Key, out var v) && v == kv.Value)));
    public void Reset()
    {
        var fresh = Import(_name, _original);
        _editing = new DbcEditSession(fresh.Model);
        _tables = fresh._tables;
        _dirty = false;
    }
    private static bool Equivalent(DbcDatabase a, DbcDatabase b) =>
        a.Nodes.SequenceEqual(b.Nodes) && a.Messages.Length == b.Messages.Length &&
        a.Messages.Zip(b.Messages).All(pair => pair.First.Id == pair.Second.Id &&
            pair.First.Name == pair.Second.Name && pair.First.Dlc == pair.Second.Dlc &&
            pair.First.Transmitter == pair.Second.Transmitter &&
            pair.First.Signals.SequenceEqual(pair.Second.Signals));
    private void Mutate(Action<DbcEditSession> action)
    {
        RequireEditable();
        action(_editing);
        _dirty = true;
    }
    private void RequireEditable()
    {
        if (!CanEdit) throw new ProjectDataException("DBC_UNSUPPORTED_READONLY",
            "DBC contains directives not safely writable: " + string.Join(", ", UnsupportedDirectives));
    }
}


