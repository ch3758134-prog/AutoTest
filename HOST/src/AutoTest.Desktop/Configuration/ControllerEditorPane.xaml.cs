using System.Windows.Controls;

namespace AutoTest.Desktop.Configuration;

/// <summary>
/// Dedicated Controller presentation. Uses the canonical EcuControlProfile fields
/// owned by ConfigurationWorkbenchViewModel; no duplicate persisted state.
/// </summary>
public partial class ControllerEditorPane : UserControl
{
    public ControllerEditorPane() => InitializeComponent();
}
