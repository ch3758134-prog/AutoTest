using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoTest.Frontend.Views;

public partial class EditorWorkspace : UserControl
{
    public event EventHandler? RequestRun;
    public EditorWorkspace() => InitializeComponent();
    private UiWorkbenchState State => (UiWorkbenchState)DataContext;

    public void FocusCaseName()
    {
        CaseNameBox.Focus();
        CaseNameBox.SelectAll();
    }
    private void StepsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (e.Key == Key.F9 && State.SelectedStep is not null)
        {
            State.SelectedStep.Breakpoint = !State.SelectedStep.Breakpoint;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            Delete_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Run_Click(object sender, RoutedEventArgs e) =>
        RequestRun?.Invoke(this, EventArgs.Empty);

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedCase is null)
        {
            var first = new CaseDraft { Name = "新测试用例" };
            State.Cases.Add(first);
            State.SelectedCase = first;
        }
        var phase = (PhasePicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "测试";
        var step = new StepDraft
        {
            Name = $"新步骤 {State.SelectedCase!.Steps.Count + 1}",
            Phase = phase
        };
        State.SelectedCase.Steps.Add(step);
        State.SelectedStep = step;
        StepsGrid.ScrollIntoView(step);
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var list = State.SelectedCase?.Steps;
        var selected = State.SelectedStep;
        if (list is null || selected is null) return;
        var clone = new StepDraft
        {
            Name = selected.Name + "（副本）",
            Phase = selected.Phase, Kind = selected.Kind,
            Target = selected.Target, Value = selected.Value,
            Note = selected.Note, Enabled = selected.Enabled
        };
        list.Insert(list.IndexOf(selected) + 1, clone);
        State.SelectedStep = clone;
        StepsGrid.ScrollIntoView(clone);
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var list = State.SelectedCase?.Steps;
        var step = State.SelectedStep;
        if (list is null || step is null) return;
        var idx = list.IndexOf(step);
        list.Remove(step);
        State.SelectedStep = list.Count == 0 ? null : list[Math.Min(idx, list.Count - 1)];
    }
    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int direction)
    {
        var list = State.SelectedCase?.Steps;
        var step = State.SelectedStep;
        if (list is null || step is null) return;
        var from = list.IndexOf(step);
        var to = from + direction;
        if (from < 0 || to < 0 || to >= list.Count) return;
        list.Move(from, to);
        StepsGrid.ScrollIntoView(step);
    }
}
