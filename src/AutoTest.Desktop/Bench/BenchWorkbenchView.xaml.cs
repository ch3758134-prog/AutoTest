using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Bench;

public partial class BenchWorkbenchView : UserControl
{
    // INTEGRATION registers this View + VM in the existing Prism Desktop container.
    public BenchWorkbenchView(BenchWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BenchWorkbenchViewModel vm)
            await vm.InitializeAsync();
    }
}
