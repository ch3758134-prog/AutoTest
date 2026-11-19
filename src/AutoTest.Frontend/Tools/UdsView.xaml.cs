using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Frontend.Tools;

public partial class UdsView : UserControl
{
    public UdsView() => InitializeComponent();
    private UiWorkbenchState State => (UiWorkbenchState)DataContext;
    private void Add_Click(object sender, RoutedEventArgs e) => AddRequest("22");
    private void AddService_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string service }) AddRequest(service);
    }
    private void AddRequest(string service)
    {
        var next = new UdsRequestDraft
        {
            Name = $"新请求 {State.UdsRequests.Count + 1}",
            Service = service
        };
        State.UdsRequests.Add(next);
        State.SelectedUds = next;
        RequestGrid.ScrollIntoView(next);
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedUds is not { } src) return;
        var copy = new UdsRequestDraft
        {
            Name = src.Name + "（副本）", Service = src.Service,
            Payload = src.Payload, TimeoutMs = src.TimeoutMs,
            Description = src.Description, Enabled = src.Enabled
        };
        State.UdsRequests.Insert(State.UdsRequests.IndexOf(src) + 1, copy);
        State.SelectedUds = copy;
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (State.SelectedUds is not { } src) return;
        State.UdsRequests.Remove(src);
        State.SelectedUds = State.UdsRequests.FirstOrDefault();
    }
    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int direction)
    {
        if (State.SelectedUds is not { } src) return;
        var from = State.UdsRequests.IndexOf(src);
        var to = from + direction;
        if (from < 0 || to < 0 || to >= State.UdsRequests.Count) return;
        State.UdsRequests.Move(from, to);
        RequestGrid.ScrollIntoView(src);
    }
}
