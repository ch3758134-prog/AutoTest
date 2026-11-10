using System.Windows.Controls;
using AutoTest.Desktop.Shell;

namespace AutoTest.Desktop.Tools.Testing;

/// <summary>
/// Formal project-bound run surface. The same MainWindowViewModel is used
/// by P01 project, P10 TEST editor and P11 Run. No second LAB document exists.
/// </summary>
public partial class ProjectRunWorkbenchView : UserControl
{
    public ProjectRunWorkbenchView(MainWindowViewModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        InitializeComponent();
        DataContext = project;
    }
}
