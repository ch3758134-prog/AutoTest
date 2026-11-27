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
    private bool _dataDirty;
    private DbcDisplayNames _displayNames;
    private readonly bool _sourcePreserving;
    private static readonly HashSet<string> SafeToPreserve = new(StringComparer.Ordinal)
    {
        "VERSION nonempty", "CM_", "BA_", "BA_DEF_", "BA_DEF_DEF_",
        "extended-frame policy not verified"
    };
    public string SourceSha256 { get; }
    public IReadOnlyList<string> UnsupportedDirectives { get; }
    public bool CanEdit => UnsupportedDirectives.Count == 0 || _sourcePreserving;
    public bool IsSourcePreserving => _sourcePreserving;
    public bool IsDirty => _dirty;
    // Alias-only edits can be safely applied to read-only DBC grammars because
    // they rewrite just the two attributes owned by AutoTest.
    public bool CanSave => !_dataDirty || CanEdit;
    public DbcDisplayNames DisplayNames => _displayNames;
    public DbcDatabase Model => _editing.Draft;
    public IReadOnlyList<DbcValueTable> ValueTables => _tables;

    private DbcWorkbenchDocument(string name, string original, DbcDatabase model,
        ImmutableArray<DbcValueTable> tables, IReadOnlyList<string> unsupported)
    {
        _original = original;
        _name = name;
        _editing = new DbcEditSession(model, allowSignalOverlaps: true);
        _displayNames = DbcDisplayNames.Parse(original);
        _tables = tables;
        UnsupportedDirectives = unsupported;
        _sourcePreserving = unsupported.Count > 0 && unsupported.All(SafeToPreserve.Contains);
        SourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))).ToLowerInvariant();
    }

    public static DbcWorkbenchDocument Import(string name, string text)
    {
        var model = DbcParserAdapter.Parse(text, name, allowSignalOverlaps: true);
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

    public void AddNode(string name) { RejectShapeEdits(); Mutate(e => e.AddNode(name)); }
    public void AddMessage(DbcMessage message)
    {
        RequireEditable();
        if (_sourcePreserving && Model.Messages.IsEmpty)
            throw new ProjectDataException("DBC_SOURCE_PRESERVE",
                "此文件没有可定位的报文区域，不能安全插入新报文。");
        Mutate(e => e.AddMessage(message));
    }
    public void UpdateMessage(uint previousId, DbcMessage replacement)
    {
        RequireEditable();
        if (_sourcePreserving && previousId != replacement.Id)
            throw new ProjectDataException("DBC_SOURCE_PRESERVE", "保留原文模式暂不允许修改 CAN ID，避免破坏注释和属性引用。");
        if (_tables.Any(t => t.MessageId == previousId) && previousId != replacement.Id)
            throw new ProjectDataException("DBC_VAL_REFERENCE", "Remove value tables before changing this frame ID.");
        Mutate(e =>
        {
            var old = e.Draft.FindMessage(previousId);
            var index = Array.FindIndex(e.Draft.Messages.ToArray(), m => m.Id == previousId);
            e.Apply(e.Draft with { Messages = e.Draft.Messages.SetItem(index, replacement with { Signals = old.Signals }) });
        });
        if (previousId != replacement.Id) _displayNames.RemapMessage(previousId, replacement.Id);
    }
    public void DeleteMessage(uint id)
    {
        RequireEditable();
        if (_sourcePreserving) DbcSourcePreservingExporter.AssertMayDeleteMessage(_original, id);
        Mutate(e => e.RemoveMessage(id));
        _tables = _tables.RemoveAll(v => v.MessageId == id);
        _displayNames.RemoveMessage(id);
    }
    public void AddSignal(uint messageId, DbcSignal signal)
    {
        RequireEditable();
        Mutate(e => e.AddSignal(messageId, signal));
    }
    public void UpdateSignal(uint messageId, string previousName, DbcSignal updated)
    {
        if (_sourcePreserving && previousName != updated.Name)
            throw new ProjectDataException("DBC_SOURCE_PRESERVE", "保留原文模式暂不允许重命名信号，避免破坏注释和枚举引用。");
        if (_tables.Any(t => t.MessageId == messageId && t.SignalName == previousName) && updated.Name != previousName)
            throw new ProjectDataException("DBC_VAL_REFERENCE", "Remove value description before renaming the signal.");
        Mutate(e => e.EditSignal(messageId, previousName, updated));
        if (previousName != updated.Name) _displayNames.RemapSignal(messageId, previousName, updated.Name);
    }
    public void DeleteSignal(uint messageId, string name)
    {
        RequireEditable();
        if (_sourcePreserving)
            DbcSourcePreservingExporter.AssertMayDeleteSignal(_original, messageId, name);
        Mutate(e => e.RemoveSignal(messageId, name));
        _tables = _tables.RemoveAll(v => v.MessageId == messageId && v.SignalName == name);
        _displayNames.RemoveSignal(messageId, name);
    }
    public void SetValueDescription(uint messageId, string name, long raw, string label)
    {
        RequireEditable();
        if (_sourcePreserving)
            throw new ProjectDataException("DBC_SOURCE_PRESERVE",
                "保留原文模式暂不支持修改 VAL_ 枚举定义，原有枚举信息已保留。");
        Model.FindSignal(messageId, name);
        if (string.IsNullOrWhiteSpace(label)) throw new ProjectDataException("DBC_VAL", "Label required.");
        var index = Array.FindIndex(_tables.ToArray(), t => t.MessageId == messageId && t.SignalName == name);
        var previous = index < 0 ? ImmutableDictionary<long, string>.Empty : _tables[index].Labels;
        var next = new DbcValueTable(messageId, name, previous.SetItem(raw, label)).Validate(Model);
        _tables = index < 0 ? _tables.Add(next) : _tables.SetItem(index, next);
        _dirty = true;
        _dataDirty = true;
    }
    public void SetMessageAlias(uint messageId, string? alias)
    {
        Model.FindMessage(messageId);
        _displayNames.SetMessageAlias(messageId, alias);
        _dirty = true;
    }

    public void SetSignalAlias(uint messageId, string signalName, string? alias)
    {
        Model.FindSignal(messageId, signalName);
        _displayNames.SetSignalAlias(messageId, signalName, alias);
        _dirty = true;
    }
    public void SetMessageComment(uint id, string? comment)
    {
        Model.FindMessage(id);
        _displayNames.SetMessageDescription(id, comment);
        _dirty = true;
    }

    public void SetSignalComment(uint id, string name, string? comment)
    {
        Model.FindSignal(id, name);
        _displayNames.SetSignalDescription(id, name, comment);
        _dirty = true;
    }

    public void SetMessageSummary(uint id, string? summary)
    {
        Model.FindMessage(id);
        _displayNames.SetMessageSummary(id, summary);
        _dirty = true;
    }
    public void SetSignalSummary(uint id, string name, string? summary)
    {
        Model.FindSignal(id, name);
        _displayNames.SetSignalSummary(id, name, summary);
        _dirty = true;
    }

    public string ExportText()
    {
        if (!_dirty) return _original;
        // The repair editor may display overlapping signals; however, no DBC
        // revision may be saved while a real CAN payload remains ambiguous.
        Model.Validate();
        if (_dataDirty) RequireEditable();
        var output = !_dataDirty || _sourcePreserving
            ? null : new StringBuilder(DbcParserAdapter.Export(Model));
        if (output is not null)
        foreach (var value in _tables.OrderBy(t => t.MessageId).ThenBy(t => t.SignalName, StringComparer.Ordinal))
        {
            output.Append("VAL_ ").Append(value.MessageId).Append(' ').Append(value.SignalName).Append(' ');
            foreach (var item in value.Labels.OrderBy(p => p.Key))
                output.Append(item.Key).Append(" \"").Append(item.Value.Replace("\\", "\\\\").Replace("\"", "\\\""))
                    .Append("\" ");
            output.AppendLine(";");
        }
        var baseSource = !_dataDirty ? _original :
            _sourcePreserving ? DbcSourcePreservingExporter.Rewrite(_original, Model) :
            output!.ToString();
        var serialized = _displayNames.Rewrite(baseSource);
        var roundTrip = Import(_name, serialized);
        if ((_dataDirty && !roundTrip.CanEdit) ||
            !Equivalent(Model, roundTrip.Model) ||
            !ValueTablesMatch(ValueTables, roundTrip.ValueTables) ||
            !_displayNames.SameExplicitNames(roundTrip.DisplayNames) ||
            !_displayNames.SameEditedComments(roundTrip.DisplayNames))
            throw new ProjectDataException("DBC_ROUNDTRIP",
                "DBC 注释或别名保存回读校验失败，原始报文、信号和原有属性均未覆盖。");
        return serialized;
    }
    private static bool ValueTablesMatch(IReadOnlyList<DbcValueTable> a, IReadOnlyList<DbcValueTable> b) =>
        a.Count == b.Count && a.All(x => b.Any(y => y.MessageId == x.MessageId &&
            y.SignalName == x.SignalName && x.Labels.Count == y.Labels.Count &&
            x.Labels.All(kv => y.Labels.TryGetValue(kv.Key, out var v) && v == kv.Value)));
    // A checkpoint retains the in-memory repair model, including temporary
    // overlapping signals; no export/reparse or revision increment is needed.
    public sealed class EditCheckpoint
    {
        internal DbcWorkbenchDocument Owner { get; }
        internal DbcDatabase Model { get; }
        internal ImmutableArray<DbcValueTable> Tables { get; }
        internal DbcDisplayNames Names { get; }
        internal bool Dirty { get; }
        internal bool DataDirty { get; }
        internal EditCheckpoint(DbcWorkbenchDocument owner)
        {
            Owner = owner; Model = owner.Model; Tables = owner._tables;
            Names = owner._displayNames.Copy(); Dirty = owner._dirty; DataDirty = owner._dataDirty;
        }
    }

    public EditCheckpoint CaptureCheckpoint() => new(this);

    /// <summary>Undo in the same workspace may span an autosave. Restore
    /// the exact historical source basis and draft, while the workspace
    /// retains the CURRENT file receipt and revision conflict checks.</summary>
    public static DbcWorkbenchDocument RecreateHistoricalDraft(EditCheckpoint checkpoint)
    {
        var owner = checkpoint.Owner;
        var document = new DbcWorkbenchDocument(owner._name, owner._original,
            checkpoint.Model, checkpoint.Tables, owner.UnsupportedDirectives);
        document._displayNames = checkpoint.Names.Copy();
        document._dirty = true;
        document._dataDirty = checkpoint.DataDirty;
        return document;
    }
    public void RestoreCheckpoint(EditCheckpoint checkpoint)
    {
        if (!ReferenceEquals(checkpoint.Owner, this))
            throw new InvalidOperationException("DBC checkpoint belongs to another file session.");
        _editing = new DbcEditSession(checkpoint.Model, allowSignalOverlaps: true);
        _tables = checkpoint.Tables;
        _displayNames = checkpoint.Names.Copy();
        _dirty = checkpoint.Dirty;
        _dataDirty = checkpoint.DataDirty;
    }

    public void ReplaceValueDescriptions(uint messageId, string name,
        IReadOnlyDictionary<long, string> values)
    {
        RejectShapeEdits();
        var signal = Model.FindSignal(messageId, name);
        foreach (var pair in values)
        {
            var raw = pair.Key;
            var fits = signal.Signed
                ? signal.BitLength == 64 || (raw >= -(1L << (signal.BitLength - 1)) &&
                    raw <= (1L << (signal.BitLength - 1)) - 1)
                : raw >= 0 && (signal.BitLength >= 63 || raw <= (1L << signal.BitLength) - 1);
            if (!fits || string.IsNullOrWhiteSpace(pair.Value) ||
                pair.Value.Any(char.IsControl))
                throw new ProjectDataException("DBC_VAL", "枚举原始值超出位长范围，或描述为空/含控制字符。");
        }
        var next = values.ToImmutableDictionary();
        var previous = _tables.FirstOrDefault(t => t.MessageId == messageId && t.SignalName == name);
        if ((previous is null && next.IsEmpty) ||
            (previous is not null && previous.Labels.Count == next.Count &&
                previous.Labels.All(p => next.TryGetValue(p.Key, out var v) && v == p.Value))) return;
        _tables = _tables.RemoveAll(t => t.MessageId == messageId && t.SignalName == name);
        if (!next.IsEmpty)
            _tables = _tables.Add(new DbcValueTable(messageId, name, next).Validate(Model));
        _dirty = _dataDirty = true;
    }

    public void Reset()
    {
        var fresh = Import(_name, _original);
        _editing = new DbcEditSession(fresh.Model, allowSignalOverlaps: true);
        _tables = fresh._tables;
        _displayNames = fresh._displayNames;
        _dirty = false;
        _dataDirty = false;
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
        _dataDirty = true;
    }
    private void RejectShapeEdits()
    {
        RequireEditable();
        if (_sourcePreserving)
            throw new ProjectDataException("DBC_SOURCE_PRESERVE",
                "当前 DBC 带有需要保留的附加元数据，本轮只允许编辑现有报文和信号的参数。");
    }

    private void RequireEditable()
    {
        if (!CanEdit) throw new ProjectDataException("DBC_UNSUPPORTED_READONLY",
            "DBC contains directives not safely writable: " + string.Join(", ", UnsupportedDirectives));
    }
}


