using System.Windows;
using System.Windows.Controls;

namespace AutoTest.Desktop.Shell;

public partial class ProjectOverviewView : UserControl
{
    public ContentControl InspectorHost { get; } = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    public ProjectOverviewView(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        InspectorHost.Content = new ProjectOverviewInspector(viewModel);
        Loaded += async (_, _) =>
        {
            try { await viewModel.RefreshEngineeringSetupAsync(); }
            catch (Exception error) when (error is System.IO.IOException or
                InvalidOperationException or UnauthorizedAccessException)
            {
                // A project overview cannot prevent the user from editing.
                // The full check surface can be retried after resource repair.
                AutoTest.Desktop.Diagnostics.UiExceptionReporter.Record(
                    "ECU engineering project overview", error);
            }
        };
    }
}
