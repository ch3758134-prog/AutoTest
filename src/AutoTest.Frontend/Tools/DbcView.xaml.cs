using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Frontend.Tools;

public partial class DbcView : UserControl
{
    public DbcView() => InitializeComponent();
    private UiWorkbenchState State => (UiWorkbenchState)DataContext;

    private void AddMessage_Click(object sender, RoutedEventArgs e)
    {
        var row = new DbcMessageDraft { Name = $"Message_{State.DbcMessages.Count + 1}" };
        State.DbcMessages.Add(row);
        State.SelectedDbcMessage = row;
        MessageList.ScrollIntoView(row);
    }
    private void RemoveMessage_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedDbcMessage is not { } current) return;
        State.DbcMessages.Remove(current);
        State.SelectedDbcMessage = State.DbcMessages.FirstOrDefault();
    }
    private void AddSignal_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedDbcMessage is null) AddMessage_Click(sender, e);
        if (State.SelectedDbcMessage is null) return;
        var row = new DbcSignalDraft { Name = $"Signal_{State.SelectedDbcMessage.Signals.Count + 1}" };
        State.SelectedDbcMessage.Signals.Add(row);
        State.SelectedDbcSignal = row;
        SignalGrid.ScrollIntoView(row);
    }
    private void RemoveSignal_Click(object sender, RoutedEventArgs e)
    {
        var message = State.SelectedDbcMessage;
        var signal = State.SelectedDbcSignal;
        if (message is null || signal is null) return;
        message.Signals.Remove(signal);
        State.SelectedDbcSignal = message.Signals.FirstOrDefault();
    }
}
