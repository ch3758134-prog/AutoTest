using System.Collections.Immutable;
using AutoTest.Projects.Project;
namespace AutoTest.Projects.Dbc;
public sealed class DbcEditSession
{
    public DbcDatabase Original { get; }
    public DbcDatabase Draft { get; private set; }
    public DbcEditSession(DbcDatabase source) { Original = source.Validate(); Draft = Original; }
    public void AddNode(string node)
    {
        if (string.IsNullOrWhiteSpace(node) || Draft.Nodes.Contains(node)) throw new ProjectDataException("DBC_NODE", "Invalid or duplicate node.");
        Apply(Draft with { Nodes = Draft.Nodes.Add(node) });
    }
    public void AddMessage(DbcMessage message) => Apply(Draft with { Messages = Draft.Messages.Add(message) });
    public void RemoveMessage(uint id) => Apply(Draft with { Messages = Draft.Messages.Remove(Draft.FindMessage(id)) });
    public void AddSignal(uint id, DbcSignal signal)
    {
        var m = Draft.FindMessage(id);
        ReplaceMessage(m with { Signals = m.Signals.Add(signal) });
    }
    public void EditSignal(uint id, string signalName, DbcSignal replacement)
    {
        var m = Draft.FindMessage(id);
        var index = -1;
        for (var i = 0; i < m.Signals.Length; i++)
            if (m.Signals[i].Name == signalName) { index = i; break; }
        if (index < 0) throw new ProjectDataException("DBC_SIGNAL_MISSING", "Signal not found.");
        ReplaceMessage(m with { Signals = m.Signals.SetItem(index, replacement) });
    }
    public void RemoveSignal(uint id, string signalName)
    {
        var m = Draft.FindMessage(id);
        var old = m.Signals.FirstOrDefault(s => s.Name == signalName) ?? throw new ProjectDataException("DBC_SIGNAL_MISSING", "Signal missing.");
        ReplaceMessage(m with { Signals = m.Signals.Remove(old) });
    }
    private void ReplaceMessage(DbcMessage message)
    {
        var index = -1;
        for (var i = 0; i < Draft.Messages.Length; i++)
            if (Draft.Messages[i].Id == message.Id) { index = i; break; }
        if (index < 0) throw new ProjectDataException("DBC_MESSAGE_MISSING", "Message missing.");
        Apply(Draft with { Messages = Draft.Messages.SetItem(index, message) });
    }
    public void Apply(DbcDatabase candidate) { Draft = candidate.Validate(); }
    public DbcDatabase Commit() => (Draft with { Revision = checked(Original.Revision + 1) }).Validate();
    public string Export() => DbcParserAdapter.Export(Draft);
    public void Reset() => Draft = Original;
}
