using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoTest.Desktop.Testing;

public partial class TestProjectWorkbenchView : UserControl
{
    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };

    private readonly Action? _openDependencyReview;
    public TestProjectWorkbenchView(TestProjectWorkbenchViewModel viewModel,
        Action? openDependencyReview = null)
    {
        _openDependencyReview = openDependencyReview;
        InitializeComponent();
        DataContext = viewModel;
        InspectorHost.Content = new TestProjectSelectionInspector(viewModel);
    }
    private void OpenDependencyReview_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _openDependencyReview?.Invoke();
    }

    private void PackagesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left &&
            DataContext is TestProjectWorkbenchViewModel vm)
            vm.OpenSelectedPackage();
    }
    private void OpenPackage_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is TestProjectWorkbenchViewModel vm &&
            sender is Button { Tag: TestProjectPackageRow row })
            vm.OpenPackage(row);
    }
    private void MoveRowUp_Click(object sender, RoutedEventArgs e) => Move(sender, -1, e);
    private void MoveRowDown_Click(object sender, RoutedEventArgs e) => Move(sender, 1, e);
    private void Move(object sender, int direction, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is TestProjectWorkbenchViewModel vm &&
            sender is Button { Tag: TestProjectPackageRow row })
            vm.MoveRow(row, direction);
    }
    private async void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is TestProjectWorkbenchViewModel vm &&
            sender is Button { Tag: TestProjectPackageRow row })
            await vm.RemoveRowAsync(row);
    }
}
