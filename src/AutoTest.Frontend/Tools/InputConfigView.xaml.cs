using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Frontend.Tools;

public partial class InputConfigView : UserControl
{
    public InputConfigView() => InitializeComponent();
    private UiWorkbenchState State => (UiWorkbenchState)DataContext;
    private void AddState_Click(object sender, RoutedEventArgs e)
    {
        var row = new InputStateDraft { Name = $"输入状态 {State.InputStates.Count + 1}" };
        State.InputStates.Add(row);
        State.SelectedInputState = row;
        StatesList.ScrollIntoView(row);
    }
    private void DeleteState_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedInputState is not { } row) return;
        State.InputStates.Remove(row);
        State.SelectedInputState = State.InputStates.FirstOrDefault();
    }
    private void ClearKeys_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedInputState is null) return;
        foreach (var key in State.SelectedInputState.Keys) key.Checked = false;
    }
}
