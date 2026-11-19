using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Frontend.Tools;

public partial class ManualBenchView : UserControl
{
    public ManualBenchView() => InitializeComponent();
    private UiWorkbenchState State => (UiWorkbenchState)DataContext;

    private void AddCan_Click(object sender, RoutedEventArgs e)
    {
        var row = new CanDraft { Name = $"报文 {State.CanRows.Count + 1}" };
        State.CanRows.Add(row);
        State.SelectedCan = row;
        CanGrid.ScrollIntoView(row);
    }
    private void CopyCan_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedCan is not { } original) return;
        var row = new CanDraft
        {
            Name = original.Name + "（副本）",
            IdHex = original.IdHex, DataHex = original.DataHex,
            Included = original.Included,
            Periodic = original.Periodic, PeriodMs = original.PeriodMs
        };
        State.CanRows.Insert(State.CanRows.IndexOf(original) + 1, row);
        State.SelectedCan = row;
    }
    private void DeleteCan_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedCan is not { } row) return;
        State.CanRows.Remove(row);
        State.SelectedCan = State.CanRows.FirstOrDefault();
    }
    private void AddAction_Click(object sender, RoutedEventArgs e)
    {
        var action = new KeyActionDraft { Name = $"输入动作 {State.KeyActions.Count + 1}" };
        State.KeyActions.Add(action);
        State.SelectedAction = action;
        ActionList.ScrollIntoView(action);
    }
    private void AddKeyStep_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedAction is null) AddAction_Click(sender, e);
        if (State.SelectedAction is null) return;
        var step = new KeyStepDraft();
        State.SelectedAction.Steps.Add(step);
        State.SelectedKeyStep = step;
    }
}
